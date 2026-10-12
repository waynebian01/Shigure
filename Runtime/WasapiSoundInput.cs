using NAudio.Wave;

namespace Shigure;

internal sealed class WasapiSoundInput : IRuntimeSoundInput
{
    private readonly object _gate = new();
    private readonly Queue<SoundRecognitionEvent> _events = new();
    private readonly List<float> _samples = [];
    private readonly List<float[]> _frames = [];
    private readonly Dictionary<string, long> _lastMatched = new(StringComparer.Ordinal);
    private readonly HashSet<string> _latched = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _quietFrames = new(StringComparer.Ordinal);
    private List<(string Name, SoundFingerprint Fingerprint)> _templates = [];
    private WasapiLoopbackCapture? _capture;
    private string _configuration = "";
    private long _frameNumber;
    private long _eventId;
    private DateTimeOffset _lastDataAt;
    private DateTimeOffset _retryAfter;
    private volatile bool _needsRestart;
    public string? FailureReason { get; private set; }

    public void Configure(IReadOnlyList<string> names, bool enabled)
    {
        var configuration = enabled ? string.Join('\n', names.Order(StringComparer.Ordinal)) : "";
        if (configuration == _configuration && (!_needsRestart && _capture is not null || configuration.Length == 0)) return;
        if (configuration == _configuration && DateTimeOffset.UtcNow < _retryAfter) return;
        Stop();
        _configuration = configuration;
        FailureReason = null;
        if (configuration.Length == 0) return;
        try
        {
            var templates = new List<(string, SoundFingerprint)>();
            foreach (var name in names)
            {
                var files = SoundCatalog.GetFiles(name).ToArray();
                if (files.Length == 0) throw new InvalidOperationException($"找不到音效“{name}”，请重新导入。");
                templates.AddRange(files.Select(file => (name, SoundFingerprint.Create(SoundCatalog.ReadMono(file)))));
            }
            if (templates.Count == 0) throw new InvalidOperationException("找不到所选音效，请重新导入。");
            lock (_gate) _templates = templates;
            var capture = new WasapiLoopbackCapture
            {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SoundFingerprint.SampleRate, 1)
            };
            _capture = capture;
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();
        }
        catch (Exception ex)
        {
            Stop();
            FailureReason = $"声音识别不可用：{ex.Message}";
            _retryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null) return;
        lock (_gate)
        {
            FailureReason = $"音频捕获中断：{e.Exception.Message}";
            _needsRestart = true;
            _retryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
            _events.Clear();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            // 无声音时 loopback 不发事件，断流后不能拼接断流前后的频谱。
            if (now - _lastDataAt > TimeSpan.FromMilliseconds(250))
            {
                _samples.Clear();
                _frames.Clear();
                _lastMatched.Clear();
                _latched.Clear();
                _quietFrames.Clear();
            }
            _lastDataAt = now;
            for (var i = 0; i + 4 <= e.BytesRecorded; i += 4)
                _samples.Add(BitConverter.ToSingle(e.Buffer, i));
            var offset = 0;
            while (offset + SoundFingerprint.Window <= _samples.Count)
            {
                _frames.Add(SoundFingerprint.Analyze(System.Runtime.InteropServices.CollectionsMarshal
                    .AsSpan(_samples).Slice(offset, SoundFingerprint.Window)));
                _frameNumber++;
                offset += SoundFingerprint.Hop;
                foreach (var group in _templates.GroupBy(template => template.Name, StringComparer.Ordinal))
                {
                    var name = group.Key;
                    var score = group.Max(template => _frames.Count < template.Fingerprint.Frames.Length ? 0
                        : template.Fingerprint.Match(_frames, _frames.Count - template.Fingerprint.Frames.Length));
                    if (score < 0.85)
                    {
                        _quietFrames.TryGetValue(name, out var quiet);
                        _quietFrames[name] = quiet + 1;
                        if (quiet >= 19) _latched.Remove(name);
                    }
                    else _quietFrames[name] = 0;
                    if (score < 0.90 || _latched.Contains(name)) continue;
                    if (_lastMatched.TryGetValue(name, out var previous)
                        && _frameNumber - previous < group.Max(template => template.Fingerprint.Frames.Length)) continue;
                    _lastMatched[name] = _frameNumber;
                    _latched.Add(name);
                    if (_events.Count == 16) _events.Dequeue();
                    _events.Enqueue(new SoundRecognitionEvent(name, ++_eventId, now));
                }
                var maxFrames = _templates.Max(template => template.Fingerprint.Frames.Length);
                if (_frames.Count > maxFrames + 10) _frames.RemoveRange(0, _frames.Count - maxFrames);
            }
            if (offset > 0) _samples.RemoveRange(0, offset);
        }
    }

    public SoundRecognitionEvent? ReadEvent()
    {
        lock (_gate)
        {
            while (_events.TryDequeue(out var sound))
                if (DateTimeOffset.UtcNow - sound.DetectedAt < TimeSpan.FromSeconds(1)) return sound;
            return null;
        }
    }

    private void Stop()
    {
        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try
            {
                try { capture.StopRecording(); }
                finally { capture.Dispose(); }
            }
            catch (Exception ex)
            {
                FailureReason = $"音频设备关闭失败：{ex.Message}";
            }
        }
        _needsRestart = false;
        lock (_gate)
        {
            _events.Clear();
            _samples.Clear();
            _frames.Clear();
            _templates.Clear();
            _lastMatched.Clear();
            _latched.Clear();
            _quietFrames.Clear();
            _frameNumber = 0;
        }
    }

    public void Dispose() => Stop();
}
