# Project: KeywordGuard Pro Requirements Resolution

## Architecture
- `KeywordGuard.Pro.UI`: The user interface app where the user configures blocked keywords. It runs a `WordWatcher` service.
- `KeywordGuard.Pro.Agent`: The background agent starting on logon. It monitors foreground window titles and closes matching browser windows.
- `KeywordGuard.Pro.Service`: The watchdog service running in Session 0. It restarts the Agent if it stops and manages agent states.
- `KeywordGuard.Pro.Security`: Shared library providing firewall, hosts, and process hardening helper methods (like `SetCritical`).

## Code Layout
- `KeywordGuard.Pro.UI/Services/WordWatcher.cs`: Foreground window title watcher and process closer for the UI thread.
- `KeywordGuard.Pro.Agent/Program.cs`: Background agent entry point, main window checking loop, and critical status manager.
- `KeywordGuard.Pro.Service/Worker.cs`: Service entry point, starts the background agent and monitors its lifetime.
- `KeywordGuard.Pro.Security/ProcessHardening.cs`: Sets or clears critical status for processes.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | E2E Test Suite | E2E Testing Track: Design and build the E2E test harness and test cases (Tiers 1-4) | None | PLANNED |
| 2 | Implementation: R1 | Implementation Track: Restrict window closing to standard web browsers, fix short domain parsing and fallback title matching | None | PLANNED |
| 3 | Implementation: R2 & R3 | Implementation Track: Fix Windows shutdown/reboot crash and implement silent background Agent startup | None | PLANNED |
| 4 | Integration & Hardening | Final Milestone: Pass 100% E2E tests, and perform Tier 5 adversarial coverage hardening | M1, M2, M3 | PLANNED |

## Interface Contracts
### Agent ↔ Service
- The Service watchdog monitors the Agent process and restarts it if it exits.
- The Agent process must run silently as `WinExe` and handle system shutdown events cleanly to avoid triggering service recovery BSODs.
- System shutdown events (WM_QUERYENDSESSION, WM_ENDSESSION) must clear the critical status before service termination.
