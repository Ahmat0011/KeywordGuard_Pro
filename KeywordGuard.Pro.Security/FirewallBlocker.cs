using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KeywordGuard.Pro.Security;

/// <summary>
/// Verwaltet transparente Windows-Firewall-Regeln für blockierte Domains.
/// </summary>
public static class FirewallBlocker
{
    private const string RulePrefix = "KeywordGuardPro_Block_";
    private const string RuleGroup = "KeywordGuard Pro";

    private static void Log(string msg)
    {
        try
        {
            string logFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KG_Pro", "agent.log");
            string? dir = Path.GetDirectoryName(logFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(logFile,
                DateTime.Now.ToString("HH:mm:ss") + " [FIREWALL] " + msg + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>
    /// Erstellt ausgehende Firewall-Regeln für die angegebenen Domains.
    /// Löst die Domains in aktuelle IPs auf und blockiert sie.
    /// </summary>
    public static void AddBlock(List<string> domains)
    {
        if (domains == null || domains.Count == 0)
        {
            Log("AddBlock abgebrochen: Keine Domains uebergeben.");
            return;
        }

        foreach (var domain in domains)
        {
            string clean = domain.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(clean)) continue;

            try
            {
                var ips = ResolveAddresses(clean);
                if (ips.Count == 0)
                {
                    Log($"Keine IPs fuer {clean} gefunden. Ueberspringe.");
                    continue;
                }

                string ipList = string.Join(",", ips);
                string outboundRule = $"{RulePrefix}{ToSafeRuleSuffix(clean)}_out";
                string inboundRule = $"{RulePrefix}{ToSafeRuleSuffix(clean)}_in";

                RunNetsh(
                    $"advfirewall firewall add rule name=\"{outboundRule}\" group=\"{RuleGroup}\" dir=out action=block remoteip={ipList} enable=yes");
                RunNetsh(
                    $"advfirewall firewall add rule name=\"{inboundRule}\" group=\"{RuleGroup}\" dir=in action=block remoteip={ipList} enable=yes");
            }
            catch (Exception ex)
            {
                Log($"Allgemeiner Fehler bei AddBlock fuer {clean}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Entfernt alle von KeywordGuard Pro erstellten Firewall-Regeln.
    /// </summary>
    public static void RemoveAll()
    {
        RunNetsh($"advfirewall firewall delete rule group=\"{RuleGroup}\"");
    }

    /// <summary>
    /// Aktualisiert die Firewall-Regeln: entfernt alte, erstellt neue mit aktuellen IPs.
    /// </summary>
    public static void UpdateBlocks(List<string> domains)
    {
        RemoveAll();
        AddBlock(domains);
    }

    /// <summary>
    /// Gibt zurück, ob mindestens eine KeywordGuard-Regel aktiv ist.
    /// </summary>
    public static bool HasActiveBlocks()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{RulePrefix}*\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output.Contains(RulePrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static List<string> ResolveAddresses(string domain)
    {
        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ResolveForHost(domain, ips);

        if (!domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            ResolveForHost("www." + domain, ips);

        return ips.ToList();
    }

    private static void ResolveForHost(string host, HashSet<string> destination)
    {
        try
        {
            var results = Dns.GetHostAddresses(host)
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(ip => ip.ToString());
            foreach (var ip in results)
                destination.Add(ip);
        }
        catch (Exception ex)
        {
            Log($"DNS-Aufloesung fehlgeschlagen ({host}): {ex.Message}");
        }
    }

    /// <summary>
    /// Führt einen netsh-Befehl aus und loggt nur unerwartete Fehler.
    /// </summary>
    private static void RunNetsh(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return;

            string output = proc.StandardOutput.ReadToEnd().Trim();
            string error = proc.StandardError.ReadToEnd().Trim();
            proc.WaitForExit(5000);

            if (proc.ExitCode != 0 && !output.Contains("No rules match", StringComparison.OrdinalIgnoreCase))
            {
                Log($"netsh {arguments} -> ExitCode={proc.ExitCode}\nOut: {output}\nErr: {error}");
            }
        }
        catch (Exception ex)
        {
            Log($"Schwerer Fehler bei netsh {arguments}: {ex.Message}");
        }
    }

    private static string ToSafeRuleSuffix(string domain)
    {
        var builder = new StringBuilder(domain.Length);
        foreach (char ch in domain)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
            else
                builder.Append('_');
        }

        if (builder.Length == 0)
            builder.Append("domain");

        if (builder.Length > 48)
            return builder.ToString(0, 48);

        return builder.ToString();
    }
}
