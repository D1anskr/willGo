using NAudio.Wave;

namespace FloatingDeskAssistant.Application;

public sealed class HistoryAudioPlayer : IDisposable
{
    private readonly Func<IWavePlayer> _createOutput;
    private IWavePlayer? _output;
    private WaveFileReader? _reader;
    private float _volume = .7f;
    public HistoryAudioPlayer(Func<IWavePlayer>? createOutput = null) => _createOutput = createOutput ?? (() => new WaveOutEvent());
    public event Action<Exception>? Failed;
    public string CurrentPath { get; private set; } = "";
    public PlaybackState State => _output?.PlaybackState ?? PlaybackState.Stopped;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;
    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set { if (_reader is not null) _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds, 0, Duration.TotalSeconds)); }
    }
    public float Volume
    {
        get => _volume;
        set { _volume = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : .7f; if (_output is not null) _output.Volume = _volume; }
    }

    public void Play(string path)
    {
        if (_reader is null || !string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            Stop();
            try
            {
                _reader = new WaveFileReader(path);
                _output = _createOutput();
                _output.PlaybackStopped += OnStopped;
                _output.Init(_reader);
                _output.Volume = _volume;
                CurrentPath = path;
            }
            catch { Stop(); throw; }
        }
        if (Position >= Duration) Position = TimeSpan.Zero;
        _output!.Play();
    }

    public void Pause() => _output?.Pause();
    private void OnStopped(object? sender, StoppedEventArgs e) { if (e.Exception is not null) Failed?.Invoke(e.Exception); }
    public void Stop()
    {
        var output = _output;
        _output = null;
        try
        {
            if (output is not null) { output.PlaybackStopped -= OnStopped; output.Stop(); }
        }
        finally
        {
            try { output?.Dispose(); }
            finally { _reader?.Dispose(); _reader = null; CurrentPath = ""; }
        }
    }
    public void Dispose() => Stop();
}
