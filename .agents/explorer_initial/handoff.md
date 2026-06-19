# Handoff Report - KeywordGuard Pro Exploration

## 1. Observation

### R1: Restrict Window Closing to Web Browsers
* **File Paths and Lines**:
  * `KeywordGuard.Pro.UI/Services/WordWatcher.cs`:
    * Line 63: `string title = GetActiveWindowTitle();`
    * Lines 70-87: Loops through all `BlockedItem` items and compares window title to value.
    * Line 83: Calls `CloseActiveWindow()`.
    * Lines 113-147: `CloseActiveWindow()` closes the active window using simulated keyboard shortcuts (`Ctrl+W` -> `Alt+F4` -> `WM_CLOSE`).
  * `KeywordGuard.Pro.Agent/Program.cs`:
    * Lines 281-313: `CheckActiveWindow` loops through targets and compares window title to value.
    * Line 308: Calls `CloseWindow(handle)`.
    * Lines 318-349: `CloseWindow()` closes the window using simulated keyboard shortcuts (`Ctrl+W` -> `Alt+F4` -> `WM_CLOSE`).
  * **Short Domain Processing**:
    * In `KeywordGuard.Pro.UI/ViewModels/MainViewModel.cs` (lines 355-357) and `KeywordGuard.Pro.Agent/Program.cs` (lines 176-178, 193-195):
      ```csharp
      string domainPart = domain.Split('.')[0];
      if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
          targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
      ```
      This adds any domain fragment of length > 1 (e.g. `'ok'` from `'ok.ru'`) to the targets list as an aggressive keyword.
    * In `WordWatcher.cs` (lines 74-76) and `Program.cs` (lines 298-300):
      ```csharp
      bool hit = item.IsAggressive
          ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
          : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);
      ```
      Because the fragment is added as `IsAggressive = true`, it is checked using `.Contains()`, matching any title containing the substring `'ok'` (e.g. "Outlook", "Book", "Cookies").
    * Furthermore, there is a fallback on lines 78-79 in `WordWatcher.cs` and lines 302-303 in `Program.cs`:
      ```csharp
      if (!hit && !item.IsAggressive)
          hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
      ```
      This overrides non-aggressive checks, causing all keywords to match using `.Contains()` regardless of `IsAggressive`.

### R2: Fix Windows Shutdown and Reboot Crash
* **File Paths and Lines**:
  * `KeywordGuard.Pro.Service/Worker.cs`:
    * Line 151: Calls `ProcessHardening.SetCritical(true)` inside the `ActivateCritical()` method.
    * Line 60: `ActivateCritical()` is called in `ExecuteAsync` when the configuration is active.
    * Running as a Windows Service in Session 0, setting the critical process flag triggers a Bluescreen of Death (BSOD) when Windows attempts to terminate the service during shutdown/reboot.
  * `KeywordGuard.Pro.Agent/Program.cs`:
    * Line 161: Calls `ProcessHardening.SetCritical(true)` inside the block loop.
    * Lines 129-136: Registers `SystemEvents.SessionEnding` and `AppDomain.CurrentDomain.ProcessExit` to clear critical process flag.
    * These handlers are unreliable for headless console processes during system shutdown, as the process may be killed by the OS before the event handlers complete.

### R3: Silent Background Agent Startup
* **File Paths and Lines**:
  * `KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj`:
    * Line 3: `<OutputType>Exe</OutputType>`
    * Compiling the background agent as `Exe` (Console Application) causes Windows to allocate and display a console window on startup.
  * `KeywordGuard.Pro.Agent/Program.cs`:
    * No calls to `AllocConsole` or console output APIs are present. All log output goes to the file defined on line 52: `%COMMONAPPDATA%\KG_Pro\agent.log`.
  * **Restart Loop Hazards**:
    * If the user manually closes the console window of the Agent, the process exits. The watchdog service (`Worker.cs` line 64) detects the process has exited and immediately restarts it, causing the console to reappear in a loop.
    * Additionally, if the user blocks a keyword that matches the console's window title (which contains the executable path/name, e.g. "KeywordGuard"), the Agent matches its own title, terminates itself, and is restarted by the service in an infinite loop.

### Existing Tests and Verification
* **Test Verification**:
  * A search for test-related files or projects returned 0 results. No tests exist in the codebase.
  * Running `dotnet test` on each project confirmed no test cases are registered.
* **Build Verification**:
  * Run `dotnet build <csproj>` on all four projects. All compiled successfully with 0 errors.

---

## 2. Logic Chain

