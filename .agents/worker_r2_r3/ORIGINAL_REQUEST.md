## 2026-06-17T09:51:26Z
You are a teamwork_preview_worker. Your role is Code Implementer.
Your working directory is d:\sahma\Documents\GitHub\KeywordGuard_Pro\.agents\worker_r2_r3.
Your task is to implement the following changes in the KeywordGuard_Pro codebase:

1. In `KeywordGuard.Pro.Service/Worker.cs`:
   Remove the call to `ProcessHardening.SetCritical` (specifically both `SetCritical(true)` and `SetCritical(false)`). You can simply comment out or delete those lines, or empty the body of the `ActivateCritical()` and `DeactivateCritical()` methods so they don't invoke the security critical functions.
2. In `KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj`:
   Change the `<OutputType>Exe</OutputType>` to `<OutputType>WinExe</OutputType>`.
3. In `KeywordGuard.Pro.Agent/Program.cs`:
   - Implement a hidden WinForms `Form` class (e.g. `HiddenForm`) inside or alongside the `Program` class.
   - The hidden Form must be minimized, not visible, not show in taskbar, borderless, opacity 0, etc. (override `SetVisibleCore` to return `false` to keep it completely hidden).
   - In this form, override the `WndProc(ref Message m)` method.
   - In `WndProc`, listen for `WM_QUERYENDSESSION` (0x0011) and `WM_ENDSESSION` (0x0016).
   - If either message is received, immediately call `ProcessHardening.SetCritical(false)` and then exit cleanly (e.g. `Application.Exit()`). Make sure to call the base `WndProc` so the OS can process the message.
   - Change the `Application.Run();` call in `Main` to `Application.Run(new HiddenForm());` to run this hidden form in the application message loop.
4. Verify that the following build commands succeed:
   - `dotnet build d:\sahma\Documents\GitHub\KeywordGuard_Pro\KeywordGuard.Pro.Security\KeywordGuard.Pro.Security.csproj`
   - `dotnet build d:\sahma\Documents\GitHub\KeywordGuard_Pro\KeywordGuard.Pro.Agent\KeywordGuard.Pro.Agent.csproj`
   - `dotnet build d:\sahma\Documents\GitHub\KeywordGuard_Pro\KeywordGuard.Pro.Service\KeywordGuard.Pro.Service.csproj`
   - `dotnet build d:\sahma\Documents\GitHub\KeywordGuard_Pro\KeywordGuard.Pro.UI\KeywordGuard.Pro.UI.csproj`

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Please write a detailed handoff.md in your working directory `d:\sahma\Documents\GitHub\KeywordGuard_Pro\.agents\worker_r2_r3\handoff.md` summarizing:
- What changes were made to each file.
- The build commands executed and their output/status.
- Confirming that no critical process APIs are called by the service anymore.
- Confirming that WndProc override handles WM_QUERYENDSESSION and WM_ENDSESSION.
Then notify me when done.
