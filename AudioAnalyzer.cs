using NAudio.Wave;
using NAudio.Dsp;

namespace AmpUp;

/// <summary>
/// Captures system audio via WASAPI loopback and computes 5 frequency band levels via FFT.
/// Band levels (SmoothedBands[0..4]) are 0.0-1.0, thread-safe to read.
/// </summary>
public class AudioAnalyzer : IDisposable
{
    // Public smoothed band levels: [sub-bass, bass, low-mid, high-mid, treble]
    public float[] SmoothedBands { get; } = new float[5];

    // Fine logarithmic spectrum used by VU tuning. This supplements the five
    // broad public bands so the room effect can target an actual Hz window.
    private const int SpectrumBinCount = 48;
    private const float SpectrumMinHz = 20f;
    private const float SpectrumMaxHz = 20000f;
    private readonly float[] _smoothedSpectrum = new float[SpectrumBinCount];
    private readonly float[] _spectrumScratch = new float[SpectrumBinCount];

    private WasapiLoopbackCapture? _capture;
    private readonly object _lock = new();
    private readonly float[] _sampleBuffer = new float[FftSize];
    private readonly Complex[] _fftBuffer = new Complex[FftSize];
    private readonly float[] _hannWindow = BuildHannWindow();
    private int _bufferPos;
    private int _analysisHop;
    private bool _running;
    private bool _disposed;
    private bool _unsupportedFormatLogged;

    // Wave format characteristics, derived ONCE in Start(). The device format cannot
    // change during a capture's lifetime, so OnDataAvailable (~every 10ms) doesn't
    // need to lock, re-read _capture, or re-normalize (ToStandardWaveFormat allocates
    // a new WaveFormat per call for WaveFormatExtensible).
    private WaveFormat? _sourceFormat;     // raw capture format (diagnostics only)
    private WaveFormat? _normalizedFormat; // normalized format used for sample decoding
    private int _formatChannels;
    private int _formatBytesPerSample;
    private int _formatFrameBytes;
    private int _formatSampleRate;

    // Band frequency ranges (Hz): [min, max]
    private static readonly (float Min, float Max)[] BandRanges =
    {
        (20f,   80f),     // 0: sub-bass
        (80f,   250f),    // 1: bass
        (250f,  2000f),   // 2: low-mid
        (2000f, 6000f),   // 3: high-mid
        (6000f, 20000f),  // 4: treble
    };

    private const float NormRef = 0.005f; // reference amplitude for normalization (WASAPI loopback levels are very low)
    private const int FftSize = 4096;
    private const int FftLog2 = 12; // log2(4096)
    // NAudio normalizes forward FFT bins by N. Compensate only for the larger
    // number of bins averaged in each broad band, preserving its prior response.
    private static readonly float BroadBandScale = MathF.Sqrt(FftSize / 1024f);
    private const int AnalysisHopBuffers = 1;
    private const int AnalysisHopSamples = FftSize / 2; // ~23Hz at 48kHz, 50% overlap

    private static float[] BuildHannWindow()
    {
        var window = new float[FftSize];
        for (int i = 0; i < FftSize; i++)
            window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
        return window;
    }

    public void Start()
    {
        if (_running || _disposed) return;

        try
        {
            WasapiLoopbackCapture capture;
            lock (_lock)
            {
                if (_running || _disposed) return;
                capture = new WasapiLoopbackCapture();
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                _bufferPos = 0;
                _analysisHop = 0;
                _unsupportedFormatLogged = false;

                // Derive format characteristics once — see field comments above.
                var sourceFormat = capture.WaveFormat;
                var normalized = NormalizeWaveFormat(sourceFormat);
                _sourceFormat = sourceFormat;
                _normalizedFormat = normalized;
                _formatChannels = normalized.Channels;
                _formatBytesPerSample = normalized.BitsPerSample / 8;
                _formatFrameBytes = _formatChannels * _formatBytesPerSample;
                _formatSampleRate = normalized.SampleRate;

                _capture = capture;
                _running = true;
            }
            capture.StartRecording();
            Logger.Log($"AudioAnalyzer started ({DescribeWaveFormat(capture.WaveFormat)})");
        }
        catch (Exception ex)
        {
            Logger.Log($"AudioAnalyzer Start failed: {ex.Message}");
            lock (_lock)
            {
                _capture?.Dispose();
                _capture = null;
                _running = false;
            }
        }
    }

