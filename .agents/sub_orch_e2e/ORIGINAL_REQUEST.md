# Original User Request

## 2026-06-17T09:50:09Z

You are teamwork_preview_orchestrator (spawned as self), acting as the E2E Testing Track Orchestrator. Your working directory is d:\sahma\Documents\GitHub\KeywordGuard_Pro\.agents\sub_orch_e2e.
Your parent is top-level orchestrator (conversation ID e42bc854-afd1-43be-896a-e965e67d4672).
Your objective is to design and create a comprehensive, opaque-box, requirement-driven E2E test suite for KeywordGuard Pro based on the requirements in d:\sahma\Documents\GitHub\KeywordGuard_Pro\.agents\ORIGINAL_REQUEST.md.

Specifically:
1. Create d:\sahma\Documents\GitHub\KeywordGuard_Pro\TEST_INFRA.md at the project root following the TEST_INFRA.md template:
   - Identify features: F1 (Whitelisted Browser Window Closing), F2 (Non-browser process safety), F3 (Domain short-part validation length > 3), F4 (Non-aggressive word boundary matching), F5 (Service SetCritical removal), F6 (Agent clean shutdown deactivation of critical flag), F7 (Silent WinExe startup).
   - Design a test case format and execution strategy. Note that we are in CODE_ONLY network mode.
2. Develop the test suite (e.g., as a PowerShell test runner `run-e2e-tests.ps1` or a C# test project) that can compile and run locally on Windows to verify all 4 tiers of tests:
   - Tier 1: Feature Coverage (5 per feature = 35 tests)
   - Tier 2: Boundary & Corner (5 per feature = 35 tests)
   - Tier 3: Cross-Feature combinations (7 tests)
   - Tier 4: Real-world application scenarios (5 tests)
   Note: The test script should be able to run and report success/failure. It can start dummy/mock executables (like a program compiled on the fly named `chrome.exe` with specific titles, and another named `notepad.exe` with similar titles) to verify that KeywordGuard Pro correctly blocks the browser process while leaving the notepad process alone. It should also simulate system events if possible, or verify process config/capabilities.
3. Once completed, write d:\sahma\Documents\GitHub\KeywordGuard_Pro\TEST_READY.md at the project root with the runner command and coverage summary.
4. Write your handoff.md in your working directory and notify the parent.
