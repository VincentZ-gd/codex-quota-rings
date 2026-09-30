using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class MembershipExpiry
{
    internal static string Value
    {
        get
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\CodexQuotaRings"))
                {
                    string value = key == null ? null : key.GetValue("MembershipExpiry") as string;
                    DateTime date;
                    return DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out date) ? value : null;
                }
            }
            catch { return null; }
        }
    }

    internal static void Set(DateTime? date)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\CodexQuotaRings"))
        {
            if (date.HasValue) key.SetValue("MembershipExpiry", date.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            else key.DeleteValue("MembershipExpiry", false);
        }
    }
}

internal sealed class MembershipExpiryDialog : Form
{
    internal MembershipExpiryDialog()
    {
        Text = "设置 Exp 到期日";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(390, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Controls.Add(new Label { Text = "请填写已确认的会员到期日。\n此日期由你手动设置，不会自动续期。", Location = new Point(20, 18), Size = new Size(350, 48) });
        DateTimePicker picker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", ShowCheckBox = true, Location = new Point(20, 77), Width = 220 };
        string current = MembershipExpiry.Value;
        if (current != null) picker.Value = DateTime.ParseExact(current, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        picker.Checked = current != null;
        Controls.Add(picker);
        Controls.Add(new Label { Text = "取消日期前的勾选可清除 Exp。", Location = new Point(20, 108), Size = new Size(350, 24) });
        Button save = new Button { Text = "保存", Location = new Point(210, 146), Size = new Size(75, 30) };
        Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(295, 146), Size = new Size(75, 30) };
        save.Click += delegate {
            try { MembershipExpiry.Set(picker.Checked ? (DateTime?)picker.Value.Date : null); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败"); }
        };
        Controls.Add(save); Controls.Add(cancel);
        AcceptButton = save; CancelButton = cancel;
    }
}

internal static class RingArt
{
    internal const int CanvasWidth = 238;
    internal static readonly RectangleF RefreshBounds = new RectangleF(126, 23, 22, 19);
    internal static GraphicsPath Round(float x, float y, float width, float height, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static bool IsDark()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return key != null && Convert.ToInt32(key.GetValue("SystemUsesLightTheme", 1)) == 0;
        }
        catch { return false; }
    }

    internal static Color Accent(QuotaWindow window, bool weekly, bool stale)
    {
        if (stale || window == null) return Color.FromArgb(139, 148, 164);
        if (window.Remaining <= 10) return Color.FromArgb(231, 83, 99);
        if (window.Remaining <= 20) return Color.FromArgb(218, 151, 38);
        return weekly ? Color.FromArgb(20, 168, 126) : Color.FromArgb(51, 129, 237);
    }

    internal static void Text(Graphics g, string text, float size, FontStyle style, Color color, RectangleF rect, StringAlignment align)
    {
        using (Font font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel))
        using (SolidBrush brush = new SolidBrush(color))
        using (StringFormat format = new StringFormat())
        {
            format.Alignment = align;
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags = StringFormatFlags.NoWrap;
            format.Trimming = StringTrimming.EllipsisCharacter;
            g.DrawString(text, font, brush, rect, format);
        }
    }

