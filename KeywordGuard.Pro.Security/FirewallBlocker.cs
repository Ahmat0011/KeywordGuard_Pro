using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace KeywordGuard.Pro.Security;

/// <summary>
/// Blockiert Domains über Windows-Firewall-Regeln (KEINE hosts-Datei!).
/// Die Regeln werden mit verschlüsselten Namen gespeichert, sodass
/// niemand sehen kann, welche Seiten blockiert werden.
/// </summary>
public static class FirewallBlocker
{
    private const string RulePrefix = "KG_P_";

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

        Log($"AddBlock gestartet fuer {domains.Count} Domain(s): {string.Join(", ", domains)}");

        foreach (var domain in domains)
        {
            string clean = domain.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(clean)) continue;

            string ruleName = $"{RulePrefix}{GetRuleHash(clean)}";

            try
            {
                Log($"Löse IPs auf fuer Domain: {clean}");
                // Domain in IPs auflösen (IPv4 + IPv6)
                var ips = new List<string>();
                try
                {
                    var mainIps = Dns.GetHostAddresses(clean)
                        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6)
                        .Select(ip => ip.ToString());
                    ips.AddRange(mainIps);
                    Log($"IPs fuer {clean}: {string.Join(", ", mainIps)}");
                }
                catch (Exception ex)
                {
                    Log($"Fehler bei DNS-Aufloesung von {clean}: {ex.Message}");
                }

                // Auch die www.-Variante auflösen, falls es sich um eine Hauptdomain handelt
                if (!clean.StartsWith("www."))
                {
                    string wwwDomain = "www." + clean;
                    try
                    {
                        var wwwIps = Dns.GetHostAddresses(wwwDomain)
                            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6)
                            .Select(ip => ip.ToString());
                        ips.AddRange(wwwIps);
                        Log($"IPs fuer {wwwDomain}: {string.Join(", ", wwwIps)}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Fehler bei DNS-Aufloesung von {wwwDomain}: {ex.Message}");
                    }
                }

                ips = ips.Distinct().ToList();

                if (ips.Count == 0)
                {
                    Log($"Keine IPs fuer {clean} (oder www.{clean}) gefunden. Ueberspringe.");
                    continue;
                }

                string ipList = string.Join(",", ips);
                Log($"Erstelle Firewall-Regel '{ruleName}' fuer IPs: {ipList}");

                // Firewall-Regel erstellen – ausgehende Verbindungen blockieren
                RunNetsh($"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block remoteip={ipList} enable=yes");

                // Auch eingehende Verbindungen blockieren (für alle Fälle)
                RunNetsh($"advfirewall firewall add rule name=\"{ruleName}_in\" dir=in action=block remoteip={ipList} enable=yes");
            }
            catch (Exception ex)
            {
                Log($"Allgemeiner Fehler bei AddBlock fuer {clean}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Entfernt ALLE Firewall-Regeln, die mit dem KG_Pro-Präfix beginnen.
    /// </summary>
    public static void RemoveAll()
    {
        Log("Entferne alle KG_Pro Firewall-Regeln...");
        try
        {
            RunPowerShell($"Remove-NetFirewallRule -DisplayName '{RulePrefix}*' -ErrorAction SilentlyContinue");
            RunPowerShell("Remove-NetFirewallRule -DisplayName 'StealthGuard_*' -ErrorAction SilentlyContinue");
        }
        catch (Exception ex)
        {
            Log($"Fehler beim Entfernen der Firewall-Regeln: {ex.Message}");
        }
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
    /// Gibt die aktuell blockierten Domains zurück (anhand der Regel-Namen).
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
            return output.Contains(RulePrefix);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Führt einen netsh-Befehl aus und loggt das Ergebnis bei Fehlern oder Warnungen.
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
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                string error = proc.StandardError.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                
                if (proc.ExitCode != 0 || !string.IsNullOrWhiteSpace(error) || (!output.Contains("Ok.") && !output.Contains("gelöscht")))
                {
                    Log($"netsh {arguments} -> ExitCode={proc.ExitCode}\nOut: {output}\nErr: {error}");
                }
                else
                {
                    Log($"netsh erfolgreich: {output}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Schwerer Fehler bei netsh {arguments}: {ex.Message}");
        }
    }

    /// <summary>
    /// Führt einen PowerShell-Befehl aus und loggt das Ergebnis bei Fehlern oder Warnungen.
    /// </summary>
    private static void RunPowerShell(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                string error = proc.StandardError.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                
                if (proc.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
                {
                    Log($"powershell {command} -> ExitCode={proc.ExitCode}\nOut: {output}\nErr: {error}");
                }
                else
                {
                    Log($"powershell erfolgreich: {output}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Schwerer Fehler bei powershell {command}: {ex.Message}");
        }
    }

    /// <summary>
    /// Erzeugt einen kurzen Hash aus dem Domain-Namen,
    /// damit der Regelname keine Rückschlüsse auf die blockierte Domain zulässt.
    /// </summary>
    private static string GetRuleHash(string domain)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(domain + "KG_Pro_Salt_2026"));
        return Convert.ToHexString(hash)[..12]; // Nur 12 Zeichen = unsichtbar
    }
}