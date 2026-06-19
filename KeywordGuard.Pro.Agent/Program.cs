using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Management;
using System.Windows.Forms;
using Microsoft.Win32;
using KeywordGuard.Pro.Security;

namespace KeywordGuard.Pro.Agent;

static class Program
{
    // ============================================================
    // Windows API
    // ============================================================
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    const uint WM_CLOSE = 0x0010;
    const byte VK_CONTROL = 0x11;
    const byte VK_W = 0x57;
    const byte VK_F4 = 0x73;
    const byte VK_MENU = 0x12;
    const uint KEYEVENTF_KEYUP = 0x0002;

    // ============================================================
    // Zustand
    // ============================================================
    private static bool _running = true;
    private static bool _isShuttingDown = false;
    private static bool _wasEverActivated = false; // Wurde der Schutz jemals aktiviert?
    private static readonly object _sessionEndingLock = new();
    private static GuardConfig? _config = null;
    private static DateTime _lastConfigLoad = DateTime.MinValue;
    private static readonly TimeSpan ConfigReloadInterval = TimeSpan.FromSeconds(3);
    private static DateTime _lastFirewallUpdate = DateTime.MinValue;
    private static readonly TimeSpan FirewallUpdateInterval = TimeSpan.FromMinutes(5);
    private static List<string> _appliedDomains = new();

    private static string LogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KG_Pro", "agent.log");

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

