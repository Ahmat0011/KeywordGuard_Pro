# KeywordGuard Pro E2E Test Suite Strategy

This document provides a comprehensive analysis of the KeywordGuard Pro repository and designs a robust, multi-tier End-to-End (E2E) test suite strategy that runs safely on Windows, simulates browser processes, verifies system hardening, and bypasses self-defense mechanisms during testing.

---

## 1. Repository & Build Structure

### Code Projects
The codebase is structured into four main C# projects targeting `.NET 10.0-Windows`:
1. **`KeywordGuard.Pro.UI`** (WPF GUI application, output type: `WinExe`):
   - Location: `KeywordGuard.Pro.UI/`
   - Entry point: `App.xaml.cs`
   - Key Class: `Services/WordWatcher.cs` (local window title monitoring and closing thread for the UI).
2. **`KeywordGuard.Pro.Agent`** (Background window title watcher, output type: `WinExe`):
   - Location: `KeywordGuard.Pro.Agent/`
   - Entry point: `Program.cs`
   - Key Class: `TaskSchedulerGuard.cs` (registers and runs onlogon task).
3. **`KeywordGuard.Pro.Service`** (Windows watchdog service running in Session 0, output type: `WinExe`):
   - Location: `KeywordGuard.Pro.Service/`
   - Entry point: `Program.cs`
   - Key Class: `Worker.cs` (monitors agent lifetime and blocks service termination if timer is active).
4. **`KeywordGuard.Pro.Security`** (Shared class library, output type: `Library`):
   - Location: `KeywordGuard.Pro.Security/`
   - Core Classes:
     - `ConfigStore.cs`: Configuration encryption and persistence.
     - `ProcessHardening.cs`: Sets/clears critical status.
     - `FirewallBlocker.cs`: Resolves domains and manages Windows Advanced Firewall rules.
     - `HostsBlocker.cs`: (Stub/Deactivated) Formerly managed the hosts file.
     - `UrlHelper.cs`: Utilities for domain extraction.

### Build and Deployment
- **Compilation**: Binaries are compiled using the standard .NET CLI command:
  ```powershell
  dotnet build "KeywordGuard.Pro.<Project>.csproj" -c Release --nologo
  ```
- **Build Output Directories**:
  - `KeywordGuard.Pro.UI/bin/Release/net10.0-windows/`
  - `KeywordGuard.Pro.Agent/bin/Release/net10.0-windows/`
  - `KeywordGuard.Pro.Service/bin/Release/net10.0-windows/`
  - `KeywordGuard.Pro.Security/bin/Release/net10.0-windows/`
- **Installation Process (`FINAL-Install.ps1`)**:
  1. Compiles all four projects in Release configuration.
  2. Stops any running instances of `KeywordGuard.Pro.Agent`, `KeywordGuard.Pro.UI`, and `KeywordGuard.Pro.Service` (using `Stop-Process` and `sc.exe stop`).
  3. Deletes existing startup entries and scheduled tasks.
  4. Copies all compiled release binaries into the target directory `C:\Program Files\KG_Pro\UI`.
  5. Registers and starts the watchdog service `KeywordGuardProService` (automatic startup, restarts on failure).
  6. Configures the folder `C:\ProgramData\KG_Pro` to grant **FullControl** to the `Users` group, allowing non-admin processes to read/write log and config files.
  7. Restricts permissions on the installation directory `C:\Program Files\KG_Pro` so only `SYSTEM` and `Administrators` have full control, denying deletion/modification to administrators (file protection).
  8. Registers the scheduled task `KeywordGuardProStartup` to run the Agent on logon with `HIGHEST` privileges.
  9. Starts the Agent and UI processes.

---

## 2. Configuration & Keyword Storage Mechanism

### Data Locations
The `ConfigStore` class manages the persistence of the configuration (`GuardConfig`) which holds blocked keywords, URLs, and the block end-time. 
- **Primary Configuration File**: `%ProgramData%\KG_Pro\SecureData\sys_config.dat`
- **Backup Configuration File**: `%ProgramData%\KG_Pro\SysConfigBackup\kg_config.enc`
- *Note on Mismatch*: While code comments in `ConfigStore.cs` suggest `%LOCALAPPDATA%` is used to allow non-admin access, the actual implementation uses `Environment.SpecialFolder.CommonApplicationData` (`C:\ProgramData`). Non-admin writing is made possible solely by the ACL changes configured during installation (granting `FullControl` to `Users` on `C:\ProgramData\KG_Pro`).

