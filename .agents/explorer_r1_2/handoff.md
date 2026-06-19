# Handoff Report — Explorer R1_2

## 1. Observation
We investigated the following files to identify the current structure, process checks, domain parsing, and fallback matching logic:
- `KeywordGuard.Pro.UI/Services/WordWatcher.cs`
- `KeywordGuard.Pro.Agent/Program.cs`
- `KeywordGuard.Pro.UI/ViewModels/MainViewModel.cs`

Specific observations:
- **Active Window Watcher (`WordWatcher.cs`):**
  - Line 63: `string title = GetActiveWindowTitle();` retrieves the active window title.
  - Lines 74-76: Matches the keyword with the active window title:
    ```csharp
    bool hit = item.IsAggressive
        ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
        : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);
    ```
  - Lines 78-79: Contains the buggy fallback logic:
    ```csharp
    if (!hit && !item.IsAggressive)
        hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
    ```
  - Lines 113-116: `CloseActiveWindow()` closes the active window using keyboard event emulation:
    ```csharp
    private static void CloseActiveWindow()
    {
        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return;
    ```

- **Agent Background Watcher (`Program.cs`):**
  - Lines 175-177: Extract and validate domain part length:
    ```csharp
    string domainPart = domain.Split('.')[0];
    if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
        targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
    ```
  - Lines 192-194: Extract and validate domain part length for configured URLs:
    ```csharp
    string domainPart = domain.Split('.')[0];
    if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
        targets.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
    ```
  - Lines 287-293: `CheckActiveWindow` retrieves foreground window and title text:
    ```csharp
    IntPtr handle = GetForegroundWindow();
    if (handle == IntPtr.Zero) return;
    if (GetWindowText(handle, buff, nChars) > 0)
    ```
  - Lines 298-300: Keyword matching:
    ```csharp
    bool hit = item.IsAggressive
        ? title.Contains(item.Value, StringComparison.OrdinalIgnoreCase)
        : Regex.IsMatch(title, @"\b" + Regex.Escape(item.Value) + @"\b", RegexOptions.IgnoreCase);
    ```
  - Lines 302-303: Contains the buggy fallback logic:
    ```csharp
    if (!hit && !item.IsAggressive)
        hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
    ```
  - Lines 318-320: `CloseWindow` method entry:
    ```csharp
    static void CloseWindow(IntPtr handle)
    {
    ```

- **UI View Model (`MainViewModel.cs`):**
  - Lines 355-357: Extract and validate domain part length:
    ```csharp
    string domainPart = domain.Split('.')[0];
    if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
        items.Add(new BlockedItem { Value = domainPart, IsAggressive = true });
    ```

---

## 2. Logic Chain
- **Browser restriction (Objective 1):** We observed that the active window handles are fetched using `GetForegroundWindow()` but their processes are not verified before title checking or window closing. By importing `GetWindowThreadProcessId` from `user32.dll` and using `System.Diagnostics.Process.GetProcessById((int)pid)`, we can retrieve the process name of the active window handle. Checking this name against a whitelist (`chrome`, `msedge`, `firefox`, `opera`, `brave`, `vivaldi`) ensures that keyword matching/blocking is only performed on web browser windows, protecting non-browser active windows from accidental closure.
- **Domain Part Length Validation (Objective 2):** We observed that when a domain is parsed (e.g. `domain.com`), the code splits the domain and adds the first part (e.g. `domain`) as an aggressive blocked keyword if `domainPart.Length > 1`. This leads to blocks on very short substrings (such as 2-character parts). Changing this to `domainPart.Length > 3` filters out domain parts that are 3 characters or less, only registering longer domain parts.
- **Fallback Matching correction (Objective 3):** We observed that if a keyword is marked as non-aggressive (`!item.IsAggressive`), and the regex word boundary check fails (`!hit`), it falls back to `title.Contains()`, which is aggressive/substring matching. This defeats the purpose of the non-aggressive flag. Removing this fallback block completely ensures that non-aggressive keywords match only via regex word boundaries.

---

## 3. Caveats
- No unit test suite exists in the project. Real-world validation depends on either manual verification or E2E integration test runs.
- `GetProcessById` can throw if the process terminates immediately after retrieving the handle. This is handled gracefully with try-catch blocks that return `false` on failure.
- Whitelist matching is case-insensitive, which matches Windows process naming conventions where names are typically lowercased (e.g., `chrome`), but handles mixed casing if encountered.

---

## 4. Conclusion
We propose the following exact changes:

