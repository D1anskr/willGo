using System.Drawing;
using System.Windows.Forms;

namespace FloatingDeskAssistant.UI.Windows;

public sealed class ScreenshotOverlayForm : Form
{
    private Point _startPoint;
    private Point _endPoint;
    private bool _isSelecting;

    private readonly Panel _confirmPanel;
    private readonly Button _confirmButton;
    private readonly Button _cancelButton;
    private readonly Label _hintLabel;

    public ScreenshotOverlayForm()
    {
        DoubleBuffered = true;
        KeyPreview = true;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        Opacity = 0.35;

        _hintLabel = new Label
        {
            AutoSize = true,
            Text = "Drag to select region. Enter/Right Click = Confirm, Esc = Cancel",
            BackColor = Color.FromArgb(210, 24, 24, 24),
            ForeColor = Color.White,
            Padding = new Padding(8, 6, 8, 6),
            Location = new Point(14, 14),
            Enabled = false
        };

        _confirmPanel = new Panel
        {
            Size = new Size(280, 44),
            BackColor = Color.FromArgb(232, 22, 22, 22),
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false
        };

        _confirmButton = new Button
        {
            Text = "Confirm (Enter)",
            Size = new Size(132, 30),
            Location = new Point(8, 6)
        };

        _cancelButton = new Button
        {
            Text = "Cancel (Esc)",
            Size = new Size(132, 30),
            Location = new Point(144, 6)
        };

        _confirmButton.Click += (_, _) => ConfirmSelection();
        _cancelButton.Click += (_, _) => CancelSelection();

        _confirmPanel.Controls.Add(_confirmButton);
        _confirmPanel.Controls.Add(_cancelButton);

        Controls.Add(_hintLabel);
        Controls.Add(_confirmPanel);

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Shown += (_, _) =>
        {
            Activate();
            Focus();
            BringToFront();
        };
    }

    public Rectangle SelectedRectangle { get; private set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (SelectedRectangle == Rectangle.Empty)
        {
            return;
        }

        using var pen = new Pen(Color.LimeGreen, 2);
        e.Graphics.DrawRectangle(pen, ToClientRectangle(SelectedRectangle));

        using var dimBrush = new SolidBrush(Color.FromArgb(90, Color.Black));
        var full = ClientRectangle;
        var selected = ToClientRectangle(SelectedRectangle);

        e.Graphics.FillRectangle(dimBrush, new Rectangle(0, 0, full.Width, selected.Top));
        e.Graphics.FillRectangle(dimBrush, new Rectangle(0, selected.Bottom, full.Width, full.Height - selected.Bottom));
        e.Graphics.FillRectangle(dimBrush, new Rectangle(0, selected.Top, selected.Left, selected.Height));
        e.Graphics.FillRectangle(dimBrush, new Rectangle(selected.Right, selected.Top, full.Width - selected.Right, selected.Height));
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _confirmPanel.Visible = false;
        _isSelecting = true;
        _startPoint = e.Location;
        _endPoint = e.Location;
        SelectedRectangle = Rectangle.Empty;
        Capture = true;
        Invalidate();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        _endPoint = e.Location;
        SelectedRectangle = BuildAbsoluteRectangle(_startPoint, _endPoint);
        Invalidate();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (_isSelecting)
        {
            _isSelecting = false;
            Capture = false;
            _endPoint = e.Location;
            SelectedRectangle = BuildAbsoluteRectangle(_startPoint, _endPoint);

            if (SelectedRectangle.Width < 6 || SelectedRectangle.Height < 6)
            {
                SelectedRectangle = Rectangle.Empty;
                _confirmPanel.Visible = false;
                Invalidate();
                return;
            }

            ShowConfirmPanelNearSelection();
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            ConfirmSelection();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ConfirmSelection();
            return;
        }

        if (e.KeyCode != Keys.Escape)
        {
            return;
        }

        CancelSelection();
    }

    private void ConfirmSelection()
    {
        if (SelectedRectangle.Width < 6 || SelectedRectangle.Height < 6)
        {
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelSelection()
    {
        SelectedRectangle = Rectangle.Empty;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void ShowConfirmPanelNearSelection()
    {
        var localSelected = ToClientRectangle(SelectedRectangle);
        var panelX = Math.Clamp(localSelected.Left, 6, ClientSize.Width - _confirmPanel.Width - 6);

        // Prefer placing action panel above the selection so it is easier to discover.
        var proposedAbove = localSelected.Top - _confirmPanel.Height - 8;
        var panelY = proposedAbove >= 6
            ? proposedAbove
            : Math.Clamp(localSelected.Bottom + 8, 6, ClientSize.Height - _confirmPanel.Height - 6);

        _confirmPanel.Location = new Point(panelX, panelY);
        _confirmPanel.Visible = true;
        _confirmPanel.BringToFront();
    }

    private Rectangle BuildAbsoluteRectangle(Point p1, Point p2)
    {
        var left = Math.Min(p1.X, p2.X);
        var top = Math.Min(p1.Y, p2.Y);
        var width = Math.Abs(p1.X - p2.X);
        var height = Math.Abs(p1.Y - p2.Y);

        return new Rectangle(left + Bounds.Left, top + Bounds.Top, width, height);
    }

    private Rectangle ToClientRectangle(Rectangle absolute)
    {
        return new Rectangle(
            absolute.Left - Bounds.Left,
            absolute.Top - Bounds.Top,
            absolute.Width,
            absolute.Height);
    }
}
