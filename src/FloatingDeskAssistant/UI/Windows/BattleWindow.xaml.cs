using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.ViewModels;
using FloatingDeskAssistant.UI.Themes;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace FloatingDeskAssistant.UI.Windows;

public partial class BattleWindow : Window
{
    private readonly BattleViewModel _viewModel;
    private readonly FusionDisplayController _fusionDisplay;
    private FusionStyle? _fusionStyle;
    private bool _standardTranscriptExpanded = true;
    private double _preferredQuestionPaneWidth = 210;
    private bool _splitterDragging;
    private bool _isComposing;
    private double _screenshotQuestionRatio = .38;
    private bool _screenshotSplitterDragging;
    public event Action? SettingsRequested;
    public event Action? FusionExitRequested;
    public event Action? PhoneRequested;
    public event Action? HistoryRequested;
    public event Action<double>? AnswerFontSizeChanged;
    public event Action<double>? QuestionPaneWidthChanged;
    public event Action<double>? ScreenshotQuestionRatioChanged;

    public BattleWindow(BattleViewModel viewModel, AppConfig config)
    {
        InitializeComponent();
        WindowCaptureProtection.Attach(this);
        _viewModel = viewModel;
        _fusionDisplay = new FusionDisplayController(this);
        DataContext = viewModel;
        UpdateConfig(config);
        _viewModel.PropertyChanged += OnViewModelChanged;
        WindowInteraction.Changed += OnInteractionChanged;
        OnInteractionChanged(WindowInteraction.IsClickThrough);
        Loaded += (_, _) =>
        {
            Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
            ApplyQuestionPaneWidth();
        };
        QuestionList.SizeChanged += (_, _) => KeepSelectedTitleVisible();
        WorkspaceGrid.SizeChanged += (_, _) => ApplyQuestionPaneWidth();
        AnswerBodyGrid.SizeChanged += (_, _) => ApplyScreenshotLayout();
        TextCompositionManager.AddPreviewTextInputStartHandler(ManualInput, (_, _) => _isComposing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(ManualInput, (_, _) => _isComposing = true);
        TextCompositionManager.AddPreviewTextInputHandler(ManualInput, (_, _) => _isComposing = false);
        ManualInput.LostKeyboardFocus += (_, _) => _isComposing = false;
        SizeChanged += (_, _) =>
        {
            var compact = ActualHeight < 600;
            QuestionText.MaxHeight = compact ? 64 : 100;
            TranscriptBox.Height = compact ? 44 : 72;
        };
        Closed += (_, _) =>
        {
            _fusionDisplay.Dispose();
            _viewModel.PropertyChanged -= OnViewModelChanged;
            WindowInteraction.Changed -= OnInteractionChanged;
        };
    }

    public void UpdateConfig(AppConfig config)
    {
        var battle = config.Battle.Clone();
        var dual = config.DualMachine.Clone();
        var style = dual.IsFusionDisplay ? dual.FusionStyle : (FusionStyle?)null;
        if (_fusionStyle != style)
        {
            if (_fusionStyle is null && style is not null)
            {
                WindowInteraction.SetClickThrough(false);
                _standardTranscriptExpanded = TranscriptExpander.IsExpanded;
                TranscriptExpander.IsExpanded = false;
            }
            else if (style is null) TranscriptExpander.IsExpanded = _standardTranscriptExpanded;
            _fusionStyle = style;
            if (style is { } palette) FusionColors.For(palette).Apply(this);
            else FusionColors.Clear(this);
            AnswerContent.Palette = ScreenshotQuestionContent.Palette = style;
        }
        FusionExitButton.Visibility = dual.IsFusionDisplay ? Visibility.Visible : Visibility.Collapsed;
        InteractionButton.Visibility = dual.IsFusionDisplay ? Visibility.Collapsed : Visibility.Visible;
        QuestionList.FontSize = dual.IsFusionDisplay ? 15 : 13;
        SetAnswerFontSize(dual.IsFusionDisplay ? dual.FusionAnswerFontSize : battle.AnswerFontSize);
        if (!_splitterDragging)
        {
            _preferredQuestionPaneWidth = battle.QuestionPaneWidth;
            ApplyQuestionPaneWidth();
        }
        if (!_screenshotSplitterDragging) { _screenshotQuestionRatio = battle.ScreenshotQuestionRatio; ApplyScreenshotLayout(); }
        WindowCaptureProtection.SetOpacity(this, config.EffectiveWindowOpacity);
        _fusionDisplay.Update(dual);
    }

    private void FusionExit_OnClick(object sender, RoutedEventArgs e) => FusionExitRequested?.Invoke();

    private void ApplyQuestionPaneWidth()
    {
        if (_splitterDragging || WorkspaceGrid.ActualWidth <= 0) return;
        var maximum = Math.Min(360, Math.Max(150, WorkspaceGrid.ActualWidth - 12 - AnswerColumn.MinWidth));
        QuestionColumn.Width = new GridLength(Math.Clamp(_preferredQuestionPaneWidth, 150, maximum));
    }

    private void QuestionSplitter_OnDragStarted(object sender, DragStartedEventArgs e) => _splitterDragging = true;

    private void QuestionSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _splitterDragging = false;
        if (e.Canceled) { ApplyQuestionPaneWidth(); return; }
        SaveQuestionPaneWidth();
    }