    internal static Bitmap Render(float scale, QuotaWindow five, QuotaWindow week, bool stale, bool dark, string expiry = null, bool refreshHover = false, bool refreshing = false)
    {
        int width = (int)Math.Round(CanvasWidth * scale), height = (int)Math.Round(44 * scale);
        Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (Bitmap large = new Bitmap(width * 3, height * 3, PixelFormat.Format32bppPArgb))
        using (Graphics g = Graphics.FromImage(large))
        {
            g.ScaleTransform(scale * 3, scale * 3);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            // No panel fill or border: the taskbar shows through every empty pixel.
            DrawRing(g, 13, five, "5h", false, stale, dark);
            DrawRing(g, 75, week, "7d", true, stale, dark);
            using (Pen separator = new Pen(dark ? Color.FromArgb(35, 255, 255, 255) : Color.FromArgb(25, 30, 44, 64), 0.7f))
                g.DrawLine(separator, 62, 14, 62, 30);
            Text(g, "Exp：" + (String.IsNullOrEmpty(expiry) ? "未设置" : expiry), 10.7f, FontStyle.Regular,
                dark ? Color.FromArgb(222, 232, 247) : Color.FromArgb(56, 69, 88),
                new RectangleF(126, 3, 111, 19), StringAlignment.Near);
            Color buttonInk = dark ? Color.FromArgb(190, 212, 243) : Color.FromArgb(72, 101, 142);
            using (GraphicsPath shape = Round(RefreshBounds.X, RefreshBounds.Y, RefreshBounds.Width, RefreshBounds.Height, 6))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(refreshHover ? 45 : 20, buttonInk))) g.FillPath(fill, shape);
            if (refreshing)
                Text(g, "…", 12, FontStyle.Bold, buttonInk, RefreshBounds, StringAlignment.Center);
            else
            {
                using (Pen arrow = new Pen(buttonInk, 1.35f))
                {
                    arrow.StartCap = arrow.EndCap = LineCap.Round;
                    g.DrawArc(arrow, 132, 27, 10, 10, 40, 290);
                    g.DrawLines(arrow, new PointF[] { new PointF(138.5f, 30), new PointF(141.5f, 29.5f), new PointF(141, 26.5f) });
                }
            }
            using (Graphics output = Graphics.FromImage(result))
            {
                output.CompositingMode = CompositingMode.SourceCopy;
                output.InterpolationMode = InterpolationMode.HighQualityBicubic;
                output.PixelOffsetMode = PixelOffsetMode.HighQuality;
                output.DrawImage(large, new Rectangle(0, 0, width, height));
            }
        }
        return result;
    }

    private static void DrawRing(Graphics g, float x, QuotaWindow window, string label, bool weekly, bool stale, bool dark)
    {
        Color accent = Accent(window, weekly, stale);
        using (Pen track = new Pen(dark ? Color.FromArgb(57, 66, 80) : Color.FromArgb(228, 233, 240), 2.6f))
        using (LinearGradientBrush gradient = new LinearGradientBrush(new RectangleF(x, 4, 36, 36), accent, Color.FromArgb(accent.A, Math.Min(255, accent.R + 15), Math.Min(255, accent.G + 30), Math.Min(255, accent.B + 18)), 45f))
        using (Pen progress = new Pen(gradient, 2.6f))
        {
            g.DrawEllipse(track, x, 4, 36, 36);
            progress.StartCap = progress.EndCap = LineCap.Round;
            if (window != null && window.Remaining > 0)
                g.DrawArc(progress, x, 4, 36, 36, -90, 360f * window.Remaining / 100f);
        }
        string value = window == null ? "—" : window.Remaining.ToString() + "%";
        Text(g, value, value.Length > 3 ? 10.7f : 12.3f, FontStyle.Bold,
            stale ? Color.FromArgb(145, 153, 168) : (dark ? Color.FromArgb(238, 244, 253) : Color.FromArgb(40, 53, 73)),
            new RectangleF(x - 1, 9, 38, 17), StringAlignment.Center);
        Text(g, label, 7.5f, FontStyle.Regular, dark ? Color.FromArgb(167, 181, 200) : Color.FromArgb(112, 128, 148),
            new RectangleF(x, 25, 36, 10), StringAlignment.Center);
    }
}

