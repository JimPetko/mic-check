using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MicCheck;

/// <summary>Notification-area icon with a small menu. Raises events; the window decides what they do.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;
    private readonly ContextMenuStrip _menu;
    private readonly Font _boldFont;
    private IntPtr _iconHandle;
    private readonly ToolStripMenuItem _virtualMicItem;
    private bool _hintShown;

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? VirtualMicToggled;

    public TrayIcon()
    {
        _image = CreateMicrophoneIcon();

        _virtualMicItem = new ToolStripMenuItem("Send voice to virtual mic") { CheckOnClick = true };
        _virtualMicItem.Click += (_, _) => VirtualMicToggled?.Invoke(this, _virtualMicItem.Checked);

        _boldFont = new Font(SystemFonts.MenuFont!, System.Drawing.FontStyle.Bold);
        var open = new ToolStripMenuItem("Open Mic Check") { Font = _boldFont };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange([open, new ToolStripSeparator(), _virtualMicItem, new ToolStripSeparator(), exit]);

        _icon = new NotifyIcon { Icon = _image, ContextMenuStrip = _menu, Text = "Mic Check", Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void SetVirtualMicState(bool enabled, string tooltip)
    {
        _virtualMicItem.Checked = enabled;
        // NotifyIcon tooltips are limited to 127 characters.
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    /// <summary>Explains once per session that closing the window didn't quit the app.</summary>
    public void ShowStillRunningHint()
    {
        if (_hintShown)
            return;
        _hintShown = true;
        _icon.ShowBalloonTip(4000, "Mic Check is still running",
            "Your processed voice is still going to the virtual mic. Right-click this icon to exit.", ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        // NotifyIcon doesn't own its menu.
        _menu.Dispose();
        _boldFont.Dispose();
        _image.Dispose();
        DestroyIcon(_iconHandle);
    }

    private Icon CreateMicrophoneIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var body = new SolidBrush(Color.FromArgb(0x4C, 0xC2, 0xFF));
            using var pen = new Pen(Color.White, 2.5f);
            using var capsule = new GraphicsPath();
            capsule.AddArc(10, 2, 12, 12, 180, 180);
            capsule.AddArc(10, 8, 12, 12, 0, 180);
            capsule.CloseFigure();
            g.FillPath(body, capsule);
            g.DrawArc(pen, 6, 6, 20, 18, 0, 180);
            g.DrawLine(pen, 16, 24, 16, 29);
            g.DrawLine(pen, 11, 29.5f, 21, 29.5f);
        }

        _iconHandle = bitmap.GetHicon();
        return Icon.FromHandle(_iconHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
