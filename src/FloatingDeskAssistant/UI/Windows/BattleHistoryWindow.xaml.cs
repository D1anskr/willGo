using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.ViewModels;
using NAudio.Wave;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace FloatingDeskAssistant.UI.Windows;

public partial class BattleHistoryWindow : Window
{
    private readonly BattleHistoryViewModel _viewModel;
    private readonly HistoryAudioPlayer _player;
    private readonly DispatcherTimer _timer;
    private string _configuredDirectory;
    private bool _updatingPosition, _closed;

    public BattleHistoryWindow(Func<string> getDirectory, AppConfig config, bool voiceActive = false, HistoryAudioPlayer? player = null)
    {
        _player = player ?? new HistoryAudioPlayer();
        InitializeComponent();
        WindowCaptureProtection.Attach(this);
        _viewModel = new BattleHistoryViewModel(getDirectory) { CanListen = !voiceActive };
        DataContext = _viewModel;
        _configuredDirectory = config.Battle.SpeechArchiveDirectory;
        WindowCaptureProtection.SetOpacity(this, config.WindowOpacity);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => UpdatePlayback();
        _player.Failed += OnPlaybackFailed;
        Loaded += async (_, _) =>
        {
            Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
            _timer.Start();
            await _viewModel.RefreshAsync();
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopPlayback(); };
        Closed += (_, _) =>
        {
            _closed = true; _timer.Stop(); _viewModel.Dispose(); _player.Failed -= OnPlaybackFailed; _player.Dispose();
        };
    }

    public Task RefreshAsync() => _viewModel.RefreshAsync();
    public void UpdateConfig(AppConfig config)
    {
        WindowCaptureProtection.SetOpacity(this, config.WindowOpacity);
        if (_configuredDirectory == config.Battle.SpeechArchiveDirectory) return;
        _configuredDirectory = config.Battle.SpeechArchiveDirectory;
        StopPlayback();
        _ = RefreshAsync();
    }
    public void SetVoiceActive(bool active)
    {
        _viewModel.CanListen = !active;
        if (active && _player.CurrentPath.Length > 0)
        {
            StopPlayback(); _viewModel.Status = "已停止历史播放，避免被当前语音识别重复采集。";
        }
        if (!active) _ = RefreshAsync();
    }

    private void Play(BattleHistoryEntry entry)
    {
        if (!_viewModel.CanListen) { _viewModel.Status = "请先结束当前战斗，再播放历史录音。"; return; }
        if (!entry.CanPlayAudio) { _viewModel.Status = "此记录没有可播放的录音，或文件仍在写入。"; return; }
        try
        {
            HistoryList.SelectedItem = entry;
            _player.Play(entry.AudioPath);
            PlayingName.Text = entry.Name; PlayingName.ToolTip = entry.AudioPath;
            UpdatePlayback();
        }
        catch (Exception ex) { StopPlayback(); _viewModel.Status = "无法播放录音：" + ex.Message; }
    }
    private void PlayRecord_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BattleHistoryEntry entry }) Play(entry);
    }
    private void Playback_OnClick(object sender, RoutedEventArgs e)
    {
        if (_player.State == PlaybackState.Playing) { _player.Pause(); UpdatePlayback(); }
        else if (HistoryList.SelectedItem is BattleHistoryEntry entry) Play(entry);
        else _viewModel.Status = "请先选择一条含录音的历史记录。";
    }
    private void Stop_OnClick(object sender, RoutedEventArgs e) => StopPlayback();
    public void StopPlayback() { _player.Stop(); if (IsInitialized) UpdatePlayback(); }
    private static string TimeLabel(TimeSpan time) => time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");
    private void UpdatePlayback()
    {
        _updatingPosition = true;
        try
        {
            PlaybackPosition.Maximum = Math.Max(.001, _player.Duration.TotalSeconds);
            PlaybackPosition.Value = _player.Position.TotalSeconds;
            PlaybackPosition.IsEnabled = _player.CurrentPath.Length > 0;
            PositionLabel.Text = TimeLabel(_player.Position); DurationLabel.Text = TimeLabel(_player.Duration);
            PlaybackButton.Content = _player.State == PlaybackState.Playing ? "\uE769" : "\uE768";
        }
        finally { _updatingPosition = false; }
    }
    private void Position_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingPosition && _player is not null) _player.Position = TimeSpan.FromSeconds(e.NewValue);
    }
    private void Volume_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player is not null) _player.Volume = (float)e.NewValue;
    }
    private void OnPlaybackFailed(Exception error) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_closed) return;
        StopPlayback(); _viewModel.Status = "录音播放中断：" + error.Message;
    }));
    private void OpenDocument_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BattleHistoryEntry entry } || !entry.HasTranscript) return;
        try
        {
            if (!File.Exists(entry.TranscriptPath)) throw new FileNotFoundException("文档已移动或删除，请刷新列表。");
            try { Process.Start(new ProcessStartInfo(entry.TranscriptPath) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1155)
            {
                var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
                start.ArgumentList.Add(entry.TranscriptPath); Process.Start(start);
            }
        }
        catch (Exception ex) { _viewModel.Status = "无法打开文档：" + ex.Message; }
    }
    private void OpenDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(_viewModel.DirectoryPath)) { _viewModel.Status = "保存目录尚不存在，请先开始一次战斗记录。"; return; }
            Process.Start(new ProcessStartInfo(_viewModel.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex) { _viewModel.Status = "无法打开目录：" + ex.Message; }
    }
    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        for (var current = e.OriginalSource as DependencyObject; current is not null && current != sender;)
        {
            if (current is ButtonBase) return;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        DragMove();
    }
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
