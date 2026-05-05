// CopiedToast.cs — Small "✓ Copied" pill that appears near the mouse cursor
// when Tier 4 (window bounding rect) fallback is triggered.
//
// Design:
//   • Blue-white pill matching GlowOverlay's color palette and fade animation.
//   • Anchored to Cursor.Position at call time, offset +14px right/down.
//   • Screen-edge guard: flips to left/above cursor if pill would overflow.
//   • Animation: appear at 0.82 opacity → fade out over ~330ms (same as GlowOverlay).
//   • Same window style flags as GlowOverlay: click-through, no focus steal,
//     no taskbar entry, self-disposing when animation completes.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HighlightOnCopy;

internal sealed class CopiedToast : Form
{
    private static readonly Color FillColor = Color.FromArgb(255, 74, 157, 232);
    private static readonly Color BorderColor = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color TextColor_ = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color TransparentHole = Color.Fuchsia;

    private const string ToastText = "✓  Copied";
    private const int CornerRadius = 20;
    private const int PaddingH = 16;
    private const int PaddingV = 8;
    private const int CursorOffset = 14;

    private const double InitialOpacity = 1;
    private const double FadeStep = 0.03;  // ~330ms fade at 16ms/tick
    private const int TimerInterval = 16;
    private const int HoldMs = 800;

    private readonly System.Windows.Forms.Timer _holdTimer;
    private readonly System.Windows.Forms.Timer _fadeTimer;

    // -------------------------
    // ---- Public factory  ----
    // -------------------------

    /// <summary>
    /// Shows a "✓ Copied" pill near the current cursor position.
    /// The toast is self-managed and disposes itself when the animation finishes.
    /// </summary>
    public static void ShowNearCursor()
    {
        var cursorPos = Cursor.Position;

        using var font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
        SizeF textSize;
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
            textSize = g.MeasureString(ToastText, font);

        int w = (int)textSize.Width + PaddingH * 2;
        int h = (int)textSize.Height + PaddingV * 2;

        var workArea = Screen.GetWorkingArea(cursorPos);
        int x = cursorPos.X + CursorOffset;
        int y = cursorPos.Y + CursorOffset;
        if (x + w > workArea.Right) x = cursorPos.X - w - 4;
        if (y + h > workArea.Bottom) y = cursorPos.Y - h - 4;
        x = Math.Max(workArea.Left, x);
        y = Math.Max(workArea.Top, y);

        var toast = new CopiedToast(new Rectangle(x, y, w, h), font.ToHfont(), w, h);
        toast.Show();
        toast._holdTimer.Start();
    }

    // ----------------------
    // ---- Construction ----
    // ----------------------

    private readonly IntPtr _fontHandle;
    private readonly int _width;
    private readonly int _height;

    private CopiedToast(Rectangle bounds, IntPtr fontHandle, int width, int height)
    {
        _fontHandle = fontHandle;
        _width = width;
        _height = height;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        AllowTransparency = true;
        TransparencyKey = TransparentHole;
        BackColor = TransparentHole;
        Opacity = InitialOpacity;

        _holdTimer = new System.Windows.Forms.Timer { Interval = HoldMs };
        _holdTimer.Tick += OnHoldTick;
        _fadeTimer = new System.Windows.Forms.Timer { Interval = TimerInterval };
        _fadeTimer.Tick += OnFadeTick;
    }

    // --------------------------------
    // ---- Window style overrides ----
    // --------------------------------

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT;
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    // ------------------
    // ---- Painting ----
    // ------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(1, 1, _width - 2, _height - 2);

        using var fillBrush = new SolidBrush(FillColor);
        FillRoundedRect(g, fillBrush, rect, CornerRadius);

        using var borderPen = new Pen(BorderColor, 1.5f);
        DrawRoundedRect(g, borderPen, rect, CornerRadius);

        using var font = Font.FromHfont(_fontHandle);
        using var textBrush = new SolidBrush(TextColor_);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(ToastText, font, textBrush, rect, format);
    }

    // -------------------
    // ---- Animation ----
    // -------------------

    private void OnHoldTick(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        _fadeTimer.Start();
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        Opacity -= FadeStep;
        if (Opacity <= 0.01)
        {
            _fadeTimer.Stop();
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _holdTimer.Stop();
        _holdTimer.Dispose();
        _fadeTimer.Stop();
        _fadeTimer.Dispose();
        base.OnFormClosed(e);
    }

    // -------------------------
    // ---- GDI+ helpers    ----
    // -------------------------

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        int x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void FillRoundedRect(Graphics g, Brush brush, Rectangle rect, int radius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var path = CreateRoundedRectPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRoundedRect(Graphics g, Pen pen, Rectangle rect, int radius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var path = CreateRoundedRectPath(rect, radius);
        g.DrawPath(pen, path);
    }
}
