# KeywordGuard Pro - End-to-End (E2E) Test Infrastructure

This document defines the E2E testing infrastructure, feature inventory, test cases, and real-world execution scenarios for **KeywordGuard Pro**.

---

## 1. Feature Inventory

KeywordGuard Pro contains the following key features targeted by the E2E test suite:

*   **F1: Whitelisted Browser Window Closing**
    *   *Description*: Window monitoring and closing is strictly confined to standard web browsers: `chrome`, `msedge`, `firefox`, `opera`, `brave`, and `vivaldi`.
    *   *Scope*: Any window title matching a blocked keyword is only closed if it belongs to one of these whitelisted processes.
*   **F2: Non-browser process safety**
    *   *Description*: Critical system and productivity applications must never be closed.
    *   *Scope*: Processes like `explorer`, `notepad`, `winword`, folder windows, and general text document viewers are excluded from termination.
*   **F3: Domain short-part validation length > 3**
    *   *Description*: Subdomain and domain parts with a length less than or equal to 3 characters are filtered out during configuration parsing.
    *   *Scope*: Short prefixes/suffixes (e.g., `com`, `uk`, `ru`, `ok`, `net`) are not registered as standalone aggressive keywords.
*   **F4: Non-aggressive word boundary matching**
    *   *Description*: Non-aggressive keywords (`IsAggressive = false`) use word boundaries (`\b`) for matching.
    *   *Scope*: If `casino` is non-aggressive, it matches `"welcome to casino"` but not `"casinosite.com"`.
*   **F5: Service SetCritical removal**
    *   *Description*: The watchdog service (`KeywordGuard.Pro.Service`) runs in Session 0 and monitors the Agent. It must not set its own process as critical.
    *   *Scope*: Calls to `SetCritical` (via `NtSetInformationProcess`) must not be applied to the service process itself.
*   **F6: Agent clean shutdown deactivation of critical flag**
    *   *Description*: The background Agent runs as a critical process to prevent unauthorized termination. However, it must deactivate its critical status upon receiving system shutdown or logoff messages.
    *   *Scope*: Intercepting `WM_QUERYENDSESSION` and `WM_ENDSESSION` to call `SetCritical(false)` before termination.
*   **F7: Silent WinExe startup**
    *   *Description*: The Agent must run silently in the background as a GUI subsystem application (`WinExe`).
    *   *Scope*: The process must not launch or flash a console window.

---

## 2. Test Cases Inventory

The E2E test suite comprises **82 test cases** divided into four distinct tiers:

### Tier 1: Feature Coverage (35 Tests, 5 per Feature)

