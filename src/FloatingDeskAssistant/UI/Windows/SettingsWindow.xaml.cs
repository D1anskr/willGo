using System;
using System.Windows;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.ViewModels;
using FormsDialogResult = System.Windows.Forms.DialogResult;

namespace FloatingDeskAssistant.UI.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(AppConfig currentConfig, AppConfig defaultConfig)
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(currentConfig, defaultConfig);
        DataContext = _viewModel;
    }

    public AppConfig? UpdatedConfig { get; private set; }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        UpdatedConfig = _viewModel.BuildUpdatedConfig();
        Normalize(UpdatedConfig);
        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DefaultsButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.RestoreDefaults();
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
