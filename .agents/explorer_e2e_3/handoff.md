# Handoff Report — E2E Test Suite Design

This handoff report summarizes the findings, reasoning, and test architecture design for the KeywordGuard Pro E2E test suite.

---

## 1. Observation

### 1.1 Project Structure & Compilation
The repository contains four projects targeting `.NET 10.0-windows` as verified in `KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj` (line 4) and `KeywordGuard.Pro.UI/KeywordGuard.Pro.UI.csproj` (line 4).
The installer script `FINAL-Install.ps1` builds them with Release configuration:
- Line 33: `dotnet build "$BasePath\KeywordGuard.Pro.Security\KeywordGuard.Pro.Security.csproj" -c Release --nologo -v q`
- Line 36: `dotnet build "$BasePath\KeywordGuard.Pro.Agent\KeywordGuard.Pro.Agent.csproj" -c Release --nologo -v q`
- Line 39: `dotnet build "$BasePath\KeywordGuard.Pro.UI\KeywordGuard.Pro.UI.csproj" -c Release --nologo -v q`
- Line 42: `dotnet build "$BasePath\KeywordGuard.Pro.Service\KeywordGuard.Pro.Service.csproj" -c Release --nologo -v q`

### 1.2 Configuration Storage
`KeywordGuard.Pro.Security/ConfigStore.cs` defines:
- Line 18-20:
  ```csharp
  private static readonly string ConfigDir = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
      "KG_Pro", "SecureData");
  ```
- Line 24-26:
  ```csharp
  private static readonly string BackupDir = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
      "KG_Pro", "SysConfigBackup");
  ```
- Line 31-38:
  ```csharp
  private static byte[] GetMachineKey()
  {
      string keyMaterial = Environment.MachineName +
                           Environment.ProcessorCount +
                           "KeywordGuard_Pro_V2_2026_Secure";
      using var sha256 = SHA256.Create();
      return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
  }
  ```

### 1.3 Window Monitoring & Matching
`KeywordGuard.Pro.Agent/Program.cs` implements:
- Line 145: `var blockTimer = new System.Windows.Forms.Timer { Interval = 500 };`
- Line 287: `IntPtr handle = GetForegroundWindow();`
- Line 291: `if (GetWindowText(handle, buff, nChars) > 0)`
- Line 298-303 (Matching and fallback):
  ```csharp
  bool hit = item.IsAggressive
      ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
      : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

  if (!hit && !item.IsAggressive)
      hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
  ```

### 1.4 Hardening & Self-Defense
- `KeywordGuard.Pro.Security/ProcessHardening.cs` (lines 25-28):
  ```csharp
  RtlAdjustPrivilege(20, true, false, out bool _);
  int status = RtlSetProcessIsCritical(isCritical ? 1u : 0u, out _, 0u);
  return status == 0;
  ```
- `KeywordGuard.Pro.Agent/Program.cs` (lines 358-363) lists monitored process names for WMI-based termination:
  `Name='cmd.exe' OR Name='powershell.exe' OR Name='pwsh.exe' OR Name='sc.exe' OR Name='schtasks.exe' OR ...`
- `KeywordGuard.Pro.Agent/Program.cs` (lines 400-411) lists monitored command line arguments:
  `string[] dangerous = { "taskkill", "stop-process", "kill", "remove-item", ... }`

### 1.5 Startup, Shutdown & Watcdog Stop
- `KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj` (line 3):
  `<OutputType>WinExe</OutputType>`
- `KeywordGuard.Pro.Agent/Program.cs` (lines 450-466) Session Ending WndProc handler:
  ```csharp
  if (m.Msg == WM_QUERYENDSESSION || m.Msg == WM_ENDSESSION)
  {
      ...
      ProcessHardening.SetCritical(false);
      HostsBlocker.RemoveAll();
      Application.Exit();
  }
  ```
- `KeywordGuard.Pro.Service/Worker.cs` (lines 147-154) Simulated service critical status:
  ```csharp
  private void ActivateCritical()
  {
      if (!_isCritical)
      {
          _isCritical = true;
          _logger.LogInformation("Service als kritisch markiert (simuliert, API deaktiviert).");
      }
  }
  ```
