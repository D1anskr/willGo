using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace FloatingDeskAssistant.Application;

public sealed class FloatingWindowCoordinator
{
    private const int RemoteMoveMarginPixels = 2;

    private readonly Window _ballWindow;
    private readonly Window _chatWindow;

    public FloatingWindowCoordinator(Window ballWindow, Window chatWindow)
    {
        _ballWindow = ballWindow;
        _chatWindow = chatWindow;
    }

    public bool IsChatVisible => _chatWindow.IsVisible;

    public void ToggleChat()
    {
        if (_chatWindow.IsVisible)
        {
            _chatWindow.Hide();
            return;
        }

        PositionChatNearBall();
        _chatWindow.Show();
        _chatWindow.Activate();
    }

    public void PositionChatNearBall()
    {
        var ballRect = GetWindowRectPixels(_ballWindow);
        var screen = Screen.FromPoint(new DrawingPoint(ballRect.Left + ballRect.Width / 2, ballRect.Top + ballRect.Height / 2));
        var area = screen.WorkingArea;

        var chatSize = GetWindowSizePixels(_chatWindow);
        var margin = 12;

        var targetX = ballRect.Right + margin;
        var targetY = ballRect.Top;

        if (targetX + chatSize.Width > area.Right)
        {
            targetX = ballRect.Left - chatSize.Width - margin;
        }

        targetX = Math.Max(area.Left + margin, Math.Min(targetX, area.Right - chatSize.Width - margin));
        targetY = Math.Max(area.Top + margin, Math.Min(targetY, area.Bottom - chatSize.Height - margin));

        SetWindowPositionPixels(_chatWindow, targetX, targetY);
    }

    public void SnapBallToEdge()
    {
        var rect = GetWindowRectPixels(_ballWindow);
        var center = new DrawingPoint(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        var screen = Screen.FromPoint(center);
        var area = screen.WorkingArea;

        var distLeft = Math.Abs(rect.Left - area.Left);
        var distRight = Math.Abs(area.Right - rect.Right);
        var distTop = Math.Abs(rect.Top - area.Top);
        var distBottom = Math.Abs(area.Bottom - rect.Bottom);

        var min = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));

        var x = rect.Left;
        var y = rect.Top;

        if (min == distLeft)
        {
            x = area.Left;
        }
        else if (min == distRight)
        {
            x = area.Right - rect.Width;
        }
        else if (min == distTop)
        {
            y = area.Top;
        }
        else
        {
            y = area.Bottom - rect.Height;
        }

        SetWindowPositionPixels(_ballWindow, x, y);
        if (_chatWindow.IsVisible)
        {
            PositionChatNearBall();
        }
    }

    public RemoteBallMoveResult MoveBallByPixels(int deltaXPixels, int deltaYPixels)
    {
        if (deltaXPixels == 0 && deltaYPixels == 0)
        {
            return new RemoteBallMoveResult(true, "Ball already stopped.");
        }

        var rect = GetWindowRectPixels(_ballWindow);
        var targetCenter = new DrawingPoint(
            rect.Left + (rect.Width / 2) + deltaXPixels,
            rect.Top + (rect.Height / 2) + deltaYPixels);
        var screen = Screen.FromPoint(targetCenter);
        var area = screen.WorkingArea;

        var minX = area.Left + RemoteMoveMarginPixels;
        var maxX = area.Right - rect.Width - RemoteMoveMarginPixels;
        var minY = area.Top + RemoteMoveMarginPixels;
        var maxY = area.Bottom - rect.Height - RemoteMoveMarginPixels;

        var targetX = maxX < minX
            ? minX
            : Math.Clamp(rect.Left + deltaXPixels, minX, maxX);
        var targetY = maxY < minY
            ? minY
            : Math.Clamp(rect.Top + deltaYPixels, minY, maxY);

        SetWindowPositionPixels(_ballWindow, targetX, targetY);
        if (_chatWindow.IsVisible)
        {
            PositionChatNearBall();
        }

        return new RemoteBallMoveResult(true, $"Ball moved to ({targetX}, {targetY}).");
    }

    private static DrawingRectangle GetWindowRectPixels(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var widthDip = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var heightDip = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        return new DrawingRectangle(
            (int)Math.Round(window.Left * dpi.DpiScaleX),
            (int)Math.Round(window.Top * dpi.DpiScaleY),
            (int)Math.Round(widthDip * dpi.DpiScaleX),
            (int)Math.Round(heightDip * dpi.DpiScaleY));
    }

    private static DrawingSize GetWindowSizePixels(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var widthDip = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var heightDip = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        return new DrawingSize(
            (int)Math.Round(widthDip * dpi.DpiScaleX),
            (int)Math.Round(heightDip * dpi.DpiScaleY));
    }

    private static void SetWindowPositionPixels(Window window, int xPixels, int yPixels)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        window.Left = xPixels / dpi.DpiScaleX;
        window.Top = yPixels / dpi.DpiScaleY;
    }
}

public sealed record RemoteBallMoveResult(bool Accepted, string Message);