    public void Stop()
    {
        if (!_running) return;

        WasapiLoopbackCapture? capture;
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
            capture = _capture;
            _capture = null;
        }

        try { capture?.StopRecording(); } catch { }
        try { capture?.Dispose(); } catch { }

        // Zero out bands
        lock (_lock)
        {
            for (int i = 0; i < 5; i++)
                SmoothedBands[i] = 0f;
            Array.Clear(_smoothedSpectrum);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // --- NAudio callbacks ---

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running || e.BytesRecorded == 0) return;

        // Format characteristics were captured once in Start() — no per-callback lock
        // or WaveFormat allocation needed (the format can't change while capturing).
        // A late callback racing Stop() only touches managed buffers below, which is
        // safe; the _running check above filters the common case.
        var sourceFormat = _sourceFormat;
        var format = _normalizedFormat;
        if (sourceFormat == null || format == null) return;

        int channels = _formatChannels;
        int bytesPerSample = _formatBytesPerSample;
        int frameBytes = _formatFrameBytes;
        if (channels <= 0 || bytesPerSample <= 0 || frameBytes <= 0)
        {
            LogUnsupportedFormatOnce(sourceFormat, format);
            return;
        }

        // Feed mono-mixed float samples into the accumulator buffer
        for (int offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
        {
            if (!TryReadMonoSample(e.Buffer, offset, format, channels, bytesPerSample, out float mono))
            {
                LogUnsupportedFormatOnce(sourceFormat, format);
                break;
            }

            _sampleBuffer[_bufferPos++] = mono;

            if (_bufferPos >= FftSize)
            {
                _analysisHop++;
                if (_analysisHop >= AnalysisHopBuffers)
                {
                    _analysisHop = 0;
                    ProcessFft(_formatSampleRate);
                }
                Array.Copy(_sampleBuffer, AnalysisHopSamples, _sampleBuffer, 0, FftSize - AnalysisHopSamples);
                _bufferPos = FftSize - AnalysisHopSamples;
            }
        }
    }

    private static WaveFormat NormalizeWaveFormat(WaveFormat format)
    {
        if (format is WaveFormatExtensible extensible)
        {
            try { return extensible.ToStandardWaveFormat(); }
            catch { }
        }

        return format;
    }

    private static bool TryReadMonoSample(
        byte[] buffer,
        int frameOffset,
        WaveFormat format,
        int channels,
        int bytesPerSample,
        out float mono)
    {
        mono = 0f;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
        {
            for (int ch = 0; ch < channels; ch++)
                mono += BitConverter.ToSingle(buffer, frameOffset + ch * bytesPerSample);
            mono /= channels;
            return true;
        }

        if (format.Encoding != WaveFormatEncoding.Pcm || bytesPerSample is < 1 or > 4)
            return false;

        for (int ch = 0; ch < channels; ch++)
        {
            int offset = frameOffset + ch * bytesPerSample;
            mono += bytesPerSample switch
            {
                1 => (buffer[offset] - 128) / 128f,
                2 => BitConverter.ToInt16(buffer, offset) / 32768f,
                3 => ReadPcm24(buffer, offset) / 8388608f,
                4 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                _ => 0f,
            };
        }

        mono /= channels;
        return true;
    }

    private static int ReadPcm24(byte[] buffer, int offset)
    {
        int sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        if ((sample & 0x800000) != 0)
            sample |= unchecked((int)0xFF000000);
        return sample;
    }

