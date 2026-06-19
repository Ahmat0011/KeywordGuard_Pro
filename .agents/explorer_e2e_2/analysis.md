# E2E Test Suite Strategy Analysis: KeywordGuard Pro

This report details the architectural, functional, and security characteristics of KeywordGuard Pro, and outlines a comprehensive strategy for a 4-tiered End-to-End (E2E) test suite that runs locally on Windows.

---

## 1. Code Structure & Build System

### Repository Layout
The repository contains four C# projects and an installer script:
*   **`KeywordGuard.Pro.UI`**: A WPF-based user interface application (`net10.0-windows`, `WinExe` output type) where users configure block rules and active duration.
*   **`KeywordGuard.Pro.Agent`**: A Windows Forms-based background monitor (`net10.0-windows`, `WinExe` output type) that handles foreground window tracking, window closing, WMI process scanning, and firewall/hosts blocking.
*   **`KeywordGuard.Pro.Service`**: A Windows Service wrapper (`net10.0-windows`, `Exe` output type) running in Session 0 to restart the Agent if it stops, and to refuse service stopping while protection is active.
*   **`KeywordGuard.Pro.Security`**: A shared class library (`net10.0`, `Library` output type) containing utilities for encryption/decryption, process hardening, firewall rule manipulation, and hosts file management.

### Build Mechanism
The projects do not share a single root `.sln` file but rather use independent `.csproj` and `.slnx` files. They are built individually using the .NET SDK:
```powershell
dotnet build "KeywordGuard.Pro.Security\KeywordGuard.Pro.Security.csproj" -c Release
dotnet build "KeywordGuard.Pro.Agent\KeywordGuard.Pro.Agent.csproj" -c Release
dotnet build "KeywordGuard.Pro.UI\KeywordGuard.Pro.UI.csproj" -c Release
dotnet build "KeywordGuard.Pro.Service\KeywordGuard.Pro.Service.csproj" -c Release
```

### Build Outputs
The compilation outputs are placed in:
*   **Agent**: `KeywordGuard.Pro.Agent/bin/[Config]/net10.0-windows/KeywordGuard.Pro.Agent.exe`
*   **UI**: `KeywordGuard.Pro.UI/bin/[Config]/net10.0-windows/KeywordGuard.Pro.UI.exe`
*   **Service**: `KeywordGuard.Pro.Service/bin/[Config]/net10.0-windows/KeywordGuard.Pro.Service.exe`
*   **Security**: `KeywordGuard.Pro.Security/bin/[Config]/net10.0/KeywordGuard.Pro.Security.dll`

