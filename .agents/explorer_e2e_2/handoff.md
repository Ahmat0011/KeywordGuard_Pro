# E2E Test Suite Strategy Design Handoff Report

This report outlines the observations, reasoning, caveats, and conclusions regarding the E2E test suite design for KeywordGuard Pro.

---

## 1. Observation

### Code & Compilation Structure
*   **Project Types**: The codebase consists of four C# projects. The Agent (`KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj` line 3) and UI (`KeywordGuard.Pro.UI/KeywordGuard.Pro.UI.csproj` line 3) are compiled as `WinExe`. The Security library (`KeywordGuard.Pro.Security/KeywordGuard.Pro.Security.csproj`) is a class library. The Service watchdog (`KeywordGuard.Pro.Service/KeywordGuard.Pro.Service.csproj` line 3) is compiled as `Exe`.
*   **Target Frameworks**: UI, Agent, and Service target `net10.0-windows` (csproj files). Security targets `net10.0` (csproj file line 3).
*   **Build Scripts**: Compilation is performed via `dotnet build` as observed in `FINAL-Install.ps1` lines 33-42:
    ```powershell
    dotnet build "$BasePath\KeywordGuard.Pro.Security\KeywordGuard.Pro.Security.csproj" -c Release --nologo -v q
    dotnet build "$BasePath\KeywordGuard.Pro.Agent\KeywordGuard.Pro.Agent.csproj" -c Release --nologo -v q
    ```

### Blocked Keywords & Configuration Storage
*   **Storage Directory**: `ConfigStore.cs` lines 18-28 uses `Environment.SpecialFolder.CommonApplicationData` to locate the config files:
    ```csharp
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KG_Pro", "SecureData");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "sys_config.dat");
    ```
*   **Encryption Scheme**: Encrypted using AES-256 CBC. The machine key is defined in `ConfigStore.cs` lines 31-38:
    ```csharp
    string keyMaterial = Environment.MachineName + Environment.ProcessorCount + "KeywordGuard_Pro_V2_2026_Secure";
    ```

### Foreground Window Detection & Title Matching
*   **API Calls**: `Program.cs` lines 287-291 uses `GetForegroundWindow()` and `GetWindowText()` to capture the active window.
*   **Matching Fallback**: `Program.cs` lines 302-303 contains a fallback for standard (non-aggressive) items:
    ```csharp
    if (!hit && !item.IsAggressive)
        hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
    ```
    This causes standard items to match on any substring contains, rendering word boundary checks ineffective.
*   **Closing Windows**: Simulated input via `keybd_event` (`Ctrl+W` then `Alt+F4`) followed by `PostMessage` with `WM_CLOSE` (0x0010) as shown in `Program.cs` lines 318-349.

### WMI Scanner Hazard
*   **Query & Blacklist**: `Program.cs` lines 358-363 scans processes and kills those matching dangerous command line strings.
    ```csharp
    string[] dangerous = { "taskkill", "stop-process", "kill", "remove-item",
        "del /f", "sc delete", "schtasks /delete", "takeown", "icacls",
        "keywordguard", "uninstall", "remove-service" };
    ```

### Process Hardening and Service SetCritical
*   **Process Hardening**: `ProcessHardening.cs` lines 9-34 calls `RtlSetProcessIsCritical` from `ntdll.dll` to protect the process.
*   **Service Watchdog Hardening**: `Worker.cs` lines 147-154 only simulates critical status for the service:
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

---

## 2. Logic Chain

1.  **Silent Startup Verification**: Since the Agent project's `<OutputType>` is set to `WinExe`, it compiles as a GUI subsystem application (Subsystem 2). When executed directly, it does not allocate or display a console. This can be verified programmatically in the test suite by checking the PE headers of the built binary.
2.  **Config Discrepancy & Writing**: `ConfigStore.cs` specifies `CommonApplicationData` (`C:\ProgramData`) for storage, which ordinarily requires elevation to write to. However, the installer runs `Set-Acl` to grant Full Control to local users. Therefore, E2E tests executing in user-context can read and write configurations successfully.
3.  **Process Simulation**: The Agent's foreground detection checks window titles but does not inspect the process name (though Milestone 2 requires browser restriction). By compiling a mock Window Simulator (`TestWindowSim.exe`), renaming/copying it to `chrome.exe` or `notepad.exe`, and setting its title via command-line arguments, we can safely simulate target browser processes and assert window closing by checking if the process is terminated.
4.  **WMI Scan Blocker**: Because the Agent scans process command-lines for `"keywordguard"` and `"kill"`, running the test runner with the word "KeywordGuard" in the folder name, executable name, or arguments will result in the Agent immediately terminating the test runner. The test suite must therefore be compiled and run under a safe alternative path and name (e.g., `C:\KG_Tests\kg_e2e.dll`).
5.  **Agent Shutdown & SetCritical Verification**: The Agent catches OS shutdown using `WM_QUERYENDSESSION` / `WM_ENDSESSION` in `HiddenForm.WndProc`. We can test this without rebooting by targeting the `HiddenForm` window handle and sending these window messages. If the Agent exits cleanly without a BSOD, we verify that `SetCritical(false)` was successfully called before termination.

---

## 3. Caveats

*   **Elevated Privilege Requirements**: Tier 3 and Tier 4 tests require Administrator privileges to interact with Firewall rules, registry entries, and Windows Services. They cannot be executed from a standard user-context prompt.
*   **BSOD Danger**: If the Agent is run as Admin and set to critical, any crash of the agent (or force kill by the test suite) will trigger a system crash (BSOD). Thus, Tier 2 tests must run the Agent *without* Admin privileges, which prevents `SetCritical(true)` from succeeding and ensures safety.
*   **WMI Killing Interruption**: Active protection loop will terminate any CLI processes containing blacklisted strings. If the runner requires invoking `sc.exe stop` or similar during tests, it must do so when the protection timer is inactive or using a renamed executable that bypasses WMI.

---

## 4. Conclusion

A 4-tiered C# test project (`KG_E2E_Test.csproj`) utilizing **xUnit/NUnit** is the optimal strategy.
*   **Tier 1**: Verifies isolation helpers (`UrlHelper`, `ConfigStore` AES keys) in milliseconds under user privileges.
*   **Tier 2**: Verifies Agent window detection and closing using a custom `TestWindowSim.exe` copied as browser executables under user privileges (to prevent BSODs).
*   **Tier 3**: Verifies elevated features (`FirewallBlocker`, `ProcessHardening`, `TaskSchedulerGuard`) under Administrator privileges.
*   **Tier 4**: Verifies watchdog service installation, Session 0 agent spawning, and refusal to stop during active protection under Administrator privileges.

---

## 5. Verification Method

To independently verify the strategy and configuration details:
1.  **Inspect Config Location**: View `KeywordGuard.Pro.Security/ConfigStore.cs` line 18-28.
2.  **Inspect Window Matcher**: View `KeywordGuard.Pro.Agent/Program.cs` line 294-304.
3.  **Verify compilation capability**:
    Run:
    ```powershell
    dotnet build KeywordGuard.Pro.Security\KeywordGuard.Pro.Security.csproj -c Release
    dotnet build KeywordGuard.Pro.Agent\KeywordGuard.Pro.Agent.csproj -c Release
    ```
4.  **Check dangerous WMI list**: View `KeywordGuard.Pro.Agent/Program.cs` lines 401-404.
