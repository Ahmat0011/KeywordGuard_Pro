## 2026-06-17T09:56:48Z
<USER_REQUEST>
You are teamwork_preview_worker.
Your working directory is d:\sahma\Documents\GitHub\KeywordGuard_Pro\.agents\worker_e2e.
Your first task is to write d:\sahma\Documents\GitHub\KeywordGuard_Pro\TEST_INFRA.md at the project root.

Follow the TEST_INFRA.md template:
- Feature Inventory:
  - F1: Whitelisted Browser Window Closing (Standard browsers: chrome, msedge, firefox, opera, brave, vivaldi only)
  - F2: Non-browser process safety (explorer, notepad, winword, folders, text documents must never be closed)
  - F3: Domain short-part validation length > 3 (subdomain parts <= 3 are not registered as standalone aggressive keywords)
  - F4: Non-aggressive word boundary matching (IsAggressive = false uses word boundaries \b, not substring matches)
  - F5: Service SetCritical removal (watchdog service must not set its own process as critical)
  - F6: Agent clean shutdown deactivation of critical flag (deactivates critical status upon system shutdown/logoff)
  - F7: Silent WinExe startup (Agent runs silently in the background as a GUI subsystem application)

- Test cases inventory mapping to Tiers 1-4:
  - Tier 1: Feature Coverage (5 per feature = 35 tests)
  - Tier 2: Boundary & Corner (5 per feature = 35 tests)
  - Tier 3: Cross-Feature combinations (7 tests)
  - Tier 4: Real-world application scenarios (5 tests)

- Test Architecture:
  - Test runner: PowerShell script run-e2e-tests.ps1 at the project root which builds and executes a C# xUnit/NUnit test assembly.
  - Test case format: Programmatic execution of mock window titles, verification of Process.HasExited, reading configuration file decryption, NtQueryInformationProcess class 29 checks, and service controller commands.

- Real-World Application Scenarios (Tier 4):
  - Scenario 1: User works in Notepad editing a text document containing "ok.ru" while a background agent is running. (Exercises F2, F3, F7)
  - Scenario 2: User opens a browser to a blocked domain with short prefix (e.g. "ok.ru") and the browser tab is closed. (Exercises F1, F3, F4, F7)
  - Scenario 3: Admin manually stops the KeywordGuard watchdog service when the timer is active vs when it is inactive. (Exercises F5, F7)
  - Scenario 4: User initiates system shutdown; the background agent cleanly handles the logoff message, clears the critical flag, and exits without triggering a BSOD. (Exercises F6, F7)
  - Scenario 5: User tries to terminate the Agent using tasklist/cmd, triggering the Agent self-defense WMI killer, while running the test suite in a safe host process. (Exercises F7)

MANDATORY INTEGRITY WARNING:
> DO NOT CHEAT. All implementations must be genuine. DO NOT
> hardcode test results, create dummy/facade implementations, or
> circumvent the intended task. A Forensic Auditor will independently
> verify your work. Integrity violations WILL be detected and your
> work WILL be rejected.

Write the file d:\sahma\Documents\GitHub\KeywordGuard_Pro\TEST_INFRA.md and save your handoff report to d:\sahma\Documents\GitHub\KeywordGuard_Pro\.agents\worker_e2e\handoff.md. When done, send me a message.
</USER_REQUEST>