| ID | Feature | Title | Description | Expected Result |
|---|---|---|---|---|
| **T1_F1_1** | F1: Whitelist | Chrome Window Closing | Blocked keyword in Google Chrome window title. | Chrome window/tab is closed. |
| **T1_F1_2** | F1: Whitelist | Edge Window Closing | Blocked keyword in Microsoft Edge window title. | Edge window/tab is closed. |
| **T1_F1_3** | F1: Whitelist | Firefox Window Closing | Blocked keyword in Mozilla Firefox window title. | Firefox window/tab is closed. |
| **T1_F1_4** | F1: Whitelist | Opera/Brave Window Closing | Blocked keyword in Opera/Brave window title. | Browser window/tab is closed. |
| **T1_F1_5** | F1: Whitelist | Custom Process Isolation | Blocked keyword in non-whitelisted browser `mycustombrowser.exe`. | Window is NOT closed. |
| **T1_F2_1** | F2: Safety | Explorer Process Exclude | Blocked keyword in a Windows Explorer folder name. | Explorer window remains open. |
| **T1_F2_2** | F2: Safety | Notepad Document Exclude | Blocked keyword in a Notepad window title. | Notepad remains open. |
| **T1_F2_3** | F2: Safety | MS Word Document Exclude | Blocked keyword in a Microsoft Word window title. | Word remains open. |
| **T1_F2_4** | F2: Safety | Folder Window Exclude | Directory name matches a blocked keyword. | Folder window remains open. |
| **T1_F2_5** | F2: Safety | Custom Editor Exclude | Blocked keyword in standard text editor. | Editor remains open. |
| **T1_F3_1** | F3: Domain | "com" Length Validation | Domain extension `"com"` (length 3) parsed from URL. | Discarded; not registered as keyword. |
| **T1_F3_2** | F3: Domain | "uk" Length Validation | Country code `"uk"` (length 2) parsed from URL. | Discarded; not registered as keyword. |
| **T1_F3_3** | F3: Domain | "ru" Length Validation | Country code `"ru"` (length 2) parsed from URL. | Discarded; not registered as keyword. |
| **T1_F3_4** | F3: Domain | "blog" Length Validation | Extension `"blog"` (length 4) parsed from URL. | Registered as keyword. |
| **T1_F3_5** | F3: Domain | Multi-part Domain Parsing | Input `"example.co.uk"` parsed. | `"example"` (len 7) is registered; `"co"` and `"uk"` are discarded. |
| **T1_F4_1** | F4: Boundary | Exact Word Match | Non-aggressive keyword `"gamble"` in title `"Let's gamble"`. | Match found (window closed). |
| **T1_F4_2** | F4: Boundary | Substring Miss | Non-aggressive keyword `"gamble"` in title `"gambler"`. | No match (window remains open). |
| **T1_F4_3** | F4: Boundary | Punctuation Boundary | Non-aggressive keyword `"gamble"` in title `"gamble!"`. | Match found (window closed). |
| **T1_F4_4** | F4: Boundary | End of Title Match | Non-aggressive keyword `"gamble"` in title `"time to gamble"`. | Match found (window closed). |
| **T1_F4_5** | F4: Boundary | Case Insensitivity | Non-aggressive keyword `"Gamble"` in title `"GAMBLE"`. | Match found (window closed). |
| **T1_F5_1** | F5: Service | Service Process Hardening Check | Watchdog service process is running. | `SetCritical` is NOT applied to it. |
| **T1_F5_2** | F5: Service | Service SCM Control | Service starts and registers with SCM. | Starts successfully without elevating itself. |
| **T1_F5_3** | F5: Service | ProcessBreakOnTermination check | NtQueryInformationProcess Class 29 on Service. | Returns `0` (False/Not Critical). |
| **T1_F5_4** | F5: Service | Force Terminate Service | Service is terminated using taskkill. | Exits cleanly without OS BSOD. |
| **T1_F5_5** | F5: Service | Watchdog Code Verification | Static audit of service project references. | Confirm absence of `SetCritical` call for service handle. |
| **T1_F6_1** | F6: Shutdown | WM_QUERYENDSESSION Intercept | Send `WM_QUERYENDSESSION` message to Agent. | Message intercepted and handler executes. |
| **T1_F6_2** | F6: Shutdown | WM_ENDSESSION Execution | Send `WM_ENDSESSION` with `wParam = true` to Agent. | `SetCritical(false)` is invoked. |
| **T1_F6_3** | F6: Shutdown | Critical Deactivation Verification | NtQueryInformationProcess Class 29 after shutdown msg. | Returns `0` (False). |
| **T1_F6_4** | F6: Shutdown | Clean Process Termination | Agent terminates during simulated system shutdown. | Exits cleanly; system does not BSOD. |
| **T1_F6_5** | F6: Shutdown | Elevated Agent Shutdown | Admin-elevated Agent process receives shutdown message. | Clears critical status and exits without BSOD. |
| **T1_F7_1** | F7: Silent | WinExe Output Verification | Inspect output type of Agent binary compiled. | Output type is `WinExe` (GUI Application). |
| **T1_F7_2** | F7: Silent | Console Window Flashing | Start Agent process. | No console window flashes or is created. |
| **T1_F7_3** | F7: Silent | MainWindowHandle Check | Start Agent and query `MainWindowHandle`. | Handle is `IntPtr.Zero` (invisible). |
| **T1_F7_4** | F7: Silent | GUI Subsystem Execution | Launch Agent. | Runs silently in the background without UI window. |
| **T1_F7_5** | F7: Silent | Stdout/Stderr Redirect | Run Agent with streams redirected to null. | Process runs correctly with no console allocation. |