### R1: Restricting Window Closing and Short Domain Fragments
1. **Observation 1.1**: The active window closing logic in `WordWatcher.cs` and `Program.cs` retrieves only the foreground window handle (`GetForegroundWindow()`) and simulates closing keys. It has no check for the process name.
2. **Observation 1.2**: Windows API `GetWindowThreadProcessId` resolves a process ID from a window handle. The .NET `Process.GetProcessById(pid)` can retrieve the process name.
3. **Conclusion 1.1**: Introducing a whitelist check of allowed browsers (`chrome`, `msedge`, `firefox`, `opera`, `brave`, `vivaldi`) based on the process name before simulating keyboard input will restrict window closing strictly to web browsers and prevent closing non-browser applications.
4. **Observation 1.3**: Domain fragments of length > 1 are added as aggressive keywords. For example, `'ok.ru'` results in `'ok'` being added.
5. **Observation 1.4**: Aggressive keywords use `.Contains()`. Non-aggressive keywords fall back to `.Contains()`. This makes any title with the substring `'ok'` (like "Outlook" or "Book") trigger window closure.
6. **Conclusion 1.2**: Restricting the domain part extraction length to `domainPart.Length > 3` will filter out short fragments like "ok" or "vk". Fixing the fallback in `WordWatcher` and `Program.cs` to not run `.Contains()` for non-aggressive keywords will ensure word boundary (`\b`) matching works as intended.

### R2: Windows Shutdown and Reboot Crash
1. **Observation 2.1**: The Windows Service `KeywordGuard.Pro.Service` calls `ProcessHardening.SetCritical(true)` when active.
2. **Observation 2.2**: Windows services are executed in Session 0 and are managed by the Service Control Manager. Termination of a critical process in Session 0 by SCM during shutdown/reboot causes a BSOD.
3. **Conclusion 2.1**: The watchdog service does not need critical process status. Setting it critical is redundant because SCM shutdown is normal, and service self-protection is already achieved via `stoppingToken` registration and `StopAsync` blocking. Removing `SetCritical(true)` from the service fixes the shutdown crash.
4. **Observation 2.3**: `KeywordGuard.Pro.Agent` sets critical process status and intercepts shutdown via `SystemEvents.SessionEnding` and `AppDomain.CurrentDomain.ProcessExit`. This is unreliable because the process can be terminated by the OS before the event handlers complete.
5. **Observation 2.4**: The Agent runs `Application.Run()` which starts a Windows message pump.
6. **Conclusion 2.2**: Creating a hidden utility form that runs in the message pump and overrides `WndProc` to handle native Win32 messages `WM_QUERYENDSESSION` (0x0011) and `WM_ENDSESSION` (0x0016) provides a guaranteed way to immediately call `ProcessHardening.SetCritical(false)` before the OS terminates the process.

### R3: Silent Background Agent Startup
1. **Observation 3.1**: The Agent csproj has `<OutputType>Exe</OutputType>`, causing a console window to display.
2. **Observation 3.2**: No console write operations are performed by the C# code itself.
3. **Observation 3.3**: The service watchdog restarts the Agent when it exits. If a user closes the console, it triggers an infinite restart loop.
4. **Conclusion 3.1**: Changing the `OutputType` to `WinExe` and using the hidden form in `Application.Run` will make the Agent launch completely silently in the background, eliminating the console window, user closures, and the associated restart loops.

---

## 3. Caveats

* **Browser Executable Names**: The browser whitelist assumes default executable names: `chrome`, `msedge`, `firefox`, `opera`, `brave`, `vivaldi`. If a portable version or renamed executable is used, it will not be matched and the window will not be closed.
* **Privileges**: Changing `SetCritical` requires administrator rights (`SeDebugPrivilege`). If the Agent is run without administrator privileges, `SetCritical` fails silently, which is normal and handled in `ProcessHardening.cs`.

---

## 4. Conclusion

1. **R1 (Browser Restriction & Domains)**: Window closing must be restricted by querying the active window's PID, fetching its process name, and verifying it is in the whitelist of browser processes. Short domain fragments like "ok" must be filtered by increasing the fragment length threshold to `> 3`, and the title-checking logic fallback must be corrected to respect word boundary checks for non-aggressive keywords.
2. **R2 (Shutdown BSOD)**: Clear critical process status from the watchdog Service entirely. In the Agent, implement a hidden Form and override `WndProc` to handle `WM_QUERYENDSESSION` / `WM_ENDSESSION` to deactivate critical status immediately during shutdown.
3. **R3 (Silent Startup)**: Change the Agent's project output type to `WinExe` to prevent console window allocation.
4. **Tests**: No tests exist in the codebase. Project compilation is verified using `dotnet build`.

---

## 5. Verification Method

### Compilation and Build Commands
* Run these commands from the root directory to verify that the projects build:
  ```powershell
  dotnet build KeywordGuard.Pro.Security\KeywordGuard.Pro.Security.csproj -c Release
  dotnet build KeywordGuard.Pro.Agent\KeywordGuard.Pro.Agent.csproj -c Release
  dotnet build KeywordGuard.Pro.UI\KeywordGuard.Pro.UI.csproj -c Release
  dotnet build KeywordGuard.Pro.Service\KeywordGuard.Pro.Service.csproj -c Release
  ```

### Code Review and Inspection Points
* Inspect `KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj` to confirm output type change.
* Inspect `KeywordGuard.Pro.UI/Services/WordWatcher.cs` and `KeywordGuard.Pro.Agent/Program.cs` to verify browser whitelist checking in window closing methods, and length validation on domain parts.
* Inspect `KeywordGuard.Pro.Service/Worker.cs` to verify absence of `ProcessHardening.SetCritical` calls.
