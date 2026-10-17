using Microsoft.Win32;

namespace SqlBackupTool;

/// <summary>Windows ile başlangıç (kullanıcı oturum açınca tepsi uygulaması).</summary>
public static class StartupHelper
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "SQLYedeklemeAraci";

    public static void Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) { Logger.Warn("Windows başlangıç anahtarı açılamadı."); return; }
            if (enable)
            {
                var exe = Paths.ExePath;
                key.SetValue(ValueName, $"\"{exe}\" --tray");
                Logger.Info("Windows ile başlat: AÇIK (tepsi).");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Info("Windows ile başlat: KAPALI.");
            }
        }
        catch (Exception ex) { Logger.Warn("Windows ile başlat ayarı: " + ex.Message); }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }
}