*Note: The installer script (`FINAL-Install.ps1`) copies all release binaries to a single directory: `C:\Program Files\KG_Pro\UI\`.*

---

## 2. Configuration & Keyword List Storage

The configuration is managed by the `ConfigStore` class in `KeywordGuard.Pro.Security`.

### Encryption Details
*   **Algorithm**: AES-256 in CBC mode with PKCS7 padding.
*   **Key Generation**: Machine-specific key material derived from:
    `Environment.MachineName + Environment.ProcessorCount + "KeywordGuard_Pro_V2_2026_Secure"` hashed using SHA-256.
*   **File Format**: `[16-byte IV] + [encrypted ciphertext JSON]`.

### Storage Locations
The Agent and UI read and write the configuration in two redundant directories:
1.  **Primary Config**: `C:\ProgramData\KG_Pro\SecureData\sys_config.dat`
2.  **Backup Config**: `C:\ProgramData\KG_Pro\SysConfigBackup\kg_config.enc`

### Key Observations & Discrepancies
*   **Comment vs. Code Discrepancy**: In `ConfigStore.cs`, the comments mention using `%LOCALAPPDATA%` to allow non-admin write access. However, the code explicitly targets `Environment.SpecialFolder.CommonApplicationData` (`C:\ProgramData`), which defaults to requiring Administrator privileges.
*   **Write Access Mitigation**: The installer (`FINAL-Install.ps1`) resolves this by executing `Set-Acl` on `C:\ProgramData\KG_Pro` to grant `FullControl` to all local users. This allows the non-elevated UI process to successfully save configuration changes.

---

## 3. Foreground Window Detection & Matching Loop

### The Monitoring Loop
The Agent runs a `System.Windows.Forms.Timer` that ticks every **500ms**:
1.  It checks the configuration's activation status via `config.IsActive()`.
2.  If active, it queries the current foreground window handle via `GetForegroundWindow()`.
3.  It fetches the window title using `GetWindowText(handle, sb, 512)`.
4.  It matches the window title against the list of blocked keywords and URLs.

### Keyword Matching Logic
The matching behavior depends on whether the item is marked as aggressive:
*   **Aggressive**: Checked via case-insensitive substring matching (`Contains` with `StringComparison.OrdinalIgnoreCase`).
*   **Standard (Non-Aggressive)**: Checked via Regex word boundaries: `\b` + `Regex.Escape(keyword)` + `\b`.
*   **The Fallback Bug**: `Program.cs` line 302-303 contains a fallback:
    ```csharp
    if (!hit && !item.IsAggressive)
        hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
    ```
    This fallback causes standard keywords to match via substring contains anyway, effectively rendering the word-boundary check useless.

### Window Closing Sequence
When a match is found, the Agent calls `CloseWindow(handle)` (and UI's `WordWatcher` calls `CloseActiveWindow()`):
1.  Brings the window to the foreground via `SetForegroundWindow`.
2.  Simulates `Ctrl + W` (closes browser tabs).
3.  Waits 200ms. If the window handle is still active, simulates `Alt + F4`.
4.  Waits 200ms. Sends a win32 `WM_CLOSE` (0x0010) message directly to the handle.

---

## 4. Safe Process Simulation & WMI Scanner Hazards

To E2E test window detection and closing without opening actual web browsers, we must mock processes.

### Safe Process Simulator Design
We can compile a helper utility (`TestWindowSim.exe`) that:
1.  Creates a window using Windows Forms or WPF.
2.  Sets its window title based on command-line arguments (e.g. `--title "My Blocked Site"`).
3.  Sets a safety timer (e.g., `--timeout 5000`) so it automatically exits if not terminated by the Agent, preventing orphaned processes.
4.  Listens to standard closing events (`FormClosing`, `WndProc`) and logs them.

During tests, we copy `TestWindowSim.exe` to a temporary directory under names like `chrome.exe`, `firefox.exe`, or `notepad.exe` and launch it. The test runner brings it to the foreground, then asserts whether the process exits.

### WMI Scanner Hazard (Critical Warning)
When the protection timer is active, the Agent runs `ScanDangerousProcesses()`, which queries WMI:
`SELECT ProcessId, CommandLine, Name FROM Win32_Process`
It immediately terminates any process containing these command-line substrings:
`"taskkill", "stop-process", "kill", "remove-item", "del /f", "sc delete", "schtasks /delete", "takeown", "icacls", "keywordguard", "uninstall", "remove-service"`

#### Impact on E2E Testing:
*   **Test Runner Termination**: If the test runner folder, dll name, or command line contains `"KeywordGuard"`, `"kill"`, or `"stop-process"`, **the Agent will instantly kill the test runner process**.
*   **Workaround**: 
    1.  Test assemblies and execution paths must **not** contain the word "KeywordGuard" (e.g., compile test assemblies as `KG_E2E_Test.dll` and run from `C:\KG_Tests\`).
    2.  Avoid launching shell commands with banned terms while protection is active.
    3.  Alternatively, introduce a `--test-mode` command-line flag in the Agent to skip `ScanDangerousProcesses` and `SetCritical` during testing.

---

## 5. Startup, Process Hardening & Shutdown Verification

### Silent Startup Verification
The Agent project has `<OutputType>WinExe</OutputType>`. 
*   **Verification**: The E2E test can parse the compiled PE header of `KeywordGuard.Pro.Agent.exe` to verify that the subsystem is set to `2` (Windows GUI), which guarantees no console window is allocated upon startup.

### Service SetCritical Verification
*   **Observation**: In `KeywordGuard.Pro.Service/Worker.cs`, the service does *not* call the real `ProcessHardening.SetCritical`. It only logs `Service als kritisch markiert (simuliert, API deaktiviert)`.
*   **Verification**: The E2E test can inspect the service logs or verify that stopping the service process via Task Manager does not trigger a BSOD.

### Agent Shutdown Behavior
If the Agent is killed while critical status is active, Windows will BSOD. The Agent implements a `HiddenForm` to catch Windows shutdown.
*   **Verification**:
    1.  Start the Agent with Admin privileges and activate the guard.
    2.  Use Win32 API `EnumThreadWindows` to locate the `HiddenForm` window handle belonging to the Agent.
    3.  Post `WM_QUERYENDSESSION` (0x0011) and `WM_ENDSESSION` (0x0016) messages directly to it.
    4.  Verify that the Agent process exits cleanly within a timeout, and that the log file shows:
        `HiddenForm WndProc received WM_QUERYENDSESSION/WM_ENDSESSION` and `Critical deactivated...`.
        Since the machine did not BSOD, this confirms that the critical status was successfully cleared by the shutdown handler.

---

## 6. Proposed E2E Test Suite Structure (Tiers 1-4)

We propose creating a C# testing library, `KG_E2E_Test.csproj`, categorizing test cases into 4 distinct tiers using test traits (`[Trait("Category", "TierX")]`):

```
KeywordGuard_Pro/
├── .agents/
├── KeywordGuard.Pro.E2E/
│   ├── KeywordGuard.Pro.E2E.csproj (renamed build output: kg_e2e.dll)
│   ├── Helpers/
│   │   ├── WindowSimulator.cs (spawns mock chrome/notepad processes)
│   │   ├── Win32Api.cs        (SetForegroundWindow, SendMessage)
│   │   └── TestConfig.cs      (wipes/restores config files)
│   ├── Tier1_UnitSimulation/
│   │   ├── UrlHelperTests.cs
│   │   └── EncryptionTests.cs
│   ├── Tier2_UserSessionE2E/
│   │   ├── WindowClosingTests.cs
│   │   └── AgentShutdownTests.cs
│   ├── Tier3_ElevatedE2E/
│   │   ├── FirewallBlockerTests.cs
│   │   └── TaskSchedulerTests.cs
│   └── Tier4_WatchdogServiceE2E/
│       └── WatchdogTests.cs
```

### Tier Descriptions

#### Tier 1: Unit & Simulation Tests (Fast, Non-Elevated)
*   **Focus**: Verifies isolated component logic without running the actual background processes or modifying system state.
*   **Tests**:
    *   Verify `UrlHelper.ExtractDomain` behaves correctly (resolving subdomains, handling paths, short domains, etc.).
    *   Verify `ConfigStore` encrypts/decrypts configurations using simulated machine metadata.
    *   Verify window title matching logic (standard vs aggressive) against mocked titles.

#### Tier 2: User-Session E2E Tests (Non-Elevated)
*   **Focus**: Verifies Agent window closing loop and clean shutdown handling under a standard user account.
*   **Tests**:
    *   Launch the Agent process in user context (non-admin, so `SetCritical` fails safely).
    *   Copy simulator to `chrome.exe`, launch with blocked title, bring to foreground, assert process exits.
    *   Copy simulator to `notepad.exe`, launch with blocked title, bring to foreground, assert process remains running.
    *   Send `WM_QUERYENDSESSION` to Agent window, assert clean process exit and log updates.

#### Tier 3: Elevated E2E Tests (Requires Admin)
*   **Focus**: Verifies system-level changes like firewall rules, task scheduler, and process critical status.
*   **Tests**:
    *   Verify `FirewallBlocker` creates rules named `KG_P_[Hash]` using `netsh`, and removes them on completion.
    *   Verify `TaskSchedulerGuard` creates the `KeywordGuardProStartup` log-on task, and removes it.
    *   Verify that `ProcessHardening.SetCritical` successfully runs as Admin and returns true.

#### Tier 4: Watchdog Service E2E Tests (Requires Admin)
*   **Focus**: Verifies Session 0 service watchdog behavior and resistance to stopping.
*   **Tests**:
    *   Install the watchdog service via `sc.exe`.
    *   Start the service, verify it is running.
    *   Wipe configuration to an active timer state. Verify the service automatically launches the Agent.
    *   Attempt to stop the service via `ServiceController.Stop()`, assert that it blocks/refuses to stop.
    *   Simulate timer expiration by rewriting the configuration file. Assert the service now stops successfully.
    *   Uninstall the watchdog service.

### Execution & Reporting Strategy
*   **Command Line**:
    ```powershell
    # Execute non-elevated tests
    dotnet test kg_e2e.dll --filter "Category=Tier1|Category=Tier2" --logger trx
    
    # Execute elevated tests (must run in Admin prompt)
    dotnet test kg_e2e.dll --filter "Category=Tier3|Category=Tier4" --logger trx
    ```
*   **Output format**: Standard Visual Studio `.trx` test report.