### Serialization & Encryption
- The configuration is serialized into JSON using `System.Text.Json`.
- The JSON string is encrypted using **AES-256-CBC** with **PKCS7** padding.
- **Key Derivation**: The 256-bit encryption key is derived via SHA256 from a machine-specific string:
  ```csharp
  string keyMaterial = Environment.MachineName + Environment.ProcessorCount + "KeywordGuard_Pro_V2_2026_Secure";
  ```
- **Format**: The written files contain the 16-byte initialization vector (IV) prepended to the ciphertext:
  `[IV (16 bytes)] + [Ciphertext]`
- **Attributes**: Once written, the files are marked with `FileAttributes.Hidden | FileAttributes.System` to hide them from standard users.

---

## 3. Foreground Window Detection & Matching

### Detection Loop
The Agent runs a `System.Windows.Forms.Timer` with a `500ms` interval on the UI thread. The UI thread is utilized to host a message pump (`Application.Run(new HiddenForm())`), which is required for `SystemEvents.SessionEnding` to trigger.
In each tick, if a configuration is loaded and the current time is before `EndTime` (checked via `config.IsActive()`), the Agent monitors the foreground window.

### Window Retrieval
- Calls native Win32 `GetForegroundWindow()` to obtain the active window handle.
- Calls `GetWindowText(handle, sb, 512)` to fetch the active window title.

### Keyword Matching Logic
The Agent extracts block targets from the config's `Keywords` and `Urls` lists.
1. **Domain Extraction**: If a keyword contains a dot and no spaces (e.g. `google.com`), the Agent uses `UrlHelper.ExtractDomain` to isolate the domain.
2. **Partial Domain Expansion**: If a domain is extracted, the Agent adds the domain itself to the block list. Additionally, if the domain can be split by a dot (e.g., `google` from `google.com`) and the prefix is longer than 1 character, it adds that prefix as an **aggressive** block target.
3. **Comparison Modes**:
   - **Aggressive (`IsAggressive = true`)**: Matches if the window title contains the block target as a substring (case-insensitive):
     `title.Contains(target, StringComparison.OrdinalIgnoreCase)`
   - **Standard (`IsAggressive = false`)**: Performs a case-insensitive regex match with word boundaries:
     `Regex.IsMatch(title, @"\b" + Regex.Escape(target) + @"\b", RegexOptions.IgnoreCase)`
   - *Logic Bug in Code*: In `Program.cs` (lines 302-303) and `WordWatcher.cs` (lines 78-79), there is a fallback:
     ```csharp
     if (!hit && !item.IsAggressive)
         hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
     ```
     This fallback overrides the word boundary regex and falls back to a plain substring search if the regex fails. In practice, this means **all standard keywords behave aggressively** (substring match). This is an identified bug slated for resolution in Milestone 2.

### Window Closing Sequence
When a title matches a blocked keyword, the Agent attempts to close the window using three methods in sequence:
1. **Tab Close (`Ctrl+W`)**: Sets the window as the foreground window, simulates pressing `Ctrl+W` (`keybd_event`), and waits 200ms. This is intended to close a single browser tab rather than the entire browser.
2. **Window Close (`Alt+F4`)**: If the foreground window handle remains unchanged, it simulates pressing `Alt+F4` and waits 200ms.
3. **Forced Close (`WM_CLOSE`)**: If the window is still open, it calls `PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)`.

*Scope Limitation*: The Agent currently closes **any** window that matches the title criteria, including text editors (Notepad), terminal windows, or IDEs. Limiting this to web browser processes is scheduled for Milestone 2.

---

## 4. Safe Process Mocking & WMI Scanner Bypass

### Process Mocking Strategy
To test window title matching and closing without launching actual web browsers, the E2E suite can simulate processes:
1. Create a dummy Windows Forms application (`KeywordGuard.Pro.TestHelper`).
2. When launched, this helper takes arguments specifying its window title and processes close events gracefully (exiting with code 0).
3. The E2E test suite copies `KeywordGuard.Pro.TestHelper.exe` to a temp directory and renames it to `chrome.exe`, `firefox.exe`, or `notepad.exe`.
4. The test suite launches the renamed process, sets the window title, and observes if the Agent detects and closes it.