    private void QuestionSplitter_OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right) SaveQuestionPaneWidth();
    }

    private void SaveQuestionPaneWidth()
    {
        _preferredQuestionPaneWidth = Math.Clamp(QuestionColumn.ActualWidth, 150, 360);
        ApplyQuestionPaneWidth();
        QuestionPaneWidthChanged?.Invoke(_preferredQuestionPaneWidth);
    }

    private static bool ShouldSubmitComposerKey(Key key, ModifierKeys modifiers, bool composing)
        => key == Key.Return && modifiers == ModifierKeys.None && !composing;

    private void ManualInput_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ShouldSubmitComposerKey(e.Key, e.KeyboardDevice.Modifiers, _isComposing)) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        ManualInput.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        if (_viewModel.SendCommand.CanExecute(null)) _viewModel.SendCommand.Execute(null);
    }

    private void SetAnswerFontSize(double size)
    {
        AnswerContent.ContentFontSize = size;
        ScreenshotQuestionContent.ContentFontSize = size;
        AnswerFontSizeLabel.Text = size.ToString("0");
        DecreaseAnswerFontButton.IsEnabled = size > 14;
        IncreaseAnswerFontButton.IsEnabled = size < 28;
    }

    private void ChangeAnswerFontSize(int delta)
    {
        var size = Math.Clamp(AnswerContent.ContentFontSize + delta, 14, 28);
        SetAnswerFontSize(size);
        AnswerFontSizeChanged?.Invoke(size);
    }

    private void DecreaseAnswerFont_OnClick(object sender, RoutedEventArgs e) => ChangeAnswerFontSize(-1);
    private void IncreaseAnswerFont_OnClick(object sender, RoutedEventArgs e) => ChangeAnswerFontSize(1);

    private void ApplyScreenshotLayout()
    {
        if (_screenshotSplitterDragging) return;
        var screenshot = _viewModel?.Selected?.IsScreenshot == true;
        ScreenshotQuestionColumn.MinWidth = screenshot ? 130 : 0;
        ScreenshotDividerColumn.Width = new GridLength(screenshot ? 12 : 0);
        ScreenshotQuestionColumn.Width = screenshot ? new GridLength(_screenshotQuestionRatio, GridUnitType.Star) : new GridLength(0);
        ScreenshotAnswerColumn.Width = new GridLength(screenshot ? 1 - _screenshotQuestionRatio : 1, GridUnitType.Star);
        AnswerReadingPane.Margin = new Thickness(screenshot ? 12 : 0, 0, 0, 0);
    }
    private void ScreenshotSplitter_OnDragStarted(object sender, DragStartedEventArgs e) => _screenshotSplitterDragging = true;
    private void ScreenshotSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _screenshotSplitterDragging = false;
        if (e.Canceled) { ApplyScreenshotLayout(); return; }
        SaveScreenshotRatio();
    }
    private void ScreenshotSplitter_OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right) SaveScreenshotRatio();
    }
    private void SaveScreenshotRatio()
    {
        var width = ScreenshotQuestionColumn.ActualWidth + ScreenshotAnswerColumn.ActualWidth;
        if (width <= 0) return;
        _screenshotQuestionRatio = Math.Clamp(ScreenshotQuestionColumn.ActualWidth / width, .25, .65);
        ApplyScreenshotLayout();
        ScreenshotQuestionRatioChanged?.Invoke(_screenshotQuestionRatio);
    }

    public bool? ConfirmAudioRecording()
    {
        var result = System.Windows.MessageBox.Show(this,
            "是否录制本次语音？\n\n是：保存全程录音和文字记录。\n否：仅保存文字记录，不录音。\n\n保存位置使用设置中的目录。",
            "willGo · 本次录制", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
        return result switch { MessageBoxResult.Yes => true, MessageBoxResult.No => false, _ => null };
    }

    public void ScrollConversation(int direction)
    {
        if (IsVisible) AnswerScroll.ScrollToVerticalOffset(AnswerScroll.VerticalOffset + direction * 120);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BattleViewModel.Selected))
        {
            AnswerScroll.ScrollToTop();
            ScreenshotQuestionScroll.ScrollToTop();
            ApplyScreenshotLayout();
            KeepSelectedTitleVisible();
        }
        if (e.PropertyName == nameof(BattleViewModel.Transcript)) TranscriptBox.ScrollToEnd();
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        for (var current = e.OriginalSource as DependencyObject; current is not null && current != sender;)
        {
            if (current is ButtonBase or TextBoxBase) return;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        DragMove();
    }
    private void KeepSelectedTitleVisible()
    {
        if (_viewModel.Selected is not null) QuestionList.ScrollIntoView(_viewModel.Selected);
    }

    private void OnInteractionChanged(bool passthrough)
    {
        InteractionIndicator.Fill = passthrough ? Brushes.White : new SolidColorBrush(Color.FromRgb(48, 52, 52));
        InteractionButton.ToolTip = passthrough ? "鼠标穿透" : "可点击";
        System.Windows.Automation.AutomationProperties.SetName(InteractionButton, (string)InteractionButton.ToolTip);
    }

    private void Interaction_OnClick(object sender, RoutedEventArgs e)
    {
        if (_fusionStyle is null) WindowInteraction.SetClickThrough(!WindowInteraction.IsClickThrough);
    }

    public RemoteBallMoveResult MoveByPixels(int dx, int dy)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        var x = (int)Math.Round(Left * dpi.DpiScaleX) + dx;
        var y = (int)Math.Round(Top * dpi.DpiScaleY) + dy;
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x + width / 2, y + height / 2)).WorkingArea;
        Left = Math.Clamp(x, area.Left + 2, Math.Max(area.Left + 2, area.Right - width - 2)) / dpi.DpiScaleX;
        Top = Math.Clamp(y, area.Top + 2, Math.Max(area.Top + 2, area.Bottom - height - 2)) / dpi.DpiScaleY;
        return new(true, "Window position updated.");
    }
    private void Settings_OnClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void Phone_OnClick(object sender, RoutedEventArgs e) => PhoneRequested?.Invoke();
    private void History_OnClick(object sender, RoutedEventArgs e) => HistoryRequested?.Invoke();
    private void Hide_OnClick(object sender, RoutedEventArgs e) => Hide();
}
