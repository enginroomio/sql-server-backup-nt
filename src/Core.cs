using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SqlBackupTool;

public static class Paths
{
    public static bool IsSystem
    {
        get { try { return System.Security.Principal.WindowsIdentity.GetCurrent().IsSystem; } catch { return false; } }
    }
    static string ProgramDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SQLBackupTool");
    // SYSTEM (server mode c) has no user profile config; it uses a ProgramData copy written by the GUI.
    public static string AppData => IsSystem ? ProgramDataDir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SQLBackupTool");
    public static string ConfigFile => Path.Combine(AppData, "config.json");
    public static string SystemConfigFile => Path.Combine(ProgramDataDir, "Service", "config.json");
    public static string LogDir => Path.Combine(AppData, "logs");
    public static string SystemLogDir => Path.Combine(ProgramDataDir, "logs");
    // Staging must be writable by the SQL Server service account (BACKUP TO DISK runs as that account),
    // therefore it lives in ProgramData; setup.ps1 grants ACLs to NT Service\MSSQL$<instance>.
    public static string DefaultStaging => Path.Combine(ProgramDataDir, "Staging");
    public static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
}

/// <summary>Scheduled-task run modes.</summary>
public enum TaskMode { Interactive = 0, NoLogon = 1, SystemAccount = 2 }
public enum DestKind { GoogleDrive = 0, LocalOrNetwork = 1 }

