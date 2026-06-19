# Handoff Report — explorer_r1_1

This handoff report details the exploration findings and designed fix strategy for R1 (restricting window closing to standard browsers, adjusting domain parsing length, and correcting fallback matching logic).

---

## 1. Observation

We explored the codebase and identified the target logic in three files:
- `KeywordGuard.Pro.UI/Services/WordWatcher.cs`
- `KeywordGuard.Pro.Agent/Program.cs`
- `KeywordGuard.Pro.UI/ViewModels/MainViewModel.cs`

Specific observed code segments:

### A. Window Closing & Monitoring Checks
- **`KeywordGuard.Pro.UI/Services/WordWatcher.cs` (Lines 63-87):**
```csharp
                    string title = GetActiveWindowTitle();
                    if (string.IsNullOrEmpty(title))
                    {
                        await Task.Delay(500, _cts.Token);
                        continue;
                    }

                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.Value)) continue;

                        bool hit = item.IsAggressive
                            ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
                            : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

                        if (!hit && !item.IsAggressive)
                            hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);

                        if (hit)
                        {
                            CloseActiveWindow();
                            await Task.Delay(1000, _cts.Token);
                            break;
                        }
                    }
```
- **`KeywordGuard.Pro.Agent/Program.cs` (Lines 281-312):**
```csharp
    static void CheckActiveWindow(List<BlockedItem> targets)
    {
        if (!_running || _isShuttingDown) return;

        const int nChars = 512;
        StringBuilder buff = new StringBuilder(nChars);
        IntPtr handle = GetForegroundWindow();

        if (handle == IntPtr.Zero) return; // Kein Fenster im Fokus

        if (GetWindowText(handle, buff, nChars) > 0)
        {
            string title = buff.ToString();
            foreach (var item in targets)
            {
                if (string.IsNullOrWhiteSpace(item.Value)) continue;

                bool hit = item.IsAggressive
                    ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
                    : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

                if (!hit && !item.IsAggressive)
                    hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);

                if (hit && _running && !_isShuttingDown)
                {
                    Log("BLOCKED: '" + item.Value + "' in Fenster '" + title + "'");
                    CloseWindow(handle);
                    break;
                }
            }
        }
    }
```

### B. Domain Part Length Validation
- **`KeywordGuard.Pro.Agent/Program.cs` (Lines 176-178 & 193-195):**
```csharp
                            string domainPart = domain.Split('.')[0];
                            if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
                                targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
```
and
```csharp
                            string domainPart = domain.Split('.')[0];
                            if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
                                targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
```
- **`KeywordGuard.Pro.UI/ViewModels/MainViewModel.cs` (Lines 355-357):**
```csharp
                string domainPart = domain.Split('.')[0];
                if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
                    items.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
```

### C. Fallback Matching Logic (Non-Aggressive Items)
- **`KeywordGuard.Pro.UI/Services/WordWatcher.cs` (Lines 78-79):**
```csharp
                        if (!hit && !item.IsAggressive)
                            hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
```
- **`KeywordGuard.Pro.Agent/Program.cs` (Lines 302-303):**
```csharp
                if (!hit && !item.IsAggressive)
                    hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
```

---

## 2. Logic Chain

1. **Browser restriction whitelist requirement:**
   - Standard browsers are whitelisted: `chrome`, `msedge`, `firefox`, `opera`, `brave`, `vivaldi`.
   - We must resolve the process name from the active window handle. To do so, `GetWindowThreadProcessId` returns the process ID of a window handle. We can then retrieve the `Process` object (e.g., using `Process.GetProcessById((int)pid)`) and check `proc.ProcessName` against the whitelist.
   - Restricting window closing to only standard web browsers prevents the application from closing other user-facing windows (such as Notepad, Windows Explorer, Word documents, etc.) even if they happen to contain the blocked keyword in the title.
   - We will insert `IsBrowserProcess` checks:
     - In the window monitoring loop of `WordWatcher.cs` and `Program.cs` BEFORE inspecting/blocking active window titles, to avoid checking titles of and logging blocks for non-browser processes.
     - Inside `CloseActiveWindow(IntPtr handle)` and `CloseWindow(IntPtr handle)` as an additional safeguard against focus-stealing/race-conditions.