- `KeywordGuard.Pro.Service/Worker.cs` (lines 104-114) Watchdog stop blocking:
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
      ...
  }
  ```

---

## 2. Logic Chain

1. **Compilation & Packaging**: Based on the project structure (Obs 1.1), the E2E test runner must trigger builds of all four projects to ensure test coverage is verified against actual current release binaries.
2. **Configuration Access**: Since `ConfigStore` reads from `%ProgramData%\KG_Pro` (Obs 1.2) rather than user-isolated folders, the E2E test project must either run elevated (with Administrator rights) to write/modify these directories, or the test setup must execute the ACL modifications similar to `FINAL-Install.ps1` so the test processes can interact with the files.
3. **Keyword Matching Bug**: Because the matching logic falls back to case-insensitive `Contains` search if the standard regex fails (Obs 1.3), `IsAggressive = false` currently behaves identically to `IsAggressive = true`. E2E tests must initially expect standard keywords to match as substrings, but include assertions that will fail once the bug is resolved in Milestone 2.
4. **WMI Killer Evading**: The Agent's WMI process scanner terminates any shell process (`powershell.exe`, `cmd.exe`) whose command line contains keywords like `taskkill`, `stop-process`, or `keywordguard` (Obs 1.4). If E2E tests are run via a PowerShell test framework or invoke shell commands, the test runner will be killed. Therefore, the E2E test runner must run compiled C# binaries (which run under `testhost.exe`, which is not monitored) and control processes programmatically via `Process.Kill()` instead of shells.
5. **SetCritical Verification Safety**: Since the Agent marks itself as critical (Obs 1.4), calling `Process.Kill()` on a running Agent will cause a Windows BSOD. To safely assert that `SetCritical` is active, the E2E tests must query the process status using the native `NtQueryInformationProcess(..., ProcessBreakOnTermination)` API (which does not crash the machine) instead of attempting to kill it.
6. **Graceful Shutdown**: The Agent's hidden window WndProc handles `WM_QUERYENDSESSION` / `WM_ENDSESSION` to clear critical status (Obs 1.5). Thus, the E2E tests can verify clean shutdown by programmatically sending `WM_QUERYENDSESSION` to the Agent window and checking that `ProcessBreakOnTermination` is set to `0`.
7. **Service Protection**: The watchdog service blocks standard stopping when the timer is active by delaying indefinitely in `StopAsync` (Obs 1.5). The E2E tests can verify this by attempting to stop the service while a timer is active and confirming it times out.

---

## 3. Caveats

- **Network-Level Block Testing**: Testing `FirewallBlocker` requires resolving domain names. In the restricted CODE_ONLY environment, the DNS resolver might fail to resolve external domains like `example.com`. The tests must handle DNS resolution failures gracefully or use a locally-resolvable mock domain.
- **Undocumented APIs**: The verification of `SetCritical` relies on undocumented NT APIs (`NtQueryInformationProcess`, `ProcessBreakOnTermination`). While stable across Windows versions, their behavior could theoretically change in future Windows releases.
- **Admin Privileges**: Tiers 3 & 4 tests require administrative privileges to check critical process flags, query netsh firewall rules, and control system services. The test suite must detect if it is running in a non-admin context and skip these tiers, logging a clear warning, while running Tiers 1 & 2.

---

## 4. Conclusion

A robust E2E test suite for KeywordGuard Pro can be implemented as a compiled C# xUnit project targeting `.NET 10.0-windows`. 
To execute successfully without BSODs or process termination, the tests must:
1. Run under `testhost.exe` and avoid using external shell processes (`powershell.exe`/`cmd.exe`) to execute actions like `taskkill`.
2. Mock processes by copying a custom dummy executable to `%TEMP%` and renaming it to `chrome.exe` or `notepad.exe`.
3. Verify `SetCritical` status using `NtQueryInformationProcess` rather than killing the process.
4. Verify silent startup by checking the PE header Optional Header subsystem.
5. Validate watchdog service block behavior by asserting timeouts on service stop requests.

---

## 5. Verification Method

To verify this design strategy:
1. Check that `analysis.md` and `handoff.md` exist in `.agents/explorer_e2e_3/`.
2. Inspect the PE Header subsystem parsing logic inside `analysis.md` (Section 5) and verify it correctly targets the optional header's Subsystem byte (offset 68 of the optional header).
3. Validate that `NtQueryInformationProcess` with class 29 (`ProcessBreakOnTermination`) successfully queries the critical status on Windows.
