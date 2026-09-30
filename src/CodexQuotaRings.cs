// Codex Quota Rings - a small, read-only Windows notification-area app.
// Build: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe
//        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll
//        /out:CodexQuotaRings.exe CodexQuotaRings.cs TaskbarView.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal sealed class QuotaWindow
{
    public int Remaining;
    public long ResetsAt;
}

internal sealed class QuotaResult
{
    public QuotaWindow FiveHours;
    public QuotaWindow Weekly;
    public DateTime CheckedAt;
}

internal static class CodexQuotaRings
{
    private const string AppName = "Codex Quota Rings";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "CodexQuotaRings";
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    [STAThread]
    private static void Main(string[] args)
    {
        NativeDock.SetProcessDPIAware();
        if (args.Length > 0 && args[0] == "--preview")
        {
            using (Bitmap preview = RingArt.Render(2f, new QuotaWindow { Remaining = 52 }, new QuotaWindow { Remaining = 18 }, false, false))
                preview.Save(args.Length > 1 ? args[1] : "preview.png", System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        if (args.Length > 0 && args[0] == "--once")
        {
            try
            {
                QuotaResult result = Query();
                Console.WriteLine("5h=" + FormatPercent(result.FiveHours));
                Console.WriteLine("7d=" + FormatPercent(result.Weekly));
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length > 0 && args[0] == "--self-test")
        {
            try
            {
                string both = "{\"result\":{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":9,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":76,\"windowDurationMins\":10080}}}}}";
                QuotaResult result = ParseRateLimits(AsObject(Json.DeserializeObject(both)));
                if (result.FiveHours == null || result.FiveHours.Remaining != 91 || result.Weekly == null || result.Weekly.Remaining != 24)
                    throw new Exception("双窗口解析失败");
                string one = "{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":76,\"windowDurationMins\":10080},\"secondary\":null}}}";
                result = ParseRateLimits(AsObject(Json.DeserializeObject(one)));
                if (result.FiveHours != null || result.Weekly == null || result.Weekly.Remaining != 24)
                    throw new Exception("单窗口解析失败");
                Console.WriteLine("PASS: 5h and 7d windows; weekly-only fallback");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
            return;
        }

        bool demo = args.Length > 0 && args[0] == "--demo";
        if (!demo) ClosePreviousVersion();
        bool created;
        using (var mutex = new Mutex(true, demo ? @"Local\CodexQuotaRings.Demo" : @"Local\CodexQuotaRings.v2", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (!demo && AutoStartEnabled()) SetAutoStart(true);
            Application.Run(new TrayContext(demo, Array.IndexOf(args, "--background") >= 0));
        }
    }

    private static void ClosePreviousVersion()
    {
        string sibling = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "CodexQuotaRings.exe");
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\CodexQuotaRings\CodexQuotaRings.exe");
        foreach (string processName in new string[] { "CodexQuotaRings", "CodexQuotaRings-v2" })
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (process.Id == Process.GetCurrentProcess().Id) continue;
                    string path = process.MainModule.FileName;
                    string versionedSibling = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "CodexQuotaRings-v2.exe");
                    if (String.Equals(path, sibling, StringComparison.OrdinalIgnoreCase) || String.Equals(path, installed, StringComparison.OrdinalIgnoreCase) || String.Equals(path, versionedSibling, StringComparison.OrdinalIgnoreCase))
                    { process.Kill(); process.WaitForExit(3000); }
                }
                catch { }
            }
        }
    }

    internal static string FormatPercent(QuotaWindow window)
    {
        return window == null ? "N/A" : window.Remaining.ToString() + "%";
    }

    internal static string FormatReset(QuotaWindow window)
    {
        if (window == null || window.ResetsAt <= 0) return "未知";
        try { return DateTimeOffset.FromUnixTimeSeconds(window.ResetsAt).LocalDateTime.ToString("MM-dd HH:mm"); }
        catch { return "未知"; }
    }

