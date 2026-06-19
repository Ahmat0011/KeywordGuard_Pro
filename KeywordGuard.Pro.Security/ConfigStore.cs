using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeywordGuard.Pro.Security;

/// <summary>
/// AES-256-verschluesselte Speicherung der Konfiguration.
/// Die Config wird mit einem maschinenspezifischen Schluessel
/// verschluesselt und an zwei versteckten Orten gespeichert.
/// </summary>
public static class ConfigStore
{
    // ============================================================
    // WICHTIG: %LOCALAPPDATA% – garantiert schreibbar OHNE Admin!
    // CommonApplicationData (C:\ProgramData) braucht Admin.

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KG_Pro", "SecureData");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "sys_config.dat");

    private static readonly string BackupDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KG_Pro", "SysConfigBackup");

    private static readonly string BackupFile = Path.Combine(BackupDir, "kg_config.enc");

    // Maschinenspezifischen Schluessel erzeugen
    private static byte[] GetMachineKey()
    {
        string keyMaterial = Environment.MachineName +
                             Environment.ProcessorCount +
                             "KeywordGuard_Pro_V2_2026_Secure";
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
    }

    public static void Save(GuardConfig config)
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);
            if (!Directory.Exists(BackupDir))
                Directory.CreateDirectory(BackupDir);

            string json = JsonSerializer.Serialize(config);
            byte[] encrypted = Encrypt(json);

            // Vorherige Attribute zuruecksetzen, falls Dateien existieren
            // (Windows verweigert sonst das Ueberschreiben bei gesetztem Hidden/System)
            try
            {
                if (File.Exists(ConfigFile))
                    File.SetAttributes(ConfigFile, FileAttributes.Normal);
                if (File.Exists(BackupFile))
                    File.SetAttributes(BackupFile, FileAttributes.Normal);
            }
            catch { }

            // Dateien schreiben
            File.WriteAllBytes(ConfigFile, encrypted);
            File.WriteAllBytes(BackupFile, encrypted);

            // Verstecken (nur System-Attribut, keine ACL-Spielereien)
            try { File.SetAttributes(ConfigFile, FileAttributes.Hidden | FileAttributes.System); } catch { }
            try { File.SetAttributes(BackupFile, FileAttributes.Hidden | FileAttributes.System); } catch { }
        }
        catch { }
    }

    public static GuardConfig? Load()
    {
        try
        {
            // Zuerst Primary versuchen
            if (File.Exists(ConfigFile))
            {
                byte[] data = File.ReadAllBytes(ConfigFile);
                string? json = Decrypt(data);
                if (json != null)
                    return JsonSerializer.Deserialize<GuardConfig>(json);
            }

            // Fallback: Backup versuchen
            if (File.Exists(BackupFile))
            {
                byte[] data = File.ReadAllBytes(BackupFile);
                string? json = Decrypt(data);
                if (json != null)
                    return JsonSerializer.Deserialize<GuardConfig>(json);
            }
        }
        catch { }
        return null;
    }

    public static bool Exists()
    {
        return File.Exists(ConfigFile) || File.Exists(BackupFile);
    }

    // ============================================================
    // AES-256 Ver-/Entschluesselung
    // ============================================================
    private static byte[] Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = GetMachineKey();
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        using var encryptor = aes.CreateEncryptor();
        byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Format: [IV (16 Bytes)] + [Ciphertext]
        byte[] result = new byte[16 + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, 16);
        Buffer.BlockCopy(cipherBytes, 0, result, 16, cipherBytes.Length);
        return result;
    }

    private static string? Decrypt(byte[] data)
    {
        if (data.Length < 17) return null; // Mindestens IV + 1 Byte

        try
        {
            using var aes = Aes.Create();
            aes.Key = GetMachineKey();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // Format: [IV (16 Bytes)] + [Ciphertext]
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