2. **Domain part length validation requirement:**
   - Setting the validation check to `domainPart.Length > 3` instead of `domainPart.Length > 1` ensures that extremely short domain components (like `go` from `go.com` or `abc` from `abc.de`) are not registered as aggressive standalone keywords.
   - This prevents false-positive aggressive blocks on common words.

3. **Fallback matching logic bug correction:**
   - When a keyword is non-aggressive (`IsAggressive = false`), it is expected to only match with word boundaries (`\b`).
   - However, the current code has a fallback `if (!hit && !item.IsAggressive) hit = title.Contains(...)` which sets `hit = true` anyway if the string is contained anywhere. This makes the `IsAggressive` flag useless.
   - Eliminating the fallback block `if (!hit && !item.IsAggressive) ...` completely restores the expected behavior where non-aggressive items only match via Regex with word boundaries.

---

## 3. Caveats

- **Process Lifetime / Thread Safety**: Resolving the process ID using `GetWindowThreadProcessId` and getting the process name via `Process.GetProcessById` could throw a `System.ArgumentException` if the process exits in the milliseconds between retrieving the ID and querying details. We must catch and handle exceptions inside a try-catch block, returning `false` (i.e. not a browser process) to remain safe.
- **Process Disposal**: The `Process` class implements `IDisposable`. We should use `using var proc = Process.GetProcessById((int)pid)` to prevent resource/handle leaks.
- **Case Insensitivity**: Browser process names can theoretically vary or contain casing depending on environment setup, so case-insensitive matching is mandatory.

---

## 4. Conclusion

We conclude that the following code changes should be applied:

### File: `KeywordGuard.Pro.UI/Services/WordWatcher.cs`

1. **Add Imports & DLL Import:**
```csharp
using System.Diagnostics;
using System.Collections.Generic;

// Inside public class WordWatcher:
[DllImport("user32.dll", SetLastError = true)]
private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
```

2. **Define Whitelist and Helper Method:**
```csharp
private static readonly HashSet<string> BrowserWhitelist = new(StringComparer.OrdinalIgnoreCase)
{
    "chrome", "msedge", "firefox", "opera", "brave", "vivaldi"
};

private static bool IsBrowserProcess(IntPtr handle)
{
    if (handle == IntPtr.Zero) return false;
    try
    {
        GetWindowThreadProcessId(handle, out uint pid);
        if (pid == 0) return false;
        using var proc = Process.GetProcessById((int)pid);
        return BrowserWhitelist.Contains(proc.ProcessName);
    }
    catch
    {
        return false;
    }
}
```

3. **Update `GetActiveWindowTitle` to accept handle & modify monitoring loop:**
```csharp
private static string GetActiveWindowTitle(IntPtr handle)
{
    if (handle == IntPtr.Zero) return "";
    const int max = 512;
    var sb = new StringBuilder(max);
    return GetWindowText(handle, sb, max) > 0 ? sb.ToString() : "";
}
```

And in the loop inside `Start`:
```csharp
                    IntPtr handle = GetForegroundWindow();
                    if (handle == IntPtr.Zero || !IsBrowserProcess(handle))
                    {
                        await Task.Delay(500, _cts.Token);
                        continue;
                    }

                    string title = GetActiveWindowTitle(handle);
                    if (string.IsNullOrEmpty(title))
                    {
                        await Task.Delay(500, _cts.Token);
                        continue;
                    }

                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.Value)) continue;

                        bool hit = item.IsAggressive
                            ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
                            : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

                        if (hit)
                        {
                            CloseActiveWindow(handle);
                            await Task.Delay(1000, _cts.Token);
                            break;
                        }
                    }
```