internal static class NativeDock
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Size { public int Width, Height; public Size(int w, int h) { Width = w; Height = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    internal delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destDc, ref Point dest, ref Size size, IntPtr sourceDc, ref Point source, int color, ref Blend blend, int flags);

    internal static float Dpi(IntPtr window)
    {
        try { uint dpi = GetDpiForWindow(window); return dpi == 0 ? 1f : dpi / 96f; }
        catch (EntryPointNotFoundException) { return 1f; }
    }

    internal static void Present(Form form, Bitmap image)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        IntPtr bitmap = image.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memoryDc, bitmap);
        try
        {
            Point dest = new Point(form.Left, form.Top), source = new Point(0, 0);
            Size size = new Size(image.Width, image.Height);
            Blend blend = new Blend { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(form.Handle, screenDc, ref dest, ref size, memoryDc, ref source, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SelectObject(memoryDc, old); DeleteObject(bitmap); DeleteDC(memoryDc); ReleaseDC(IntPtr.Zero, screenDc); }
    }
}

internal sealed class TaskbarRings : Form
{
    private readonly Timer placementTimer = new Timer();
    private readonly Action showDetails;
    private readonly Action refresh;
    private readonly ToolTip hint = new ToolTip();
    private QuotaWindow five, week;
    private bool stale, dark;
    private float scale = 1f;
    private int offset;
    private int defaultX;
    private int minX, maxX;
    private Point dragStart, originalLocation;
    private bool dragging, moved;
    private bool refreshHover, refreshPressed, refreshing;
    private long hiddenSince;
    private IntPtr foregroundHook;
    private NativeDock.WinEventDelegate foregroundCallback;
    private string expiry = MembershipExpiry.Value;