    private static void Log(string msg)
    {
        try
        {
            string? dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 200 * 1024)
                File.Delete(LogFile);
            File.AppendAllText(LogFile,
                DateTime.Now.ToString("HH:mm:ss") + " [AGENT] " + msg + Environment.NewLine);
        }
        catch { }
    }

    private static GuardConfig? GetConfig()
    {
        if (_config == null || (DateTime.Now - _lastConfigLoad) > ConfigReloadInterval)
        {
            _config = ConfigStore.Load();
            _lastConfigLoad = DateTime.Now;
            if (_config != null)
                Log("Config geladen: EndTime=" + (_config.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "NULL"));
            else
                Log("Config ist NULL (keine Datei vorhanden)");
        }
        return _config;
    }

    [STAThread]
    static void Main(string[] args)
    {
        // ============================================================
        // SESSION 0 CHECK – BEVOR IRGENDWAS ANDERES PASSIERT!
        // ============================================================
        try
        {
            if (Process.GetCurrentProcess().SessionId == 0)
            {
                Log("Session 0 erkannt -> Beende sofort. onlogon startet mich korrekt.");
                return;
            }
        }
        catch { }

        // ============================================================
        // MUTEX – Nur eine Instanz pro Benutzersession
        // ============================================================
        bool createdNew;
        using var mutex = new Mutex(true, "KeywordGuard_Pro_Agent_Mutex", out createdNew);
        if (!createdNew)
        {
            Log("Mutex bereits belegt -> Andere Instanz laeuft. Beende.");
            return;
        }

        Log("Agent gestartet. SessionId=" + Process.GetCurrentProcess().SessionId
            + ", Admin=" + ProcessHardening.IsAdmin());

        // ============================================================
        // TaskScheduler-Aufgaben registrieren (Selbstheilung)
        // ============================================================
        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            TaskSchedulerGuard.EnsureStartupTask(exePath);
            Log("TaskScheduler-Aufgabe registriert: " + exePath);
        }

        // ============================================================
        // SessionEnding-Handler (sauberes Herunterfahren)
        // Benoetigt eine Windows-Message-Pump (Application.Run())!
        // ============================================================
        SystemEvents.SessionEnding += OnSessionEnding;
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            _isShuttingDown = true;
            _running = false;
            ProcessHardening.SetCritical(false);
            HostsBlocker.RemoveAll();
        };

        // ============================================================
        // HAUPTLOOP – Blockiert URLs + Woerter (via Timer)
        // Nutzt System.Windows.Forms.Timer, der auf dem UI-Thread laeuft
        // und die Message-Pump fuer SystemEvents.SessionEnding bereitstellt.
        // ============================================================
        Log("Starte Message-Pump mit Timer-basierter Blockierung...");

        var blockTimer = new System.Windows.Forms.Timer { Interval = 500 };
        blockTimer.Tick += (s, e) =>
        {
            if (_isShuttingDown || !_running || ProcessHardening.IsSystemShuttingDown())
            {
                _isShuttingDown = true;
                _running = false;
                ProcessHardening.SetCritical(false);
                blockTimer.Stop();
                return;
            }

            try
            {
                var config = GetConfig();
                if (config != null && config.IsActive())
                {
                    bool justActivated = !_wasEverActivated;
                    _wasEverActivated = true;
                    ProcessHardening.SetCritical(true);

                    // KEYWORD-BLOCKIERUNG
                    var targets = new List<BlockedItem>();

                    foreach (var kw in config.Keywords)
                    {
                        string val = kw.Value.Trim();
                        if (string.IsNullOrWhiteSpace(val)) continue;

                        string? domain = UrlHelper.ExtractDomain(val);
                        if (domain != null)
                        {
                            targets.Add(new BlockedItem { Value = domain, IsAggressive = kw.IsAggressive });

                            if (kw.IsAggressive)
                            {
                                string domainPart = domain.Split('.')[0];
                                if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 3)
                                    targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
                            }
                        }
                        else
                        {
                            targets.Add(new BlockedItem { Value = val, IsAggressive = kw.IsAggressive });
                        }
                    }

                    foreach (var url in config.Urls)
                    {
                        string? domain = UrlHelper.ExtractDomain(url);
                        if (domain != null)
                        {
                            targets.Add(new BlockedItem { Value = domain, IsAggressive = true });

                            string domainPart = domain.Split('.')[0];
                            if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 3)
                                targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
                        }
                    }

                    if (targets.Count > 0)
                        CheckActiveWindow(targets);

                    // Firewall-Blockierung aktualisieren
                    var domains = ExtractDomains(config.Keywords).OrderBy(d => d).ToList();
                    bool domainsChanged = !_appliedDomains.SequenceEqual(domains);
                    if (justActivated || domainsChanged || (DateTime.Now - _lastFirewallUpdate) > FirewallUpdateInterval)
                    {
                        if (domains.Count > 0)
                        {
                            HostsBlocker.AddUrls(domains);
                            FirewallBlocker.UpdateBlocks(domains);
                            _appliedDomains = domains;
                            Log("Hosts + Firewall-Regeln im Agenten aktualisiert (Domains geaendert=" + domainsChanged + ").");
                        }
                        _lastFirewallUpdate = DateTime.Now;
                    }

                    ScanDangerousProcesses();
                }
                else if (config == null)
                {
                    if (!_wasEverActivated)
                        ProcessHardening.SetCritical(false);
                }
                else
                {
                    // Timer ist abgelaufen (EndTime erreicht) – normal deaktivieren!
                    ProcessHardening.SetCritical(false);
                    if (_wasEverActivated)
                    {
                        FirewallBlocker.RemoveAll();
                        HostsBlocker.RemoveAll();
                        _wasEverActivated = false;
                        Log("Timer abgelaufen -> Hosts + Firewall-Regeln entfernt, Critical deaktiviert.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Timer-Fehler: " + ex.Message);
            }
        };
        blockTimer.Start();
        Log("Blockierungs-Timer gestartet (500ms).");

        // ============================================================
        // Application.Run() – STARTET DIE MESSAGE PUMP!
        // Ohne dies feuert SystemEvents.SessionEnding NIE.
        // Application.Run() blockiert, bis die Anwendung beendet wird.
        // ============================================================
        Log("Application.Run() gestartet. Agent aktiv.");
        Application.Run(new HiddenForm());

        Log("Message-Pump beendet. Agent faehrt herunter.");
        blockTimer.Stop();
        _running = false;
        ProcessHardening.SetCritical(false);
        Log("Agent beendet.");
    }

    // ============================================================
    // SessionEnding – Kernel entkoppeln, BSOD verhindern
    // ============================================================
    private static void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        Log("SessionEnding: Reason=" + e.Reason);
        lock (_sessionEndingLock)
        {
            if (_isShuttingDown) return;
            _isShuttingDown = true;
            _running = false;
        }

        ProcessHardening.SetCritical(false);
        HostsBlocker.RemoveAll();
        Log("Critical deaktiviert + Hosts bereinigt. Warte 1000ms fuer Kernel...");
        Thread.Sleep(1000);
        Log("SessionEnding abgeschlossen.");
    }

    // ============================================================
    // Aktives Fenster auf blockierte Begriffe pruefen
    // ============================================================
    static void CheckActiveWindow(List<BlockedItem> targets)
    {
        if (!_running || _isShuttingDown || ProcessHardening.IsSystemShuttingDown()) return;

        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return; // Kein Fenster im Fokus

        if (!IsBrowserProcess(handle)) return;

        const int nChars = 512;
        StringBuilder buff = new StringBuilder(nChars);

        if (GetWindowText(handle, buff, nChars) > 0)
        {
            string title = buff.ToString();
            foreach (var item in targets)
            {
                if (string.IsNullOrWhiteSpace(item.Value)) continue;

                bool hit = item.IsAggressive
                    ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
                    : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

                if (hit && _running && !_isShuttingDown)
                {
                    Log("BLOCKED: '" + item.Value + "' in Fenster '" + title + "'");
                    CloseWindow(handle);
                    break;
                }
            }
        }
    }

    // ============================================================
    // Fenster schliessen (Direktes WM_CLOSE an das Handle senden)
    // ============================================================
    static void CloseWindow(IntPtr handle)
    {
        try 
        { 
            PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); 
            Log("WM_CLOSE an Fenster gesendet.");
        } 
        catch (Exception ex) 
        {
            Log("Fehler beim Schliessen des Fensters: " + ex.Message);
        }
    }

    // ============================================================
    // WMI-Scanner – Killt gefaehrliche Prozesse
    // ============================================================
    static void ScanDangerousProcesses()
    {
        try
        {
            var query = "SELECT ProcessId, CommandLine, Name FROM Win32_Process WHERE " +
                "Name='cmd.exe' OR Name='powershell.exe' OR Name='pwsh.exe' OR " +
                "Name='sc.exe' OR Name='schtasks.exe' OR " +
                "Name='wmic.exe' OR Name='taskmgr.exe' OR " +
                "Name='procexp.exe' OR Name='procexp64.exe' OR " +
                "Name='ProcessHacker.exe' OR Name='procmon.exe'";

            using var searcher = new ManagementObjectSearcher(query);
            foreach (var obj in searcher.Get())
            {
                string cmdLine = obj["CommandLine"]?.ToString() ?? "";
                string name = obj["Name"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(cmdLine)) continue;

                int pid = Convert.ToInt32(obj["ProcessId"]);
                if (pid == Process.GetCurrentProcess().Id) continue;

                // Task-Manager, Process Explorer etc. sofort killen
                if (name.Equals("taskmgr.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("procexp.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("procexp64.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ProcessHacker.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("procmon.exe", StringComparison.OrdinalIgnoreCase))
                {
                    try { Process.GetProcessById(pid).Kill(); Log("KILL: " + name + " (PID " + pid + ")"); } catch { }
                    continue;
                }

                // sc.exe / schtasks.exe mit gefaehrlichen Parametern killen
                if (name.Equals("sc.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("schtasks.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (cmdLine.Contains("KeywordGuard", StringComparison.OrdinalIgnoreCase) ||
                        cmdLine.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                        cmdLine.Contains("stop", StringComparison.OrdinalIgnoreCase) ||
                        cmdLine.Contains("disable", StringComparison.OrdinalIgnoreCase))
                    {
                        try { Process.GetProcessById(pid).Kill(); Log("KILL: " + name + " (PID " + pid + ")"); } catch { }
                        continue;
                    }
                }

                // Shells mit gefaehrlichen Befehlen killen (nur wenn sie unser Projekt targeten)
                if (name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
                {
                    bool targetsProject = cmdLine.Contains("keywordguard", StringComparison.OrdinalIgnoreCase) ||
                                          cmdLine.Contains("kg_pro", StringComparison.OrdinalIgnoreCase);
                    if (targetsProject)
                    {
                        string[] dangerous = { "taskkill", "stop-process", "kill", "remove-item",
                            "del", "delete", "stop", "disable", "takeown", "icacls", "uninstall", "remove-service" };
                        foreach (var kw in dangerous)
                        {
                            if (cmdLine.Contains(kw, StringComparison.OrdinalIgnoreCase))
                            {
                                try { Process.GetProcessById(pid).Kill(); Log("KILL SHELL: PID " + pid + " ('" + kw + "')"); } catch { }
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static List<string> ExtractDomains(List<BlockedItem> items)
    {
        var domains = new List<string>();
        foreach (var item in items)
        {
            string? domain = UrlHelper.ExtractDomain(item.Value);
            if (domain != null && !domains.Contains(domain))
            {
                domains.Add(domain);
            }
        }
        return domains;
    }

    private class HiddenForm : Form
    {
        private const int WM_QUERYENDSESSION = 0x0011;
        private const int WM_ENDSESSION = 0x0016;

        public HiddenForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Opacity = 0;
            this.Size = new System.Drawing.Size(1, 1);
            
            // Force handle creation so WndProc intercepts session ending
            var forceHandle = this.Handle;
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_QUERYENDSESSION || m.Msg == WM_ENDSESSION)
            {
                Log($"HiddenForm WndProc received WM_QUERYENDSESSION/WM_ENDSESSION (Msg: 0x{m.Msg:X4})");
                lock (_sessionEndingLock)
                {
                    if (!_isShuttingDown)
                    {
                        _isShuttingDown = true;
                        _running = false;
                    }
                }
                ProcessHardening.SetCritical(false);
                HostsBlocker.RemoveAll();
                Log("Critical deactivated + Hosts cleaned by HiddenForm WndProc. Exiting...");
                Application.Exit();
            }
            base.WndProc(ref m);
        }
    }
}