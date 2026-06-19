using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using KeywordGuard.Pro.Security;

namespace KeywordGuard.Pro.UI.Services;

/// <summary>
/// Ueberwacht das aktive Fenster und schliesst es bei Treffern.
/// Wird vom UI gestartet, wenn der Timer aktiv ist.
/// </summary>
public class WordWatcher
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint WM_CLOSE = 0x0010;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_W = 0x57;
    private const byte VK_F4 = 0x73;
    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public void Start(Func<List<BlockedItem>> itemsProvider, Func<bool> isActive)
    {
        if (_task != null && !_task.IsCompleted) return;

        _cts = new CancellationTokenSource();
        _task = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (!isActive())
                    {
                        await Task.Delay(500, _cts.Token);
                        continue;
                    }

                    var items = itemsProvider();
                    if (items.Count == 0)
                    {
                        await Task.Delay(500, _cts.Token);
                        continue;
                    }

                    IntPtr handle = GetForegroundWindow();
                    if (handle == IntPtr.Zero)
                    {
                        await Task.Delay(500, _cts.Token);
                        continue;
                    }

                    if (!IsBrowserProcess(handle))
                    {
                        await Task.Delay(500, _cts.Token);
                        continue;
                    }

                    string title = GetActiveWindowTitle(handle);
                    if (string.IsNullOrEmpty(title))
                    {
                        await Task.Delay(500, _cts.Token);
                        continue;
                    }

                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.Value)) continue;

                        bool hit = item.IsAggressive
                            ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
                            : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

                        if (hit)
                        {
                            CloseActiveWindow(handle);
                            await Task.Delay(1000, _cts.Token);
                            break;
                        }
                    }

                    await Task.Delay(500, _cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private static bool IsBrowserProcess(IntPtr hWnd)
    {
        try
        {
            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            if (processId == 0) return false;

            using (var proc = Process.GetProcessById((int)processId))
            {
                string procName = proc.ProcessName;
                string[] whitelist = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi" };
                foreach (string name in whitelist)
                {
                    if (string.Equals(name, procName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private static string GetActiveWindowTitle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return "";

        const int max = 512;
        var sb = new StringBuilder(max);
        return GetWindowText(handle, sb, max) > 0 ? sb.ToString() : "";
    }

    private static void CloseActiveWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        try 
        { 
            PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); 
        } 
        catch { }
    }
}