public class AppConfig
{
    public string InstanceName { get; set; } = @".\SQLEXPRESS";
    public string DriveFolder { get; set; } = "";
    public DestKind Destination { get; set; } = DestKind.GoogleDrive;
    public string LocalFolder { get; set; } = "";
    public TaskMode Mode { get; set; } = TaskMode.Interactive;
    public string StagingFolder { get; set; } = Paths.DefaultStaging;
    public int RetentionDays { get; set; } = 7;
    public string BackupTime { get; set; } = "02:00";
    public bool ScheduleEnabled { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = false;
    public bool SetupCompleted { get; set; } = false;
    public Dictionary<string, DbStatus> LastResults { get; set; } = new();
    public bool EncryptEnabled { get; set; } = false;
    /// <summary>Zip password, DPAPI protected (scope = SecretScope), base64. Never plaintext.</summary>
    public string EncryptedPassword { get; set; } = "";
    public bool UseSqlAuth { get; set; } = false;
    public string SqlUser { get; set; } = "";
    public string SqlPasswordEnc { get; set; } = "";
    /// <summary>"CurrentUser" or "LocalMachine" — scope the stored secrets are currently encrypted with.</summary>
    public string SecretScope { get; set; } = "CurrentUser";
    public DateTime? LastJobTime { get; set; }
    public bool? LastJobOk { get; set; }
    public string LastJobSummary { get; set; } = "";

    // ---- E-posta bildirimi (isteğe bağlı; basit-ayarlar.json → Eposta) ----
    public bool MailEnabled { get; set; } = false;
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;
    public string MailFrom { get; set; } = "";
    public string MailTo { get; set; } = "";
    public string SmtpUser { get; set; } = "";
    /// <summary>SMTP password, DPAPI protected. Never plaintext in config/logs.</summary>
    public string SmtpPasswordEnc { get; set; } = "";
    public bool MailOnSuccess { get; set; } = true;
    public bool MailOnFailure { get; set; } = true;

    static System.Security.Cryptography.DataProtectionScope ScopeOf(string s) =>
        s == "LocalMachine" ? System.Security.Cryptography.DataProtectionScope.LocalMachine : System.Security.Cryptography.DataProtectionScope.CurrentUser;
    public string RequiredScope => Mode == TaskMode.SystemAccount ? "LocalMachine" : "CurrentUser";

    string Protect(string? plain) => string.IsNullOrEmpty(plain) ? "" :
        Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, ScopeOf(SecretScope)));

    string? Unprotect(string enc, string what)
    {
        if (string.IsNullOrEmpty(enc)) return null;
        try { return Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(enc), null, ScopeOf(SecretScope))); }
        catch (Exception ex) { Logger.Error($"{what} çözülemedi (farklı Windows kullanıcısı/hesap?): {ex.Message}"); return null; }
    }

    public void SetPassword(string? plain) => EncryptedPassword = Protect(plain);
    public string? GetPassword() => Unprotect(EncryptedPassword, "Şifreleme parolası");
    public void SetSqlPassword(string? plain) => SqlPasswordEnc = Protect(plain);
    public string? GetSqlPassword() => Unprotect(SqlPasswordEnc, "SQL oturum parolası");
    public void SetSmtpPassword(string? plain) => SmtpPasswordEnc = Protect(plain);
    public string? GetSmtpPassword() => Unprotect(SmtpPasswordEnc, "SMTP parolası");

    /// <summary>Re-encrypt stored secrets when the task mode requires a different DPAPI scope.</summary>
    public void EnsureSecretScope()
    {
        if (SecretScope == RequiredScope) return;
        var zip = GetPassword(); var sql = GetSqlPassword(); var smtp = GetSmtpPassword();
        SecretScope = RequiredScope;
        EncryptedPassword = Protect(zip);
        SqlPasswordEnc = Protect(sql);
        SmtpPasswordEnc = Protect(smtp);
        Logger.Info($"Kayıtlı parolalar yeni DPAPI kapsamıyla yeniden şifrelendi: {SecretScope}");
    }

    public string ConnectionString
    {
        get
        {
            var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = InstanceName, InitialCatalog = "master", TrustServerCertificate = true,
                ConnectTimeout = 15, ApplicationName = "SQL Yedekleme Araci"
            };
            if (UseSqlAuth && !string.IsNullOrWhiteSpace(SqlUser)) { b.UserID = SqlUser; b.Password = GetSqlPassword() ?? ""; }
            else b.IntegratedSecurity = true;
            return b.ConnectionString;
        }
    }

    /// <summary>Root folder of the selected destination (Drive "My Drive" or local/UNC path).</summary>
    public string DestRoot => Destination == DestKind.LocalOrNetwork ? LocalFolder : DriveFolder;
    public string DestLabel => Destination == DestKind.LocalOrNetwork ? "Yerel/Ağ klasörü" : "Google Drive";
    public string DriveTargetFolder => Path.Combine(DestRoot, "SQLBackup");

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            var f = File.Exists(Paths.ConfigFile) ? Paths.ConfigFile : Paths.IsSystem && File.Exists(Paths.SystemConfigFile) ? Paths.SystemConfigFile : null;
            if (f != null)
            {
                var c = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(f));
                if (c != null) { c.Normalize(); return c; }
            }
        }
        catch (Exception ex) { Logger.Warn("Ayar dosyası okunamadı, varsayılanlar kullanılıyor: " + ex.Message); }
        var d = new AppConfig();
        d.DriveFolder = DriveDetector.Detect() ?? "";
        var inst = InstanceDetector.Detect();
        if (inst.Count > 0 && !inst.Contains(@".\SQLEXPRESS", StringComparer.OrdinalIgnoreCase)) d.InstanceName = inst[0];
        // Windows Server: yerel klasör varsayılanı (Google Drive oturumu gerektirmez).
        if (OsInfo.IsServer)
        {
            d.Destination = DestKind.LocalOrNetwork;
            if (string.IsNullOrWhiteSpace(d.LocalFolder)) d.LocalFolder = @"C:\SQLBackup";
        }
        return d;
    }

    void Normalize()
    {
        if (string.IsNullOrWhiteSpace(InstanceName)) InstanceName = @".\SQLEXPRESS";
        if (RetentionDays < 1 || RetentionDays > 365) RetentionDays = 7;
        if (!TimeSpan.TryParse(BackupTime, out _)) BackupTime = "02:00";
        if (string.IsNullOrWhiteSpace(StagingFolder)) StagingFolder = Paths.DefaultStaging;
        if (!PathSafety.IsSafeStagingPath(StagingFolder)) StagingFolder = Paths.DefaultStaging;
        if (SecretScope != "LocalMachine") SecretScope = "CurrentUser";
        if (SmtpPort < 1 || SmtpPort > 65535) SmtpPort = 587;
        LastResults ??= new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Paths.AppData);
        var json = JsonSerializer.Serialize(this, Opts);
        var tmp = Paths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, Paths.ConfigFile, true);
        try { PathSafety.HardenUserFileAcl(Paths.ConfigFile); } catch { /* non-fatal */ }
        // Mode c: SYSTEM reads a copy from ProgramData\SQLBackupTool\Service (ACL: user + SYSTEM + Administrators).
        if (Mode == TaskMode.SystemAccount && !Paths.IsSystem)
        {
            try
            {
                var dir = Path.GetDirectoryName(Paths.SystemConfigFile)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Paths.SystemConfigFile, json);
            }
            catch (Exception ex) { Logger.Warn("SYSTEM için ayar kopyası yazılamadı (yönetici gerekebilir): " + ex.Message); }
        }
    }

    /// <summary>Apply core end-user settings. Destination is set separately via <see cref="ApplyDestination"/>.</summary>
    public void ApplySimple(string instance, string backupFolder, int retention, string time)
    {
        if (!string.IsNullOrWhiteSpace(instance)) InstanceName = instance.Trim();
        // backupFolder here is only the local/UNC fallback path — does not force destination kind.
        if (!string.IsNullOrWhiteSpace(backupFolder))
            LocalFolder = backupFolder.Trim().TrimEnd('\\', '/');
        if (retention >= 1 && retention <= 365) RetentionDays = retention;
        if (TimeSpan.TryParse(time, out _)) BackupTime = time.Trim();
        Normalize();
    }

    /// <summary>
    /// Destination: Google Drive (sync client "My Drive" folder) or local/UNC.
    /// Architecture stays SQL BACKUP → staging .bak → ZIP → destination\SQLBackup (Drive sync uploads cloud-side).
    /// Empty driveFolderPath triggers DriveDetector when kind is GoogleDrive.
    /// </summary>
    public string? ApplyDestination(string? kind, string? driveFolderPath, string? localFolderPath)
    {
        var k = (kind ?? "").Trim();
        bool wantDrive = k.Equals("GoogleDrive", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Drive", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Google Drive", StringComparison.OrdinalIgnoreCase)
            || k == "0";
        bool wantLocal = k.Equals("Yerel", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Local", StringComparison.OrdinalIgnoreCase)
            || k.Equals("LocalOrNetwork", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Ag", StringComparison.OrdinalIgnoreCase)
            || k.Equals("UNC", StringComparison.OrdinalIgnoreCase)
            || k == "1";

        if (wantLocal || (!wantDrive && !string.IsNullOrWhiteSpace(localFolderPath) && string.IsNullOrWhiteSpace(k)))
        {
            Destination = DestKind.LocalOrNetwork;
            if (!string.IsNullOrWhiteSpace(localFolderPath))
                LocalFolder = localFolderPath.Trim().TrimEnd('\\', '/');
            if (string.IsNullOrWhiteSpace(LocalFolder)) LocalFolder = @"C:\SQLBackup";
            return null;
        }

        // Default / explicit Google Drive
        Destination = DestKind.GoogleDrive;
        if (!string.IsNullOrWhiteSpace(driveFolderPath))
            DriveFolder = driveFolderPath.Trim().TrimEnd('\\', '/');
        else if (string.IsNullOrWhiteSpace(DriveFolder) || !Directory.Exists(DriveFolder))
            DriveFolder = DriveDetector.Detect() ?? DriveFolder;
        if (string.IsNullOrWhiteSpace(DriveFolder) || !Directory.Exists(DriveFolder))
            return "Google Drive klasörü bulunamadı. Google Drive for Desktop kurup kişisel hesapla giriş yapın, veya 'GoogleDriveKlasoru' yolunu yazın (ör. G:\\My Drive).";
        return null;
    }

    /// <summary>Optional mail block from basit-ayarlar.json. Empty SmtpParola keeps the previously stored DPAPI secret.</summary>
    public void ApplyMailSimple(
        bool? enabled, string? host, int? port, bool? ssl,
        string? from, string? to, string? user, string? passwordPlain,
        bool? onSuccess, bool? onFailure)
    {
        if (enabled.HasValue) MailEnabled = enabled.Value;
        if (host != null) SmtpHost = host.Trim();
        if (port is >= 1 and <= 65535) SmtpPort = port.Value;
        if (ssl.HasValue) SmtpSsl = ssl.Value;
        if (from != null) MailFrom = from.Trim();
        if (to != null) MailTo = to.Trim();
        if (user != null) SmtpUser = user.Trim();
        if (!string.IsNullOrEmpty(passwordPlain)) SetSmtpPassword(passwordPlain);
        if (onSuccess.HasValue) MailOnSuccess = onSuccess.Value;
        if (onFailure.HasValue) MailOnFailure = onFailure.Value;
        Normalize();
    }
}

/// <summary>Detects locally installed SQL Server instances (registry + services).</summary>
public static class InstanceDetector
{
    static string Map(string name) => name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "(local)" : @".\" + name;

    public static List<string> Detect()
    {
        var set = new List<string>();
        void Add(string n) { var m = Map(n); if (!set.Contains(m, StringComparer.OrdinalIgnoreCase)) set.Add(m); }
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var k = hk.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
                if (k != null) foreach (var v in k.GetValueNames()) Add(v);
            }
            catch { }
        }
        try
        {
            foreach (var s in System.ServiceProcess.ServiceController.GetServices())
            {
                if (s.ServiceName.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)) Add("MSSQLSERVER");
                else if (s.ServiceName.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase)) Add(s.ServiceName[6..]);
                s.Dispose();
            }
        }
        catch { }
        // Prefer .\SQLEXPRESS first
        set.Sort((a, b) => (b.Equals(@".\SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ? 1 : 0) - (a.Equals(@".\SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ? 1 : 0));
        return set;
    }

    /// <summary>Fill an editable ComboBox with detected instances, keeping current value.</summary>
    public static void Fill(ComboBox cb, string current)
    {
        cb.Items.Clear();
        foreach (var i in Detect()) cb.Items.Add(i);
        cb.Text = !string.IsNullOrWhiteSpace(current) ? current : cb.Items.Count > 0 ? (string)cb.Items[0]! : @".\SQLEXPRESS";
    }
}

