// GlowOverlay.cs — Transparent, click-through overlay that draws a soft glow
// highlight over the copied selection, then fades out.
//
// Design notes:
//   • Uses a standard WinForms Form with AllowTransparency = true. WinForms
//     internally implements this with UpdateLayeredWindow, so there is no WPF
//     or DirectComposition dependency.
//   • TransparencyKey = Color.Fuchsia makes the background a "hole" through
//     which the desktop is visible. Only the drawn highlight regions are opaque.
//   • WS_EX_TRANSPARENT lets mouse clicks pass through (click-through window).
//   • WS_EX_NOACTIVATE prevents the overlay from stealing keyboard focus.
//   • A single Form covers the union bounding box of all selection rectangles
//     and draws all of them internally — avoids the GDI handle cost of one
//     window per rectangle.
//   • Animation: Opacity decreases from InitialOpacity to 0 over ~350ms using
//     a 16ms WinForms Timer (≈60 fps). The form self-destructs when done.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HighlightOnCopy;

internal sealed class GlowOverlay : Form
{
    // Highlight colour: a soft blue-white that is visible on light and dark backgrounds.
    // Uses full opacity here; the Form's Opacity property provides the fade.
    private static readonly Color FillColor = Color.FromArgb(255, 100, 180, 255);
    private static readonly Color BorderColor = Color.FromArgb(255, 140, 210, 255);

    private const double InitialOpacity = 0.82;
    // FadeStep per 16ms tick → total fade time ≈ InitialOpacity / FadeStep * 16ms ≈ 330ms
    private const double FadeStep = 0.04;
    private const int TimerInterval = 16; // ms — matches 60 Hz display refresh

    // Transparent background colour (must not appear in any drawn highlight).
    private static readonly Color TransparentHole = Color.Fuchsia;

    private readonly Rectangle[] _screenRects; // In screen/physical pixel coordinates
    private readonly System.Windows.Forms.Timer _fadeTimer;

    // ------------------------
    // ---- Public factory ----
    // ------------------------

    /// <summary>
    /// Creates and immediately shows a glow overlay over the given screen rectangles.
    /// The overlay is self-managed and disposes itself when the animation finishes.
    /// </summary>
    public static void ShowOver(Rectangle[] screenRects)
    {
        if (screenRects.Length == 0) return;

        // Compute the union of all rectangles so the Form is sized to contain all.
        var union = screenRects[0];
        foreach (var r in screenRects)
            union = Rectangle.Union(union, r);

        var overlay = new GlowOverlay(screenRects, union);
        overlay.Show();
        overlay._fadeTimer.Start();
    }

    // ----------------------
    // ---- Construction ----
    // ----------------------

    private GlowOverlay(Rectangle[] screenRects, Rectangle unionBounds)
    {
        _screenRects = screenRects;

        // ---- Window appearance ----
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        // Expand 6px in every direction so the border glow is not clipped.
        Bounds = Rectangle.Inflate(unionBounds, 6, 6);
        TopMost = true;
        ShowInTaskbar = false;
        AllowTransparency = true;
        TransparencyKey = TransparentHole;
        BackColor = TransparentHole; // entire background is transparent
        Opacity = InitialOpacity;

        // ---- Fade-out timer ----
        _fadeTimer = new System.Windows.Forms.Timer { Interval = TimerInterval };
        _fadeTimer.Tick += OnFadeTick;
    }

    // --------------------------------
    // ---- Window style overrides ----
    // --------------------------------

    /// <summary>Prevent the overlay from being listed in Alt+Tab or the taskbar.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT; // Mouse events pass through
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE;  // Never steals keyboard focus
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;  // Absent from Alt+Tab
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
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Convert screen coordinates to form-local coordinates.
        int originX = Location.X;
        int originY = Location.Y;

        using var fillBrush = new SolidBrush(FillColor);
        using var borderPen = new Pen(BorderColor, 1.5f);

        foreach (var screenRect in _screenRects)
        {
            var local = new Rectangle(
                screenRect.X - originX,
                screenRect.Y - originY,
                screenRect.Width,
                screenRect.Height);

            FillRoundedRect(g, fillBrush, local, cornerRadius: 3);
            DrawRoundedRect(g, borderPen, local, cornerRadius: 3);
        }
    }

    // -------------------
    // ---- Animation ----
    // -------------------

    private void OnFadeTick(object? sender, EventArgs e)
    {
        Opacity -= FadeStep;
        if (Opacity <= 0.01)
        {
            _fadeTimer.Stop();
            Close(); // triggers FormClosed → Dispose
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _fadeTimer.Stop();
        _fadeTimer.Dispose();
        base.OnFormClosed(e);
    }

    // ---------------------------------------------------------------------------
    // ---- GDI+ helpers — rounded rectangle (not in System.Drawing natively) ----
    // ---------------------------------------------------------------------------

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
    {
        int d = radius * 2;
        // Clamp diameter so it fits even for very small rects
        d = Math.Min(d, Math.Min(rect.Width, rect.Height));
        int x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;

        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void FillRoundedRect(Graphics g, Brush brush, Rectangle rect, int cornerRadius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var path = CreateRoundedRectPath(rect, cornerRadius);
        g.FillPath(brush, path);
    }

    private static void DrawRoundedRect(Graphics g, Pen pen, Rectangle rect, int cornerRadius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var path = CreateRoundedRectPath(rect, cornerRadius);
        g.DrawPath(pen, path);
    }
}
