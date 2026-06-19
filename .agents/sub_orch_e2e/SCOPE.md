# Scope: E2E Testing Track

## Architecture
- Test runner: PowerShell script `run-e2e-tests.ps1` at project root.
- Test cases: Custom C# helper assemblies or PowerShell functions checking window titles, processes, service properties, and simulating logoff events.
- Features to test: F1, F2, F3, F4, F5, F6, F7.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Create TEST_INFRA.md | Design features and test case inventory at project root | none | PLANNED |
| 2 | Develop E2E Test Suite | Implement the test cases and runner script `run-e2e-tests.ps1` | M1 | PLANNED |
| 3 | Run & Verify E2E Tests | Execute the test suite and ensure all pass or report correctly | M2 | PLANNED |
| 4 | Create TEST_READY.md | Write test readiness and coverage summary at project root | M3 | PLANNED |

## Interface Contracts
- The test runner must be executable via PowerShell: `.\run-e2e-tests.ps1`
- It must return exit code 0 on success, non-zero on failure.
- Output formatting must clearly detail results of all 4 tiers of tests.
