using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.ViewModels;

namespace FloatingDeskAssistant.UI.Windows;

public partial class BallWindow : Window
{
    private const int DragThresholdPixels = 2;
    private const long ChatRepositionIntervalMs = 16;

    private readonly MainViewModel _viewModel;
    private FloatingWindowCoordinator? _windowCoordinator;
    private System.Drawing.Point _dragStartScreenPixels;
    private double _startLeft;
    private double _startTop;
    private bool _isDragging;
    private long _lastChatRepositionAt;

    public BallWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += OnLoaded;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += OnMouseRightButtonUp;
        MouseEnter += (_, _) => UpdateOpacity(Math.Min(1.0, _viewModel.BallOpacity + 0.25));
        MouseLeave += (_, _) => UpdateOpacity(_viewModel.BallOpacity);

        _viewModel.ConfigChanged += _ => UpdateOpacity(_viewModel.BallOpacity);
    }

    public void SetCoordinator(FloatingWindowCoordinator coordinator)
    {
        _windowCoordinator = coordinator;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateOpacity(_viewModel.BallOpacity);
        var primary = Screen.PrimaryScreen ?? Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var areaPixels = primary.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);

        Left = (areaPixels.Left / dpi.DpiScaleX) + 40;
        Top = (areaPixels.Top / dpi.DpiScaleY) + 160;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartScreenPixels = System.Windows.Forms.Cursor.Position;
        _startLeft = Left;
        _startTop = Top;
        _isDragging = false;
        _lastChatRepositionAt = 0;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentScreenPixels = System.Windows.Forms.Cursor.Position;
        var dxPixels = currentScreenPixels.X - _dragStartScreenPixels.X;
        var dyPixels = currentScreenPixels.Y - _dragStartScreenPixels.Y;

        if (!_isDragging && Math.Abs(dxPixels) <= DragThresholdPixels && Math.Abs(dyPixels) <= DragThresholdPixels)
        {
            return;
        }

        _isDragging = true;

        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _startLeft + (dxPixels / dpi.DpiScaleX);
        Top = _startTop + (dyPixels / dpi.DpiScaleY);

        RepositionVisibleChatDuringDrag();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            return;
        }

        ReleaseMouseCapture();
        _lastChatRepositionAt = 0;

        if (_isDragging)
        {
            _windowCoordinator?.PositionChatNearBall();
            return;
        }

        _windowCoordinator?.ToggleChat();
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        UpdateBallContextMenu();

        if (ContextMenu is null)
        {
            return;
        }

        ContextMenu.PlacementTarget = this;
        ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void BallContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        UpdateBallContextMenu();
    }

    private void CompactModeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ToggleCompactChatModeCommand.CanExecute(null))
        {
            _viewModel.ToggleCompactChatModeCommand.Execute(null);
        }
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void UpdateBallContextMenu()
    {
        if (CompactModeMenuItem is null)
        {
            return;
        }

        CompactModeMenuItem.Header = _viewModel.IsCompactChatMode
            ? "关闭极简模式"
            : "开启极简模式";
    }

    private void RepositionVisibleChatDuringDrag()
    {
        if (_windowCoordinator is null || !_windowCoordinator.IsChatVisible)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastChatRepositionAt < ChatRepositionIntervalMs)
        {
            return;
        }

        _lastChatRepositionAt = now;
        _windowCoordinator.PositionChatNearBall();
    }

    private void UpdateOpacity(double value)
    {
        Opacity = Math.Clamp(value, 0.05, 1.0);
    }
}
