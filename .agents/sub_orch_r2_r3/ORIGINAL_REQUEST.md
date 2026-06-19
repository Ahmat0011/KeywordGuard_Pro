# Original User Request

## 2026-06-17T09:50:09Z

You are teamwork_preview_orchestrator (spawned as self), acting as the R2 & R3 Implementation Sub-orchestrator. Your working directory is d:\sahma\Documents\GitHub\KeywordGuard_Pro\.agents\sub_orch_r2_r3.
Your parent is top-level orchestrator (conversation ID e42bc854-afd1-43be-896a-e965e67d4672).
Your task is to implement the requirements for R2 (Fix Windows Shutdown and Reboot Crash) and R3 (Silent Background Agent Startup).

Specifically, you need to:
1. Decompose your scope and create SCOPE.md in your working directory.
2. Spawn Worker / Reviewer / Challenger / Auditor subagents to:
   - Remove the `ProcessHardening.SetCritical` call from `KeywordGuard.Pro.Service/Worker.cs` to prevent the service process from setting itself as critical.
   - Modify `KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj` to change `<OutputType>Exe</OutputType>` to `<OutputType>WinExe</OutputType>`.
   - In `KeywordGuard.Pro.Agent/Program.cs`, implement a hidden Form running in the Application message loop. Override `WndProc` to handle system events `WM_QUERYENDSESSION` (0x0011) and `WM_ENDSESSION` (0x0016), and call `ProcessHardening.SetCritical(false)` immediately upon receiving them before exit.
   - Ensure the background agent can run silently and exit cleanly when system shutdown/reboot/logoff occurs.
   - Ensure the code builds cleanly.
3. Verify your implementation. Note: MANDATORY INTEGRITY WARNING must be included in your Worker prompts. An auditor must verdict CLEAN.
4. Write SCOPE.md, progress.md, and your handoff.md in your working directory, then notify the parent.
