# Handoff Report - Explorer R1.3 (Final Findings)

## 1. Observation
We examined the following files in the `KeywordGuard_Pro` codebase:
1. **`KeywordGuard.Pro.UI/Services/WordWatcher.cs`**:
   - Lines 74-80:
     ```csharp
     bool hit = item.IsAggressive
         ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
         : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

     if (!hit && !item.IsAggressive)
         hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
     ```
   - Lines 103-111 (`GetActiveWindowTitle()`):
     ```csharp
     private static string GetActiveWindowTitle()
     {
         IntPtr handle = GetForegroundWindow();
         if (handle == IntPtr.Zero) return "";

         const int max = 512;
         var sb = new StringBuilder(max);
         return GetWindowText(handle, sb, max) > 0 ? sb.ToString() : "";
     }
     ```

2. **`KeywordGuard.Pro.Agent/Program.cs`**:
   - Lines 176-178 and 193-195 (domain parsing):
     ```csharp
     string domainPart = domain.Split('.')[0];
     if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
         targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
     ```
   - Lines 298-303:
     ```csharp
     bool hit = item.IsAggressive
         ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
         : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);

     if (!hit && !item.IsAggressive)
         hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
     ```

3. **`KeywordGuard.Pro.UI/ViewModels/MainViewModel.cs`**:
   - Lines 355-357 (domain parsing):
     ```csharp
     string domainPart = domain.Split('.')[0];
     if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
         items.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
     ```

## 2. Logic Chain
- **Browser restriction logic**: To prevent the Agent and UI from closing non-browser active windows (like Notepad or IDEs), we need to check if the active window handle is associated with a whitelisted browser process. The whitelisted process names are: `chrome`, `msedge`, `firefox`, `opera`, `brave`, `vivaldi`.
- **Process retrieval**: Calling `GetWindowThreadProcessId` via P/Invoke yields the process ID (PID) of the active window handle. We can then instantiate a `System.Diagnostics.Process` object using `Process.GetProcessById((int)processId)` and inspect its `ProcessName` property.
- **Domain Part Length Validation**: Domain parts are extracted and added to blocks so that subdomains or parent domains can be checked. However, matching very short domain parts (like "co", "de", "in") leads to false positives. Changing `domainPart.Length > 1` to `domainPart.Length > 3` will require domain parts to be at least 4 characters long (e.g. `google` in `google.com`), preventing matches on top-level domain fragments.
- **Fallback Matching Logic**: In both `WordWatcher.cs` and `Program.cs`, when `!item.IsAggressive`, the code first performs a regex check with word boundaries. However, the subsequent check `if (!hit && !item.IsAggressive) hit = title.Contains(item.Value, ...)` falls back to a simple substring `Contains()`, effectively turning every non-aggressive check into an aggressive check. Deleting this fallback ensures non-aggressive items only match exact word boundaries.

## 3. Caveats
- `Process.GetProcessById` can throw an `ArgumentException` if the process has already exited by the time it is called. The helper function `IsBrowserProcess` must catch all exceptions and return `false` in those cases to prevent crashes.
- The `ProcessName` returned by `Process` does not contain the `.exe` extension (e.g. `"chrome"` for `chrome.exe`), which aligns with our case-insensitive whitelist comparison.

## 4. Conclusion
We have designed exact code changes to resolve the R1 requirements:
1. Whitelist Active Browser Processes: Add a helper `IsBrowserProcess(IntPtr hWnd)` that obtains the PID via `GetWindowThreadProcessId` and checks against the six whitelisted browser names (`chrome`, `msedge`, `firefox`, `opera`, `brave`, `vivaldi`). Use this helper in `WordWatcher.cs` and `Program.cs` before evaluating matches.
2. Restrict Domain Part validation: Update the check `domainPart.Length > 1` to `domainPart.Length > 3` in `Program.cs` (lines 177, 194) and `MainViewModel.cs` (line 356).
3. Fix Non-aggressive Matching: Remove the `if (!hit && !item.IsAggressive) hit = title.Contains(...)` fallback from both `WordWatcher.cs` and `Program.cs`.

## 5. Verification Method
- **Compilation check**: Build the projects using dotnet CLI:
  - `dotnet build KeywordGuard.Pro.UI/KeywordGuard.Pro.UI.csproj`
  - `dotnet build KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj`
- **Manual Verification**: After implementation, launch the Agent with a non-aggressive blocked keyword (e.g., `"game"`).
  - Open a browser and type `"game"` in the URL bar/title -> The window should close.
  - Open Notepad and type `"game"` in the document/title -> The window should NOT close.
  - Open a browser with `"foogame"` in the title -> The window should NOT close (since it lacks word boundaries and is non-aggressive).
  - Add a short domain like `ab.com` -> The parsed domain part `"ab"` should not be blocked since `domainPart.Length <= 3`.

---

## Proposed Code Changes

