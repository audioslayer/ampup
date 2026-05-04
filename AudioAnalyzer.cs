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

    private WasapiLoopbackCapture? _capture;
    private readonly object _lock = new();
    private readonly float[] _sampleBuffer = new float[1024];
    private readonly Complex[] _fftBuffer = new Complex[FftSize];
    private readonly float[] _hannWindow = BuildHannWindow();
    private int _bufferPos;
    private int _analysisHop;
    private bool _running;
    private bool _disposed;

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
    private const int FftSize = 1024;
    private const int FftLog2 = 10; // log2(1024)
    private const int AnalysisHopBuffers = 2; // ~23Hz at 48kHz, enough for 20 FPS LEDs

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
                _capture = capture;
                _running = true;
            }
            capture.StartRecording();
            Logger.Log("AudioAnalyzer started");
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

        // Capture a local reference under lock — Stop() can null _capture between the
        // _running check above and the dereference below (TOCTOU race).
        WasapiLoopbackCapture? capture;
        lock (_lock) { capture = _capture; }
        if (capture == null) return;

        var format = capture.WaveFormat;
        int channels = format.Channels;
        int bytesPerSample = format.BitsPerSample / 8;

        // Feed mono-mixed float samples into the accumulator buffer
        for (int offset = 0; offset + bytesPerSample * channels <= e.BytesRecorded; offset += bytesPerSample * channels)
        {
            float mono = 0f;

            if (format.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
            {
                // 32-bit float samples — mix down to mono
                for (int ch = 0; ch < channels; ch++)
                    mono += BitConverter.ToSingle(e.Buffer, offset + ch * 4);
                mono /= channels;
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 2)
            {
                // 16-bit PCM — mix down to mono, normalize to -1..1
                for (int ch = 0; ch < channels; ch++)
                    mono += BitConverter.ToInt16(e.Buffer, offset + ch * 2) / 32768f;
                mono /= channels;
            }
            else
            {
                // Unsupported format — skip
                break;
            }

            _sampleBuffer[_bufferPos++] = mono;

            if (_bufferPos >= FftSize)
            {
                _analysisHop++;
                if (_analysisHop >= AnalysisHopBuffers)
                {
                    _analysisHop = 0;
                    ProcessFft(format.SampleRate);
                }
                _bufferPos = 0;
            }
        }
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
                float mag = MathF.Sqrt(complex[bin].X * complex[bin].X + complex[bin].Y * complex[bin].Y);
                sumSq += mag * mag;
                count++;
            }

            float rms = count > 0 ? MathF.Sqrt(sumSq / count) : 0f;
            float raw = Math.Clamp(rms / NormRef, 0f, 1f);

            // Apply attack/decay smoothing
            lock (_lock)
            {
                float current = SmoothedBands[band];
                SmoothedBands[band] = raw > current
                    ? current * 0.5f + raw * 0.5f    // attack: fast
                    : current * 0.88f + raw * 0.12f; // decay: slow
            }
        }
    }
}
