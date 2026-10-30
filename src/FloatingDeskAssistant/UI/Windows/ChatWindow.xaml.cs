using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.ViewModels;

namespace FloatingDeskAssistant.UI.Windows;

public partial class ChatWindow : Window
{
    public event Action? BattleModeRequested;
    private void BattleMode_OnClick(object sender, RoutedEventArgs e) => BattleModeRequested?.Invoke();
    private const double CompactMinWindowHeight = 260;
    private const double PassiveCompactMinWindowHeight = 180;
    private const double MinimumConversationHeight = 140;
    private const double CompactMinimumConversationHeight = 96;
    private const double PassiveMinimumConversationHeight = 120;
    private const int ScreenMarginPixels = 12;
    private const double AutoSizeTolerance = 1;
    private const double HistoryMaxViewportHeight = 180;
    private const double HistoryMinViewportHeight = 72;
    private const double LatestColumnWindowSpacing = 10;
    private const int MaxLatestMessageColumns = 3;
    private const double DefaultMainColumnTextWidth = 332;
    private const double DefaultColumnTextHeight = 180;
    private const double BubbleVerticalPadding = 20;
    private const double BubbleHorizontalPadding = 20;
    private const double TimestampBlockHeight = 16;
    private const double BubbleToTimestampSpacing = 4;
    private const double LatestMessageBottomSpacing = 8;
    private const double ExtensionWindowChromePadding = 16;
    private const double ImageRenderWidth = 220;
    private const double ImageRenderMaxHeight = 200;
    private const string SegmentBoundaryCharacters = " \t\r\n,.;:!?，。；：！？、)]}》】」』’”";

    private readonly MainViewModel _viewModel;
    private readonly List<LatestMessageColumnWindow> _latestColumnWindows = new();
    private double _expandedHeight = 620;
    private double _expandedMinHeight;
    private double _expandedMaxHeight = double.PositiveInfinity;
    private ResizeMode _expandedResizeMode = ResizeMode.CanResizeWithGrip;
    private bool _adaptiveHeightScheduled;
    private bool _compactSizingApplied;
    private bool _isApplyingAdaptiveHeight;
    private bool _isUpdatingLatestMessageLayout;
    private MessageItemViewModel? _displayedMessage;

    public ChatWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        WindowCaptureProtection.Attach(this, 14, 8);
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += OnClosed;
        LocationChanged += OnLocationChanged;
        SizeChanged += OnSizeChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ConfigChanged += _ => Dispatcher.BeginInvoke(() =>
        {
            ApplyLayoutMode();
            ScheduleAdaptiveHeightUpdate();
        });
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowCaptureProtection.SetOpacity(this, _viewModel.WindowOpacity);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureLatestColumnWindows();

