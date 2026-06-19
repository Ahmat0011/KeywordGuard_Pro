# KeywordGuard Pro E2E Test Suite Strategy & Codebase Analysis

## 1. Executive Summary
This analysis outlines the architecture of KeywordGuard Pro, its security controls, process and keyword matching mechanisms, and proposes a comprehensive end-to-end (E2E) test suite strategy. The proposed strategy is structured into four distinct execution tiers to validate compilation, configuration encryption, core process-blocking capabilities, and service watchdog/lifecycle protection.

---

## 2. Codebase Structure & Build Analysis

### 2.1. Project Modules
The repository consists of four C# projects targeting .NET 10.0 and Windows platforms:
- **`KeywordGuard.Pro.Security`**: A Class Library (`net10.0` with `SupportedOSPlatformVersion` = `10.0.19041.0`) containing helper methods for URL/domain handling, AES-256 encrypted configuration storage, hosts file editing (currently disabled by user constraints), and Windows Firewall rule management.
- **`KeywordGuard.Pro.Agent`**: A Windows Forms application (`net10.0-windows` with `<OutputType>WinExe</OutputType>`) running in the user session. It continuously checks foreground window titles and closes matches using standard keystrokes or Windows messages.
- **`KeywordGuard.Pro.UI`**: A WPF application (`net10.0-windows` with `<OutputType>WinExe</OutputType>`) where the user configures the blocked keyword list and monitoring duration. It also includes an active window title watcher (`WordWatcher`) running within the UI process when the UI is active.
- **`KeywordGuard.Pro.Service`**: A Windows Service (`net10.0-windows` with `<OutputType>Exe</OutputType>`) acting as a watchdog to monitor the Agent process and restart it if terminated. It runs under Session 0.

### 2.2. Build Directories
Binaries compile to their standard MSBuild output directories:
- `KeywordGuard.Pro.Security/bin/[Configuration]/net10.0/`
- `KeywordGuard.Pro.Agent/bin/[Configuration]/net10.0-windows/`
- `KeywordGuard.Pro.UI/bin/[Configuration]/net10.0-windows/`
- `KeywordGuard.Pro.Service/bin/[Configuration]/net10.0-windows/`

### 2.3. Deployment & Installation Layout
During installation (via `FINAL-Install.ps1`), all compiled executables and dependencies are consolidated into a single folder:
- **Unified Location**: `C:\Program Files\KG_Pro\UI`
- **Output Files**:
  - `KeywordGuard.Pro.Agent.exe`
  - `KeywordGuard.Pro.UI.exe`
  - `KeywordGuard.Pro.Service.exe`
  - `KeywordGuard.Pro.Security.dll`
  - Associated dependency and config files (`*.json`, `*.dll`, `*.deps.json`).

---

## 3. Configuration Storage & Cryptography

### 3.1. File Locations
The configuration (`GuardConfig`) contains a list of blocked URLs, keywords, and an expiration timestamp (`EndTime`). It is saved in two physical locations:
1. **Primary Config File**: `C:\ProgramData\KG_Pro\SecureData\sys_config.dat`
2. **Backup Config File**: `C:\ProgramData\KG_Pro\SysConfigBackup\kg_config.enc`

*Note on Path Discrepancy*: Although comments in the codebase mention `%LOCALAPPDATA%` (to bypass admin restrictions), the code actually uses `Environment.SpecialFolder.CommonApplicationData` (which resolves to `C:\ProgramData`). 

### 3.2. Access Permissions (ACLs)
Because `C:\ProgramData` normally requires Administrator privileges for write operations, the installer explicitly modifies the folder access control list (ACL) during setup:
- It grants **Full Control** over `C:\ProgramData\KG_Pro` to the built-in Windows **Users** group (`S-1-5-32-545`).
- This allows both the non-admin UI and background Agent to read and write the configuration files without raising UAC prompts.

### 3.3. AES-256 Encryption & File Attributes
- **Key Derivation**: The encryption key is derived dynamically using machine characteristics:
  ```csharp
  string keyMaterial = Environment.MachineName + Environment.ProcessorCount + "KeywordGuard_Pro_V2_2026_Secure";
  ```
  This string is hashed via SHA256 to generate a 256-bit AES key.
