using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace KeywordGuard.Pro.Security;

public static class ProcessHardening
{
    private const int PROCESS_TERMINATE = 0x0001;
    private const int PROCESS_VM_WRITE = 0x0020;
    private const int PROCESS_SUSPEND_RESUME = 0x0800;
    private const uint ProcessDenyMask = PROCESS_TERMINATE | PROCESS_VM_WRITE | PROCESS_SUSPEND_RESUME;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_SHUTTINGDOWN = 0x2000;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint GetSecurityInfo(
        IntPtr handle,
        SE_OBJECT_TYPE objectType,
        SECURITY_INFORMATION securityInfo,
        out IntPtr ownerSid,
        out IntPtr groupSid,
        out IntPtr dacl,
        out IntPtr sacl,
        out IntPtr securityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(
        IntPtr handle,
        SE_OBJECT_TYPE objectType,
        SECURITY_INFORMATION securityInfo,
        IntPtr ownerSid,
        IntPtr groupSid,
        IntPtr dacl,
        IntPtr sacl);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint SetEntriesInAcl(
        int entryCount,
        EXPLICIT_ACCESS[] entries,
        IntPtr existingAcl,
        out IntPtr newAcl);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitiateSystemShutdownEx(
        string? machineName,
        string? message,
        uint timeout,
        [MarshalAs(UnmanagedType.Bool)] bool forceAppsClosed,
        [MarshalAs(UnmanagedType.Bool)] bool rebootAfterShutdown,
        uint reason);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeSecurityDescriptor(
        IntPtr pSecurityDescriptor,
        uint dwRevision);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSecurityDescriptorDacl(
        IntPtr pSecurityDescriptor,
        [MarshalAs(UnmanagedType.Bool)] bool bDaclPresent,
        IntPtr pDacl,
        [MarshalAs(UnmanagedType.Bool)] bool bDaclDefaulted);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(
        IntPtr Handle,
        int SecurityInformation,
        IntPtr SecurityDescriptor);

    private const uint SECURITY_DESCRIPTOR_REVISION = 1;
    private const uint SHTDN_REASON_MAJOR_OTHER = 0x00000000;
    private const uint SHTDN_REASON_MINOR_OTHER = 0x00000000;
    private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

    private static int _systemShutdownFlag;
    private static int _emergencyShutdownState;
    private static int _selfProtectionApplied;

    /// <summary>
    /// Ueberprueft, ob das System gerade herunterfaehrt oder neu startet.
    /// </summary>
    public static bool IsSystemShuttingDown()
    {
        if (Volatile.Read(ref _systemShutdownFlag) != 0)
        {
            return true;
        }

        try
        {
            if (Environment.HasShutdownStarted) return true;
            return GetSystemMetrics(SM_SHUTTINGDOWN) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static readonly string LegalShutdownSignalFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KG_Pro", "legal_shutdown.signal");

    public static void MarkSystemShutdown()
    {
        Interlocked.Exchange(ref _systemShutdownFlag, 1);
        MarkLegalShutdown();
    }

    public static void MarkLegalShutdown()
    {
        try
        {
            string? dir = Path.GetDirectoryName(LegalShutdownSignalFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(LegalShutdownSignalFile, "1");
        }
        catch { }
    }

    public static bool CheckLegalShutdownSignal()
    {
        try
        {
            return File.Exists(LegalShutdownSignalFile);
        }
        catch
        {
            return false;
        }
    }

    public static void ClearLegalShutdownSignal()
    {
        try
        {
            if (File.Exists(LegalShutdownSignalFile))
            {
                File.Delete(LegalShutdownSignalFile);
            }
        }
        catch { }
    }

    public static bool ApplySelfProtection()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _selfProtectionApplied, 1, 1) != 0)
        {
            return true;
        }

        IntPtr securityDescriptor = IntPtr.Zero;
        IntPtr newAcl = IntPtr.Zero;
        GCHandle adminSidHandle = default;
        GCHandle systemSidHandle = default;

        try
        {
            using var process = Process.GetCurrentProcess();
            IntPtr processHandle = process.Handle;
            if (processHandle == IntPtr.Zero)
            {
                return false;
            }

            uint getSecurityResult = GetSecurityInfo(
                processHandle,
                SE_OBJECT_TYPE.SE_KERNEL_OBJECT,
                SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
                out _,
                out _,
                out IntPtr existingDacl,
                out _,
                out securityDescriptor);

            if (getSecurityResult != 0)
            {
                throw new Win32Exception((int)getSecurityResult);
            }

            var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            var adminBytes = new byte[adminSid.BinaryLength];
            adminSid.GetBinaryForm(adminBytes, 0);
            adminSidHandle = GCHandle.Alloc(adminBytes, GCHandleType.Pinned);

            var systemBytes = new byte[systemSid.BinaryLength];
            systemSid.GetBinaryForm(systemBytes, 0);
            systemSidHandle = GCHandle.Alloc(systemBytes, GCHandleType.Pinned);

            var entries = new[]
            {
                CreateDenyEntry(adminSidHandle.AddrOfPinnedObject()),
                CreateDenyEntry(systemSidHandle.AddrOfPinnedObject())
            };

            uint setEntriesResult = SetEntriesInAcl(entries.Length, entries, existingDacl, out newAcl);
            if (setEntriesResult != 0)
            {
                throw new Win32Exception((int)setEntriesResult);
            }

            IntPtr pSD = Marshal.AllocHGlobal(20);
            try
            {
                if (!InitializeSecurityDescriptor(pSD, SECURITY_DESCRIPTOR_REVISION))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (!SetSecurityDescriptorDacl(pSD, true, newAcl, false))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (!SetKernelObjectSecurity(processHandle, (int)SECURITY_INFORMATION.DACL_SECURITY_INFORMATION, pSD))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                if (pSD != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pSD);
                }
            }

            Interlocked.Exchange(ref _selfProtectionApplied, 1);
            return true;
        }
        catch
        {
            Interlocked.Exchange(ref _selfProtectionApplied, 0);
            return false;
        }
        finally
        {
            if (adminSidHandle.IsAllocated)
            {
                adminSidHandle.Free();
            }

            if (systemSidHandle.IsAllocated)
            {
                systemSidHandle.Free();
            }

            if (newAcl != IntPtr.Zero)
            {
                LocalFree(newAcl);
            }

            if (securityDescriptor != IntPtr.Zero)
            {
                LocalFree(securityDescriptor);
            }
        }
    }

