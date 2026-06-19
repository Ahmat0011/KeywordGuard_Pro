# Scope: R1 Implementation - Restrict Window Closing to Web Browsers and Short Domains

## Architecture
- `KeywordGuard.Pro.UI` (WordWatcher.cs, MainViewModel.cs)
- `KeywordGuard.Pro.Agent` (Program.cs)

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | R1.1: Exploration & Plan | Explore WordWatcher.cs, Program.cs, and MainViewModel.cs to trace current logic and identify necessary changes. | None | DONE |
| 2 | R1.2: Core Code Modifications | Modify browser process verification (GetWindowThreadProcessId + whitelist check), domain validation length (> 3), and fallback logic correction in WordWatcher.cs and Program.cs. | R1.1 | DONE |
| 3 | R1.3: Verification & Review | Spawn Reviewers and Challenger to verify correct behavior. Spawn Forensic Auditor to perform integrity audit. | R1.2 | IN_PROGRESS |

## Interface Contracts
- **Browser restriction whitelist**: `chrome`, `msedge`, `firefox`, `opera`, `brave`, `vivaldi`.
- **Domain part length validation**: `domainPart.Length > 3` (replaces `domainPart.Length > 1`).
- **Word boundary fallback**: Non-aggressive items must only use regex word boundaries, not fall back to simple `Contains()`.