4. **Modify `CloseActiveWindow` to accept handle & check whitelist:**
```csharp
    private static void CloseActiveWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        if (!IsBrowserProcess(handle)) return;
        
        // ... (rest of CloseActiveWindow remains the same, except using 'handle' parameter instead of calling GetForegroundWindow() again at the start)
```

---

### File: `KeywordGuard.Pro.Agent/Program.cs`

1. **Add DLL Import:**
```csharp
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
```

2. **Define Whitelist and Helper Method:**
```csharp
    private static readonly HashSet<string> BrowserWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi"
    };

    private static bool IsBrowserProcess(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        try
        {
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid == 0) return false;
            using var proc = Process.GetProcessById((int)pid);
            return BrowserWhitelist.Contains(proc.ProcessName);
        }
        catch
        {
            return false;
        }
    }
```

3. **Update Domain Parsing Validation Checks:**
Replace `domainPart.Length > 1` with `domainPart.Length > 3` on lines 177 and 194.
```csharp
                            if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 3)
```

4. **Update `CheckActiveWindow`:**
```csharp
    static void CheckActiveWindow(List<BlockedItem> targets)
    {
        if (!_running || _isShuttingDown) return;

        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return; // Kein Fenster im Fokus
        if (!IsBrowserProcess(handle)) return;

        const int nChars = 512;
        StringBuilder buff = new StringBuilder(nChars);
        if (GetWindowText(handle, buff, nChars) > 0)
        {
            string title = buff.ToString();
            foreach (var item in targets)
            {
                if (string.IsNullOrWhiteSpace(item.Value)) continue;

                bool hit = item.IsAggressive
                    ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
                    : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

                if (hit && _running && !_isShuttingDown)
                {
                    Log("BLOCKED: '" + item.Value + "' in Fenster '" + title + "'");
                    CloseWindow(handle);
                    break;
                }
            }
        }
    }
```

5. **Update `CloseWindow`:**
```csharp
    static void CloseWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        if (!IsBrowserProcess(handle)) return;

        // ... (rest of CloseWindow remains the same)
```

---

### File: `KeywordGuard.Pro.UI/ViewModels/MainViewModel.cs`

1. **Update Domain Parsing Validation Check:**
Replace `domainPart.Length > 1` with `domainPart.Length > 3` on line 356:
```csharp
                string domainPart = domain.Split('.')[0];
                if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 3)
                    items.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
```

---

## 5. Verification Method

To independently verify these changes:
1. **Compilation Check**:
   Run `dotnet build KeywordGuard.Pro.Agent\KeywordGuard.Pro.Agent.csproj` and `dotnet build KeywordGuard.Pro.UI\KeywordGuard.Pro.UI.csproj` to verify that there are no syntax errors, missing namespaces, or parameter mismatch errors.
2. **Behavioral Test (Non-Browser Windows)**:
   - Configure a blocked keyword (e.g. `test`).
   - Open a browser (e.g. Chrome) and navigate to a page containing `test` in the title. Verify the window is successfully closed.
   - Open a Notepad document and write `test` in its title (e.g. save it as `test.txt`). Verify the Notepad window is **not** closed.
3. **Behavioral Test (Domain Parsing)**:
   - Block `go.com` or `abc.de`.
   - Verify that the short parts `go` or `abc` are not added as aggressive blockers (since length <= 3) and therefore do not false-positively block other windows.
4. **Behavioral Test (Non-Aggressive Match Fallback)**:
   - Add a keyword (e.g. `match`) as non-aggressive (unchecked "Aggressive" flag).
   - Navigate to a page with title `matchmaker`. Since it doesn't match the word boundary, it should **not** close.
   - Navigate to a page with title `my match page`. Since it matches the word boundary, it **must** close.