---

### Tier 2: Boundary & Corner (35 Tests, 5 per Feature)

| ID | Feature | Title | Description | Expected Result |
|---|---|---|---|---|
| **T2_F1_1** | F1: Whitelist | Modified Process Executable | Title matches keyword, process is `chrome.exe.lnk`. | Window is NOT closed. |
| **T2_F1_2** | F1: Whitelist | Empty Title String | Browser window title is empty (`""`). | Evaluates gracefully; no crash. |
| **T2_F1_3** | F1: Whitelist | Unicode/Emoji Title | Browser window title contains Unicode characters. | Matcher handles and matches correctly. |
| **T2_F1_4** | F1: Whitelist | Concurrency Stress | 20 browser windows with keywords open rapidly. | All windows closed within 1 second. |
| **T2_F1_5** | F1: Whitelist | Uppercase Executable Name | Process is named `CHROME.EXE` (uppercase). | Window is closed. |
| **T2_F2_1** | F2: Safety | Window Title is Blocked Keyword | Notepad window title is exactly `"gamble"`. | Notepad window is NOT closed. |
| **T2_F2_2** | F2: Safety | Directory Named as Executable | Explorer opens folder named `"chrome.exe"`. | Explorer window is NOT closed. |
| **T2_F2_3** | F2: Safety | Text Document in Browser | File `file://c:/ok.txt` opened in Chrome. | Chrome window IS closed. |
| **T2_F2_4** | F2: Safety | Task Manager safety | Task Manager displays a blocked keyword in list. | Task Manager remains open. |
| **T2_F2_5** | F2: Safety | Console Output safety | CMD console outputs a blocked keyword. | CMD window remains open. |
| **T2_F3_1** | F3: Domain | Exact 3 Characters Input | User enters `"abc"` as keyword. | Not registered as standalone keyword. |
| **T3_F3_2** | F3: Domain | Whitespace Padding | User enters `"   ok.ru   "`. | Trimmed; `"ok"` and `"ru"` are ignored. |
| **T2_F3_3** | F3: Domain | Exact 4 Characters Prefix | User enters `"test.ru"`. | `"test"` registered; `"ru"` ignored. |
| **T2_F3_4** | F3: Domain | Repeated Dots | User enters `"......."`. | Handled gracefully without exception. |
| **T2_F3_5** | F3: Domain | Special Characters in Domain | User enters `"a-b.co"`. | `"a-b"` (len 3) and `"co"` (len 2) ignored. |
| **T2_F4_1** | F4: Boundary | Start of String | Non-aggressive keyword at start: `"gamble is bad"`. | Matches (window closed). |
| **T2_F4_2** | F4: Boundary | Special Symbol Boundaries | Title contains `"[gamble]"`. | Matches (window closed). |
| **T2_F4_3** | F4: Boundary | Numeric Adjacency | Title contains `"gamble123"`. | No match (window remains open). |
| **T2_F4_4** | F4: Boundary | Multiple Spaces | Title contains `"welcome   to   gamble"`. | Matches (window closed). |
| **T2_F4_5** | F4: Boundary | Multi-line Carriage Return | Keyword followed by `\r\n`. | Matches (window closed). |
| **T2_F5_1** | F5: Service | Service Recovery Action | Stop service abruptly (simulated crash). | SCM restarts service; no BSOD occurs. |
| **T2_F5_2** | F5: Service | SeDebugPrivilege check | Service runs with SYSTEM privileges. | Watchdog runs, but doesn't set critical flag. |
| **T2_F5_3** | F5: Service | Service Custom Commands | Send custom control commands to service. | Service handles them; remains non-critical. |
| **T2_F5_4** | F5: Service | Multiple Start/Stop Cycles | Start and stop the service 10 times consecutively. | No memory leak or critical state corruption. |
| **T2_F5_5** | F5: Service | Restricted Account Service | Run service under a non-SYSTEM service account. | Fails to set critical (as expected) but runs. |
| **T2_F6_1** | F6: Shutdown | Cancelled System Shutdown | Send `WM_QUERYENDSESSION` then abort. | Agent handles abort; maintains critical state. |
| **T2_F6_2** | F6: Shutdown | User Logoff Event | User logs off without turning off machine. | Critical flag cleared; exits without BSOD. |
| **T2_F6_3** | F6: Shutdown | High CPU Load Shutdown | Send shutdown signal while CPU is pegged. | Flag cleared within 100ms; exits safely. |
| **T2_F6_4** | F6: Shutdown | Monitor Loop Blocking | Shutdown received during window scan block. | Shutdown event interrupts loop and executes. |
| **T2_F6_5** | F6: Shutdown | Non-elevated Agent Shutdown | Run Agent non-elevated; send shutdown. | Exits cleanly. |
| **T2_F7_1** | F7: Silent | Session 0 Execution | Start Agent in Session 0 (Service context). | Launches silently without GUI context errors. |
| **T2_F7_2** | F7: Silent | Task Scheduler Execution | Launch Agent via Task Scheduler. | Runs silently in background. |
| **T2_F7_3** | F7: Silent | Stream Redirection to File | Output redirected to text file. | Runs silently. |
| **T2_F7_4** | F7: Silent | Rapid Restart Cycles | Start/stop Agent 15 times in 5 seconds. | Launches silently each time; no phantom consoles. |
| **T2_F7_5** | F7: Silent | Sandbox Environment | Start Agent in Windows Sandbox. | Runs silently in background. |