- **Encryption Algorithm**: AES-256 in Cipher Block Chaining (CBC) mode with PKCS7 padding.
- **Binary Format**: The written file uses a custom format consisting of:
  - First 16 bytes: Initialization Vector (IV)
  - Remaining bytes: Encrypted ciphertext of the serialized JSON configuration.
- **Hiding Mechanism**: After writing, the files' NTFS attributes are modified to `FileAttributes.Hidden | FileAttributes.System`. Before writing, files are set to `FileAttributes.Normal` to ensure they can be overwritten without permission errors.

---

## 4. Window Title Matching & Browser Detection Logic

### 4.1. Foreground Window Detection
The Agent and UI use standard User32 Win32 APIs:
- `GetForegroundWindow()`: Retrieves the handle (`IntPtr`) of the active window in the foreground.
- `GetWindowText(IntPtr hWnd, StringBuilder text, int count)`: Retrieves the title text of the window.

### 4.2. Keyword Matching & Normalization Rules
Before checking, keywords and URLs are normalized:
- **Domains**: If the keyword represents a domain/URL (e.g. `domain.com`), the agent extracts the domain name. The domain and its primary segment (e.g. `domain` if longer than 1 character) are added to the list of target keywords as aggressive rules.
- **Matching Logic**:
  - **Aggressive Match**: If `IsAggressive = true`, it checks if the title contains the keyword:
    `title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)`
  - **Standard Match**: If `IsAggressive = false`, it attempts to match words at boundaries:
    `Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase)`
    - **Fallback**: If the regex fails, it falls back to a simple `title.Contains` check. (Note: This fallback effectively defeats the purpose of the word-boundary regex and is scheduled for fixing in Milestone 2).

### 4.3. Browser Restriction Scope
- **Current State**: The active window check does **not** filter by process type or executable name. If *any* active window (including Notepad, Word, or folder explorer) contains the blocked keyword in its title, the Agent will trigger.
- **Milestone 2 Goal**: Limit window closing exclusively to standard web browsers (e.g. `chrome.exe`, `msedge.exe`, `firefox.exe`, `opera.exe`, etc.) to prevent unwanted side-effects on other applications.

### 4.4. Window Closing Keystroke Simulation
When a match is found, the Agent attempts to close the window using a three-stage sequence:
1. **Stage 1 (Keystroke Ctrl+W)**: Attempts to close the active tab. It calls `SetForegroundWindow`, sleeps for 50ms, simulates control and 'W' key-down events, and then releases them.
2. **Stage 2 (Keystroke Alt+F4)**: If the foreground window handle has not changed after 200ms, it simulates an `Alt+F4` key combination to close the entire window.
3. **Stage 3 (WM_CLOSE)**: If the window is still open after another 200ms, it sends a `WM_CLOSE` message (value `0x0010`) to the window handle using `PostMessage`.

---

## 5. Mocking & Process Simulation Strategy

To test window closing behavior safely, repeatably, and without dependencies on third-party browsers, the E2E test suite should utilize simulated targets:

### 5.1. Creating a Process Simulator
A dedicated C# console or WinForms utility (`WindowMock.exe`) should be compiled. This utility should:
1. Accept arguments for the process name and desired window title (e.g., `WindowMock.exe --title "Forbidden Keyword - Google Chrome"`).
2. Set its window title programmatically to the requested string.
3. Keep running until closed.
4. Listen for and handle closing events (like standard form closing, `Ctrl+W` shortcuts, or `WM_CLOSE` windows messages) and exit cleanly with code `0`.

### 5.2. Simulating Browser Names (UAC/AppPaths)
To satisfy browser-specific process checks (in future milestones), the test suite can:
- Copy the compiled `WindowMock.exe` to a temporary test directory.
- Rename the copied executable to target browser executables, e.g., `chrome.exe`, `msedge.exe`, or `firefox.exe`.
- Launch the renamed process from the temporary folder. (Since the agent identifies processes by process name/details, renaming the executable is sufficient).

### 5.3. Asserting Window Closing
To verify the window has been successfully closed, the test code should:
- Record the process ID (PID) of the launched mock process.
- Perform a polling check with a timeout (e.g., up to 5 seconds):
  ```csharp
  bool exited = mockProcess.WaitForExit(5000);
  ```