### Proposal A: Browser Process Whitelisting
1. **Modify `WordWatcher.cs`:**
   - Add the DllImport and `IsBrowserProcess` helper method:
     ```csharp
     [DllImport("user32.dll", SetLastError = true)]
     private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

     private static bool IsBrowserProcess(IntPtr hWnd)
     {
         if (hWnd == IntPtr.Zero) return false;
         GetWindowThreadProcessId(hWnd, out uint pid);
         if (pid == 0) return false;
         try
         {
             using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
             string name = proc.ProcessName;
             return name.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("opera", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("brave", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("vivaldi", StringComparison.OrdinalIgnoreCase);
         }
         catch
         {
             return false;
         }
     }
     ```
   - Update `GetActiveWindowTitle()` to filter out non-browser window titles:
     ```csharp
     private static string GetActiveWindowTitle()
     {
         IntPtr handle = GetForegroundWindow();
         if (handle == IntPtr.Zero) return "";

         if (!IsBrowserProcess(handle)) return "";

         const int max = 512;
         var sb = new StringBuilder(max);
         return GetWindowText(handle, sb, max) > 0 ? sb.ToString() : "";
     }
     ```
   - Update `CloseActiveWindow()` to double-check the process whitelist before performing keyboard/message close actions:
     ```csharp
     private static void CloseActiveWindow()
     {
         IntPtr handle = GetForegroundWindow();
         if (handle == IntPtr.Zero) return;

         if (!IsBrowserProcess(handle)) return;
         // ...
     ```

2. **Modify `Program.cs`:**
   - Add the DllImport and `IsBrowserProcess` helper method:
     ```csharp
     [DllImport("user32.dll", SetLastError = true)]
     static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

     static bool IsBrowserProcess(IntPtr hWnd)
     {
         if (hWnd == IntPtr.Zero) return false;
         GetWindowThreadProcessId(hWnd, out uint pid);
         if (pid == 0) return false;
         try
         {
             using var proc = Process.GetProcessById((int)pid);
             string name = proc.ProcessName;
             return name.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("opera", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("brave", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("vivaldi", StringComparison.OrdinalIgnoreCase);
         }
         catch
         {
             return false;
         }
     }
     ```
   - Update `CheckActiveWindow()` to filter out non-browser window handles:
     ```csharp
     static void CheckActiveWindow(List<BlockedItem> targets)
     {
         if (!_running || _isShuttingDown) return;

         const int nChars = 512;
         StringBuilder buff = new StringBuilder(nChars);
         IntPtr handle = GetForegroundWindow();

         if (handle == IntPtr.Zero) return;

         if (!IsBrowserProcess(handle)) return;

         if (GetWindowText(handle, buff, nChars) > 0)
         // ...
     ```
   - Update `CloseWindow()` to double-check the whitelist:
     ```csharp
     static void CloseWindow(IntPtr handle)
     {
         if (!IsBrowserProcess(handle)) return;
         // ...
     ```

### Proposal B: Domain Validation Length
1. **Modify `MainViewModel.cs` (line 356):**
   - Change `domainPart.Length > 1` to `domainPart.Length > 3`:
     ```csharp
     if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 3)
     ```
2. **Modify `Program.cs` (lines 177 and 194):**
   - Change `domainPart.Length > 1` to `domainPart.Length > 3`:
     ```csharp
     if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 3)
     ```

### Proposal C: Fallback Matching Correction
1. **Modify `WordWatcher.cs` (lines 78-79):**
   - Delete the fallback block:
     ```csharp
     if (!hit && !item.IsAggressive)
         hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
     ```
2. **Modify `Program.cs` (lines 302-303):**
   - Delete the fallback block:
     ```csharp
     if (!hit && !item.IsAggressive)
         hit = title.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
     ```

---

## 5. Verification Method
1. **Compilation Check:**
   Run the following commands to ensure all assemblies compile after applying the changes:
   - `dotnet build KeywordGuard.Pro.UI/KeywordGuard.Pro.UI.csproj`
   - `dotnet build KeywordGuard.Pro.Agent/KeywordGuard.Pro.Agent.csproj`
2. **Manual / Runtime Check:**
   - Configure a non-aggressive keyword (e.g., `reddit` without aggressive checked). Open a browser window with title containing `reddit` (e.g. `reddit.com` or `subreddit`). The window should close.
   - Open a non-browser application like Notepad, save a file containing `reddit` (e.g., `reddit_notes.txt`). The window should **NOT** close because it is not a browser process.
   - Configure a keyword containing a short subdomain/TLD part (e.g., `abc.de`). Check that `abc` (length 3) is NOT auto-added as a blocked keyword, whereas `abcde` (if `abcde.com`) would be added.
