using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class TaskbarPlacementCheck
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(out bool enabled);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void CheckDuringAnimation(IntPtr window, TaskbarRings rings, Rectangle bounds)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            Check(NativeDock.IsWindowVisible(window), "Animation hid rings");
            Check(rings.ScreenBounds == bounds, "Animation moved rings");
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0)
                Check(cloaked == 0, "Compositor cloaked rings during animation");
            Thread.Sleep(5);
        } while (watch.ElapsedMilliseconds < 160);
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            var visibility = new TaskbarVisibility();
            Check(visibility.Update(false, 0) == 1, "Visible taskbar should position rings");
            Check(visibility.Update(true, 10) == 0, "Transient hide must freeze coordinates");
            Check(visibility.Update(true, 1800) == 0, "Do not move rings offscreen during switch animation");
            Check(visibility.Update(false, 1900) == 1, "Visible taskbar must reset debounce");
            Check(visibility.Update(true, 2000) == 0, "A second short hide gets its own grace period");
            Check(visibility.Update(true, 3999) == 0, "Keep the previous bounds throughout grace");
            Check(visibility.Update(true, 4000) == -1, "Real auto-hide must still hide the rings");
            Check(visibility.Update(false, 4100) == 1, "Auto-hide recovery must be immediate");

            using (var menu = new ContextMenuStrip())
            using (var dock = new TaskbarRings(menu, delegate { }, delegate { }))
            {
                // No account request, background app replacement or desktop input.
                IntPtr taskbar = NativeDock.FindWindow("Shell_TrayWnd", null);
                if (Application.ExecutablePath.EndsWith("GuiCheck.exe", StringComparison.OrdinalIgnoreCase))
                    Check(taskbar != IntPtr.Zero, "Real-desktop check could not find Explorer taskbar");
                IntPtr handle = dock.Handle;
                dock.UpdateValues(new QuotaWindow { Remaining = 71 }, new QuotaWindow { Remaining = 84 }, false);
                dock.Show();
                Application.DoEvents();
                Check((GetWindowLong(handle, -20) & 0x08000000) != 0, "No-activate style missing");
                Check((GetWindowLong(handle, -20) & 0x80000) != 0, "Per-pixel layered style missing");
                if (taskbar != IntPtr.Zero)
                {
                    Check(dock.IsEmbedded, "Rings must be a native taskbar child");
                    Check(NativeDock.GetParent(handle) == taskbar, "Wrong taskbar parent");
                    Check((GetWindowLong(handle, -16) & unchecked((int)0x80000000)) == 0, "Popup style must be removed");
                    bool inDesktopWindows = false;
                    EnumWindows(delegate(IntPtr window, IntPtr unused) { if (window == handle) inDesktopWindows = true; return true; }, IntPtr.Zero);
                    Check(!inDesktopWindows, "Show Desktop must not enumerate rings as a desktop window");
                    Rectangle bounds = dock.ScreenBounds;
                    NativeDock.Rect taskbarBounds;
                    Check(NativeDock.GetWindowRect(taskbar, out taskbarBounds), "No taskbar bounds");
                    Check(bounds.IntersectsWith(Rectangle.FromLTRB(taskbarBounds.Left, taskbarBounds.Top, taskbarBounds.Right, taskbarBounds.Bottom)), "Child rings were positioned outside the taskbar");
                    using (var switchingWindow = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.FixedToolWindow, Size = new Size(150, 70), StartPosition = FormStartPosition.Manual, Location = new Point(50, 50) })
                    {
                        switchingWindow.Show();
                        for (int i = 0; i < 10; i++)
                        {
                            switchingWindow.WindowState = FormWindowState.Minimized;
                            CheckDuringAnimation(handle, dock, bounds);
                            Application.DoEvents();
                            Check(NativeDock.IsWindowVisible(handle), "Minimizing another window hid rings");
                            Check(dock.ScreenBounds == bounds, "Minimizing moved rings");
                            switchingWindow.WindowState = FormWindowState.Normal;
                            CheckDuringAnimation(handle, dock, bounds);
                            Application.DoEvents();
                            Check(NativeDock.IsWindowVisible(handle), "Restoring another window hid rings");
                            Check(dock.ScreenBounds == bounds, "Restoring moved rings");
                            // Refreshing an embedded layered bitmap must not move it.
                            dock.SetRefreshing(i % 2 == 0);
                            Check(dock.ScreenBounds == bounds, "Layered redraw changed child coordinates");
                        }
                    }
                    using (var details = new QuotaDetails(dock.ScreenBounds))
                    {
                        details.UpdateValues(new QuotaResult { FiveHours = new QuotaWindow { Remaining = 71 }, Weekly = new QuotaWindow { Remaining = 84 }, CheckedAt = DateTime.Now }, false, null);
                        details.Show();
                        Application.DoEvents();
                        Check(details.Bottom <= bounds.Top + 10, "Hover card anchor was not converted to screen coordinates");
                    }
                    Console.WriteLine("PASS: layered taskbar child, correct screen bounds, outside desktop-window enumeration, ten minimize/restore cycles and redraws");
                }
                bool composed;
                if (!dock.IsEmbedded && DwmIsCompositionEnabled(out composed) == 0 && composed)
                {
                    // EXCLUDED_FROM_PEEK is documented for Set, not Get.
                    // Check that the compositor accepts it on this real HWND.
                    int hr = NativeDock.KeepVisibleDuringPeek(handle);
                    Check(hr == 0, "DWM Peek exclusion failed: HRESULT " + hr.ToString("X8"));
                    Console.WriteLine("PASS: detached fallback has no-activate and DWM Peek exclusion");
                }
            }
            Console.WriteLine("PASS: switching animation retains bounds; sustained auto-hide and recovery");
            File.WriteAllText(Path.Combine(Application.StartupPath, "taskbar-native-check.txt"), "PASS: taskbar child, desktop enumeration exclusion, stable minimize/restore animation samples, layered redraw and hover-card screen coordinates.\r\nChecked: " + DateTime.Now.ToString("O"));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); File.WriteAllText(Path.Combine(Application.StartupPath, "taskbar-native-check.txt"), "FAIL: " + ex.ToString()); return 1; }
    }
}
