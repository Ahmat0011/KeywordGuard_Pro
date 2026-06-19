# Original User Request

## Initial Request — 2026-06-17T09:44:14Z

Resolve multiple critical issues in KeywordGuard Pro, including text file blocking, false-positive closing of unrelated applications, PC shutdown reboots/crashes, and the visible CMD/PowerShell window on startup.

Working directory: d:\sahma\Documents\GitHub\KeywordGuard_Pro
Integrity mode: development

## Requirements

### R1. Restrict Window Closing to Web Browsers
- The active window keyword/title checking must ONLY apply to standard web browsers: Google Chrome (`chrome`), Microsoft Edge (`msedge`), Mozilla Firefox (`firefox`), Opera (`opera`), Brave (`brave`), and Vivaldi (`vivaldi`).
- Non-browser processes (such as Windows Explorer (`explorer`), Notepad (`notepad`), Word (`winword`), text documents, folders, and system applications) must never be closed by the window watcher.
- Short domain fragments (like `ok` from `ok.ru`) must not be aggressively added as standalone keywords unless they are longer than 3 characters, preventing false-positive matches on unrelated window titles (like `Dokumente` or `Outlook`).

### R2. Fix Windows Shutdown and Reboot Crash
- The watchdog service (`KeywordGuard.Pro.Service`) and the background agent (`KeywordGuard.Pro.Agent`) must stop cleanly when a standard Windows shutdown or reboot is initiated.
- The Service must not mark its own process as critical (removing the `SetCritical` flag for the service process) to eliminate shutdown timing crashes.
- The Agent process must cleanly catch Windows shutdown/logoff events (`SystemEvents.SessionEnding` and `AppDomain.CurrentDomain.ProcessExit`) and immediately deactivate its critical process status (`ProcessHardening.SetCritical(false)`) before exiting.
- Normal Windows shutdown, logoff, or reboot must complete successfully without blue screens (BSODs), "problem occurred" screens, or unexpected reboots.

### R3. Silent Background Agent Startup
- The background Agent project (`KeywordGuard.Pro.Agent`) must be configured as a Windows GUI application (`OutputType` = `WinExe` in `.csproj`) instead of a Console application (`Exe`).
- This ensures that when the Agent starts automatically on logon (via Task Scheduler), it runs completely silently in the background without allocating or displaying any visible black command prompt (CMD) or PowerShell window.
- Closing the UI window must not crash or trigger restart loops in the background services.

## Acceptance Criteria

### Window/App Blocking
- [ ] Active window blocking triggers ONLY for browsers (`chrome`, `msedge`, `firefox`, `opera`, `brave`, `vivaldi`) when a blocked term is found in their title.
- [ ] Windows Explorer, Notepad, TXT files, and other non-browser applications remain open and are never closed, even if their titles contain substrings of blocked domains (e.g. opening `Dokumente` or file containing the letters `ok` does not close it).
- [ ] Subdomain parts of length <= 3 (like `ok` from `ok.ru`) are not registered as standalone aggressive keywords.

### Shutdown Safety
- [ ] The watchdog service does not set itself as a critical process, eliminating service-related shutdown BSODs.
- [ ] Standard Windows shutdown/reboot completes cleanly without BSOD, warnings, or restart loops.

### Silent Startup
- [ ] The Agent starts silently at Windows logon without showing any black console window.
- [ ] `KeywordGuard.Pro.Agent.csproj` OutputType is set to `WinExe`.
