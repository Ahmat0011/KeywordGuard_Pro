using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeywordGuard.Pro.Security;

/// <summary>
/// Persistiert die Konfiguration in einem transparenten, benutzerbezogenen Speicherort.
/// Legacy-verschlüsselte Dateien werden weiterhin gelesen und in das neue Format migriert.
/// </summary>
public static class ConfigStore
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KeywordGuardPro");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");
    private static readonly string BackupFile = Path.Combine(ConfigDir, "config.backup.json");

    // Legacy (nur für Migration)
    private static readonly string LegacyPrimary = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KG_Pro", "SecureData", "sys_config.dat");
    private static readonly string LegacyBackup = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KG_Pro", "SysConfigBackup", "kg_config.enc");

    public static void Save(GuardConfig config)
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(ConfigFile, json, Encoding.UTF8);
            File.WriteAllText(BackupFile, json, Encoding.UTF8);
        }
        catch { }
    }

    public static GuardConfig? Load()
    {
        try
        {
            if (TryLoadJsonFile(ConfigFile, out var config) && config != null)
                return config;
            if (TryLoadJsonFile(BackupFile, out config) && config != null)
                return config;

            if (TryLoadLegacyEncrypted(out config) && config != null)
            {
                Save(config);
                return config;
            }
        }
        catch { }
        return null;
    }

    public static bool Exists()
    {
        return File.Exists(ConfigFile) ||
               File.Exists(BackupFile) ||
               File.Exists(LegacyPrimary) ||
               File.Exists(LegacyBackup);
    }

    private static bool TryLoadJsonFile(string path, out GuardConfig? config)
    {
        config = null;
        try
        {
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path, Encoding.UTF8);
            config = JsonSerializer.Deserialize<GuardConfig>(json);
            return config != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadLegacyEncrypted(out GuardConfig? config)
    {
        config = null;
        return TryLoadLegacyEncryptedFile(LegacyPrimary, out config) ||
               TryLoadLegacyEncryptedFile(LegacyBackup, out config);
    }

    private static bool TryLoadLegacyEncryptedFile(string path, out GuardConfig? config)
    {
        config = null;
        try
        {
            if (!File.Exists(path)) return false;
            byte[] data = File.ReadAllBytes(path);
            string? json = DecryptLegacy(data);
            if (json == null) return false;
            config = JsonSerializer.Deserialize<GuardConfig>(json);
            return config != null;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] GetLegacyMachineKey()
    {
        string keyMaterial = Environment.MachineName +
                             Environment.ProcessorCount +
                             "KeywordGuard_Pro_V2_2026_Secure";
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
    }

    private static string? DecryptLegacy(byte[] data)
    {
        if (data.Length < 17) return null;

        try
        {
            using var aes = Aes.Create();
            aes.Key = GetLegacyMachineKey();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            byte[] iv = new byte[16];
            Buffer.BlockCopy(data, 0, iv, 0, 16);
            aes.IV = iv;

            byte[] cipherBytes = new byte[data.Length - 16];
            Buffer.BlockCopy(data, 16, cipherBytes, 0, cipherBytes.Length);

            using var decryptor = aes.CreateDecryptor();
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }
}