---

### Tier 3: Cross-Feature Combinations (7 Tests)

| ID | Title | Features | Description | Expected Result |
|---|---|---|---|---|
| **T3_1** | Browser Whitelist + Word Boundary | F1 + F4 | Non-aggressive keyword `"bet"` is checked in Chrome. Title goes from `"Better"` to `"Bet now"`. | `"Better"` does not trigger close; `"Bet now"` triggers close. |
| **T3_2** | Notepad Safety + Short Domain | F2 + F3 | Notepad is editing a text document named `"ok.ru"`. | Notepad is NOT closed. `"ok"` and `"ru"` are not registered. |
| **T3_3** | Browser Whitelist + Short Domain | F1 + F3 | Google Chrome visits website with title `"ok.ru"`. | Chrome is NOT closed because `"ok"` and `"ru"` are filtered out. |
| **T3_4** | Watchdog + Silent + Critical | F5 + F7 | Service (non-critical) starts Agent (silent). | Watchdog monitors successfully; both processes run silently. |
| **T3_5** | Critical + Shutdown + Silent | F6 + F7 | Silent Agent is critical. User triggers system shutdown. | Agent clears critical flag, exits silently. No BSOD. |
| **T3_6** | Word Safety + Word Boundary | F2 + F4 | Microsoft Word edits document titled `"My Casino Plan"` (non-aggressive). | MS Word is NOT closed because it is a protected process. |
| **T3_7** | Domain Filter + Boundary | F3 + F4 | Config has `"ok.ru"` and `"casino"`. Chrome titles are `"ok"` and `"casino"`. | Chrome with `"ok"` remains open; Chrome with `"casino"` is closed. |

---

### Tier 4: Real-World Application Scenarios (5 Tests)

| ID | Title | Features | Description | Expected Result |
|---|---|---|---|---|
| **T4_1** | Scenario 1: Notepad Editing | F2, F3, F7 | User edits a document in Notepad containing `"ok.ru"`. Agent runs silently in the background. | Notepad is unaffected. No windows close. |
| **T4_2** | Scenario 2: Browser Blocked Domain | F1, F3, F4, F7 | User opens Chrome to `"ok.ru"`. Prefix is short. If a longer configured keyword matches, it is closed. | Browser tab is closed ONLY if a valid keyword matches. |
| **T4_3** | Scenario 3: Watchdog Manual Stop | F5, F7 | Admin stops the watchdog service manually via SCM (active vs inactive timer). | Service stops cleanly. No BSOD. |
| **T4_4** | Scenario 4: Clean System Shutdown | F6, F7 | User shuts down system. Agent intercepts, clears critical flag, and exits. | System shuts down cleanly. No BSOD. |
| **T4_5** | Scenario 5: Self-Defense Killing | F7 | User attempts to terminate Agent via CMD/tasklist. WMI self-defense handles host process verification. | Agent self-defense triggers in safe host environment. |