    private void LogUnsupportedFormatOnce(WaveFormat sourceFormat, WaveFormat normalizedFormat)
    {
        lock (_lock)
        {
            if (_unsupportedFormatLogged) return;
            _unsupportedFormatLogged = true;
        }

        Logger.Log($"AudioAnalyzer unsupported format: source={DescribeWaveFormat(sourceFormat)}, normalized={DescribeWaveFormat(normalizedFormat)}");
    }

    private static string DescribeWaveFormat(WaveFormat format)
    {
        string text = $"{format.Encoding}, {format.SampleRate} Hz, {format.Channels} ch, {format.BitsPerSample} bit";
        if (format is WaveFormatExtensible extensible)
            text += $", sub={extensible.SubFormat}";
        return text;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            Logger.Log($"AudioAnalyzer recording stopped with error: {e.Exception.Message}");

        // Auto-restart after 2s on unexpected stop (device change, etc.)
        bool shouldRestart;
        lock (_lock)
        {
            shouldRestart = _running && !_disposed;
            if (shouldRestart)
            {
                _running = false;
                // Dispose the old capture under lock; Start() will create a new one
                var old = _capture;
                _capture = null;
                try { old?.Dispose(); } catch { }
            }
        }

        if (shouldRestart)
        {
            Task.Delay(2000).ContinueWith(_ =>
            {
                if (!_disposed)
                    Start();
            });
        }
    }

    // --- FFT processing ---

    private void ProcessFft(int sampleRate)
    {
        // Build complex buffer with Hann window applied
        var complex = _fftBuffer;
        for (int i = 0; i < FftSize; i++)
        {
            complex[i].X = _sampleBuffer[i] * _hannWindow[i]; // real
            complex[i].Y = 0f;                         // imaginary
        }

        FastFourierTransform.FFT(true, FftLog2, complex);

        // Extract 5 band RMS values from FFT bins
        float binHz = (float)sampleRate / FftSize;
        int halfBins = FftSize / 2;

        for (int band = 0; band < 5; band++)
        {
            var (minHz, maxHz) = BandRanges[band];
            int binMin = Math.Max(1, (int)(minHz / binHz));
            int binMax = Math.Min(halfBins - 1, (int)(maxHz / binHz));

            if (binMin > binMax)
            {
                continue;
            }

            float sumSq = 0f;
            int count = 0;
            for (int bin = binMin; bin <= binMax; bin++)
            {
                sumSq += complex[bin].X * complex[bin].X + complex[bin].Y * complex[bin].Y;
                count++;
            }

            float rms = count > 0 ? MathF.Sqrt(sumSq / count) : 0f;
            float raw = Math.Clamp(rms * BroadBandScale / NormRef, 0f, 1f);

            // Apply attack/decay smoothing
            lock (_lock)
            {
                float current = SmoothedBands[band];
                SmoothedBands[band] = raw > current
                    ? current * 0.5f + raw * 0.5f    // attack: fast
                    : current * 0.88f + raw * 0.12f; // decay: slow
            }
        }

        // Build a finer logarithmic spectrum from the same FFT. Keeping this
        // work here avoids another capture/FFT pipeline for the room VU tuner.
        float logRange = MathF.Log(SpectrumMaxHz / SpectrumMinHz);
        for (int spectrumBin = 0; spectrumBin < SpectrumBinCount; spectrumBin++)
        {
            float lowHz = SpectrumMinHz * MathF.Exp(logRange * spectrumBin / SpectrumBinCount);
            float highHz = SpectrumMinHz * MathF.Exp(logRange * (spectrumBin + 1) / SpectrumBinCount);
            int binMin = Math.Max(1, (int)MathF.Floor(lowHz / binHz));
            int binMax = Math.Min(halfBins - 1, (int)MathF.Ceiling(highHz / binHz));
            if (binMin > binMax)
            {
                _spectrumScratch[spectrumBin] = 0f;
                continue;
            }

            float sumSq = 0f;
            int count = 0;
            for (int fftBin = binMin; fftBin <= binMax; fftBin++)
            {
                sumSq += complex[fftBin].X * complex[fftBin].X
                    + complex[fftBin].Y * complex[fftBin].Y;
                count++;
            }
            float rms = count > 0 ? MathF.Sqrt(sumSq / count) : 0f;
            _spectrumScratch[spectrumBin] = Math.Clamp(
                rms / NormRef, 0f, 1f);
        }

        lock (_lock)
        {
            for (int i = 0; i < SpectrumBinCount; i++)
            {
                float raw = _spectrumScratch[i];
                float current = _smoothedSpectrum[i];
                _smoothedSpectrum[i] = raw > current
                    ? current * 0.45f + raw * 0.55f
                    : current * 0.86f + raw * 0.14f;
            }
        }
    }

