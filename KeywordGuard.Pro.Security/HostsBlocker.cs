using System.Text;

namespace KeywordGuard.Pro.Security;

/// <summary>
/// Blockiert URLs ueber die Windows-Hosts-Datei.
/// 
/// KEINE sichtbare Markierung in der hosts-Datei!
/// Die Liste der blockierten Domains wird NUR im verschluesselten
/// ConfigStore gespeichert (AES-256). Niemand kann sehen,
/// welche Seiten blockiert werden.
/// </summary>
public static class HostsBlocker
{
    private static readonly string HostsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");

    private const string Redirect = "127.0.0.1";

    /// <summary>
    /// Fuegt URLs zur hosts-Datei hinzu (deaktiviert nach Benutzer-Constraint).
    /// </summary>
    public static void AddUrls(List<string> urls)
    {
        // Deaktiviert nach Benutzer-Constraint (keine hosts-Datei fuer Blockierung verwenden)
        return;
    }

    /// <summary>
    /// Entfernt blockierte URLs aus der hosts-Datei (deaktiviert nach Benutzer-Constraint).
    /// </summary>
    public static void RemoveAll()
    {
        // Deaktiviert nach Benutzer-Constraint (keine hosts-Datei fuer Blockierung verwenden)
        return;
    }

    /// <summary>
    /// Bereinigt eine URL: entfernt Protokoll, www, Pfade.
    /// Liefert den reinen Domain-Namen.
    /// </summary>
    private static string CleanUrl(string url)
    {
        string clean = url.Trim()
            .Replace("https://", "")
            .Replace("http://", "")
            .Replace("www.", "")
            .Trim('/');

        int slashIndex = clean.IndexOf('/');
        if (slashIndex > 0)
            clean = clean.Substring(0, slashIndex);

        int portIndex = clean.IndexOf(':');
        if (portIndex > 0)
            clean = clean.Substring(0, portIndex);

        return clean;
    }
}