### WMI Scanner Bypass
The Agent's WMI process scanner (`ScanDangerousProcesses`) runs every 500ms when the block timer is active.
- It scans for process names: `cmd.exe`, `powershell.exe`, `pwsh.exe`, `sc.exe`, `schtasks.exe`, `wmic.exe`, `taskmgr.exe`, `procexp.exe`, `procexp64.exe`, `ProcessHacker.exe`, `procmon.exe`.
- If a shell process (like PowerShell or CMD) has a command line containing monitored keywords (`taskkill`, `stop-process`, `keywordguard`, `kill`, etc.), **the Agent immediately kills the shell process**.

**CRITICAL CRITERIA FOR E2E TEST RUNNERS:**
If the E2E test runner is executed via a PowerShell script or a shell command containing the name "KeywordGuard", or if the script attempts to kill the Agent via `Stop-Process` or `taskkill`, the Agent's WMI scanner will instantly kill the PowerShell/CMD test process.
- **Solution**: The E2E test suite must be compiled as a C# test project (using xUnit, NUnit, or MSTest). When executed via `dotnet test`, the tests run inside `testhost.exe`.
- `testhost.exe` is **not** monitored by the WMI scanner.
- Any process control (like stopping the Agent or Service) within the tests must be executed programmatically in C# (e.g. `Process.Kill()`) rather than executing command-line shells.

---

## 5. Startup, Shutdown, and SetCritical Verification

### Silent Startup Verification
The Agent and UI must run silently without opening console windows.
- Output type `WinExe` configures the PE Subsystem of the executables to `2` (Windows GUI) instead of `3` (Windows Console).
- The E2E suite can verify this programmatically by reading the PE header of the compiled executables:
  - Locate the PE header offset from bytes `0x3C`–`0x3F`.
  - Navigate to the COFF Optional Header.
  - Read the `Subsystem` field (2 bytes located at offset `68` from the start of the optional header).
  - Assert that the value equals `2` (`IMAGE_SUBSYSTEM_WINDOWS_GUI`).

### SetCritical status Verification
When the block timer is active, the Agent calls `ProcessHardening.SetCritical(true)`. This marks the process as critical (`RtlSetProcessIsCritical` in `ntdll.dll`). If killed while critical, Windows triggers a BSOD.
- **Safety Rule**: Never kill the Agent while `SetCritical(true)` is active.
- **How to verify safely**: The E2E test suite can query the critical status of the Agent process using the native `NtQueryInformationProcess` API with `ProcessBreakOnTermination` (class 29):
  ```csharp
  [DllImport("ntdll.dll")]
  private static extern int NtQueryInformationProcess(
      IntPtr processHandle,
      int processInformationClass,
      ref uint processInformation,
      uint processInformationLength,
      out uint returnLength
  );
  ```
  - Retrieve the process handle for the running Agent.
  - Call `NtQueryInformationProcess` with class `29` and a 4-byte buffer.
  - If the returned value is `1`, the process is critical. If `0`, it is normal.
  - This allows verification without triggering system crashes.

### Agent Shutdown Behavior
When the user logs off or the PC shuts down, the Agent must gracefully remove the critical flag to prevent a BSOD.
- **Verification Method**:
  1. Start the Agent with an active timer.
  2. Confirm `ProcessBreakOnTermination` is `1` (critical).
  3. Send `WM_QUERYENDSESSION` (0x0011) or `WM_ENDSESSION` (0x0016) to the Agent's hidden window (`HiddenForm`).
  4. Query `ProcessBreakOnTermination` and confirm it has reverted to `0` (non-critical).
  5. Wait for the process to exit and verify it terminates with exit code `0`.
  6. Parse `C:\ProgramData\KG_Pro\agent.log` and verify the log lines:
     `HiddenForm WndProc received WM_QUERYENDSESSION/WM_ENDSESSION...`
     `Critical deactivated + Hosts cleaned by HiddenForm WndProc. Exiting...`

