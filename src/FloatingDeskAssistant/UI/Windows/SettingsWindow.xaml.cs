using System;
using System.IO;
using System.Diagnostics;
using FloatingDeskAssistant.Application;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.ViewModels;
using FormsDialogResult = System.Windows.Forms.DialogResult;

namespace FloatingDeskAssistant.UI.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private CancellationTokenSource? _speechTest;
    private readonly DualMachineService? _dualMachine;
    private bool _peerAction;

    public SettingsWindow(AppConfig currentConfig, AppConfig defaultConfig, DualMachineService? dualMachine = null)
    {
        InitializeComponent();
        FloatingDeskAssistant.Application.WindowCaptureProtection.Attach(this);
        _viewModel = new SettingsViewModel(currentConfig, defaultConfig);
        _dualMachine = dualMachine;
        DataContext = _viewModel;
        RefreshSpeechCredentials();
        RefreshDualMachineStatus();
        if (currentConfig.DualMachine.IsData) SettingsTabs.SelectedItem = DualMachineTab;
        if (_dualMachine is not null) _dualMachine.StateChanged += OnDualMachineStateChanged;
        Loaded += (_, _) =>
        {
            Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
        };
        Closed += (_, _) =>
        {
            _speechTest?.Cancel();
            if (_dualMachine is not null) _dualMachine.StateChanged -= OnDualMachineStateChanged;
        };
    }

    public AppConfig? UpdatedConfig { get; private set; }

    public void UpdateOpacity(double ballOpacity, double windowOpacity)
    {
        _viewModel.UpdateOpacity(ballOpacity, windowOpacity);
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        UpdatedConfig = _viewModel.BuildUpdatedConfig();
        if (UpdatedConfig.DualMachine.Enabled)
        {
            try
            {
                if (!int.TryParse(DualMachinePortBox.Text, out var port) || port is < 1024 or > 65535)
                    throw new ArgumentException("监听端口必须在 1024 到 65535 之间。");
                UpdatedConfig.DualMachine.Port = port;
                if (UpdatedConfig.DualMachine.IsDisplay) DualMachineOffer.Parse(UpdatedConfig.DualMachine.PeerOffer);
            }
            catch (Exception ex)
            {
                SettingsTabs.SelectedItem = DualMachineTab;
                System.Windows.MessageBox.Show(this, ex.Message, "双机设置", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        if (string.IsNullOrWhiteSpace(UpdatedConfig.Battle.SpeechArchiveDirectory) ||
            !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(UpdatedConfig.Battle.SpeechArchiveDirectory.Trim())))
        {
            SettingsTabs.SelectedItem = SpeechTab;
            System.Windows.MessageBox.Show(this, "请选择录音记录的完整保存目录。", "保存目录", MessageBoxButton.OK, MessageBoxImage.Information);
            SpeechArchivePathBox.Focus();
            return;
        }
        Normalize(UpdatedConfig);
        try { UpdatedConfig.Battle.Speech.Validate(requireCredentials: false); }
        catch (Exception ex)
        {
            SettingsTabs.SelectedItem = SpeechTab;
            System.Windows.MessageBox.Show(this, ex.Message, "豆包语音设置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        for (var current = e.OriginalSource as DependencyObject; current is not null && current != sender;)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase) return;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        DragMove();
    }

    private void DefaultsButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.RestoreDefaults();
        RefreshSpeechCredentials();
    }

    private void OnDualMachineStateChanged() => Dispatcher.BeginInvoke(RefreshDualMachineStatus);

    private void RefreshDualMachineStatus()
    {
        if (_dualMachine is null) return;
        DualMachineStatusText.Text = _dualMachine.Status;
        var selected = PairingOffersBox.SelectedItem as string;
        PairingOffersBox.ItemsSource = _dualMachine.PairingOffers;
        PairingOffersBox.SelectedItem = _dualMachine.PairingOffers.Contains(selected) ? selected : _dualMachine.PairingOffers.FirstOrDefault();
    }

    private void CopyPairingOffer_OnClick(object sender, RoutedEventArgs e)
    {
        if (PairingOffersBox.SelectedItem is string offer) System.Windows.Clipboard.SetText(offer);
        else DualMachineStatusText.Text = "请先开启双机模式，选择数据端并保存。";
    }

    private async void ReconnectPeer_OnClick(object sender, RoutedEventArgs e) => await RunPeerActionAsync(false);
    private async void ResetPairing_OnClick(object sender, RoutedEventArgs e) => await RunPeerActionAsync(true);
    private async Task RunPeerActionAsync(bool reset)
    {
        if (_peerAction || _dualMachine is null) return;
        _peerAction = true;
        ReconnectPeerButton.IsEnabled = false;
        try
        {
            if (reset) await _dualMachine.ResetPairingAsync();
            else await _dualMachine.ReconnectAsync();
            RefreshDualMachineStatus();
        }
        catch (Exception ex) { DualMachineStatusText.Text = "连接失败：" + ex.Message; }
        finally { _peerAction = false; ReconnectPeerButton.IsEnabled = true; }
    }

    private void RefreshSpeechCredentials()
    {
        SpeechApiKeyBox.Password = _viewModel.EditableConfig.Battle.Speech.ApiKey;
        SpeechAccessTokenBox.Password = _viewModel.EditableConfig.Battle.Speech.AccessToken;
    }

    private void SpeechApiKey_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.EditableConfig.Battle.Speech.ApiKey = SpeechApiKeyBox.Password;
    }

    private void SpeechAccessToken_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.EditableConfig.Battle.Speech.AccessToken = SpeechAccessTokenBox.Password;
    }

    private static void OpenSpeechPage(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void OpenSpeechConsole_OnClick(object sender, RoutedEventArgs e) => OpenSpeechPage("https://console.volcengine.com/speech/");
    private void OpenSpeechKeys_OnClick(object sender, RoutedEventArgs e) => OpenSpeechPage("https://console.volcengine.com/speech/new/setting/apikeys?projectName=default");

    private async void TestSpeechConnection_OnClick(object sender, RoutedEventArgs e)
    {
        if (_speechTest is not null) return;
        _speechTest = new();
        TestSpeechConnectionButton.IsEnabled = false;
        SpeechConnectionStatus.Text = "正在连接豆包语音…";
        try
        {
            var options = _viewModel.BuildUpdatedConfig().Battle;
            string? failure = null;
            await using var client = new DoubaoSpeechClient(options, SpeechSource.Microphone, _ => { }, message => Interlocked.CompareExchange(ref failure, message, null));
            await client.StartAsync(_speechTest.Token);
            await client.SendAudioAsync(new byte[6400], _speechTest.Token);
            await client.CompleteAsync();
            SpeechConnectionStatus.Text = failure ?? "连接成功 · 豆包流式语音识别模型 2.0";
        }
        catch (OperationCanceledException) { SpeechConnectionStatus.Text = "已取消连接测试"; }
        catch (Exception ex) { SpeechConnectionStatus.Text = ex.Message; }
        finally
        {
            _speechTest.Dispose(); _speechTest = null;
            TestSpeechConnectionButton.IsEnabled = true;
        }
    }

    private void ChooseSpeechArchiveDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "录音与文字记录保存目录" };
        var current = Environment.ExpandEnvironmentVariables(SpeechArchivePathBox.Text.Trim());
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) == true)
        {
            SpeechArchivePathBox.Text = dialog.FolderName;
            SpeechArchivePathBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        }
    }

    private void ChooseRemoteRegionButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var overlay = new ScreenshotOverlayForm();
        var dialogResult = overlay.ShowDialog();
        if (dialogResult == FormsDialogResult.OK && overlay.SelectedRectangle.Width >= 6 && overlay.SelectedRectangle.Height >= 6)
        {
            _viewModel.SetRemoteCaptureRegion(overlay.SelectedRectangle);
        }

        Activate();
        Focus();
    }

    private void ClearRemoteRegionButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearRemoteCaptureRegion();
    }

    private void AddPromptPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.AddPromptPreset();
    }

    private void MovePromptPresetUpButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetPromptPresetEditor(sender, out var preset))
        {
            _viewModel.MovePromptPreset(preset, -1);
        }
    }

    private void MovePromptPresetDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetPromptPresetEditor(sender, out var preset))
        {
            _viewModel.MovePromptPreset(preset, 1);
        }
    }

    private void DeletePromptPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetPromptPresetEditor(sender, out var preset))
        {
            _viewModel.RemovePromptPreset(preset);
        }
    }

    private static void Normalize(AppConfig config)
    {
        config.BallOpacity = Math.Clamp(config.BallOpacity, 0.05, 1.0);
        config.WindowOpacity = Math.Clamp(config.WindowOpacity, 0.3, 1.0);
        config.CompactModeLevel = Enum.IsDefined(typeof(CompactModeLevel), config.CompactModeLevel)
            ? config.CompactModeLevel
            : CompactModeLevel.Mode1;
        config.RemoteCaptureMode = Enum.IsDefined(typeof(RemoteCaptureMode), config.RemoteCaptureMode)
            ? config.RemoteCaptureMode
            : RemoteCaptureMode.FullDesktop;
        config.RemoteCaptureRegionWidth = Math.Max(0, config.RemoteCaptureRegionWidth);
        config.RemoteCaptureRegionHeight = Math.Max(0, config.RemoteCaptureRegionHeight);
        if (config.RemoteCaptureRegionWidth < 6 || config.RemoteCaptureRegionHeight < 6)
        {
            config.RemoteCaptureRegionX = 0;
            config.RemoteCaptureRegionY = 0;
            config.RemoteCaptureRegionWidth = 0;
            config.RemoteCaptureRegionHeight = 0;
        }

        config.Primary.TimeoutSeconds = Math.Max(1, config.Primary.TimeoutSeconds);
        config.Primary.MaxRetries = Math.Max(1, config.Primary.MaxRetries);
        config.Secondary.TimeoutSeconds = Math.Max(1, config.Secondary.TimeoutSeconds);
        config.Secondary.MaxRetries = Math.Max(1, config.Secondary.MaxRetries);

        NormalizeEndpoint(config.Primary);
        NormalizeEndpoint(config.Secondary);
        foreach (var endpoint in new[] { config.ConversationModels.Primary, config.ConversationModels.Secondary })
        {
            endpoint.TimeoutSeconds = Math.Max(1, endpoint.TimeoutSeconds);
            endpoint.MaxRetries = Math.Max(1, endpoint.MaxRetries);
            NormalizeEndpoint(endpoint);
        }
    }

    private static bool TryGetPromptPresetEditor(object sender, out PromptPresetEditorViewModel preset)
    {
        preset = null!;
        return sender is FrameworkElement element
            && element.DataContext is PromptPresetEditorViewModel viewModel
            && (preset = viewModel) is not null;
    }

    private static void NormalizeEndpoint(ModelEndpointConfig endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint.BaseUrl))
        {
            endpoint.BaseUrl = endpoint.BaseUrl.Trim();
            return;
        }

        endpoint.BaseUrl = endpoint.Protocol == ModelApiProtocol.AnthropicMessages
            ? "https://api.anthropic.com/v1"
            : "https://api.openai.com/v1";
    }
}
