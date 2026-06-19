using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KeywordGuard.Pro.Security;

namespace KeywordGuard.Pro.UI.ViewModels;

public class KeywordDisplayItem : INotifyPropertyChanged
{
    private string _value = "";
    private bool _isAggressive;

    public string Value { get => _value; set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(AggressiveText)); } }
    public bool IsAggressive { get => _isAggressive; set { _isAggressive = value; OnPropertyChanged(); OnPropertyChanged(nameof(AggressiveText)); } }
    public string AggressiveText => IsAggressive ? "[AGGRESSIV]" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly Services.WordWatcher _wordWatcher = new();
    private readonly DispatcherTimer _countdownTimer = new();
    private readonly DispatcherTimer _firewallUpdateTimer = new(); // aktualisiert IPs alle 5 Min
    private DateTime? _endTime;
    private bool _isTimerRunning;

    private string _keywordInput = "";
    private string _days = "0";
    private string _hours = "0";
    private string _minutes = "30";
    private string _statusText = "";
    private string _countdownText = "00d 00h 00m 00s";
    private bool _isAggressive = false;

    public ObservableCollection<KeywordDisplayItem> KeywordItems { get; } = new();

    public string KeywordInput { get => _keywordInput; set { _keywordInput = value; OnPropertyChanged(); } }
    public string Days { get => _days; set { _days = value; OnPropertyChanged(); } }
    public string Hours { get => _hours; set { _hours = value; OnPropertyChanged(); } }
    public string Minutes { get => _minutes; set { _minutes = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string CountdownText { get => _countdownText; set { _countdownText = value; OnPropertyChanged(); } }
    public bool IsAggressive { get => _isAggressive; set { _isAggressive = value; OnPropertyChanged(); } }

    public bool IsTimerRunning
    {
        get => _isTimerRunning;
        set
        {
            _isTimerRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanStartTimer));
            OnPropertyChanged(nameof(CountdownVisible));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool CanDelete => !IsTimerRunning;
    public bool CanStartTimer => !IsTimerRunning;
    public Visibility CountdownVisible => IsTimerRunning ? Visibility.Visible : Visibility.Collapsed;

    public string FooterText
    {
        get
        {
            if (IsTimerRunning) return "🔒 Schutz aktiv – " + CountdownText;
            return "💡 Timer läuft nicht. Wörter eintragen und Timer starten.";
        }
    }

    public ICommand AddKeywordCommand { get; }
    public ICommand RemoveKeywordCommand { get; }
    public ICommand StartTimerCommand { get; }

    public MainViewModel()
    {
        AddKeywordCommand = new RelayCommand(AddKeyword);
        RemoveKeywordCommand = new RelayCommand<KeywordDisplayItem?>(RemoveKeyword);
        StartTimerCommand = new RelayCommand(StartTimer);

        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (s, e) => UpdateCountdown();

        // Firewall-Update-Timer: alle 5 Minuten IPs neu auflösen
        _firewallUpdateTimer.Interval = TimeSpan.FromMinutes(5);
        _firewallUpdateTimer.Tick += (s, e) => UpdateFirewallRules();

        // Prüfen, ob Timer noch läuft (nach Absturz o.Ä.)
        RestoreRunningTimer();

        if (!IsTimerRunning)
            StatusText = "✅ Bereit. Gespeicherte Blockierungen sind über „Hide“ erreichbar.";
    }

    private void LoadStoredKeywordsToUI()
    {
        try
        {
            var cfg = ConfigStore.Load();
            if (cfg == null) return;

            KeywordItems.Clear();
            foreach (var kw in cfg.Keywords)
            {
                KeywordItems.Add(new KeywordDisplayItem { Value = kw.Value, IsAggressive = kw.IsAggressive });
            }
            StatusText = $"ℹ️ {cfg.Keywords.Count} gespeicherte Blockierungen geladen.";
        }
        catch { }
    }

    public bool HasConfiguredPin()
    {
        var cfg = ConfigStore.Load();
        return !string.IsNullOrWhiteSpace(cfg?.PinHash) && !string.IsNullOrWhiteSpace(cfg?.PinSalt);
    }

    public bool IsValidPin(string pin)
    {
        return !string.IsNullOrWhiteSpace(pin) && pin.Length == 4 && pin.All(char.IsDigit);
    }

    public bool CreatePin(string pin)
    {
        if (!IsValidPin(pin)) return false;

        var cfg = ConfigStore.Load() ?? new GuardConfig();
        if (!string.IsNullOrWhiteSpace(cfg.PinHash) && !string.IsNullOrWhiteSpace(cfg.PinSalt))
            return false;

        (string hash, string salt) = CreatePinHash(pin);
        cfg.PinHash = hash;
        cfg.PinSalt = salt;
        ConfigStore.Save(cfg);
        return true;
    }

    public bool VerifyPin(string pin)
    {
        if (!IsValidPin(pin)) return false;
        var cfg = ConfigStore.Load();
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.PinHash) || string.IsNullOrWhiteSpace(cfg.PinSalt))
            return false;

        return VerifyPinHash(pin, cfg.PinSalt, cfg.PinHash);
    }

    public bool ResetPin(string pin)
    {
        if (IsTimerRunning || !IsValidPin(pin)) return false;

        var cfg = ConfigStore.Load() ?? new GuardConfig();
        (string hash, string salt) = CreatePinHash(pin);
        cfg.PinHash = hash;
        cfg.PinSalt = salt;
        ConfigStore.Save(cfg);
        return true;
    }

    public List<KeywordDisplayItem> GetStoredKeywords()
    {
        var cfg = ConfigStore.Load();
        if (cfg == null)
            return new List<KeywordDisplayItem>();

        return cfg.Keywords
            .Select(k => new KeywordDisplayItem { Value = k.Value, IsAggressive = k.IsAggressive })
            .OrderBy(k => k.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool TryAddStoredKeyword(string value, bool isAggressive, out string message)
    {
        message = "";
        if (IsTimerRunning) { message = "Timer aktiv: Änderungen nicht erlaubt."; return false; }

        string trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) { message = "Bitte einen Wert eingeben."; return false; }

        var cfg = ConfigStore.Load() ?? new GuardConfig();
        if (cfg.Keywords.Any(k => k.Value.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            message = "Eintrag existiert bereits.";
            return false;
        }

        cfg.Keywords.Add(new BlockedItem { Value = trimmed, IsAggressive = isAggressive });
        ConfigStore.Save(cfg);
        message = "Eintrag hinzugefügt.";
        return true;
    }

    public bool TryUpdateStoredKeyword(string originalValue, string newValue, bool isAggressive, out string message)
    {
        message = "";
        if (IsTimerRunning) { message = "Timer aktiv: Änderungen nicht erlaubt."; return false; }

        string trimmed = newValue.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) { message = "Bitte einen Wert eingeben."; return false; }

        var cfg = ConfigStore.Load();
        if (cfg == null) { message = "Keine gespeicherte Konfiguration gefunden."; return false; }

        var existing = cfg.Keywords.FirstOrDefault(k => k.Value.Equals(originalValue, StringComparison.OrdinalIgnoreCase));
        if (existing == null) { message = "Eintrag nicht gefunden."; return false; }

        bool duplicate = cfg.Keywords.Any(k =>
            !k.Value.Equals(originalValue, StringComparison.OrdinalIgnoreCase) &&
            k.Value.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            message = "Neuer Wert existiert bereits.";
            return false;
        }

        existing.Value = trimmed;
        existing.IsAggressive = isAggressive;
        ConfigStore.Save(cfg);
        message = "Eintrag aktualisiert.";
        return true;
    }

    public bool TryDeleteStoredKeyword(string value, out string message)
    {
        message = "";
        if (IsTimerRunning) { message = "Timer aktiv: Änderungen nicht erlaubt."; return false; }

        var cfg = ConfigStore.Load();
        if (cfg == null) { message = "Keine gespeicherte Konfiguration gefunden."; return false; }

        var existing = cfg.Keywords.FirstOrDefault(k => k.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (existing == null) { message = "Eintrag nicht gefunden."; return false; }

        cfg.Keywords.Remove(existing);
        ConfigStore.Save(cfg);
        message = "Eintrag gelöscht.";
        return true;
    }

    private static (string Hash, string Salt) CreatePinHash(string pin)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] pinBytes = Encoding.UTF8.GetBytes(pin);
        byte[] combined = new byte[salt.Length + pinBytes.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(pinBytes, 0, combined, salt.Length, pinBytes.Length);

        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(combined);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    private static bool VerifyPinHash(string pin, string saltBase64, string hashBase64)
    {
        try
        {
            byte[] salt = Convert.FromBase64String(saltBase64);
            byte[] pinBytes = Encoding.UTF8.GetBytes(pin);
            byte[] combined = new byte[salt.Length + pinBytes.Length];
            Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
            Buffer.BlockCopy(pinBytes, 0, combined, salt.Length, pinBytes.Length);

            using var sha = SHA256.Create();
            byte[] computedHash = sha.ComputeHash(combined);
            byte[] storedHash = Convert.FromBase64String(hashBase64);
            return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stellt einen noch laufenden Timer wieder her (nach Programmabsturz).
    /// </summary>
    private void RestoreRunningTimer()
    {
        try
        {
            var cfg = ConfigStore.Load();
            if (cfg != null && cfg.IsActive())
            {
                _endTime = cfg.EndTime;
                IsTimerRunning = true;
                _countdownTimer.Start();
                _firewallUpdateTimer.Start();
                _wordWatcher.Start(GetBlockedItems, () => IsTimerRunning);
                StatusText = "🔒 Schutz wurde wiederhergestellt (Timer lief noch).";

                // Hosts-Datei + Firewall neu aktivieren
                var domains = ExtractDomains(cfg.Keywords);
                if (domains.Count > 0)
                {
                    HostsBlocker.AddUrls(domains);
                    FirewallBlocker.AddBlock(domains);
                }
            }
        }
        catch { }
    }

    private void AddKeyword()
    {
        if (string.IsNullOrWhiteSpace(KeywordInput)) return;

        // Teilen nach Komma, Semikolon, Leerzeichen und Zeilenumbruch
        var parts = KeywordInput.Split(new[] { ',', ';', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            string kw = part.Trim();
            if (!string.IsNullOrWhiteSpace(kw))
            {
                AddSingleKeyword(kw);
            }
        }

        KeywordInput = "";
    }

    private void AddSingleKeyword(string value)
    {
        string kw = value.Trim();
        if (string.IsNullOrWhiteSpace(kw)) return;

        if (!KeywordItems.Any(k => k.Value.Equals(kw, StringComparison.OrdinalIgnoreCase)))
        {
            KeywordItems.Add(new KeywordDisplayItem { Value = kw, IsAggressive = IsAggressive });
        }

        var cfg = ConfigStore.Load() ?? new GuardConfig();
        if (!cfg.Keywords.Any(k => k.Value.Equals(kw, StringComparison.OrdinalIgnoreCase)))
        {
            cfg.Keywords.Add(new BlockedItem { Value = kw, IsAggressive = IsAggressive });
            ConfigStore.Save(cfg);

            if (IsTimerRunning)
            {
                var domains = ExtractDomains(cfg.Keywords);
                if (domains.Count > 0)
                {
                    FirewallBlocker.UpdateBlocks(domains);
                }
            }
        }
    }

    private void RemoveKeyword(KeywordDisplayItem? item)
    {
        if (!CanDelete || item == null) return;
        KeywordItems.Remove(item);

        var cfg = ConfigStore.Load();
        if (cfg != null)
        {
            var existing = cfg.Keywords.FirstOrDefault(k => k.Value.Equals(item.Value, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                cfg.Keywords.Remove(existing);
                ConfigStore.Save(cfg);
            }
        }
    }

    private List<string> ExtractDomains(List<BlockedItem> items)
    {
        var domains = new List<string>();
        foreach (var item in items)
        {
            string? domain = UrlHelper.ExtractDomain(item.Value);
            if (domain != null && !domains.Contains(domain))
            {
                domains.Add(domain);
            }
        }
        return domains;
    }

    private void StartTimer()
    {
        try
        {
            bool daysOk = int.TryParse(Days, out var days);
            bool hoursOk = int.TryParse(Hours, out var hours);
            bool minutesOk = int.TryParse(Minutes, out var minutes);

            if (!daysOk || !hoursOk || !minutesOk)
            { StatusText = "⚠️ Ungültige Zahl!"; return; }
            if (days < 0 || hours < 0 || minutes < 0)
            { StatusText = "⚠️ Keine negativen Werte!"; return; }
            if (days == 0 && hours == 0 && minutes == 0)
            { StatusText = "⚠️ Bitte mindestens 1 Minute."; return; }
            if (_isTimerRunning) return;

            // Admin-Check: Firewall braucht Admin-Rechte
            bool isAdmin = ProcessHardening.IsAdmin();

            _endTime = DateTime.Now + new TimeSpan(days, hours, minutes, 0);

            // Config laden oder neu erstellen
            var cfg = ConfigStore.Load();
            if (cfg == null)
            {
                // Config konnte nicht geladen werden – neue erstellen mit aktuellen UI-Keywords
                cfg = new GuardConfig();
            }

            // UI-Keywords in Config übernehmen
            foreach (var kw in KeywordItems)
            {
                if (!string.IsNullOrWhiteSpace(kw.Value) && !cfg.Keywords.Any(k => k.Value.Equals(kw.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    cfg.Keywords.Add(new BlockedItem { Value = kw.Value, IsAggressive = kw.IsAggressive });
                }
            }

            cfg.EndTime = _endTime;
            ConfigStore.Save(cfg);

            // ===== URL-BLOCKIERUNG STRATEGIE =====
            // 1. Hosts-Datei (DNS-Level): immer, funktioniert ohne Admin
            // 2. Firewall (IP-Level): nur mit Admin

            var domains = ExtractDomains(cfg.Keywords);

            // HOSTS-DATEI blockieren – KEIN Admin nötig!
            if (domains.Count > 0)
                HostsBlocker.AddUrls(domains);

            // FIREWALL blockieren (nur mit Admin)
            if (domains.Count > 0)
            {
                if (isAdmin)
                {
                    FirewallBlocker.UpdateBlocks(domains);
                }
                else
                {
                    StatusText = "⚠️ Kein Admin – Firewall-Blockierung nicht möglich, nur DNS (hosts) + Fenster-Schließen aktiv";
                }
            }

            IsTimerRunning = true;
            _countdownTimer.Start();
            _firewallUpdateTimer.Start();
            _wordWatcher.Start(GetBlockedItems, () => IsTimerRunning);

            int totalKeywords = cfg.Keywords.Count;
            string firewallStatus = isAdmin ? "🔒" : "⚠️";
            StatusText = $"{firewallStatus} FESTUNG AKTIV – {days}d {hours}h {minutes}m " +
                         $"({totalKeywords} Keywords, {domains.Count} Domains)" +
                         (isAdmin ? " [Firewall+DNS]" : " [KEIN ADMIN – DNS+WordWatcher ohne Firewall]");

            // Keine UI-Leerung mehr für bessere Transparenz
            // KeywordItems.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show("FEHLER in StartTimer:\n" + ex.Message, "Timer Fehler");
        }
    }

    private void UpdateFirewallRules()
    {
        try
        {
            var cfg = ConfigStore.Load();
            if (cfg == null || !cfg.IsActive()) return;

            var domains = ExtractDomains(cfg.Keywords);
            if (domains.Count > 0)
                FirewallBlocker.UpdateBlocks(domains);
        }
        catch { }
    }

    private List<BlockedItem> GetBlockedItems()
    {
        var config = ConfigStore.Load();
        if (config == null) return new List<BlockedItem>();

        var items = new List<BlockedItem>();

        foreach (var kw in config.Keywords)
        {
            string val = kw.Value.Trim();
            if (string.IsNullOrWhiteSpace(val)) continue;

            string? domain = UrlHelper.ExtractDomain(val);
            if (domain != null)
            {
                // URLs/domains always use aggressive (Contains) matching for the full domain
                // so that titles like "OK.RU - Google Chrome" are detected regardless of
                // whether the keyword was added as aggressive or not.
                items.Add(new BlockedItem { Value = domain, IsAggressive = true });

                // Always add the domain prefix to catch site brand names in titles
                // (e.g. "ok" from "ok.ru" matches "OK | Odnoklassniki - Google Chrome").
                // Short prefixes (<=3 chars) use word-boundary matching to avoid false positives
                // in words like "booking" or "Outlook"; long prefixes use Contains.
                string domainPart = domain.Split('.')[0];
                if (!string.IsNullOrWhiteSpace(domainPart) && domainPart != domain && domainPart.Length > 1)
                    items.Add(new BlockedItem { Value = domainPart, IsAggressive = domainPart.Length > 3 });
            }
            else
            {
                items.Add(new BlockedItem { Value = val, IsAggressive = kw.IsAggressive });
            }
        }
        return items;
    }

    private void UpdateCountdown()
    {
        if (!_endTime.HasValue) return;
        var remaining = _endTime.Value - DateTime.Now;
        if (remaining.TotalSeconds <= 0) { EndTimer(); return; }
        CountdownText = $"{remaining.Days}d {remaining.Hours:00}h {remaining.Minutes:00}m {remaining.Seconds:00}s";
        OnPropertyChanged(nameof(FooterText));
    }

    private void EndTimer()
    {
        _countdownTimer.Stop();
        _firewallUpdateTimer.Stop();
        IsTimerRunning = false;
        _endTime = null;
        CountdownText = "00d 00h 00m 00s";
        StatusText = "⏰ Timer abgelaufen. Alle Einschränkungen aufgehoben.";
        _wordWatcher.Stop();

        // ALLE Blockaden entfernen
        FirewallBlocker.RemoveAll();
        HostsBlocker.RemoveAll();

        var cfg = ConfigStore.Load() ?? new GuardConfig();
        cfg.EndTime = null;
        ConfigStore.Save(cfg);

        OnPropertyChanged(nameof(FooterText));
    }

    public void OnWindowClosing()
    {
        // Config laden – wenn fehlschlägt, NICHT überschreiben (Keywords wären weg)
        var cfg = ConfigStore.Load();
        if (cfg == null)
        {
            // Config nicht lesbar – alte Datei belassen, nichts überschreiben
            return;
        }

        // Keywords aus der UI sichern (falls welche da sind)
        foreach (var kw in KeywordItems)
            if (!string.IsNullOrWhiteSpace(kw.Value) &&
                !cfg.Keywords.Any(k => k.Value.Equals(kw.Value, StringComparison.OrdinalIgnoreCase)))
                cfg.Keywords.Add(new BlockedItem { Value = kw.Value, IsAggressive = kw.IsAggressive });

        if (_endTime.HasValue && IsTimerRunning)
            cfg.EndTime = _endTime;
        else
            cfg.EndTime = null;

        ConfigStore.Save(cfg);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    public RelayCommand(Action execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<bool>? _canExecute;
    public RelayCommand(Action<T?> execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute((T?)parameter);
}