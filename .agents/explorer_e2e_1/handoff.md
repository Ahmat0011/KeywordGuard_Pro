# Handoff Report — E2E Test Suite Strategy for KeywordGuard Pro

This report details the findings and the proposed strategy for the KeywordGuard Pro E2E test suite.

---

## 1. Observation

### 1.1. Code Structure & Build System
- Four projects exist under the root directory:
  - `KeywordGuard.Pro.Security` (Class Library, targets `net10.0`)
  - `KeywordGuard.Pro.Agent` (Windows Forms, targets `net10.0-windows`, `<OutputType>WinExe</OutputType>` in `KeywordGuard.Pro.Agent.csproj:3`)
  - `KeywordGuard.Pro.UI` (WPF, targets `net10.0-windows`, `<OutputType>WinExe</OutputType>` in `KeywordGuard.Pro.UI.csproj:3`)
  - `KeywordGuard.Pro.Service` (Windows Service, targets `net10.0-windows`, `<OutputType>Exe</OutputType>` in `KeywordGuard.Pro.Service.csproj:3`)
- Built using `dotnet build [csproj] -c Release`.
- Build outcomes consolidated in the installer `FINAL-Install.ps1` via Robocopy to `C:\Program Files\KG_Pro\UI`.

### 1.2. Configuration Location and Access
- Primary file: `C:\ProgramData\KG_Pro\SecureData\sys_config.dat`
- Backup file: `C:\ProgramData\KG_Pro\SysConfigBackup\kg_config.enc`
- Resolved in `ConfigStore.cs` lines 18-28:
  ```csharp
  private static readonly string ConfigDir = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
      "KG_Pro", "SecureData");
  private static readonly string ConfigFile = Path.Combine(ConfigDir, "sys_config.dat");
  ```
- ACL setup in `FINAL-Install.ps1` lines 108-111:
  ```powershell
  $Acl = Get-Acl $LogDir
  $SidUsers = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-32-545")
  $Acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($SidUsers, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
  ```
- Hiding attributes in `ConfigStore.cs` lines 68-69:
  ```csharp
  try { File.SetAttributes(ConfigFile, FileAttributes.Hidden | FileAttributes.System); } catch { }
  ```

### 1.3. Match and Detection Logic
- Active window handle retrieved via `GetForegroundWindow()` (`Program.cs:18`) and title retrieved via `GetWindowText` (`Program.cs:21`).
- Window title match evaluation in `Program.cs:298-303`:
  ```csharp
  bool hit = item.IsAggressive
      ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
      : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

  if (!hit && !item.IsAggressive)
      hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
  ```
- Keystroke closing sequence in `Program.cs:324-328` (`Ctrl+W` tab closing simulation):
  ```csharp
  keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
  keybd_event(VK_W, 0, 0, UIntPtr.Zero);
  Thread.Sleep(50);
  keybd_event(VK_W, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
  keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
  ```
  Followed by `Alt+F4` and `WM_CLOSE` if window remains open.
- Process checks: There is currently **no** restriction limiting window closing to standard web browsers (it evaluates any active window). It is scheduled to be implemented in Milestone 2.

### 1.4. Silent Startup & Shutdown Behavior
- Subsystem is GUI (`WinExe`) for the Agent, ensuring silent background operation.
- Critical status toggling: `ProcessHardening.SetCritical` invokes `RtlSetProcessIsCritical` from `ntdll.dll`.
- Safe deactivation during Windows shutdown/logoff in `Program.cs:450-466` (WndProc of `HiddenForm` intercepts `WM_QUERYENDSESSION` / `WM_ENDSESSION`):
  ```csharp
  if (m.Msg == WM_QUERYENDSESSION || m.Msg == WM_ENDSESSION)
  {
      ...
      ProcessHardening.SetCritical(false);
      HostsBlocker.RemoveAll();
      Log("Critical deactivated + Hosts cleaned by HiddenForm WndProc. Exiting...");
      Application.Exit();
  }
  ```