    /// <summary>
    /// Returns normalized energy in a tunable frequency window. A triangular
    /// weighting favors the selected center while the width controls how much
    /// neighboring spectrum contributes.
    /// </summary>
    public float GetFrequencyLevel(float centerHz, float widthHz)
    {
        centerHz = Math.Clamp(centerHz, SpectrumMinHz, SpectrumMaxHz);
        widthHz = Math.Clamp(widthHz, 10f, SpectrumMaxHz);
        float halfWidth = Math.Max(widthHz * 0.5f, 5f);
        float weighted = 0f;
        float weightTotal = 0f;
        float nearestDistance = float.MaxValue;
        float nearestValue = 0f;
        float logRange = MathF.Log(SpectrumMaxHz / SpectrumMinHz);

        lock (_lock)
        {
            for (int i = 0; i < SpectrumBinCount; i++)
            {
                float binCenter = SpectrumMinHz * MathF.Exp(logRange * (i + 0.5f) / SpectrumBinCount);
                float distance = MathF.Abs(binCenter - centerHz);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestValue = _smoothedSpectrum[i];
                }

                float weight = 1f - distance / halfWidth;
                if (weight <= 0f) continue;
                weighted += _smoothedSpectrum[i] * weight;
                weightTotal += weight;
            }
        }

        return weightTotal > 0f ? Math.Clamp(weighted / weightTotal, 0f, 1f) : nearestValue;
    }

    /// <summary>
    /// Copies a logarithmically spaced slice of the live FFT into the caller's
    /// buffer. Room visualizers use this instead of stretching the five broad
    /// bands across every light, so kick, vocals, hats, and harmonics can move
    /// independently across the room.
    /// </summary>
    public void CopySpectrum(Span<float> destination, float minHz = 40f, float maxHz = 16000f)
    {
        if (destination.IsEmpty) return;

        minHz = Math.Clamp(minHz, SpectrumMinHz, SpectrumMaxHz - 1f);
        maxHz = Math.Clamp(maxHz, minHz + 1f, SpectrumMaxHz);
        float sourceLogRange = MathF.Log(SpectrumMaxHz / SpectrumMinHz);
        float requestedLogRange = MathF.Log(maxHz / minHz);

        lock (_lock)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                float t = destination.Length == 1 ? 0.5f : i / (float)(destination.Length - 1);
                float hz = minHz * MathF.Exp(requestedLogRange * t);
                float sourcePosition = MathF.Log(hz / SpectrumMinHz) / sourceLogRange
                    * SpectrumBinCount - 0.5f;
                int low = Math.Clamp((int)MathF.Floor(sourcePosition), 0, SpectrumBinCount - 1);
                int high = Math.Min(low + 1, SpectrumBinCount - 1);
                float fraction = Math.Clamp(sourcePosition - low, 0f, 1f);
                destination[i] = _smoothedSpectrum[low] * (1f - fraction)
                    + _smoothedSpectrum[high] * fraction;
            }
        }
    }
}
