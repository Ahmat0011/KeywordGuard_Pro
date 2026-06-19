using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using KeywordGuard.Pro.Security;

namespace KeywordGuard.Pro.Agent.Tests;

public class Program
{
    // ============================================================
    // Windows API P/Invoke
    // ============================================================
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION = 0x0016;

    public static void Main(string[] args)
    {
        Console.WriteLine("=== KeywordGuard Pro R2/R3 Verification Suite ===");

        // 1. Verify that the running KeywordGuard.Pro.Agent has a hidden Form handle
        VerifyRunningAgentWindowHandle();

        // 2. Test Original Implementation (Unforced Handle Creation)
        TestOriginalImplementation();

        // 3. Test Corrected Implementation (Forced Handle Creation)
        TestCorrectedImplementation();
    }

    private static void VerifyRunningAgentWindowHandle()
    {
        Console.WriteLine("\n--- Step 1: Checking Window Handles of running KeywordGuard.Pro.Agent process ---");
        var processes = Process.GetProcessesByName("KeywordGuard.Pro.Agent");
        if (processes.Length == 0)
        {
            Console.WriteLine("[WARN] KeywordGuard.Pro.Agent process is not running.");
            return;
        }

        foreach (var proc in processes)
        {
            Console.WriteLine($"Found Agent Process: PID={proc.Id}");
            var handles = FindWindowsForProcess((uint)proc.Id);
            if (handles.Count == 0)
            {
                Console.WriteLine($"[FAIL] No window handles found for Agent PID {proc.Id}. The process has no window handles in the OS.");
            }
            else
            {
                Console.WriteLine($"Found {handles.Count} window handle(s) for PID {proc.Id}:");
                foreach (var handle in handles)
                {
                    var className = new StringBuilder(256);
                    GetClassName(handle, className, 256);
                    Console.WriteLine($"  - Handle: {handle} (0x{handle.ToString("X")}) | Class: {className}");
                }
            }
        }
    }

    private static void TestOriginalImplementation()
    {
        Console.WriteLine("\n--- Step 2: Testing Original Implementation (As in Agent/Program.cs) ---");
        var formThreadStarted = new ManualResetEvent(false);
        var handleCreatedFired = false;
        FormOriginal? form = null;

        var thread = new Thread(() =>
        {
            form = new FormOriginal();
            form.HandleCreated += (s, e) => {
                handleCreatedFired = true;
                formThreadStarted.Set();
            };
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Wait up to 3 seconds
        bool started = formThreadStarted.WaitOne(3000);
        
        if (!started)
        {
            Console.WriteLine("[FAIL] Original implementation: Handle was NOT created automatically! The thread did not fire HandleCreated.");
            Console.WriteLine($"IsHandleCreated: {form?.IsHandleCreated}");
        }
        else
        {
            Console.WriteLine("[PASS] Original implementation: Handle was created.");
        }

        // Clean up
        if (form != null)
        {
            try {
                form.Invoke(new Action(() => {
                    form.Close();
                    Application.ExitThread();
                }));
            } catch { }
        }
        thread.Join(1000);
    }

    private static void TestCorrectedImplementation()
    {
        Console.WriteLine("\n--- Step 3: Testing Corrected Implementation (Forcing Handle Creation) ---");
        var formThreadStarted = new ManualResetEvent(false);
        var interceptedQueryEnd = false;
        var interceptedEndSession = false;
        FormCorrected? form = null;

        var thread = new Thread(() =>
        {
            form = new FormCorrected();
            form.HandleCreated += (s, e) => formThreadStarted.Set();
            
            // Forcing handle creation by reading the Handle property
            var forceHandle = form.Handle; 
            
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!formThreadStarted.WaitOne(3000))
        {
            Console.WriteLine("[FAIL] Corrected implementation: Failed to create handle even with forced creation.");
            return;
        }

        IntPtr handle = form!.Handle;
        Console.WriteLine($"Corrected implementation: Handle created successfully: {handle} (0x{handle.ToString("X")})");

        // Hook intercept flags
        form.OnQueryEndSessionReceived += () => interceptedQueryEnd = true;
        form.OnEndSessionReceived += () => interceptedEndSession = true;

        // Send messages
        Console.WriteLine("Sending WM_QUERYENDSESSION (0x0011)...");
        SendMessage(handle, WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);

        Console.WriteLine("Sending WM_ENDSESSION (0x0016)...");
        SendMessage(handle, WM_ENDSESSION, IntPtr.Zero, IntPtr.Zero);

        // Wait for clean exit
        bool exited = thread.Join(3000);

        if (exited && interceptedQueryEnd && interceptedEndSession)
        {
            Console.WriteLine("[PASS] Corrected implementation: Successfully intercepted shutdown messages and exited cleanly!");
        }
        else
        {
            Console.WriteLine($"[FAIL] Corrected implementation failed. Exited: {exited}, Intercepted QueryEnd: {interceptedQueryEnd}, Intercepted EndSession: {interceptedEndSession}");
        }
    }

    private static System.Collections.Generic.List<IntPtr> FindWindowsForProcess(uint targetPid)
    {
        var handles = new System.Collections.Generic.List<IntPtr>();
        EnumWindows((hWnd, lParam) =>
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == targetPid)
            {
                handles.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return handles;
    }

    // Original Form Class matching implementation exactly
    private class FormOriginal : Form
    {
        public FormOriginal()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Opacity = 0;
            this.Size = new System.Drawing.Size(1, 1);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }
    }

    // Corrected Form Class that supports verification
    private class FormCorrected : Form
    {
        public event Action? OnQueryEndSessionReceived;
        public event Action? OnEndSessionReceived;

        public FormCorrected()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Opacity = 0;
            this.Size = new System.Drawing.Size(1, 1);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_QUERYENDSESSION)
            {
                OnQueryEndSessionReceived?.Invoke();
            }
            else if (m.Msg == WM_ENDSESSION)
            {
                OnEndSessionReceived?.Invoke();
                Application.Exit();
            }
            base.WndProc(ref m);
        }
    }
}
