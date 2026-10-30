using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using NAudio.Wave;
using Button = System.Windows.Controls.Button;

internal static class BattleHistoryTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "history-tests", Guid.NewGuid().ToString("N"), "历史 记录");
        Directory.CreateDirectory(root);
        var complete = "willGo-20261019123000";
        CreateWave(Path.Combine(root, complete + ".WAV"));
        await File.WriteAllTextAsync(Path.Combine(root, complete + ".md"), "# 会话记录\n\n### 12:30:01 我：\n\n我想一想什么是 Kafka？");
        await File.WriteAllTextAsync(Path.Combine(root, "willGo-20261019122000.md"), "仅文字");
        CreateWave(Path.Combine(root, "willGo-20261019121000.wav"));
        await File.WriteAllTextAsync(Path.Combine(root, "willGo-20261018-101530-125.md"), "旧格式");
        await File.WriteAllTextAsync(Path.Combine(root, "复盘记录.md"), "自定义名称");
        File.SetLastWriteTime(Path.Combine(root, "复盘记录.md"), new DateTime(2026, 10, 17, 10, 0, 0));
        await File.WriteAllTextAsync(Path.Combine(root, complete + "-2.md"), "同秒记录");
        await File.WriteAllTextAsync(Path.Combine(root, "unrelated.txt"), "ignore");
        Directory.CreateDirectory(Path.Combine(root, "子目录"));
        await File.WriteAllTextAsync(Path.Combine(root, "子目录", "not-a-session.md"), "ignore");
        var entries = BattleHistory.Read(root);
        check(entries.Count == 6 && entries.Select(entry => entry.StartedAt).SequenceEqual(entries.Select(entry => entry.StartedAt).OrderDescending()),
            "history groups file pairs into newest-first sessions and excludes unrelated files and subdirectories");
        var paired = entries.Single(entry => entry.Name == complete);
        check(paired.HasAudio && paired.HasTranscript && paired.CanPlayAudio && paired.TimeLabel == "2026-10-19 12:30:00",
            "history pairs audio and transcript case-insensitively using the original session start timestamp");
        check(entries.Single(entry => entry.Name.EndsWith("122000")).HasAudio == false && entries.Single(entry => entry.Name.EndsWith("121000")).HasTranscript == false,
            "text-only and orphaned audio sessions both remain visible");
        check(entries.Single(entry => entry.Name.Contains("20261018-")).StartedAt == new DateTime(2026, 10, 18, 10, 15, 30, 125) &&
            entries.Single(entry => entry.Name == "复盘记录").StartedAt == new DateTime(2026, 10, 17, 10, 0, 0),
            "history supports legacy timestamp filenames and modification-time fallback for custom names");
        check(BattleHistory.Read(Path.Combine(root, "missing")).Count == 0, "missing archive directories have an empty history");
        using (var writer = new FileStream(paired.AudioPath, FileMode.Open, FileAccess.Write, FileShare.Read))
            check(!BattleHistory.Read(root).Single(entry => entry.Name == complete).CanPlayAudio, "in-progress recording files cannot be played before the writer closes");
        using (var writer = new FileStream(paired.TranscriptPath, FileMode.Open, FileAccess.Write, FileShare.Read))
            check(BattleHistory.Read(root).Single(entry => entry.Name == complete).TranscriptBusy, "history distinguishes a live transcript from a completed document");

        var outputs = new List<FakeOutput>();
        using var player = new HistoryAudioPlayer(() => { var output = new FakeOutput(); outputs.Add(output); return output; });
        var original = await File.ReadAllBytesAsync(paired.AudioPath);
        player.Play(paired.AudioPath);
        check(player.State == PlaybackState.Playing && player.Duration == TimeSpan.FromSeconds(2) && outputs.Count == 1,
            "history player reads the real WAV format and starts output with the correct duration");
        player.Position = TimeSpan.FromSeconds(1.25); player.Pause();
        check(player.State == PlaybackState.Paused && Math.Abs(player.Position.TotalSeconds - 1.25) < .01, "history playback supports seeking and pause");
        player.Play(paired.AudioPath);
        check(outputs.Count == 1 && player.Position.TotalSeconds > 1 && player.State == PlaybackState.Playing,
            "resume retains playback position without reopening the file or device");
        player.Volume = .25f;
        check(outputs[0].Volume == .25f, "history volume reaches the output device");
        check(BattleHistory.Read(root).Single(entry => entry.Name == complete).CanPlayAudio, "an existing playback reader is not mistaken for an active recording writer");
        player.Position = TimeSpan.FromMinutes(10);
        check(player.Position <= player.Duration, "seeking is clamped to the WAV duration");
        player.Play(paired.AudioPath);
        check(player.Position == TimeSpan.Zero, "replaying a completed track restarts at the beginning");
        player.Play(entries.Single(entry => entry.Name.EndsWith("121000")).AudioPath);
        check(outputs.Count == 2 && outputs[0].Disposed, "switching tracks releases the previous output and reader");
        player.Stop();
        using (var exclusive = new FileStream(paired.AudioPath, FileMode.Open, FileAccess.Read, FileShare.None))
            check(player.State == PlaybackState.Stopped && player.CurrentPath.Length == 0 && outputs[1].Disposed, "stop releases all playback handles and resets the transport");
        check(original.SequenceEqual(await File.ReadAllBytesAsync(paired.AudioPath)), "history browsing and playback leave archived audio unchanged");
        using (var brokenPlayer = new HistoryAudioPlayer(() => new FakeOutput { FailInit = true }))
        {
            try { brokenPlayer.Play(paired.AudioPath); } catch (InvalidOperationException) { }
            using var exclusive = new FileStream(paired.AudioPath, FileMode.Open, FileAccess.Read, FileShare.None);
            check(brokenPlayer.CurrentPath.Length == 0, "output initialization failures release the WAV reader");
        }

        var directory = root;
        using var historyVm = new BattleHistoryViewModel(() => directory);
        await historyVm.RefreshAsync();
        check(historyVm.Entries.Count == 6 && !historyVm.IsLoading && !historyVm.IsEmpty, "history view model refresh loads a complete directory snapshot");
        directory = Path.Combine(root, "missing"); await historyVm.RefreshAsync();
        check(historyVm.IsEmpty && historyVm.Status.Contains("尚不存在"), "missing directory refresh clears stale rows and reports an empty state");
        directory = "relative-path"; await historyVm.RefreshAsync();
        check(historyVm.IsEmpty && historyVm.Status.Contains("完整"), "invalid archive paths report an actionable settings message");

        var config = AppConfig.CreateDefault(); config.WindowOpacity = 1; config.Battle.SpeechArchiveDirectory = root;
        var window = new BattleHistoryWindow(() => root, config, player: player);
        window.Show(); await window.RefreshAsync(); await Task.Delay(100);
        var vm = (BattleHistoryViewModel)window.DataContext;
        var grid = (DataGrid)window.FindName("HistoryList");
        check(grid.Items.Count == 6 && !grid.CanUserSortColumns && grid.ActualWidth > 650, "history dashboard displays newest-first records with stable readable columns");
        var play = Visuals<Button>(window).Single(button => button.DataContext is BattleHistoryEntry entry && entry.Name == complete && button.ToolTip?.ToString() == "播放录音");
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(player.State == PlaybackState.Playing && ((TextBlock)window.FindName("PlayingName")).Text == complete,
            "history row play button starts in-dashboard audio and shows the selected filename");
        ((Button)window.FindName("PlaybackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(player.State == PlaybackState.Paused, "dashboard transport button pauses the current recording");
        await window.RefreshAsync();
        check(grid.SelectedItem is BattleHistoryEntry selected && selected.Name == complete,
            "refresh retains the selected recording while playback is paused");
        ((Button)window.FindName("PlaybackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(player.State == PlaybackState.Playing, "paused recording resumes after refreshing the history list");
        ((Slider)window.FindName("PlaybackPosition")).Value = .8;
        ((Slider)window.FindName("PlaybackVolume")).Value = .4;
        check(Math.Abs(player.Position.TotalSeconds - .8) < .01 && Math.Abs(player.Volume - .4f) < .001, "dashboard progress and volume sliders control the audio player");
        window.SetVoiceActive(true); await Task.Delay(30);
        check(player.State == PlaybackState.Stopped && !((Button)window.FindName("PlaybackButton")).IsEnabled && !vm.CanListen, "starting live capture stops history output and disables playback to prevent transcription feedback");
        window.SetVoiceActive(false); await window.RefreshAsync(); await Task.Delay(50);
        grid.SelectedItem = vm.Entries.Single(entry => entry.Name == complete);
        ((Button)window.FindName("PlaybackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Hide();
        check(player.State == PlaybackState.Stopped, "hiding the history dashboard stops background audio");
        window.Show(); await window.RefreshAsync(); await Task.Delay(60);
        SaveImage(window, "battle-history.png");
        window.Width = 820; window.Height = 440; await Task.Delay(80);
        var progress = (Slider)window.FindName("PlaybackPosition");
        var bounds = progress.TransformToAncestor(window).TransformBounds(new Rect(progress.RenderSize));
        check(progress.ActualWidth > 100 && bounds.Bottom <= window.ActualHeight && grid.ActualHeight > 80,
            "minimum-size history keeps its list and playback controls accessible");
        SaveImage(window, "battle-history-minimum.png");
        var document = Visuals<Button>(window).First(button => button.DataContext is BattleHistoryEntry entry && entry.Name == complete && button.ToolTip?.ToString() == "打开文字文档");
        File.Delete(paired.TranscriptPath);
        document.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(vm.Status.Contains("已移动或删除"), "opening a document removed after scanning reports an error without launching another application");
        await window.RefreshAsync();
        check(!vm.Entries.Single(entry => entry.Name == complete).HasTranscript, "refresh updates file availability after a transcript is deleted externally");
        window.Close();
        using (var exclusive = new FileStream(paired.AudioPath, FileMode.Open, FileAccess.Read, FileShare.None))
            check(player.CurrentPath.Length == 0, "closing history releases playback resources");
    }

    private static void CreateWave(string path)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 2));
        var silence = new byte[16000 * 4 * 2]; writer.Write(silence, 0, silence.Length);
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static void SaveImage(Window window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(file);
    }
    private sealed class FakeOutput : IWavePlayer
    {
        public bool Disposed, FailInit;
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; }
        public WaveFormat OutputWaveFormat { get; private set; } = new(16000, 16, 2);
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider provider) { if (FailInit) throw new InvalidOperationException("test output unavailable"); OutputWaveFormat = provider.WaveFormat; }
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new StoppedEventArgs()); }
        public void Dispose() => Disposed = true;
    }
}
