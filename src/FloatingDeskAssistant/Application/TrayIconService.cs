using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace FloatingDeskAssistant.Application;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;
    private readonly ContextMenuStrip _menu;

    public TrayIconService(Action show, Action settings, Action toggleInteraction, Action exit)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FloatingDeskAssistant.Assets.willGo.ico")
            ?? throw new InvalidOperationException("Missing willGo tray icon.");
        _image = new Icon(stream);
        _menu = new ContextMenuStrip();
        _menu.Items.Add("显示 willGo", null, (_, _) => show());
        _menu.Items.Add("设置", null, (_, _) => settings());
        _menu.Items.Add("切换鼠标穿透", null, (_, _) => toggleInteraction());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => exit());
        _icon = new NotifyIcon { Icon = _image, Text = "willGo", ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => show();
    }

    public bool Visible => _icon.Visible;

    public void SetRecording(bool recording) => _icon.Text = recording ? "willGo - 正在采集麦克风与系统播放" : "willGo";

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }
}
