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
    private int _bufferPos;
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

    private const float NormRef = 0.1f; // reference amplitude for normalization
    private const int FftSize = 1024;
    private const int FftLog2 = 10; // log2(1024)

    public void Start()
    {
        if (_running || _disposed) return;

        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _bufferPos = 0;
            _running = true;
            _capture.StartRecording();
            Logger.Log("AudioAnalyzer started");
        }
        catch (Exception ex)
        {
            Logger.Log($"AudioAnalyzer Start failed: {ex.Message}");
            _capture?.Dispose();
            _capture = null;
            _running = false;
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        try
        {
            _capture?.StopRecording();
        }
        catch { }

        // Zero out bands
        lock (_lock)
        {
            for (int i = 0; i < 5; i++)
                SmoothedBands[i] = 0f;
        }

        Logger.Log("AudioAnalyzer stopped");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _capture?.Dispose();
        _capture = null;
    }

    // --- NAudio callbacks ---

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running || e.BytesRecorded == 0) return;

        var format = _capture!.WaveFormat;
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
                ProcessFft(format.SampleRate);
                _bufferPos = 0;
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            Logger.Log($"AudioAnalyzer recording stopped with error: {e.Exception.Message}");

        // Auto-restart after 2s on unexpected stop (device change, etc.)
        if (_running && !_disposed)
        {
            _running = false;
            _capture?.Dispose();
            _capture = null;

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
        var complex = new Complex[FftSize];
        for (int i = 0; i < FftSize; i++)
        {
            float window = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
            complex[i].X = _sampleBuffer[i] * window; // real
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