public static class OsInfo
{
    public static bool IsServer
    {
        get
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                return (k?.GetValue("InstallationType") as string)?.Contains("Server", StringComparison.OrdinalIgnoreCase) == true
                    || (k?.GetValue("ProductName") as string)?.Contains("Server", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch { return false; }
        }
    }
    public static bool IsAdmin
    {
        get
        {
            try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }
    public const string DriveServerNote = "Sunucu notu: Google Drive for Desktop Windows Server 2016+ destekler, WebView2 gerektirir, UNC/ağ yollarında çalışmaz; uygulama kişisel hesapla girişli olmalı ve tercihen etkileşimli oturumda çalışmalıdır.";
}

/// <summary>Path / SQL literal safety helpers.</summary>
public static class PathSafety
{
    // Allow Windows drive / UNC paths with common safe characters only (blocks ; ' " and control chars).
    static readonly System.Text.RegularExpressions.Regex StagingRx = new(
        @"^(?:[A-Za-z]:|\\\\[^\\/:*?""<>|\r\n]+\\[^\\/:*?""<>|\r\n]+)(?:\\[^<>:""|?*\r\n';']+)+$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex DbNameRx = new(
        @"^[\w.\- ]{1,128}$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsSafeStagingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path.Trim().TrimEnd('\\'));
            if (full.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            if (full.Contains('\'', StringComparison.Ordinal) || full.Contains(';', StringComparison.Ordinal)) return false;
            if (Path.GetPathRoot(full)?.TrimEnd('\\') == full.TrimEnd('\\')) return false; // drive root forbidden
            return StagingRx.IsMatch(full) || full.StartsWith(Paths.DefaultStaging, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SQLBackupTool"), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool IsSafeDbName(string? name) =>
        !string.IsNullOrEmpty(name) && DbNameRx.IsMatch(name) && name.IndexOf('\'') < 0;

    /// <summary>Ensure bakPath resolves under expectedRoot (blocks path traversal from SQL result).</summary>
    public static bool IsUnderRoot(string bakPath, string expectedRoot)
    {
        try
        {
            var full = Path.GetFullPath(bakPath);
            var root = Path.GetFullPath(expectedRoot).TrimEnd('\\') + "\\";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void HardenUserFileAcl(string file)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(file)) return;
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var psi = new ProcessStartInfo("icacls.exe", $"\"{file}\" /inheritance:r /grant:r \"{user}:F\" /grant:r \"*S-1-5-18:F\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(15000);
    }

    /// <summary>Restrict a newly created backup destination folder to the current user + SYSTEM (+ Administrators).</summary>
    public static void HardenBackupFolderAcl(string sqlBackupDir, DestKind? kind = null)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(sqlBackupDir)) return;
        // Skip UNC shares — ACL model differs; leave share permissions to the admin.
        if (sqlBackupDir.StartsWith(@"\\", StringComparison.Ordinal)) return;
        // Never lock down Google Drive sync folders — Drive for Desktop needs its own ACLs / virtual FS.
        if (kind == DestKind.GoogleDrive || LooksLikeGoogleDrivePath(sqlBackupDir)) return;
        try
        {
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var args = $"\"{sqlBackupDir}\" /inheritance:r /grant:r \"{user}:(OI)(CI)F\" /grant:r \"*S-1-5-18:(OI)(CI)F\" /grant:r \"*S-1-5-32-544:(OI)(CI)M\"";
            var psi = new ProcessStartInfo("icacls.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit(20000);
            if (p.ExitCode == 0) Logger.Info("Yedek klasörü ACL sıkılaştırıldı (kullanıcı + SYSTEM + Administrators): " + sqlBackupDir);
            else Logger.Warn("Yedek klasörü ACL ayarlanamadı; NTFS izinlerini elle kontrol edin.");
        }
        catch (Exception ex) { Logger.Warn("Yedek klasörü ACL: " + ex.Message); }
    }

    public static bool LooksLikeGoogleDrivePath(string path)
    {
        try
        {
            var p = path.Replace('/', '\\');
            return p.Contains(@"\My Drive", StringComparison.OrdinalIgnoreCase)
                || p.Contains(@"\Drive'ım", StringComparison.OrdinalIgnoreCase)
                || p.Contains(@"\Google Drive", StringComparison.OrdinalIgnoreCase)
                || p.Contains("GoogleDrive", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

/// <summary>Write-test a destination folder (create+delete a tiny file).</summary>
public static class DestCheck
{
    public static string? WriteTest(string root)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root)) return "Klasör yolu boş.";
            if (!Directory.Exists(root)) return "Klasör bulunamadı veya ağ paylaşımına erişilemiyor: " + root;
            var dir = Path.Combine(root, "SQLBackup");
            Directory.CreateDirectory(dir);
            var t = Path.Combine(dir, $".yazma_testi_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(t, "ok");
            File.Delete(t);
            // ACL harden only for local disks — not Drive sync roots (caller may pass DestKind).
            PathSafety.HardenBackupFolderAcl(dir, PathSafety.LooksLikeGoogleDrivePath(root) ? DestKind.GoogleDrive : DestKind.LocalOrNetwork);
            return null;
        }
        catch (Exception ex) { return "Klasöre yazılamıyor: " + ex.Message; }
    }
}

public class DbStatus
{
    public DateTime Time { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}

public static class Logger
{
    static readonly object Lock = new();
    public static event Action<string>? LineWritten;
    public static string TodayFile => Path.Combine(Paths.LogDir, $"SQLBackup_{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string m) => Write("BILGI", m);
    public static void Warn(string m) => Write("UYARI", m);
    public static void Error(string m) => Write("HATA", m);
    public static void Success(string m) => Write("BASARI", m);

    static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}";
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Paths.LogDir);
                File.AppendAllText(TodayFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* logging must never crash the app */ }
        LineWritten?.Invoke(line);
    }

    public static void Cleanup(int keepDays = 30)
    {
        try
        {
            foreach (var f in Directory.GetFiles(Paths.LogDir, "SQLBackup_*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-keepDays)) File.Delete(f);
        }
        catch { }
    }
}

public static class Staging
{
    /// <summary>Create staging folder and restrict ACL to current user + SYSTEM (+ SQL service account, which
    /// must write the .bak — without it BACKUP TO DISK fails). Non-fatal on failure.</summary>
    public static void Ensure(AppConfig cfg)
    {
        try
        {
            bool created = !Directory.Exists(cfg.StagingFolder);
            Directory.CreateDirectory(cfg.StagingFolder);
            if (!created && File.Exists(Path.Combine(cfg.StagingFolder, ".acl_ok"))) return;
            var inst = cfg.InstanceName.Contains('\\') ? cfg.InstanceName[(cfg.InstanceName.LastIndexOf('\\') + 1)..] : "MSSQLSERVER";
            var svc = inst.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "NT Service\\MSSQLSERVER" : $"NT Service\\MSSQL${inst}";
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var args = $"\"{cfg.StagingFolder}\" /inheritance:r /grant:r \"{user}:(OI)(CI)F\" /grant:r \"*S-1-5-18:(OI)(CI)F\" /grant:r \"{svc}:(OI)(CI)M\"";
            var psi = new ProcessStartInfo("icacls.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            if (p.ExitCode == 0) { File.WriteAllText(Path.Combine(cfg.StagingFolder, ".acl_ok"), DateTime.Now.ToString("s")); Logger.Info("Geçici klasör izinleri kısıtlandı (kullanıcı + SYSTEM + SQL hizmeti)."); }
            else Logger.Warn($"Geçici klasör izinleri ayarlanamadı (kod {p.ExitCode}): {o.Trim()} — setup.bat'i tekrar çalıştırın.");
        }
        catch (Exception ex) { Logger.Warn("Geçici klasör hazırlanamadı: " + ex.Message); }
    }
}

public enum HealthLevel { Ok, Warn, Fail }
public record HealthItem(string Name, HealthLevel Level, string Message, string Fix);

public static class HealthCheck
{
    public static string Mark(HealthLevel l) => l switch { HealthLevel.Ok => "✓", HealthLevel.Warn => "⚠", _ => "✗" };
    public static Color ColorOf(HealthLevel l) => l switch { HealthLevel.Ok => Color.ForestGreen, HealthLevel.Warn => Color.DarkOrange, _ => Color.Firebrick };

    public static HealthItem Sql(AppConfig cfg)
    {
        try
        {
            var dbs = new BackupEngine(cfg).ListUserDatabases();
            var ok = dbs.Count(d => d.Eligible);
            return ok > 0 ? new("SQL bağlantısı", HealthLevel.Ok, $"Bağlandı, {ok} veritabanı bulundu", "")
                          : new("SQL bağlantısı", HealthLevel.Warn, "Bağlandı ama yedeklenecek veritabanı yok", "SQL Server'da kendi veritabanınız olduğundan emin olun.");
        }
        catch (Exception ex) { var f = BackupEngine.Friendly(ex); return new("SQL bağlantısı", HealthLevel.Fail, f.Text, f.Action); }
    }

    public static HealthItem Drive(AppConfig cfg)
    {
        var name = cfg.DestLabel;
        var err = DestCheck.WriteTest(cfg.DestRoot);
        if (err == null) return new(name, HealthLevel.Ok, "Klasör var ve yazılabiliyor: " + cfg.DriveTargetFolder, "");
        var fix = cfg.Destination == DestKind.GoogleDrive
            ? "Google Drive uygulaması kurulu değil veya giriş yapılmamış. Kurup giriş yapın, sonra klasörü seçin."
            : "Yolun doğru olduğunu, ağ paylaşımına erişildiğini ve yazma izni olduğunu kontrol edin.";
        return new(name, HealthLevel.Fail, err, fix);
    }

    public static HealthItem Task_()
    {
        var s = Scheduler.Status();
        return s.StartsWith("Görev kayıtlı") ? new("Zamanlanmış görev", HealthLevel.Ok, s, "")
            : new("Zamanlanmış görev", HealthLevel.Warn, "Otomatik yedekleme kayıtlı değil", "Ayarlar'da 'Zamanlanmış görevi kaydet' düğmesine basın.");
    }

    public static HealthItem StagingItem(AppConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(cfg.StagingFolder);
            var t = Path.Combine(cfg.StagingFolder, $".test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(t, "ok"); File.Delete(t);
            return new("Geçici klasör", HealthLevel.Ok, cfg.StagingFolder, "");
        }
        catch (Exception ex) { return new("Geçici klasör", HealthLevel.Fail, ex.Message, "setup.bat dosyasını tekrar çalıştırın (izinleri düzeltir)."); }
    }

    public static HealthItem LastBackup(AppConfig cfg) =>
        cfg.LastJobTime == null ? new("Son yedek", HealthLevel.Warn, "Henüz yedek alınmadı", "'Şimdi Güvenli Yedek Al' ile bir deneme yapın.")
        : cfg.LastJobOk == true && cfg.LastJobTime > DateTime.Now.AddHours(-36) ? new("Son yedek", HealthLevel.Ok, $"{cfg.LastJobTime:dd.MM.yyyy HH:mm} başarılı", "")
        : cfg.LastJobOk == true ? new("Son yedek", HealthLevel.Warn, $"{cfg.LastJobTime:dd.MM.yyyy HH:mm} (eski)", "Bilgisayar 02:00'de açık mıydı? Elle yedek alın.")
        : new("Son yedek", HealthLevel.Fail, $"{cfg.LastJobTime:dd.MM.yyyy HH:mm} başarısız", "Günlükler sekmesindeki kırmızı satırlara bakın.");

    public static List<HealthItem> RunAll(AppConfig cfg) => new() { Sql(cfg), Drive(cfg), Task_(), StagingItem(cfg) };

    public static string Format(IEnumerable<HealthItem> items) => string.Join(Environment.NewLine,
        items.Select(i => $"{Mark(i.Level)} {i.Name}: {i.Message}" + (i.Level != HealthLevel.Ok && i.Fix.Length > 0 ? $"  → {i.Fix}" : "")));
}

public static class DriveDetector
{
    /// <summary>Find the Google Drive for Desktop "My Drive" folder.</summary>
    public static string? Detect()
    {
        var candidates = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Mirror mode / legacy Backup&Sync
        candidates.Add(Path.Combine(home, "My Drive"));
        candidates.Add(Path.Combine(home, "Google Drive", "My Drive"));
        candidates.Add(Path.Combine(home, "Google Drive"));
        candidates.Add(Path.Combine(home, "Drive'ım"));
        // Stream mode: virtual drive letter (default G:)
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Google\DriveFS");
            if (key?.GetValue("DefaultMountPoint") is string mp && mp.Length > 0)
            {
                var root = mp.Length == 1 ? mp + @":\" : mp;
                candidates.Add(Path.Combine(root, "My Drive"));
                candidates.Add(Path.Combine(root, "Drive'ım"));
            }
        }
        catch { }
        foreach (var di in DriveInfo.GetDrives())
        {
            try
            {
                if (!di.IsReady) continue;
                if (di.VolumeLabel.Contains("Google Drive", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(Path.Combine(di.RootDirectory.FullName, "My Drive"));
                    candidates.Add(Path.Combine(di.RootDirectory.FullName, "Drive'ım"));
                }
            }
            catch { }
        }
        candidates.Add(@"G:\My Drive");
        candidates.Add(@"G:\Drive'ım");
        return candidates.FirstOrDefault(Directory.Exists);
    }
}

public static class Scheduler
{
    public const string TaskName = "SQLBackupTool_Gunluk";

    static (int code, string output) Run(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(30000);
        return (p.ExitCode, o.Trim());
    }

    /// <summary>Run schtasks elevated via UAC (no output capture possible). Returns exit code or -1 if UAC declined.</summary>
    static (int code, string output) RunElevated(string args)
    {
        if (OsInfo.IsAdmin) return Run(args);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })!;
            p.WaitForExit(60000);
            return (p.ExitCode, p.ExitCode == 0 ? "Başarılı (yönetici onayıyla)." : $"schtasks çıkış kodu {p.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, "Yönetici onayı verilmedi (UAC iptal edildi)."); }
    }

    static string Q(string s) => s.Replace("\"", "\\\"");

    public static string CreateCommand(string time) =>
        $"/Create /TN \"{TaskName}\" /TR \"\\\"{Paths.ExePath}\\\" --backup\" /SC DAILY /ST {time} /F";

    public static (bool ok, string msg) Register(string time) => Register(time, TaskMode.Interactive, null);

    /// <param name="windowsPassword">Only for NoLogon mode; passed to Task Scheduler via a short-lived ACL-locked temp file (never on schtasks argv / never persisted by the app).</param>
    public static (bool ok, string msg) Register(string time, TaskMode mode, string? windowsPassword)
    {
        var t = TimeSpan.Parse(time);
        var hhmm = $"{t.Hours:00}:{t.Minutes:00}";
        var baseCmd = CreateCommand(hhmm);
        (int code, string o) r;
        switch (mode)
        {
            case TaskMode.NoLogon:
                if (string.IsNullOrEmpty(windowsPassword)) return (false, "Windows oturum parolası gerekli.");
                r = RegisterNoLogonSafe(hhmm, windowsPassword);
                break;
            case TaskMode.SystemAccount:
                r = RunElevated(baseCmd + " /RU SYSTEM /RL HIGHEST");
                break;
            default:
                r = Run(baseCmd);
                break;
        }
        Logger.Info($"Zamanlanmış görev kaydı ({time}, mod {mode}) sonuç kodu {r.code}: {r.o}"); // password never logged
        return (r.code == 0, r.o);
    }

    /// <summary>
    /// Avoid putting the Windows password on the schtasks command line (visible in process lists).
    /// Writes password to a user-only temp file, elevates PowerShell Register-ScheduledTask, then deletes the file.
    /// </summary>
    static (int code, string output) RegisterNoLogonSafe(string hhmm, string windowsPassword)
    {
        string? pwdFile = null;
        string? ps1 = null;
        try
        {
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var exe = Paths.ExePath;
            pwdFile = Path.Combine(Path.GetTempPath(), $"sqlbackup_task_{Guid.NewGuid():N}.pwd");
            ps1 = Path.Combine(Path.GetTempPath(), $"sqlbackup_task_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(pwdFile, windowsPassword, Encoding.UTF8);
            windowsPassword = ""; // drop local copy ASAP
            PathSafety.HardenUserFileAcl(pwdFile);

            // Script reads+deletes the password file immediately; never Write-Host / never leaves file on disk.
            var script = $@"
$ErrorActionPreference = 'Stop'
$pwdFile = '{pwdFile.Replace("'", "''")}'
$tn = '{TaskName.Replace("'", "''")}'
$user = '{user.Replace("'", "''")}'
$exe = '{exe.Replace("'", "''")}'
$time = '{hhmm}'
$plain = $null
try {{
  $plain = [IO.File]::ReadAllText($pwdFile)
  Remove-Item -LiteralPath $pwdFile -Force -ErrorAction SilentlyContinue
  Unregister-ScheduledTask -TaskName $tn -Confirm:$false -ErrorAction SilentlyContinue
  $action = New-ScheduledTaskAction -Execute $exe -Argument '--backup'
  $trigger = New-ScheduledTaskTrigger -Daily -At $time
  $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
  Register-ScheduledTask -TaskName $tn -Action $action -Trigger $trigger -User $user -Password $plain -Settings $settings -RunLevel Limited | Out-Null
  Write-Output 'OK'
  exit 0
}} catch {{
  Write-Output $_.Exception.Message
  exit 1
}} finally {{
  $plain = $null
  Remove-Item -LiteralPath $pwdFile -Force -ErrorAction SilentlyContinue
}}
";
            File.WriteAllText(ps1, script, Encoding.UTF8);
            PathSafety.HardenUserFileAcl(ps1);

            if (OsInfo.IsAdmin)
            {
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using var p = Process.Start(psi)!;
                var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(60000);
                return (p.ExitCode, o.Trim());
            }
            try
            {
                using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\"")
                {
                    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
                })!;
                p.WaitForExit(90000);
                return (p.ExitCode, p.ExitCode == 0 ? "Başarılı (yönetici onayıyla, parola komut satırında yok)." : $"PowerShell görev kaydı çıkış kodu {p.ExitCode}");
            }
            catch (System.ComponentModel.Win32Exception) { return (-1, "Yönetici onayı verilmedi (UAC iptal edildi)."); }
        }
        catch (Exception ex) { return (1, "Görev kaydı başarısız: " + ex.Message); }
        finally
        {
            try { if (pwdFile != null && File.Exists(pwdFile)) File.Delete(pwdFile); } catch { }
            try { if (ps1 != null && File.Exists(ps1)) File.Delete(ps1); } catch { }
        }
    }

    public static (bool ok, string msg) Unregister()
    {
        var (code, o) = Run($"/Delete /TN \"{TaskName}\" /F");
        if (code != 0 && (o.Contains("Access", StringComparison.OrdinalIgnoreCase) || o.Contains("Erişim", StringComparison.OrdinalIgnoreCase)))
            (code, o) = RunElevated($"/Delete /TN \"{TaskName}\" /F");
        Logger.Info($"Zamanlanmış görev silme sonuç kodu {code}: {o}");
        return (code == 0, o);
    }

    /// <summary>Turkish status: state, last run + result (0 = başarılı), next run.</summary>
    public static string Status()
    {
        try
        {
            var (code, o) = Run($"/Query /TN \"{TaskName}\" /V /FO LIST");
            if (code != 0) return "Zamanlanmış görev kayıtlı değil.";
            string? next = null, status = null, last = null, result = null, runAs = null;
            foreach (var raw in o.Split('\n'))
            {
                var line = raw.Trim();
                var i = line.IndexOf(':');
                if (i < 0) continue;
                var k = line[..i]; var v = line[(i + 1)..].Trim();
                bool S(params string[] keys) => keys.Any(x => k.StartsWith(x, StringComparison.OrdinalIgnoreCase));
                if (next == null && S("Next Run", "Sonraki")) next = v;
                else if (status == null && (k.Equals("Status", StringComparison.OrdinalIgnoreCase) || k.Equals("Durum", StringComparison.OrdinalIgnoreCase))) status = v;
                else if (last == null && S("Last Run", "Son Çalış", "Son Calis")) last = v;
                else if (result == null && S("Last Result", "Son Sonuç", "Son Sonuc")) result = v;
                else if (runAs == null && S("Run As User", "Farklı Kullanıcı", "Kullanıcı olarak")) runAs = v;
            }
            string res = result == null ? "?" : result == "0" ? "başarılı (0)" : result == "267011" ? "henüz çalışmadı" : $"HATA ({result})";
            return $"Görev kayıtlı. Durum: {status ?? "?"} | Son çalıştırma: {last ?? "?"} → {res} | Sonraki: {next ?? "?"}" + (runAs != null ? $" | Hesap: {runAs}" : "");
        }
        catch (Exception ex) { return "Görev durumu okunamadı: " + ex.Message; }
    }
}

/// <summary>Best-effort Windows Event Log writer (source created by setup.ps1, elevated).</summary>
public static class EventLogWriter
{
    public const string Source = "SQLYedeklemeAraci";
    public static void Write(bool ok, string message)
    {
        try
        {
            if (!System.Diagnostics.EventLog.SourceExists(Source)) return;
            System.Diagnostics.EventLog.WriteEntry(Source, message.Length > 30000 ? message[..30000] : message,
                ok ? EventLogEntryType.Information : EventLogEntryType.Error, ok ? 1000 : 1001);
        }
        catch { /* silently fall back to file logs */ }
    }
}