- Query the system process list: `Process.GetProcessById(pid)` should throw an `ArgumentException` (indicating the process is dead) or return `null`.
- Inspect the agent's log file (`C:\ProgramData\KG_Pro\agent.log`) for the exact entry confirming the block:
  `HH:mm:ss [AGENT] BLOCKED: '<keyword>' in Fenster '<title>'`

---

## 6. Lifecycle: Startup, Hardening, and Shutdown

### 6.1. Silent Startup
- The Agent and UI are compiled with `<OutputType>WinExe</OutputType>`.
- In Windows, this sets the executable subsystem to `IMAGE_SUBSYSTEM_WINDOWS_GUI` (value `2`).
- When executed, Windows does not allocate or attach a console window. Standard output streams are detached, and the process runs silently in the background.

### 6.2. Process Hardening (`SetCritical`)
- `ProcessHardening.SetCritical` enables `SeDebugPrivilege` (privilege ID 20) via `RtlAdjustPrivilege` and marks the process as critical using `RtlSetProcessIsCritical` from `ntdll.dll`.
- **BSOD Risk**: If a critical process is killed using `taskkill /f` or via Task Manager, the Windows kernel will crash immediately (BSOD).
- **Safety in E2E Testing**:
  - The E2E tests **must not** forcefully terminate the Agent while critical status is active to avoid crashing the test runner machine.
  - To safely test `SetCritical` removal, the test suite must send a shutdown signal (reproducing Windows shutdown) which triggers the Agent to deactivate critical status before exiting.

### 6.3. Agent Shutdown & Signal Simulation
- The Agent registers for `SystemEvents.SessionEnding` and runs a hidden form (`HiddenForm`) that listens for `WM_QUERYENDSESSION` (`0x0011`) and `WM_ENDSESSION` (`0x0016`) messages.
- Upon receiving these signals:
  - It sets `_isShuttingDown = true` and `_running = false`.
  - It calls `ProcessHardening.SetCritical(false)` to clear the critical state.
  - It removes firewall blocks via `HostsBlocker.RemoveAll()` and `FirewallBlocker.RemoveAll()`.
  - It logs: `"Critical deactivated + Hosts cleaned by HiddenForm WndProc. Exiting..."`.
- **E2E Verification**: The test suite can launch the Agent, find its hidden window, send a `WM_QUERYENDSESSION` message using `PostMessage`, and assert that the process exits cleanly (exit code 0) within 3 seconds, and verify the log contains the deactivation messages.

### 6.4. Watchdog Service & Protection Behavior
- The watchdog service (`KeywordGuardProService`) checks every 2 seconds if the Agent is running and restarts it via `schtasks.exe /run /tn "KeywordGuardProStartup"` (to run in the active user session).
- **Self-Protection Block**: If the service receives a stop request (e.g., `sc stop`), and the config timer is still active, it blocks stopping by entering a blocking loop until the timer expires.
- **E2E Verification**: The test suite can write an active config, try to stop the service, assert that the service remains in the `STOP_PENDING` or `RUNNING` state (does not stop), then overwrite the config with an expired timer, try to stop the service again, and assert that it stops successfully.

---

## 7. E2E Test Suite Architecture & Design

### 7.1. Directory Structure
The E2E test suite should be placed in a dedicated C# project `KeywordGuard.Pro.E2ETests`.
```
KeywordGuard_Pro/
├── KeywordGuard.Pro.E2ETests/
│   ├── KeywordGuard.Pro.E2ETests.csproj
│   ├── AssemblySetup.cs                 # Global setup/teardown (UAC check, service cleaning)
│   ├── WindowMock/                      # Subfolder for window simulator
│   │   ├── Program.cs                   # WindowMock source code
│   │   └── WindowMock.csproj
│   ├── Tiers/
│   │   ├── Tier1_BuildAndStaticTests.cs  # Compilation & PE header checks
│   │   ├── Tier2_ConfigAndCryptoTests.cs # Config location, ACLs, AES validation
│   │   ├── Tier3_CoreFunctionTests.cs    # Window detection, matching & closing tests
│   │   └── Tier4_SystemLifecycleTests.cs # Watchdog restart, SetCritical shutdown, service block
│   └── Helpers/
│       ├── ProcessHelper.cs             # Process launching, renaming, and termination
│       ├── ConfigHelper.cs              # Helper to write/encrypt test configs
│       └── ServiceHelper.cs             # Wrapper around sc.exe/schtasks.exe commands
```

