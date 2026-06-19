using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace KeywordGuard.Pro.Agent.Tests;

class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    const uint WM_QUERYENDSESSION = 0x0011;
    const uint WM_ENDSESSION = 0x0016;

    static void Main(string[] args)
    {
        Console.WriteLine("[TEST] Starting HiddenForm Verification...");

        // Find HiddenForm type via reflection
        var programType = typeof(KeywordGuard.Pro.Agent.Program);
        var hiddenFormType = programType.GetNestedType("HiddenForm", BindingFlags.NonPublic | BindingFlags.Public);
        if (hiddenFormType == null)
        {
            Console.WriteLine("[FAIL] HiddenForm type not found in KeywordGuard.Pro.Agent.Program!");
            Environment.Exit(1);
        }

        Console.WriteLine("[PASS] Found HiddenForm type.");

        // Instantiate HiddenForm
        Form? hiddenForm = Activator.CreateInstance(hiddenFormType) as Form;
        if (hiddenForm == null)
        {
            Console.WriteLine("[FAIL] Failed to instantiate HiddenForm!");
            Environment.Exit(1);
        }
        Console.WriteLine("[PASS] Instantiated HiddenForm.");

        // We run the hidden form on a background STA thread with a message pump
        IntPtr hwnd = IntPtr.Zero;
        var runException = null as Exception;
        var formThread = new Thread(() =>
        {
            try
            {
                // Force handle creation
                hwnd = hiddenForm.Handle;
                Console.WriteLine($"[TEST] HiddenForm handle created: 0x{hwnd.ToInt64():X}");
                
                // Run the application loop
                Application.Run(hiddenForm);
                Console.WriteLine("[TEST] Application.Run completed.");
            }
            catch (Exception ex)
            {
                runException = ex;
                Console.WriteLine($"[TEST] Exception in form thread: {ex.Message}");
            }
        });
        formThread.SetApartmentState(ApartmentState.STA);
        formThread.Start();

        // Wait for handle to be initialized
        int retries = 50;
        while (hwnd == IntPtr.Zero && retries > 0 && runException == null)
        {
            Thread.Sleep(100);
            retries--;
        }

        if (runException != null)
        {
            Console.WriteLine($"[FAIL] Form thread crashed: {runException}");
            Environment.Exit(1);
        }

        if (hwnd == IntPtr.Zero)
        {
            Console.WriteLine("[FAIL] Hwnd was not created in time!");
            Environment.Exit(1);
        }

        Console.WriteLine("[PASS] Window handle verified.");

        // Now, we will send WM_QUERYENDSESSION/WM_ENDSESSION to the handle
        Console.WriteLine("[TEST] Sending WM_QUERYENDSESSION to HiddenForm...");
        // wParam: 0, lParam: 0
        SendMessage(hwnd, WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);

        Console.WriteLine("[TEST] Sending WM_ENDSESSION to HiddenForm...");
        SendMessage(hwnd, WM_ENDSESSION, IntPtr.Zero, IntPtr.Zero);

        // Wait for the form thread to exit
        Console.WriteLine("[TEST] Waiting for form thread to exit...");
        if (formThread.Join(5000))
        {
            Console.WriteLine("[PASS] Form thread exited cleanly after receiving shutdown messages.");
        }
        else
        {
            Console.WriteLine("[FAIL] Form thread did not exit within 5 seconds.");
            Environment.Exit(1);
        }

        Console.WriteLine("[PASS] HiddenForm mechanism successfully verified!");
    }
}
