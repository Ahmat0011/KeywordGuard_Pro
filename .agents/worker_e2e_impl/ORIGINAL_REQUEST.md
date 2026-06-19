## 2026-06-17T09:59:13Z
You are teamwork_preview_worker.
Your working directory is d:\sahma\Documents\GitHub\KeywordGuard_Pro\.agents\worker_e2e_impl.
Your task is to implement the E2E test project and runner script for KeywordGuard Pro.

Specifically, create the following files:

1. `d:\sahma\Documents\GitHub\KeywordGuard_Pro\KeywordGuard.Pro.E2ETests\KeywordGuard.Pro.E2ETests.csproj`:
   - Setup as a console application targeting `.NET 10.0-windows` (using Microsoft.NET.Sdk).
   - Reference the `KeywordGuard.Pro.Security` project:
     `<ProjectReference Include="..\KeywordGuard.Pro.Security\KeywordGuard.Pro.Security.csproj" />`
   - Set `<UseWindowsForms>true</UseWindowsForms>` and/or `<UseWPF>true</UseWPF>` so it can open Windows Forms/WPF windows for mocking.
   - Do NOT add external third-party dependencies (like xUnit/NUnit NuGet packages) to avoid offline restore failures. Implement a clean, lightweight custom test runner framework in Program.cs that prints outputs and returns exit code 0 if all tests pass, or non-zero on failure.

2. `d:\sahma\Documents\GitHub\KeywordGuard_Pro\KeywordGuard.Pro.E2ETests\Program.cs`:
   - Program entry point handling command-line arguments:
     - If `--mock-window "Title" "ProcessName"` is passed, create a hidden or visible Windows Form window with the title "Title", keep it open, and run the message pump. (To test whitelisting, the main test runner will copy `KeywordGuard.Pro.E2ETests.exe` to `%TEMP%\<ProcessName>`, launch it with these arguments, and verify if the Agent closes/terminates it).
     - Otherwise, run the E2E test suite (Tiers 1-4).
   - Implement native helper methods:
     - PE Header parsing logic to read the optional header subsystem of Agent and UI binaries. Assert that the subsystem is `2` (`IMAGE_SUBSYSTEM_WINDOWS_GUI`).
     - `NtQueryInformationProcess` using Information Class 29 (`ProcessBreakOnTermination`) to verify the critical status of processes without causing BSODs.
     - Win32 `SendMessage`/`PostMessage` sending `WM_QUERYENDSESSION` / `WM_ENDSESSION` to verify the Agent's session-ending deactivation logic.
   - Implement the test cases:
     - Tier 1 (5 tests per feature: F1-F7, total 35 tests)
     - Tier 2 (5 boundary tests per feature, total 35 tests)
     - Tier 3 (7 cross-feature combination tests)
     - Tier 4 (5 real-world scenarios)
   - Ensure the test runner cleans up configuration files and terminated processes in a `finally` block or during teardown.

3. `d:\sahma\Documents\GitHub\KeywordGuard_Pro\run-e2e-tests.ps1`:
   - PowerShell script at the project root.
   - Compiles all projects (Security, UI, Agent, Service) in Release configuration.
   - Compiles the E2E tests project.
   - Launches `KeywordGuard.Pro.E2ETests.exe` and forwards the exit code.

MANDATORY INTEGRITY WARNING:
> DO NOT CHEAT. All implementations must be genuine. DO NOT
> hardcode test results, create dummy/facade implementations, or
> circumvent the intended task. A Forensic Auditor will independently
> verify your work. Integrity violations WILL be detected and your
> work WILL be rejected.

Save your handoff report to d:\sahma\Documents\GitHub\KeywordGuard_Pro\.agents\worker_e2e_impl\handoff.md. When complete, send me a message.