### 7.2. Four Tiers of Tests

#### Tier 1: Build & Static Verification
- **Test 1.1**: Clean and rebuild all projects using `dotnet build` in Release mode. Assert success (exit code 0).
- **Test 1.2**: Check that the output directory contains the key binaries.
- **Test 1.3**: Read the PE headers of `KeywordGuard.Pro.Agent.exe` and `KeywordGuard.Pro.UI.exe` to verify the Subsystem field is `2` (Windows GUI / WinExe) and `KeywordGuard.Pro.Service.exe` is `3` (Windows Console).

#### Tier 2: Configuration & Cryptography Verification
- **Test 2.1**: Write a known `GuardConfig` using the application's configuration writer or helper. Verify that files are created at `C:\ProgramData\KG_Pro\SecureData\sys_config.dat` and `C:\ProgramData\KG_Pro\SysConfigBackup\kg_config.enc`.
- **Test 2.2**: Assert that the config files cannot be parsed directly as plain-text JSON (verifying they are encrypted).
- **Test 2.3**: Read the files, apply the AES-256 decryption using the machine key (`Environment.MachineName` + processor count + salt), and assert that the decrypted contents match the serialized test configuration.
- **Test 2.4**: Query the file attributes of the saved config files and assert that the `Hidden` and `System` flags are set.
- **Test 2.5**: Query the NTFS access control lists (ACL) of the `C:\ProgramData\KG_Pro` directory and verify that the `Users` group (`S-1-5-32-545`) has `FullControl` rights.

#### Tier 3: Core Functional (Process & Window Blocking) Verification
- **Test 3.1**: Set a config with a keyword (e.g. `blocksite.com`) and an active blocking window. Launch the Agent. Copy `WindowMock.exe` to `chrome.exe`, launch it with the title `"blocksite.com - Google Chrome"`. Assert that `chrome.exe` is terminated within 5 seconds.
- **Test 3.2**: Launch the mock process with a title that does *not* contain the keyword (e.g., `"safeplace.org - Google Chrome"`). Assert that the process remains running after 5 seconds.
- **Test 3.3**: Launch a dummy executable named `taskmgr.exe` or `ProcessHacker.exe` in a temporary folder. Assert that the Agent immediately kills it (WMI process scanner validation).
- **Test 3.4**: Verify firewall rules are generated for the blocked domain (using `netsh advfirewall show rule`).

#### Tier 4: System Integration & Lifecycle Verification
- **Test 4.1**: Install and start the watchdog service. Assert that the service starts `KeywordGuard.Pro.Agent` automatically if a user session is active.
- **Test 4.2**: Terminate the Agent process manually. Assert that the watchdog service restarts the Agent via Task Scheduler within 5 seconds.
- **Test 4.3**: Write an active config (active timer). Attempt to stop the service (`sc stop`). Assert that the service refuses to stop and remains in `STOP_PENDING` or `RUNNING`.
- **Test 4.4**: Write a configuration with an expired timer. Attempt to stop the service. Assert that the service stops cleanly within 5 seconds.
- **Test 4.5**: Start the Agent. Find its hidden `HiddenForm` window handle. Send `WM_QUERYENDSESSION` to the window. Assert that the Agent exits cleanly (exit code 0), and that the log file shows the critical flag was deactivated and hosts/firewall blocks were removed.

---

## 8. Test Execution and Reporting
- **Test Framework**: MSTest or xUnit, allowing easy integration with the dotnet CLI.
- **CLI Commands**:
  - Run all tests: `dotnet test KeywordGuard.Pro.E2ETests.csproj --logger:"html;logfilename=e2e_results.html"`
  - Run specific tier: `dotnet test KeywordGuard.Pro.E2ETests.csproj --filter "FullyQualifiedName~Tiers.Tier3"`
- **Required Privileges**: The test runner (CMD or PowerShell) **must** be executed with **Administrator privileges** to support service installation (`sc.exe`), Task Scheduler creation (`schtasks.exe`), and firewall rules checking.