---

## 3. Test Architecture

### 3.1 Test Runner (`run-e2e-tests.ps1`)
The E2E test execution is orchestrated via `run-e2e-tests.ps1` at the project root. This PowerShell script:
1. Locates or restores the .NET SDK.
2. Compiles the C# xUnit/NUnit test assembly located in the test project.
3. Configures test dependencies and test databases/mock configurations.
4. Executes the test suite with specific CLI parameters (`dotnet test`).
5. Aggregates results and exports them to a JUnit-compatible XML file.

### 3.2 Test Case Format
Test cases are written in C# and execute the following programmatic checks:
*   **Mock Window Titles**: Utilizes Windows API (`SetWindowText` or mock window creation helper class) to spawn a window with a specific title and class, simulating Chrome/Firefox/Edge.
*   **Process Exit Verification**: Asserts that `Process.HasExited` is true within the expected polling interval for target browser processes.
*   **Configuration Decryption Check**: Reads and decrypts KeywordGuard Pro configuration files to verify that parsed rules match expected criteria.
*   **NtQueryInformationProcess Verification**: Invokes Native Windows API `NtQueryInformationProcess` with information class 29 (`ProcessBreakOnTermination`) to programmatically assert the critical/non-critical status of the running processes.
*   **Service Controller Commands**: Uses `.NET ServiceController` API to start, stop, pause, and send custom commands to `KeywordGuard.Pro.Service` and asserts service states and event logs.

---

## 4. Real-World Application Scenarios (Tier 4) Detailed Breakdown

### Scenario 1: Notepad Document Safety
*   **Action**: User opens `notepad.exe` and edits a text file containing the text `"ok.ru"`.
*   **Verification**: The test harness verifies:
    1. The process name is `notepad` (protected via F2).
    2. The domain parser filters out `"ok"` and `"ru"` because their lengths are <= 3 (F3).
    3. The Notepad window remains open, and the Agent does not terminate the process.

### Scenario 2: Browser Blocked Domain Visit
*   **Action**: User launches `chrome.exe` and navigates to a blocked URL. The tab title becomes `"ok.ru"`.
*   **Verification**:
    1. If `"ok.ru"` is the configured keyword, it is split into parts (`"ok"` and `"ru"`). Since both are <= 3 chars, they are not registered as standalone aggressive keywords.
    2. However, if a valid domain like `"gamblingsite.com"` is visited, the tab title matches `"gamblingsite"`.
    3. The Agent detects the process `chrome.exe` is in the browser whitelist (F1).
    4. The Agent successfully terminates the browser window or sends key inputs to close the tab.

### Scenario 3: Watchdog Service Control
*   **Action**: Administrator opens a PowerShell console and executes `Stop-Service KeywordGuard.Pro.Service`.
*   **Verification**:
    1. The test verifies the service does not crash.
    2. The watchdog service process is checked using class 29 `ProcessBreakOnTermination` to confirm it is not marked critical (F5).
    3. The service terminates cleanly without triggering a Blue Screen of Death (BSOD) or a system reboot.

### Scenario 4: Clean System Shutdown
*   **Action**: The system initiates shutdown. Windows sends `WM_QUERYENDSESSION` and `WM_ENDSESSION` to all top-level windows.
*   **Verification**:
    1. The background Agent receives the message.
    2. The Agent calls `SetCritical(false)` (invoking `NtSetInformationProcess` with class 29 set to 0).
    3. The Agent process exits cleanly.
    4. The watchdog service also stops cleanly without trying to restart the Agent or triggering a BSOD.

### Scenario 5: Agent Self-Defense & WMI Killer
*   **Action**: A user or script attempts to terminate the Agent process using standard commands.
*   **Verification**:
    1. The Agent runs in a GUI subsystem mode silently (F7).
    2. The test harness runs inside a safe host process.
    3. The Agent's self-defense mechanisms monitor process creation/termination and handle unauthorized termination attempts.