- Watchdog Stop blocking when the timer is active in `Worker.cs:104-113`:
  ```csharp
  public override async Task StopAsync(CancellationToken cancellationToken)
  {
      var config = ConfigStore.Load();
      bool timerActive = (config != null && config.IsActive()) ||
                         (config == null && _wasEverActive);
      if (timerActive && !Environment.HasShutdownStarted)
      {
          _logger.LogWarning("StopAsync blockiert – Timer noch aktiv!");
          await Task.Delay(Timeout.Infinite, cancellationToken);
          return;
      }
  ```

---

## 2. Logic Chain

1. **Build Verification**: Because the project outputs target Windows platforms specifically and compile using the `dotnet build` toolchain, we can compile the code under Release/Debug configurations and dynamically check the PE subsystem field of the produced `.exe` files. This ensures static compliance with the silent startup requirements (`WinExe`).
2. **Configuration Verification**: Since the configuration is encrypted via AES-256 with a machine-specific key, the E2E test suite can verify that files contain ciphertext (cannot be parsed as plaintext JSON) and can be successfully decrypted using the same derivation key.
3. **Mocking Process Termination**: Because the Agent closes windows by sending input to the active window title matching the keywords, we can construct a dummy application (`WindowMock.exe`), launch it, name it `chrome.exe` (or any target name), set its window title to match a configured blocked word, and verify that the process is terminated (the PID exits) within a short window.
4. **Shutdown Signal Verification**: Because the Agent handles `WM_QUERYENDSESSION`/`WM_ENDSESSION` to deactivate `SetCritical` safely and avoid a kernel BSOD, we can simulate this event during an E2E test by retrieving the Agent's `HiddenForm` window handle and sending a mock `WM_QUERYENDSESSION` (0x0011) message. This enables safe testing of the clean exit code path without crashing the host OS.
5. **Watchdog Service Lifecycle**: Since the service blocks termination when a timer is active, the E2E test can write an active config, issue a stop command (`sc stop`), and confirm it enters `STOP_PENDING` or remains active. It can then write an expired config, repeat the stop command, and confirm it stops cleanly.

---

## 3. Caveats
- **Environment.HasShutdownStarted**: Since this property is a static .NET runtime property, we cannot easily mock it on a live OS without a real system reboot. The service stopping test relies on simulating standard stop requests (`StopAsync`) via `sc.exe stop` which defaults `Environment.HasShutdownStarted` to `false`, representing a manual administrative stop.
- **Firewall Rules**: Testing firewall rules creation via `netsh` requires administrative privileges for the test runner process. Without Admin access, the firewall assertions will fail.
- **Hosts File Blocker**: The codebase has commented out and stubbed the hosts file modifier (`HostsBlocker.cs`). Only the Firewall rules and active window checkers are functional.

---

## 4. Conclusion
A comprehensive 4-Tier E2E test suite strategy is feasible and can be safely executed on a Windows system. The suite should:
- Verify builds statically (PE headers).
- Test configuration serialization, encryption, and NTFS ACL permissions.
- Validate matching and process termination dynamically using a simulated process target (`WindowMock.exe`).
- Safely test critical status cleaning and service shutdown blocking by mocking Win32 message events (`WM_QUERYENDSESSION`).

---

## 5. Verification Method

### 5.1. Compilation Verification
Run the compilation suite using PowerShell in the repository root:
```powershell
dotnet build KeywordGuard.Pro.Security/KeywordGuard.Pro.Security.csproj -c Release
dotnet build KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj -c Release
dotnet build KeywordGuard.Pro.UI/KeywordGuard.Pro.UI.csproj -c Release
dotnet build KeywordGuard.Pro.Service/KeywordGuard.Pro.Service.csproj -c Release
```
Check that the output directories contain the `.exe` and `.dll` binaries.

### 5.5. E2E Test Execution Command
When the test suite project is implemented (e.g. `KeywordGuard.Pro.E2ETests.csproj`), it can be executed using the standard test command:
```powershell
dotnet test KeywordGuard.Pro.E2ETests/KeywordGuard.Pro.E2ETests.csproj --logger:"html;logfilename=e2e_results.html"
```
Ensure the command runs in an elevated shell (run as Administrator) to allow UAC-level service operations.