    public static bool TriggerEmergencyShutdown(string? reason = null)
    {
        while (true)
        {
            int state = Volatile.Read(ref _emergencyShutdownState);
            if (state == 2)
            {
                return true;
            }

            if (state == 3)
            {
                return false;
            }

            if (state == 1)
            {
                Thread.Sleep(25);
                continue;
            }

            if (Interlocked.CompareExchange(ref _emergencyShutdownState, 1, 0) == 0)
            {
                break;
            }
        }

        if (IsSystemShuttingDown())
        {
            Interlocked.Exchange(ref _emergencyShutdownState, 2);
            return true;
        }

        bool shutdownStarted = false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/s /t 0 /f",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process != null)
            {
                shutdownStarted = true;
            }
        }
        catch
        {
        }

        if (!shutdownStarted)
        {
            try
            {
                shutdownStarted = InitiateSystemShutdownEx(
                    null,
                    reason,
                    0,
                    forceAppsClosed: true,
                    rebootAfterShutdown: false,
                    SHTDN_REASON_MAJOR_OTHER | SHTDN_REASON_MINOR_OTHER | SHTDN_REASON_FLAG_PLANNED);
            }
            catch
            {
            }
        }

        Interlocked.Exchange(ref _emergencyShutdownState, shutdownStarted ? 2 : 3);
        return shutdownStarted;
    }

    /// <summary>
    /// Kritischer Prozessmodus bleibt absichtlich deaktiviert,
    /// um sicherheitskritische Nebenwirkungen und AV-Fehlalarme zu vermeiden.
    /// </summary>
    public static bool SetCritical(bool isCritical)
    {
        return false;
    }

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static EXPLICIT_ACCESS CreateDenyEntry(IntPtr sidPointer)
    {
        return new EXPLICIT_ACCESS
        {
            grfAccessPermissions = ProcessDenyMask,
            grfAccessMode = ACCESS_MODE.DENY_ACCESS,
            grfInheritance = 0,
            Trustee = new TRUSTEE
            {
                pMultipleTrustee = IntPtr.Zero,
                MultipleTrusteeOperation = MULTIPLE_TRUSTEE_OPERATION.NO_MULTIPLE_TRUSTEE,
                TrusteeForm = TRUSTEE_FORM.TRUSTEE_IS_SID,
                TrusteeType = TRUSTEE_TYPE.TRUSTEE_IS_UNKNOWN,
                ptstrName = sidPointer
            }
        };
    }

    private enum SE_OBJECT_TYPE
    {
        SE_UNKNOWN_OBJECT_TYPE = 0,
        SE_FILE_OBJECT,
        SE_SERVICE,
        SE_PRINTER,
        SE_REGISTRY_KEY,
        SE_LMSHARE,
        SE_KERNEL_OBJECT
    }

    [Flags]
    private enum SECURITY_INFORMATION : uint
    {
        DACL_SECURITY_INFORMATION = 0x00000004
    }

    private enum ACCESS_MODE : uint
    {
        DENY_ACCESS = 3
    }

    private enum MULTIPLE_TRUSTEE_OPERATION
    {
        NO_MULTIPLE_TRUSTEE
    }

    private enum TRUSTEE_FORM
    {
        TRUSTEE_IS_SID = 0
    }

    private enum TRUSTEE_TYPE
    {
        TRUSTEE_IS_UNKNOWN = 0
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EXPLICIT_ACCESS
    {
        public uint grfAccessPermissions;
        public ACCESS_MODE grfAccessMode;
        public uint grfInheritance;
        public TRUSTEE Trustee;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TRUSTEE
    {
        public IntPtr pMultipleTrustee;
        public MULTIPLE_TRUSTEE_OPERATION MultipleTrusteeOperation;
        public TRUSTEE_FORM TrusteeForm;
        public TRUSTEE_TYPE TrusteeType;
        public IntPtr ptstrName;
    }
}