        if (_viewModel.Messages is INotifyCollectionChanged changed)
        {
            changed.CollectionChanged += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    HistoryScrollViewer.ScrollToTop();
                    ScheduleAdaptiveHeightUpdate();
                }, DispatcherPriority.Loaded);
            };
        }

        InputTextBox.SizeChanged += (_, _) => ScheduleAdaptiveHeightUpdate();
        ApplyLayoutMode();
        ScheduleAdaptiveHeightUpdate();
    }

    private void ApplyLayoutMode()
    {
        if (_viewModel.IsCompactChatMode)
        {
            if (!_compactSizingApplied)
            {
                _expandedHeight = ActualHeight > 0 ? ActualHeight : Height;
                _expandedMinHeight = MinHeight;
                _expandedMaxHeight = MaxHeight;
                _expandedResizeMode = ResizeMode;
            }

            var compactMinWindowHeight = GetCompactMinimumWindowHeight();
            _compactSizingApplied = true;
            ResizeMode = _viewModel.IsPassiveCompactChatMode ? ResizeMode.NoResize : _expandedResizeMode;
            MinHeight = compactMinWindowHeight;
            MaxHeight = _expandedMaxHeight;

            if (Height < compactMinWindowHeight)
            {
                Height = compactMinWindowHeight;
            }

            ScheduleAdaptiveHeightUpdate();
            return;
        }

        if (_compactSizingApplied)
        {
            Height = _expandedHeight;
            MinHeight = _expandedMinHeight;
            MaxHeight = _expandedMaxHeight;
            ResizeMode = _expandedResizeMode;
        }

        _compactSizingApplied = false;
        ScheduleAdaptiveHeightUpdate();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.WindowOpacity))
        {
            WindowCaptureProtection.SetOpacity(this, _viewModel.WindowOpacity);
            foreach (var window in _latestColumnWindows)
            {
                WindowCaptureProtection.SetOpacity(window, _viewModel.WindowOpacity);
            }
        }

        if (e.PropertyName is nameof(MainViewModel.IsCompactChatMode)
            or nameof(MainViewModel.IsPassiveCompactChatMode)
            or nameof(MainViewModel.FooterVisibility))
        {
            ApplyLayoutMode();
            ScheduleAdaptiveHeightUpdate();
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.MessageFontSize)
            or nameof(MainViewModel.CurrentMessage)
            or nameof(MainViewModel.HistoryMessages)
            or nameof(MainViewModel.HasHistoryMessages)
            or nameof(MainViewModel.HistoryVisibility)
            or nameof(MainViewModel.PendingImageVisibility)
            or nameof(MainViewModel.StatusText)
            or nameof(MainViewModel.StatusVisibility)
            or nameof(MainViewModel.WindowBackdropBrush))
        {
            ScheduleAdaptiveHeightUpdate();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isApplyingAdaptiveHeight)
        {
            return;
        }

        if (e.WidthChanged)
        {
            ScheduleAdaptiveHeightUpdate();
        }
    }

    private void OnIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        ScheduleAdaptiveHeightUpdate();

        if (!IsVisible)
        {
            HideLatestColumnWindows();
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        PositionLatestColumnWindows();
    }

    private void ScheduleAdaptiveHeightUpdate()
    {
        if (!IsLoaded || _adaptiveHeightScheduled)
        {
            return;
        }

        _adaptiveHeightScheduled = true;
        Dispatcher.BeginInvoke(ApplyAdaptiveHeight, DispatcherPriority.Loaded);
    }

    private void ApplyAdaptiveHeight()
    {
        _adaptiveHeightScheduled = false;

        if (!IsLoaded)
        {
            return;
        }

        UpdateLayout();

        var conversationHeight = Math.Max(0, ConversationHostBorder.ActualHeight);
        var chromeHeight = Math.Max(0, ActualHeight - conversationHeight);
        var minimumConversationHeight = GetMinimumConversationHeight();
        var minimumWindowHeight = Math.Max(
            _viewModel.IsCompactChatMode ? GetCompactMinimumWindowHeight() : 0,
            chromeHeight + minimumConversationHeight);
        var maximumWindowHeight = GetMaximumWindowHeight(minimumWindowHeight);
        var desiredHistoryHeight = GetDesiredHistoryViewportHeight();
        var availableLatestMessageHeight = Math.Max(
            48,
            maximumWindowHeight - chromeHeight - desiredHistoryHeight - (_viewModel.HasHistoryMessages ? LatestMessageBottomSpacing : 0));

        _isApplyingAdaptiveHeight = true;
        try
        {
            UpdateLatestMessageLayout(availableLatestMessageHeight);
            UpdateLayout();

            var desiredConversationHeight = Math.Max(minimumConversationHeight, GetDesiredConversationHeight());
            var targetHeight = Math.Clamp(chromeHeight + desiredConversationHeight, minimumWindowHeight, maximumWindowHeight);

            MinHeight = minimumWindowHeight;
            MaxHeight = maximumWindowHeight;

            if (Math.Abs(Height - targetHeight) > AutoSizeTolerance)
            {
                Height = targetHeight;
            }

            ClampToWorkingArea();
            UpdateLayout();
            PositionLatestColumnWindows();
        }
        finally
        {
            _isApplyingAdaptiveHeight = false;
        }
    }

    private double GetDesiredHistoryViewportHeight()
    {
        if (_viewModel.HistoryVisibility != Visibility.Visible)
        {
            return 0;
        }

        var extentHeight = Math.Max(0, HistoryScrollViewer.ExtentHeight);
        if (extentHeight <= AutoSizeTolerance)
        {
            return HistoryMinViewportHeight;
        }

        return Math.Clamp(extentHeight, HistoryMinViewportHeight, HistoryMaxViewportHeight);
    }

    private double GetDesiredConversationHeight()
    {
        var currentHeight = CurrentMessageHost.Visibility == Visibility.Visible
            ? CurrentMessageHost.ActualHeight + CurrentMessageHost.Margin.Top + CurrentMessageHost.Margin.Bottom
            : 0;

        var separatorHeight = HistorySeparator.Visibility == Visibility.Visible
            ? HistorySeparator.ActualHeight + HistorySeparator.Margin.Bottom
            : 0;

        var historyHeight = HistoryHostBorder.Visibility == Visibility.Visible
            ? HistoryHostBorder.ActualHeight
            : 0;

        return ConversationHostBorder.Padding.Top
            + currentHeight
            + separatorHeight
            + historyHeight
            + ConversationHostBorder.Padding.Bottom;
    }

    private void UpdateLatestMessageLayout(double availableLatestMessageHeight)
    {
        if (_isUpdatingLatestMessageLayout)
        {
            return;
        }

        _isUpdatingLatestMessageLayout = true;
        try
        {
            var message = _viewModel.CurrentMessage;
            if (message is null)
            {
                ClearCurrentMessageLayout();
                return;
            }

            ApplyCurrentMessageAppearance(message);

            var mainColumnTextWidth = GetMainColumnTextWidth();
            var mainColumnTextHeight = Math.Max(
                24,
                availableLatestMessageHeight - GetColumnChromeHeight(message, includeImage: message.HasImage, includeTimestamp: true));
            var overflowColumnTextWidth = GetOverflowColumnTextWidth();
            var overflowColumnTextHeight = Math.Max(
                24,
                availableLatestMessageHeight - GetColumnChromeHeight(message, includeImage: false, includeTimestamp: false));

            var (segments, _) = BuildLatestMessageSegments(
                message.DisplayText,
                mainColumnTextWidth,
                mainColumnTextHeight,
                overflowColumnTextWidth,
                overflowColumnTextHeight);

            CurrentMessageContentPresenter.ApplySlice(
                message.DisplayText,
                segments[0].Start,
                segments[0].Length,
                message.TextBrush,
                _viewModel.MessageFontSize);
            CurrentMessageHost.Visibility = Visibility.Visible;
            CurrentMessageHost.MaxHeight = availableLatestMessageHeight;
            HistorySeparator.Visibility = _viewModel.HistoryVisibility;

            UpdateLatestColumnWindows(message, segments);
        }
        finally
        {
            _isUpdatingLatestMessageLayout = false;
        }
    }

    private void ClearCurrentMessageLayout()
    {
        _displayedMessage = null;
        CurrentMessageContentPresenter.ApplySlice(string.Empty, 0, 0, System.Windows.Media.Brushes.White, _viewModel.MessageFontSize);
        CurrentMessageTimestamp.Text = string.Empty;
        CurrentMessageImage.Source = null;
        CurrentMessageImage.Visibility = Visibility.Collapsed;
        CurrentMessageHost.Visibility = Visibility.Collapsed;
        CurrentMessageHost.MaxHeight = double.PositiveInfinity;
        HistorySeparator.Visibility = Visibility.Collapsed;
        HideLatestColumnWindows();
    }

    private void ApplyCurrentMessageAppearance(MessageItemViewModel message)
    {
        if (!ReferenceEquals(_displayedMessage, message))
        {
            _displayedMessage = message;
            CurrentMessageHost.ScrollToTop();
        }
        CurrentMessagePanel.HorizontalAlignment = message.Alignment;
        CurrentMessageBubble.HorizontalAlignment = message.Alignment;
        CurrentMessageBubble.Background = message.BubbleBrush;
        CurrentMessageTimestamp.Text = message.TimestampText;
        CurrentMessageTimestamp.Visibility = Visibility.Visible;

        if (message.HasImage)
        {
            CurrentMessageImage.Source = message.ImageSource;
            CurrentMessageImage.Visibility = Visibility.Visible;
        }
        else
        {
            CurrentMessageImage.Source = null;
            CurrentMessageImage.Visibility = Visibility.Collapsed;
        }
    }

    private (List<MessageTextSlice> Segments, string Remainder) BuildLatestMessageSegments(
        string text,
        double mainColumnTextWidth,
        double mainColumnTextHeight,
        double overflowColumnTextWidth,
        double overflowColumnTextHeight)
    {
        var normalizedText = text ?? string.Empty;
        var segments = new List<MessageTextSlice>();
        if (string.IsNullOrEmpty(normalizedText))
        {
            segments.Add(new MessageTextSlice(0, 0));
            return (segments, string.Empty);
        }

        var remainingStart = 0;
        var remainingLength = normalizedText.Length;
        segments.Add(ExtractSegment(normalizedText, ref remainingStart, ref remainingLength, mainColumnTextWidth, mainColumnTextHeight));

        for (var columnIndex = 1; columnIndex < MaxLatestMessageColumns && remainingLength > 0; columnIndex++)
        {
            var isLastSupportedColumn = columnIndex == MaxLatestMessageColumns - 1;
            if (isLastSupportedColumn)
            {
                segments.Add(new MessageTextSlice(remainingStart, remainingLength));
                remainingStart = normalizedText.Length;
                remainingLength = 0;
                break;
            }

            segments.Add(ExtractSegment(normalizedText, ref remainingStart, ref remainingLength, overflowColumnTextWidth, overflowColumnTextHeight));
        }

        var remainder = remainingLength > 0
            ? normalizedText.Substring(remainingStart, remainingLength)
            : string.Empty;
        return (segments, remainder);
    }

    private MessageTextSlice ExtractSegment(string sourceText, ref int remainingStart, ref int remainingLength, double textWidth, double textHeight)
    {
        if (remainingLength <= 0)
        {
            return new MessageTextSlice(remainingStart, 0);
        }

        var remaining = sourceText.Substring(remainingStart, remainingLength);
        var fittingLength = FindFittingTextLength(remaining, textWidth, textHeight);
        if (fittingLength >= remaining.Length)
        {
            var fullSegment = new MessageTextSlice(remainingStart, remainingLength);
            remainingStart = sourceText.Length;
            remainingLength = 0;
            return fullSegment;
        }

        fittingLength = AdjustSegmentBoundary(remaining, fittingLength);
        fittingLength = Math.Clamp(fittingLength, 1, remaining.Length);

        var trimmedLength = remaining[..fittingLength].TrimEnd().Length;
        if (trimmedLength <= 0)
        {
            trimmedLength = fittingLength;
        }

        var consumedLength = fittingLength;
        while (consumedLength < remaining.Length && char.IsWhiteSpace(remaining[consumedLength]))
        {
            consumedLength++;
        }

        var slice = new MessageTextSlice(remainingStart, trimmedLength);
        remainingStart += consumedLength;
        remainingLength -= consumedLength;
        return slice;
    }

    private int FindFittingTextLength(string text, double textWidth, double textHeight)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        if (textWidth <= AutoSizeTolerance || textHeight <= AutoSizeTolerance)
        {
            return Math.Min(1, text.Length);
        }

        if (DoesTextFit(text, textWidth, textHeight))
        {
            return text.Length;
        }

        var low = 1;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (DoesTextFit(text[..mid], textWidth, textHeight))
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    private bool DoesTextFit(string text, double textWidth, double textHeight)
    {
        return MeasureTextHeight(text, textWidth) <= textHeight + AutoSizeTolerance;
    }

    private double MeasureTextHeight(string text, double textWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var typeface = new Typeface(
            FontFamily,
            FontStyle,
            FontWeight,
            FontStretch);

        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            _viewModel.MessageFontSize,
            System.Windows.Media.Brushes.White,
            dpi.PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, textWidth)
        };

        return Math.Ceiling(formattedText.Height);
    }

    private int AdjustSegmentBoundary(string text, int proposedLength)
    {
        if (proposedLength <= 1 || proposedLength >= text.Length)
        {
            return proposedLength;
        }

        var searchStart = Math.Max(0, proposedLength - 48);
        for (var index = proposedLength - 1; index >= searchStart; index--)
        {
            if (SegmentBoundaryCharacters.IndexOf(text[index]) >= 0)
            {
                return index + 1;
            }
        }

        return proposedLength;
    }

    private double GetColumnChromeHeight(MessageItemViewModel message, bool includeImage, bool includeTimestamp)
    {
        var height = BubbleVerticalPadding;
        if (includeImage && message.HasImage)
        {
            height += GetRenderedImageHeight(message) + 8;
        }

        if (includeTimestamp)
        {
            height += BubbleToTimestampSpacing + TimestampBlockHeight + LatestMessageBottomSpacing;
        }

        return height;
    }

    private double GetRenderedImageHeight(MessageItemViewModel message)
    {
        if (message.ImageSource is not ImageSource imageSource
            || imageSource.Width <= AutoSizeTolerance
            || imageSource.Height <= AutoSizeTolerance)
        {
            return ImageRenderMaxHeight;
        }

        var scale = Math.Min(ImageRenderWidth / imageSource.Width, ImageRenderMaxHeight / imageSource.Height);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return ImageRenderMaxHeight;
        }

        return Math.Min(ImageRenderMaxHeight, imageSource.Height * scale);
    }

    private double GetMainColumnTextWidth()
    {
        var hostWidth = CurrentMessageHost.ActualWidth > AutoSizeTolerance
            ? CurrentMessageHost.ActualWidth
            : ConversationHostBorder.ActualWidth - ConversationHostBorder.Padding.Left - ConversationHostBorder.Padding.Right;

        if (hostWidth <= AutoSizeTolerance)
        {
            hostWidth = DefaultMainColumnTextWidth + BubbleHorizontalPadding;
        }

        return Math.Max(120, hostWidth - BubbleHorizontalPadding);
    }

    private double GetOverflowColumnTextWidth()
    {
        return GetMainColumnTextWidth();
    }

    private void EnsureLatestColumnWindows()
    {
        while (_latestColumnWindows.Count < MaxLatestMessageColumns - 1)
        {
            var window = new LatestMessageColumnWindow
            {
                DataContext = _viewModel,
                Owner = this,
                ShowActivated = false,
                Topmost = true
            };

            _latestColumnWindows.Add(window);
            window.SourceInitialized += (_, _) => WindowCaptureProtection.SetOpacity(window, _viewModel.WindowOpacity);
        }
    }

    private void UpdateLatestColumnWindows(MessageItemViewModel message, IReadOnlyList<MessageTextSlice> segments)
    {
        EnsureLatestColumnWindows();

        for (var index = 0; index < _latestColumnWindows.Count; index++)
        {
            var segmentIndex = index + 1;
            if (segmentIndex >= segments.Count || segments[segmentIndex].Length <= 0)
            {
                _latestColumnWindows[index].Tag = false;
                _latestColumnWindows[index].Hide();
                continue;
            }

            _latestColumnWindows[index].Tag = true;
            _latestColumnWindows[index].ApplySegment(
                message.Alignment,
                message.BubbleBrush,
                message.TextBrush,
                _viewModel.WindowBackdropBrush,
                _viewModel.MessageFontSize,
                message.DisplayText,
                segments[segmentIndex].Start,
                segments[segmentIndex].Length);
        }
    }

    private void HideLatestColumnWindows()
    {
        foreach (var window in _latestColumnWindows)
        {
            window.Tag = false;
            window.Hide();
        }
    }

    private void PositionLatestColumnWindows()
    {
        if (!IsLoaded || !IsVisible || CurrentMessageHost.Visibility != Visibility.Visible)
        {
            HideLatestColumnWindows();
            return;
        }

        if (!TryGetElementScreenBounds(CurrentMessageHost, out var hostBounds))
        {
            HideLatestColumnWindows();
            return;
        }

        var visibleWindows = _latestColumnWindows.Where(window => Equals(window.Tag, true)).ToArray();
        if (visibleWindows.Length == 0)
        {
            return;
        }

        var screen = GetCurrentScreen();
        var workArea = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var spacingPixels = (int)Math.Round(LatestColumnWindowSpacing * dpi.DpiScaleX);
        var frameInsetPixelsX = (int)Math.Round(8 * dpi.DpiScaleX);
        var frameInsetPixelsY = (int)Math.Round(8 * dpi.DpiScaleY);
        var windowWidthPixels = hostBounds.Width + (frameInsetPixelsX * 2);
        var windowHeightPixels = hostBounds.Height + (frameInsetPixelsY * 2);
        var totalRequiredWidth = (visibleWindows.Length * windowWidthPixels) + (Math.Max(0, visibleWindows.Length - 1) * spacingPixels);

        var spaceRight = workArea.Right - hostBounds.Right - spacingPixels;
        var spaceLeft = hostBounds.Left - workArea.Left - spacingPixels;
        var placeRight = spaceRight >= totalRequiredWidth || spaceRight >= spaceLeft;
        var anchorTop = hostBounds.Top - frameInsetPixelsY;
        var minTop = workArea.Top + ScreenMarginPixels;
        var maxTop = workArea.Bottom - ScreenMarginPixels - windowHeightPixels;
        anchorTop = maxTop < minTop ? minTop : Math.Clamp(anchorTop, minTop, maxTop);

        for (var index = 0; index < visibleWindows.Length; index++)
        {
            var x = placeRight
                ? hostBounds.Right + spacingPixels + (index * (windowWidthPixels + spacingPixels))
                : hostBounds.Left - spacingPixels - windowWidthPixels - (index * (windowWidthPixels + spacingPixels));

            x = Math.Max(workArea.Left + ScreenMarginPixels, Math.Min(x, workArea.Right - ScreenMarginPixels - windowWidthPixels));

            var window = visibleWindows[index];
            window.Width = windowWidthPixels / dpi.DpiScaleX;
            window.Height = windowHeightPixels / dpi.DpiScaleY;
            window.Left = x / dpi.DpiScaleX;
            window.Top = anchorTop / dpi.DpiScaleY;

            if (!window.IsVisible)
            {
                window.Show();
            }

        }
    }

    private double GetCompactMinimumWindowHeight()
    {
        return _viewModel.IsPassiveCompactChatMode
            ? PassiveCompactMinWindowHeight
            : CompactMinWindowHeight;
    }

    private double GetMinimumConversationHeight()
    {
        if (_viewModel.IsPassiveCompactChatMode)
        {
            return PassiveMinimumConversationHeight;
        }

        return _viewModel.IsCompactChatMode
            ? CompactMinimumConversationHeight
            : MinimumConversationHeight;
    }

    private double GetMaximumWindowHeight(double minimumWindowHeight)
    {
        var screen = GetCurrentScreen();
        var dpi = VisualTreeHelper.GetDpi(this);
        var availableHeight = (screen.WorkingArea.Height - (ScreenMarginPixels * 2)) / dpi.DpiScaleY;
        return Math.Max(minimumWindowHeight, availableHeight);
    }

    private Screen GetCurrentScreen()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var centerX = (int)Math.Round((Left + (width / 2)) * dpi.DpiScaleX);
        var centerY = (int)Math.Round((Top + (height / 2)) * dpi.DpiScaleY);
        return Screen.FromPoint(new System.Drawing.Point(centerX, centerY));
    }

    private void ClampToWorkingArea()
    {
        var screen = GetCurrentScreen();
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var marginX = ScreenMarginPixels / dpi.DpiScaleX;
        var marginY = ScreenMarginPixels / dpi.DpiScaleY;
        var workArea = screen.WorkingArea;

        var minLeft = (workArea.Left / dpi.DpiScaleX) + marginX;
        var maxLeft = ((workArea.Right - ScreenMarginPixels) / dpi.DpiScaleX) - width;
        var minTop = (workArea.Top / dpi.DpiScaleY) + marginY;
        var maxTop = ((workArea.Bottom - ScreenMarginPixels) / dpi.DpiScaleY) - height;

        var targetLeft = maxLeft < minLeft ? minLeft : Math.Clamp(Left, minLeft, maxLeft);
        var targetTop = maxTop < minTop ? minTop : Math.Clamp(Top, minTop, maxTop);

        if (Math.Abs(Left - targetLeft) > AutoSizeTolerance)
        {
            Left = targetLeft;
        }

        if (Math.Abs(Top - targetTop) > AutoSizeTolerance)
        {
            Top = targetTop;
        }
    }

    private bool TryGetElementScreenBounds(FrameworkElement element, out System.Drawing.Rectangle bounds)
    {
        bounds = System.Drawing.Rectangle.Empty;
        if (!IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var windowRect))
        {
            return false;
        }

        try
        {
            var relativeTopLeft = element.TransformToAncestor(this).Transform(new System.Windows.Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(this);
            var left = windowRect.Left + (int)Math.Round(relativeTopLeft.X * dpi.DpiScaleX);
            var top = windowRect.Top + (int)Math.Round(relativeTopLeft.Y * dpi.DpiScaleY);
            var width = (int)Math.Round(element.ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Round(element.ActualHeight * dpi.DpiScaleY);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            bounds = new System.Drawing.Rectangle(left, top, width, height);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void ScrollConversation(int direction)
    {
        if (!IsVisible || !IsLoaded || direction == 0) return;
        var pixels = Math.Sign(direction) * Math.Max(36, _viewModel.MessageFontSize * 3);
        var latestScrollable = false;
        if (CurrentMessageHost.ScrollableHeight > AutoSizeTolerance)
        {
            CurrentMessageHost.ScrollToVerticalOffset(Math.Clamp(
                CurrentMessageHost.VerticalOffset + pixels, 0, CurrentMessageHost.ScrollableHeight));
            latestScrollable = true;
        }
        foreach (var window in _latestColumnWindows) latestScrollable |= window.ScrollConversation(pixels);
        if (!latestScrollable && HistoryHostBorder.Visibility == Visibility.Visible)
        {
            HistoryScrollViewer.ScrollToVerticalOffset(Math.Clamp(
                HistoryScrollViewer.VerticalOffset + pixels, 0, HistoryScrollViewer.ScrollableHeight));
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        foreach (var window in _latestColumnWindows.ToArray())
        {
            window.Close();
        }
    }

    private void HideButton_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Window_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.M)
        {
            e.Handled = true;
            _viewModel.ToggleCompactChatModeCommand.Execute(null);
            return;
        }

        if (_viewModel.IsCompactChatMode && e.Key == Key.Escape)
        {
            e.Handled = true;
            _viewModel.ToggleCompactChatModeCommand.Execute(null);
        }
    }

    private async void InputTextBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers == ModifierKeys.Shift)
        {
            return;
        }

        e.Handled = true;
        await _viewModel.SendTextAsync();
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private readonly record struct MessageTextSlice(int Start, int Length);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectStruct
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RectStruct rect);
}