    internal static bool AutoStartEnabled()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            return key != null && key.GetValue(RunName) != null;
    }

    internal static void SetAutoStart(bool enabled)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled) key.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --background");
            else key.DeleteValue(RunName, false);
        }
    }

    private static string FindCodex()
    {
        string explicitPath = Environment.GetEnvironmentVariable("CODEX_QUOTA_CODEX_EXE");
        if (!String.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath)) return explicitPath;
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string part in path.Split(Path.PathSeparator))
        {
            try
            {
                string file = Path.Combine(part.Trim('"'), "codex.exe");
                if (File.Exists(file)) return file;
            }
            catch { }
        }
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"OpenAI\Codex\bin");
        if (Directory.Exists(root))
        {
            string best = null;
            DateTime latest = DateTime.MinValue;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string file = Path.Combine(dir, "codex.exe");
                if (File.Exists(file) && File.GetLastWriteTimeUtc(file) > latest)
                {
                    best = file;
                    latest = File.GetLastWriteTimeUtc(file);
                }
            }
            if (best != null) return best;
        }
        throw new Exception("找不到 Codex CLI。请设置 CODEX_QUOTA_CODEX_EXE 环境变量。 ");
    }

    internal static QuotaResult Query()
    {
        var start = new ProcessStartInfo(FindCodex(), "app-server --listen stdio://");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        // Some packaged Codex builds do not resolve the user home in a detached process.
        // Point Codex at its existing profile when no explicit profile is configured.
        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEX_HOME")))
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string config = Path.Combine(profile, ".codex");
            if (Directory.Exists(config)) Environment.SetEnvironmentVariable("CODEX_HOME", config);
            if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME")) && Directory.Exists(profile))
                Environment.SetEnvironmentVariable("HOME", profile);
        }
        using (Process proc = new Process())
        {
            proc.StartInfo = start;
            if (!proc.Start()) throw new Exception("无法启动 Codex app-server。");
            var stderr = new StringBuilder();
            proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
            {
                if (eventArgs.Data != null && stderr.Length < 2000) stderr.AppendLine(eventArgs.Data);
            };
            proc.BeginErrorReadLine();
            try
            {
                Send(proc, "{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"codex_quota_rings\",\"title\":\"Codex Quota Rings\",\"version\":\"1.0.0\"}}}");
                Dictionary<string, object> init = ReadResponse(proc, 1, 20000, stderr);
                CheckError(init);
                Send(proc, "{\"method\":\"initialized\",\"params\":{}}");
                Send(proc, "{\"method\":\"account/rateLimits/read\",\"id\":2}");
                Dictionary<string, object> message = ReadResponse(proc, 2, 20000, stderr);
                CheckError(message);
                return ParseRateLimits(message);
            }
            finally
            {
                try { if (!proc.HasExited) proc.Kill(); } catch { }
                try { proc.WaitForExit(2000); } catch { }
            }
        }
    }

    private static void CheckError(Dictionary<string, object> response)
    {
        Dictionary<string, object> error = AsObject(Get(response, "error"));
        if (error != null) throw new Exception("Codex 返回错误: " + Convert.ToString(Get(error, "message")));
    }

    private static void Send(Process proc, string line)
    {
        proc.StandardInput.WriteLine(line);
        proc.StandardInput.Flush();
    }

    private static Dictionary<string, object> ReadResponse(Process proc, int id, int timeoutMs, StringBuilder stderr)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            Task<string> read = Task.Factory.StartNew(() => proc.StandardOutput.ReadLine());
            int remaining = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
            if (!read.Wait(remaining)) throw new Exception("Codex 额度查询超时。");
            string line = read.Result;
            if (line == null) throw new Exception("Codex app-server 已退出。" + stderr.ToString());
            Dictionary<string, object> message = null;
            try { message = AsObject(Json.DeserializeObject(line)); } catch { }
            if (message != null && Get(message, "id") != null && Convert.ToInt32(Get(message, "id")) == id)
                return message;
        }
        throw new Exception("Codex 额度查询超时。");
    }

    private static Dictionary<string, object> AsObject(object value) { return value as Dictionary<string, object>; }
    private static object Get(Dictionary<string, object> obj, string key)
    {
        object value;
        return obj != null && obj.TryGetValue(key, out value) ? value : null;
    }

    private static QuotaResult ParseRateLimits(Dictionary<string, object> message)
    {
        Dictionary<string, object> result = AsObject(Get(message, "result"));
        if (result == null) throw new Exception("额度查询没有返回结果。");
        Dictionary<string, object> byId = AsObject(Get(result, "rateLimitsByLimitId"));
        Dictionary<string, object> bucket = byId == null ? null : AsObject(Get(byId, "codex"));
        if (bucket == null) bucket = AsObject(Get(result, "rateLimits"));
        if (bucket == null) throw new Exception("账号未返回 Codex 额度。");
        var parsed = new QuotaResult();
        parsed.CheckedAt = DateTime.Now;
        AcceptWindow(parsed, AsObject(Get(bucket, "primary")));
        AcceptWindow(parsed, AsObject(Get(bucket, "secondary")));
        return parsed;
    }

    private static void AcceptWindow(QuotaResult result, Dictionary<string, object> data)
    {
        if (data == null || Get(data, "windowDurationMins") == null || Get(data, "usedPercent") == null) return;
        int minutes = Convert.ToInt32(Get(data, "windowDurationMins"));
        int remaining = Math.Max(0, Math.Min(100, (int)Math.Round(100 - Convert.ToDouble(Get(data, "usedPercent")))));
        long reset = Get(data, "resetsAt") == null ? 0 : Convert.ToInt64(Get(data, "resetsAt"));
        var window = new QuotaWindow { Remaining = remaining, ResetsAt = reset };
        if (minutes == 300) result.FiveHours = window;
        else if (minutes == 10080) result.Weekly = window;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    internal static Icon MakeIcon(QuotaWindow window, Color color, bool stale)
    {
        const int size = 64;
        using (Bitmap bitmap = new Bitmap(size, size))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            using (var track = new Pen(Color.FromArgb(140, 120, 120, 120), 7f))
            using (var progress = new Pen(stale ? Color.Gray : color, 7f))
            {
                track.StartCap = track.EndCap = LineCap.Round;
                progress.StartCap = progress.EndCap = LineCap.Round;
                graphics.DrawEllipse(track, 6, 6, 52, 52);
                if (window != null && window.Remaining > 0)
                    graphics.DrawArc(progress, 6, 6, 52, 52, -90, Math.Max(3, 360f * window.Remaining / 100f));
            }
            string label = window == null ? "?" : window.Remaining.ToString();
            float fontSize = label.Length == 3 ? 22f : 27f;
            using (var font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(stale || window == null ? Color.Gray : color))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                graphics.DrawString(label, font, brush, new RectangleF(6, 8, 52, 48), format);
            }
            IntPtr handle = bitmap.GetHicon();
            try { return (Icon)Icon.FromHandle(handle).Clone(); }
            finally { DestroyIcon(handle); }
        }
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon fiveIcon = new NotifyIcon();
    private readonly NotifyIcon weekIcon = new NotifyIcon();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly Control dispatcher = new Control();
    private ToolStripMenuItem startupItem;
    private QuotaResult latest;
    private bool stale;
    private string error;
    private int refreshing;
    private readonly TaskbarRings dock;
    private QuotaDetails details;
    private readonly bool demo;
    private bool showOnFirstRead;
    private int consecutiveFailures;

    internal TrayContext(bool demoMode, bool background)
    {
        demo = demoMode;
        showOnFirstRead = false; // Resident display; open details only when requested.
        IntPtr unused = dispatcher.Handle;
        ToolStripMenuItem refreshItem = new ToolStripMenuItem("立即刷新");
        refreshItem.Click += delegate { Refresh(); };
        menu.Items.Add(refreshItem);
        ToolStripMenuItem detailsItem = new ToolStripMenuItem("查看详情");
        detailsItem.Click += delegate { ShowDetails(); };
        menu.Items.Add(detailsItem);
        ToolStripMenuItem expiryItem = new ToolStripMenuItem("设置 Exp 到期日…");
        expiryItem.Click += delegate {
            using (MembershipExpiryDialog dialog = new MembershipExpiryDialog())
                if (dialog.ShowDialog() == DialogResult.OK) { dock.UpdateExpiry(); UpdateIcons(); }
        };
        menu.Items.Add(expiryItem);
        startupItem = new ToolStripMenuItem("开机启动");
        startupItem.Checked = CodexQuotaRings.AutoStartEnabled();
        startupItem.Click += delegate
        {
            try { CodexQuotaRings.SetAutoStart(!startupItem.Checked); startupItem.Checked = !startupItem.Checked; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "开机启动设置失败"); }
        };
        menu.Items.Add(startupItem);
        ToolStripMenuItem resetPosition = new ToolStripMenuItem("恢复任务栏默认位置");
        resetPosition.Click += delegate { dock.ResetPosition(); };
        menu.Items.Add(resetPosition);
        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += delegate { ExitThread(); };
        menu.Items.Add(exitItem);

        fiveIcon.Text = "Codex 5小时: 正在查询";
        weekIcon.Text = "Codex 周额度: 正在查询";
        fiveIcon.Icon = CodexQuotaRings.MakeIcon(null, Color.DodgerBlue, false);
        weekIcon.Icon = CodexQuotaRings.MakeIcon(null, Color.MediumSeaGreen, false);
        fiveIcon.ContextMenuStrip = menu;
        weekIcon.ContextMenuStrip = menu;
        fiveIcon.MouseClick += OnClick;
        weekIcon.MouseClick += OnClick;
        fiveIcon.Visible = true;
        weekIcon.Visible = false;
        dock = new TaskbarRings(menu, ShowDetails, Refresh);
        dock.Show();

        timer.Interval = 5 * 60 * 1000;
        timer.Tick += delegate { Refresh(); };
        timer.Start();
        if (demo)
        {
            latest = new QuotaResult { FiveHours = new QuotaWindow { Remaining = 52, ResetsAt = DateTimeOffset.Now.AddHours(2).ToUnixTimeSeconds() }, Weekly = new QuotaWindow { Remaining = 18, ResetsAt = DateTimeOffset.Now.AddDays(3).ToUnixTimeSeconds() }, CheckedAt = DateTime.Now };
            UpdateIcons();
        }
        else Refresh();
    }

    private void OnClick(object sender, MouseEventArgs args)
    {
        if (args.Button == MouseButtons.Left) ShowDetails();
    }

    private void Refresh()
    {
        if (demo) return;
        if (Interlocked.Exchange(ref refreshing, 1) != 0) return;
        dock.SetRefreshing(true);
        ThreadPool.QueueUserWorkItem(delegate
        {
            QuotaResult result = null;
            string failure = null;
            try { result = CodexQuotaRings.Query(); }
            catch (Exception ex) { failure = ex.Message; }
            try
            {
                if (fiveIcon.Visible)
                    dispatcher.BeginInvoke((MethodInvoker)delegate
                    {
                        if (result != null) { latest = result; stale = false; error = null; }
                        else { stale = true; error = failure; }
                        consecutiveFailures = result == null ? consecutiveFailures + 1 : 0;
                        timer.Interval = consecutiveFailures > 0 && consecutiveFailures <= 3 ? 30000 : 5 * 60 * 1000;
                        UpdateIcons();
                        Interlocked.Exchange(ref refreshing, 0);
                        dock.SetRefreshing(false);
                        if (showOnFirstRead) { showOnFirstRead = false; ShowDetails(); }
                    });
                else Interlocked.Exchange(ref refreshing, 0);
            }
            catch { Interlocked.Exchange(ref refreshing, 0); }
        });
    }

    private void UpdateIcons()
    {
        QuotaWindow five = latest == null ? null : latest.FiveHours;
        QuotaWindow week = latest == null ? null : latest.Weekly;
        SwapIcon(fiveIcon, CodexQuotaRings.MakeIcon(five, Color.FromArgb(24, 120, 210), stale));
        SwapIcon(weekIcon, CodexQuotaRings.MakeIcon(week, Color.FromArgb(13, 155, 95), stale));
        fiveIcon.Text = Tooltip("5小时", five);
        weekIcon.Text = Tooltip("周额度", week);
        dock.UpdateValues(five, week, stale);
        if (details != null && !details.IsDisposed) details.UpdateValues(latest, stale, error);
        try
        {
            string statusDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaRings");
            Directory.CreateDirectory(statusDir);
            File.WriteAllText(Path.Combine(statusDir, "status.json"), new JavaScriptSerializer().Serialize(new {
                version = "2.3.0-compact-refresh", successfulQuery = !stale && latest != null,
                membershipExpiry = MembershipExpiry.Value,
                fiveHoursRemaining = five == null ? (int?)null : five.Remaining,
                weeklyRemaining = week == null ? (int?)null : week.Remaining,
                checkedAt = latest == null ? null : latest.CheckedAt.ToString("o"),
                taskbarVisible = dock.Visible,
                position = new { x = dock.Left, y = dock.Top, width = dock.Width, height = dock.Height }
            }), Encoding.UTF8);
        }
        catch { }
    }

    private string Tooltip(string name, QuotaWindow window)
    {
        string status = stale ? " [旧数据]" : "";
        return "Codex " + name + "剩余 " + CodexQuotaRings.FormatPercent(window) + status;
    }

    private static void SwapIcon(NotifyIcon tray, Icon icon)
    {
        Icon old = tray.Icon;
        tray.Icon = icon;
        if (old != null) old.Dispose();
    }

    private void ShowDetails()
    {
        if (details != null && !details.IsDisposed) { details.Close(); details = null; return; }
        details = new QuotaDetails(dock.Bounds);
        details.UpdateValues(latest, stale, error);
        details.Show();
        details.Activate();
    }

    protected override void ExitThreadCore()
    {
        timer.Stop();
        dock.Close();
        dock.Dispose();
        if (details != null && !details.IsDisposed) details.Dispose();
        fiveIcon.Visible = false;
        weekIcon.Visible = false;
        fiveIcon.Dispose();
        weekIcon.Dispose();
        menu.Dispose();
        dispatcher.Dispose();
        timer.Dispose();
        base.ExitThreadCore();
    }
}