### Target 1: `KeywordGuard.Pro.UI/Services/WordWatcher.cs`

#### Chunk 1: Add using System.Diagnostics;
```csharp
<<<<
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using KeywordGuard.Pro.Security;
====
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using KeywordGuard.Pro.Security;
>>>>
```

#### Chunk 2: Import `GetWindowThreadProcessId`
```csharp
<<<<
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const uint WM_CLOSE = 0x0010;
====
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint WM_CLOSE = 0x0010;
>>>>
```

#### Chunk 3: Update `Start` loop to retrieve handle, verify browser process, and pass handle to `CloseActiveWindow`
```csharp
<<<<
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
====
                    IntPtr handle = GetForegroundWindow();
                    if (handle == IntPtr.Zero)
                    {
                        await Task.Delay(500, _cts.Token);
                        continue;
                    }

                    if (!IsBrowserProcess(handle))
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
>>>>
```

#### Chunk 4: Add `IsBrowserProcess` and update `GetActiveWindowTitle` to accept handle
```csharp
<<<<
    private static string GetActiveWindowTitle()
    {
        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return "";

        const int max = 512;
        var sb = new StringBuilder(max);
        return GetWindowText(handle, sb, max) > 0 ? sb.ToString() : "";
    }

    private static void CloseActiveWindow()
    {
        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return;
====
    private static bool IsBrowserProcess(IntPtr hWnd)
    {
        try
        {
            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            if (processId == 0) return false;

            using (var proc = Process.GetProcessById((int)processId))
            {
                string procName = proc.ProcessName;
                string[] whitelist = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi" };
                return whitelist.Any(w => string.Equals(w, procName, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch
        {
            return false;
        }
    }

    private static string GetActiveWindowTitle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return "";

        const int max = 512;
        var sb = new StringBuilder(max);
        return GetWindowText(handle, sb, max) > 0 ? sb.ToString() : "";
    }

    private static void CloseActiveWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
>>>>
```


### Target 2: `KeywordGuard.Pro.Agent/Program.cs`

#### Chunk 1: Import `GetWindowThreadProcessId`
```csharp
<<<<
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    const uint WM_CLOSE = 0x0010;
====
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    const uint WM_CLOSE = 0x0010;
>>>>
```

#### Chunk 2: Add `IsBrowserProcess` helper
```csharp
<<<<
    private static void Log(string msg)
    {
====
    private static bool IsBrowserProcess(IntPtr hWnd)
    {
        try
        {
            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            if (processId == 0) return false;

            using (var proc = Process.GetProcessById((int)processId))
            {
                string procName = proc.ProcessName;
                string[] whitelist = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi" };
                return whitelist.Any(w => string.Equals(w, procName, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch
        {
            return false;
        }
    }

    private static void Log(string msg)
    {
>>>>
```

#### Chunk 3: Update domain validation length checks in Program.cs
```csharp
<<<<
                            string domainPart = domain.Split('.')[0];
                            if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
                                targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
                        }
                        else
                        {
                            targets.Add(new BlockedItem { Value = val, IsAggressive = kw.IsAggressive });
                        }
                    }

                    foreach (var url in config.Urls)
                    {
                        string? domain = UrlHelper.ExtractDomain(url);
                        if (domain != null)
                        {
                            targets.Add(new BlockedItem { Value = domain, IsAggressive = true });

                            string domainPart = domain.Split('.')[0];
                            if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
                                targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
                        }
                    }
====
                            string domainPart = domain.Split('.')[0];
                            if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 3)
                                targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
                        }
                        else
                        {
                            targets.Add(new BlockedItem { Value = val, IsAggressive = kw.IsAggressive });
                        }
                    }

                    foreach (var url in config.Urls)
                    {
                        string? domain = UrlHelper.ExtractDomain(url);
                        if (domain != null)
                        {
                            targets.Add(new BlockedItem { Value = domain, IsAggressive = true });

                            string domainPart = domain.Split('.')[0];
                            if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 3)
                                targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
                        }
                    }
>>>>
```

#### Chunk 4: Whitelist check and fallback matching fix in `CheckActiveWindow`
```csharp
<<<<
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
====
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
>>>>
```


### Target 3: `KeywordGuard.Pro.UI/ViewModels/MainViewModel.cs`

#### Chunk 1: Update domain validation length check
```csharp
<<<<
            string? domain = UrlHelper.ExtractDomain(val);
            if (domain != null)
            {
                items.Add(new BlockedItem { Value = domain, IsAggressive = kw.IsAggressive });
                string domainPart = domain.Split('.')[0];
                if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
                    items.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
            }
====
            string? domain = UrlHelper.ExtractDomain(val);
            if (domain != null)
            {
                items.Add(new BlockedItem { Value = domain, IsAggressive = kw.IsAggressive });
                string domainPart = domain.Split('.')[0];
                if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 3)
                    items.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
            }
>>>>
```