    internal TaskbarRings(ContextMenuStrip menu, Action details, Action refreshAction)
    {
        Text = "Codex 额度 · 任务栏";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ContextMenuStrip = menu;
        showDetails = details;
        refresh = refreshAction;
        dark = RingArt.IsDark();
        Size = new Size(RingArt.CanvasWidth, 44);
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\CodexQuotaRings"))
                if (key != null) offset = Convert.ToInt32(key.GetValue("TaskbarOffset", 0));
        }
        catch { }
        placementTimer.Interval = 1000;
        placementTimer.Tick += delegate { PlaceOnTaskbar(); };
        Shown += delegate {
            PlaceOnTaskbar(); Redraw(); placementTimer.Start();
            if (foregroundHook == IntPtr.Zero)
            {
                foregroundCallback = delegate {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) PlaceOnTaskbar(); });
                };
                foregroundHook = NativeDock.SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
            }
        };
        hint.SetToolTip(this, "5h：5 小时剩余 · 7d：每周剩余\n点击查看详情，拖动调整位置");
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80000 | 0x80 | 0x08000000; return p; }
    }

    internal void UpdateValues(QuotaWindow nextFive, QuotaWindow nextWeek, bool isStale)
    { five = nextFive; week = nextWeek; stale = isStale; Redraw(); }

    internal void UpdateExpiry() { expiry = MembershipExpiry.Value; PlaceOnTaskbar(); Redraw(); }
    internal void SetRefreshing(bool value) { refreshing = value; Redraw(); }

    private void Redraw()
    {
        if (!IsHandleCreated || IsDisposed) return;
        using (Bitmap bitmap = RingArt.Render(scale, five, week, stale, dark, expiry, refreshHover, refreshing)) NativeDock.Present(this, bitmap);
    }

    internal void ResetPosition()
    { offset = 0; SavePosition(); PlaceOnTaskbar(); }

    private void SavePosition()
    {
        try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\CodexQuotaRings")) key.SetValue("TaskbarOffset", offset); }
        catch { }
    }

    private void PlaceOnTaskbar()
    {
        if (dragging) return;
        IntPtr taskbar = NativeDock.FindWindow("Shell_TrayWnd", null);
        NativeDock.Rect rect;
        if (taskbar == IntPtr.Zero || !NativeDock.GetWindowRect(taskbar, out rect)) return;
        Rectangle screen = Screen.FromHandle(taskbar).Bounds;
        bool hidden = !NativeDock.IsWindowVisible(taskbar) || rect.Top >= screen.Bottom - 2 || rect.Bottom <= screen.Top + 2;
        // Foreground changes and switcher overlays must never hide the quota display.
        // Only a taskbar that stays physically hidden for two seconds hides this window.
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (hidden)
        {
            if (hiddenSince == 0) hiddenSince = now;
            if ((now - hiddenSince) / (double)System.Diagnostics.Stopwatch.Frequency >= 2)
            { if (Visible) Hide(); return; }
        }
        else hiddenSince = 0;
        float nextScale = NativeDock.Dpi(taskbar);
        bool nextDark = RingArt.IsDark();
        bool rerender = Math.Abs(nextScale - scale) > 0.01 || nextDark != dark;
        scale = nextScale; dark = nextDark;
        int width = (int)Math.Round(RingArt.CanvasWidth * scale), height = Math.Min((int)Math.Round(44 * scale), rect.Bottom - rect.Top - 2);
        NativeDock.Rect tray;
        IntPtr trayHandle = NativeDock.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        int rightEdge = rect.Right - (int)(300 * scale);
        if (trayHandle != IntPtr.Zero && NativeDock.GetWindowRect(trayHandle, out tray)) rightEdge = tray.Left;
        defaultX = rightEdge - width - (int)(8 * scale);
        minX = rect.Left + (int)(24 * scale);
        maxX = Math.Max(minX, rightEdge - width - (int)(4 * scale));
        int x = Math.Max(minX, Math.Min(maxX, defaultX + offset));
        int y = rect.Top + (rect.Bottom - rect.Top - height) / 2;
        if (!Visible) Show();
        if (Left != x || Top != y || Width != width || Height != height || rerender)
        {
            NativeDock.SetWindowPos(Handle, new IntPtr(-1), x, y, width, height, 0x10);
            Redraw();
        }
        else NativeDock.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (RingArt.RefreshBounds.Contains(e.X / scale, e.Y / scale))
        { refreshPressed = true; Capture = true; return; }
        dragging = true; moved = false; Capture = true;
        dragStart = Cursor.Position; originalLocation = Location;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hover = RingArt.RefreshBounds.Contains(e.X / scale, e.Y / scale);
        if (hover != refreshHover)
        {
            refreshHover = hover; Cursor = hover ? Cursors.Hand : Cursors.Default;
            hint.SetToolTip(this, hover ? "立即刷新额度" : "5h：5 小时剩余 · 7d：每周剩余\n点击查看详情，拖动调整位置");
            Redraw();
        }
        if (!dragging) return;
        int dx = Cursor.Position.X - dragStart.X;
        if (Math.Abs(dx) > 4) moved = true;
        if (moved) Left = Math.Max(minX, Math.Min(maxX, originalLocation.X + dx));
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (refreshPressed)
        {
            refreshPressed = false; Capture = false;
            if (!refreshing && RingArt.RefreshBounds.Contains(e.X / scale, e.Y / scale)) refresh();
            return;
        }
        if (!dragging) return;
        dragging = false; Capture = false;
        if (moved) { offset = Left - defaultX; SavePosition(); }
        else showDetails();
    }
    protected override void OnMouseLeave(EventArgs e)
    { base.OnMouseLeave(e); refreshHover = false; Cursor = Cursors.Default; Redraw(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { if (foregroundHook != IntPtr.Zero) NativeDock.UnhookWinEvent(foregroundHook); placementTimer.Dispose(); hint.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class QuotaDetails : Form
{
    private readonly float scale;
    private readonly bool dark;
    private QuotaResult latest;
    private bool stale;
    private readonly ToolTip hint = new ToolTip();

    internal QuotaDetails(Rectangle anchor)
    {
        scale = NativeDock.Dpi(NativeDock.FindWindow("Shell_TrayWnd", null));
        dark = RingArt.IsDark();
        Text = "Codex 额度详情";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size((int)(320 * scale), (int)(226 * scale));
        BackColor = dark ? Color.FromArgb(28, 33, 43) : Color.FromArgb(250, 252, 255);
        using (GraphicsPath shape = RingArt.Round(0, 0, Width, Height, 14 * scale)) Region = new Region(shape);
        Rectangle working = Screen.FromRectangle(anchor).WorkingArea;
        Location = new Point(Math.Max(working.Left + 8, Math.Min(working.Right - Width - 8, anchor.Right - Width)), Math.Max(working.Top + 8, anchor.Top - Height - (int)(10 * scale)));
        KeyPreview = true;
        KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
    }
    protected override CreateParams CreateParams
    { get { CreateParams p = base.CreateParams; p.ClassStyle |= 0x20000; p.ExStyle |= 0x80; return p; } }

    internal void UpdateValues(QuotaResult result, bool isStale, string error)
    {
        latest = result; stale = isStale;
        hint.SetToolTip(this, isStale ? "连接暂时不可用，可点击任务栏 Exp 下方按钮刷新。" : "每 5 分钟更新一次；点击任务栏 Exp 下方按钮立即刷新。");
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color ink = dark ? Color.FromArgb(237, 243, 252) : Color.FromArgb(34, 47, 66);
        Color muted = dark ? Color.FromArgb(154, 169, 192) : Color.FromArgb(115, 130, 151);
        RingArt.Text(g, "Codex 额度", 18, FontStyle.Bold, ink, new RectangleF(20, 16, 230, 28), StringAlignment.Near);
        RingArt.Text(g, "剩余百分比", 11, FontStyle.Regular, muted, new RectangleF(20, 43, 230, 20), StringAlignment.Near);
        RingArt.Text(g, "×", 22, FontStyle.Regular, muted, new RectangleF(275, 15, 30, 30), StringAlignment.Center);
        Card(g, new RectangleF(16, 77, 140, 110), latest == null ? null : latest.FiveHours, false, ink, muted);
        Card(g, new RectangleF(164, 77, 140, 110), latest == null ? null : latest.Weekly, true, ink, muted);
        string status = stale ? (latest == null ? "连接暂时不可用 · 正在自动重试" : "连接暂时不可用 · 显示上次读数") : (latest == null ? "正在查询额度…" : "更新于 " + latest.CheckedAt.ToString("HH:mm") + " · 每 5 分钟刷新");
        RingArt.Text(g, status, 10, FontStyle.Regular, muted, new RectangleF(20, 194, 280, 20), StringAlignment.Near);
    }

    private void Card(Graphics g, RectangleF rect, QuotaWindow window, bool weekly, Color ink, Color muted)
    {
        Color accent = RingArt.Accent(window, weekly, stale);
        using (GraphicsPath shape = RingArt.Round(rect.X, rect.Y, rect.Width, rect.Height, 12))
        using (SolidBrush brush = new SolidBrush(dark ? Color.FromArgb(36, 43, 56) : Color.White)) g.FillPath(brush, shape);
        RingArt.Text(g, weekly ? "每周" : "5 小时", 11, FontStyle.Regular, muted, new RectangleF(rect.X + 14, rect.Y + 9, 110, 20), StringAlignment.Near);
        RingArt.Text(g, window == null ? "—" : window.Remaining.ToString() + "%", 29, FontStyle.Bold, accent, new RectangleF(rect.X + 12, rect.Y + 28, 116, 44), StringAlignment.Near);
        RingArt.Text(g, "重置 " + CodexQuotaRings.FormatReset(window), 9, FontStyle.Regular, muted, new RectangleF(rect.X + 14, rect.Y + 81, 118, 20), StringAlignment.Near);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        float x = e.X / scale, y = e.Y / scale;
        if (x >= 270 && y <= 52) Close();
    }
    protected override void Dispose(bool disposing)
    { if (disposing) hint.Dispose(); base.Dispose(disposing); }
}
