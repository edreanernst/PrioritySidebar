using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace PrioritySidebar;

/// <summary>Settings saved in %APPDATA%\PrioritySidebar. The token is encrypted for the current Windows user.</summary>
public sealed class AppSettings
{
    public string ApiKey { get; set; } = "";
    public string? TokenProtected { get; set; }
    public string? BoardId { get; set; }
    public string? ListId { get; set; }
    public string? ListName { get; set; }
    public string? MonitorDevice { get; set; }
    public double Width { get; set; } = 320;
    public int PollSeconds { get; set; } = 30;

    [JsonIgnore]
    public bool IsNew { get; private set; }

    [JsonIgnore]
    public string Token
    {
        get
        {
            if (string.IsNullOrEmpty(TokenProtected)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(TokenProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (CryptographicException)
            {
                return "";
            }
        }
        set => TokenProtected = string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    [JsonIgnore]
    public bool IsComplete => ApiKey.Length > 0 && Token.Length > 0 && !string.IsNullOrEmpty(ListId) && !string.IsNullOrEmpty(BoardId);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Storage.SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Storage.SettingsPath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A damaged settings file just means setting up again.
        }
        return new AppSettings { IsNew = true };
    }

    public void Save()
    {
        Directory.CreateDirectory(Storage.Folder);
        File.WriteAllText(Storage.SettingsPath, JsonSerializer.Serialize(this, Storage.Indented));
        IsNew = false;
    }
}

public static class Storage
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrioritySidebar");

    public static string SettingsPath => Path.Combine(Folder, "settings.json");
    private static string CachePath => Path.Combine(Folder, "cards-cache.json");

    /// <summary>The last list loaded from Trello, shown while offline.</summary>
    public static List<TrelloCard> LoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
                return JsonSerializer.Deserialize<List<TrelloCard>>(File.ReadAllText(CachePath)) ?? [];
        }
        catch (Exception)
        {
        }
        return [];
    }

    public static void SaveCache(List<TrelloCard> cards)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cards));
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>The "start when Windows starts" switch, stored in the current user's Run key.</summary>
public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PrioritySidebar";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