---

## 6. Proposed E2E Test Suite Structure

A four-tier C# xUnit test project (`KeywordGuard.Pro.E2ETests`) running on local Windows machines.

```
KeywordGuard.Pro.E2ETests/
├── KeywordGuard.Pro.E2ETests.csproj  (xUnit test project)
├── Helpers/
│   ├── PeHeaderVerifier.cs            (Parses PE headers to verify WinExe subsystem)
│   ├── NativeMethods.cs               (NtQueryInformationProcess, SendMessage)
│   └── ProcessMock.cs                 (Creates renamed dummy processes)
├── Tier1_IsolatedComponentTests.cs     (Tests ConfigStore and UrlHelper)
├── Tier2_WindowBlockingTests.cs       (Tests window title matching & closing)
├── Tier3_SystemHardeningTests.cs      (Tests SetCritical, WMI killer, Firewall rules)
└── Tier4_WatchdogServiceTests.cs      (Tests service recovery & block stop)
```

### Tier 1: Isolated Component Tests (No Admin Required)
- **Test 1.1: Configuration Cryptography**: Save a `GuardConfig` using `ConfigStore.Save`, read the raw file, verify it is encrypted, decrypt it using the test key, and verify the structure matches.
- **Test 1.2: Domain Extraction Edge Cases**: Feed `UrlHelper.ExtractDomain` various strings (`https://www.youtube.com/watch?v=123`, `sub.domain.co.uk:8080`, `invalid_domain`, `youtube.com`) and verify outputs.
- **Test 1.3: PE Subsystem Check**: Programmatically parse `KeywordGuard.Pro.Agent.exe` and `KeywordGuard.Pro.UI.exe` PE headers. Assert that the Subsystem is `2` (`IMAGE_SUBSYSTEM_WINDOWS_GUI`), ensuring silent startup.

### Tier 2: Window Blocking & Closing Tests (No Admin Required)
- **Test 2.1: Substring Matching (Aggressive)**: Configure a keyword as aggressive. Spawn a mock process named `chrome.exe` with title `My youtube video`. Verify the Agent/UI detects it and closes the process. Assert that `Process.HasExited` is true.
- **Test 2.2: Regex Matching (Non-Aggressive)**: Configure a keyword as standard (non-aggressive). Spawn a mock process with title `youtube`. Verify it is closed. Spawn another mock process with title `myyoutube`. Verify it is **not** closed (once the regex bug is fixed).
- **Test 2.3: Closing Mechanism Sequence**: Spawn a custom mock process that records received messages. Verify that the Agent first sends `Ctrl+W`, then `Alt+F4`, and finally `WM_CLOSE`.

### Tier 3: System Hardening & Defense Tests (Admin Required)
- **Test 3.1: SetCritical Verification**: Enable the block timer. Fetch the Agent process handle. Call `NtQueryInformationProcess` and assert that the `ProcessBreakOnTermination` value is `1`.
- **Test 3.2: WMI Process Terminations**: Start the Agent with an active timer. Spawn a mock process named `taskmgr.exe`. Assert that the Agent's WMI scanner terminates it within 1 second.
- **Test 3.3: Firewall Rule Integrity**: Start the Agent with a block on `example.com`. Query the active Windows Firewall rules using `netsh` or PowerShell NetSecurity cmdlets. Verify a rule named `KG_P_<hash>` is created, blocking outgoing traffic for resolved IPs. Re-verify the rule is deleted when the timer expires.

### Tier 4: Service Watchdog & Lifecycle Tests (Admin Required)
- **Test 4.1: Service Recovery**: Verify `KeywordGuardProService` is running. Terminate `KeywordGuard.Pro.Agent.exe` (while the critical flag is deactivated, e.g. timer inactive). Verify that the service automatically restarts the Agent.
- **Test 4.2: Protected Service Stopping**: Set an active block timer. Issue a stop command (`sc stop KeywordGuardProService`). Assert that the command times out or is rejected because the service blocks termination.
- **Test 4.3: Session Ending BSOD Prevention**: With the block active, send a simulated `WM_QUERYENDSESSION` message to the Agent's hidden window handle. Assert that `ProcessBreakOnTermination` changes to `0` and the process exits cleanly.
