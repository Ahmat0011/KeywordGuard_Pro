# Scope: R2 & R3 Implementation (Shutdown Crash & Silent Startup)

## Architecture
- `KeywordGuard.Pro.Service` (Windows Service watch dog) references `KeywordGuard.Pro.Security` for process hardening and `KeywordGuard.Pro.Agent` for configs/task scheduling.
- `KeywordGuard.Pro.Agent` (User-session background worker) references `KeywordGuard.Pro.Security` for process hardening and `System.Management` for WMI scanning.
- We are changing `KeywordGuard.Pro.Agent` from a console application (`Exe`) to a windows application (`WinExe`) to run silently (no visible console window).
- We are implementing a hidden WinForms `Form` in the Agent's message loop (`Application.Run`) to capture Windows shutdown/reboot/logoff messages (`WM_QUERYENDSESSION`, `WM_ENDSESSION`) immediately and synchronously, unsetting critical process status to prevent BSOD/system hangs during shutdown.
- We are removing the critical process hardening from the Service since it runs in Session 0 and does not need to protect itself from user session actions in this manner, preventing crash/BSOD during service stop or shutdown.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Service: Remove SetCritical | Remove `ProcessHardening.SetCritical` calls from `KeywordGuard.Pro.Service/Worker.cs` | None | IN_PROGRESS (worker_1) |
| 2 | Agent: WinExe OutputType | Modify `KeywordGuard.Pro.Agent.csproj` to use `<OutputType>WinExe</OutputType>` | None | IN_PROGRESS (worker_1) |
| 3 | Agent: Hidden Form WndProc | Implement hidden form and override `WndProc` in `KeywordGuard.Pro.Agent/Program.cs` to handle shutdown events | M2 | IN_PROGRESS (worker_1) |
| 4 | Verification & Audit | Perform build verification and run the Forensic Auditor for compliance | M1, M3 | PLANNED |

## Interface Contracts
### `KeywordGuard.Pro.Agent.Program` ↔ Windows OS Session Management
- `WM_QUERYENDSESSION` (0x0011): Sent when user/system requests shutdown. The hidden Form must intercept this in `WndProc`, call `ProcessHardening.SetCritical(false)`, and allow the OS to proceed.
- `WM_ENDSESSION` (0x0016): Sent after query session results are known. Intercepted in `WndProc` to call `ProcessHardening.SetCritical(false)`.
- `Application.Run(Form)`: Boots the WinForms message loop with the hidden form to receive window messages.
