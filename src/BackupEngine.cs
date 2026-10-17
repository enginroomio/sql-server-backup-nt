using System.Reflection;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Data.SqlClient;

namespace SqlBackupTool;

public record DbInfo(string Name, string State, bool ReadOnly, bool Snapshot)
{
    public bool Eligible => State == "ONLINE" && !ReadOnly && !Snapshot;
}

public record DbResult(string Db, bool Success, bool Skipped, string Message, string? ZipPath);
public record FriendlyError(string Text, string Action);

public class JobResult
{
    public List<DbResult> Items { get; } = new();
    public int Ok => Items.Count(i => i.Success);
    public int Failed => Items.Count(i => !i.Success && !i.Skipped);
    public int Skipped => Items.Count(i => i.Skipped);
    public bool AllOk => Failed == 0 && Ok > 0;
    public string Summary => Failed == 0 && Ok > 0
        ? $"Yedekleme başarılı: {Ok} veritabanı güvenle yedeklendi" + (Skipped > 0 ? $", {Skipped} atlandı." : ".")
        : Ok + Failed == 0 ? "Yedeklenecek veritabanı bulunamadı."
        : $"Yedeklemede sorun: {Failed} başarısız, {Ok} başarılı. " +
          string.Join(" | ", Items.Where(i => !i.Success && !i.Skipped).Select(i => $"{i.Db}: {i.Message}"));
}

public class BackupEngine
{
    readonly AppConfig _cfg;
    public event Action<int, int, string>? Progress; // index, total, db name

    public BackupEngine(AppConfig cfg) => _cfg = cfg;

    public static string LoadScript()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("backup.master.sql")
                      ?? throw new InvalidOperationException("Gömülü backup.master.sql bulunamadı.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    // ---------- Friendly error mapping ----------
    public static FriendlyError Friendly(Exception ex)
    {
        int code = ex is SqlException se ? se.Number : 0;
        var msg = ex.Message;
        if (code == 0) { var m = Regex.Match(msg, @"Hata (\d+):"); if (m.Success) code = int.Parse(m.Groups[1].Value); }
        return code switch
        {
            18456 => new("SQL Server oturum açmayı reddetti.", "Windows kullanıcınıza SQL Server'da yetki verin (KURULUM_AYARLAR.md → Gerekli İzinler)."),
            4060 => new("Veritabanı açılamadı.", "Veritabanının çevrimiçi olduğunu ve erişim izniniz olduğunu kontrol edin."),
            2 or 53 or -1 or 26 => new("SQL Server'a ulaşılamadı.", "SQL Server (SQLEXPRESS) hizmetinin çalıştığını ve sunucu adının doğru olduğunu kontrol edin."),
            229 or 262 or 916 => new("Bu işlem için yetkiniz yok.", "Kullanıcınıza sysadmin veya db_backupoperator rolü verin."),
            3201 or 3013 when msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || msg.Contains("Erişim", StringComparison.OrdinalIgnoreCase)
                => new("SQL Server geçici klasöre yazamadı.", "setup.bat'i tekrar çalıştırın (klasör izinlerini düzeltir)."),
            112 or 3202 => new("Diskte yer kalmadı.", "C: sürücüsünde ve Google Drive'da yer açın."),
            1844 => new("Bu SQL sürümü sıkıştırmayı desteklemiyor.", "Otomatik olarak sıkıştırmasız yedeğe geçildi; işlem gerekmez."),
            _ when ex is IOException && (msg.Contains("disk", StringComparison.OrdinalIgnoreCase) || msg.Contains("space", StringComparison.OrdinalIgnoreCase))
                => new("Diskte yer kalmadı.", "C: sürücüsünde ve Google Drive'da yer açın."),
            _ when ex is UnauthorizedAccessException => new("Klasöre erişim izni yok.", "Google Drive klasörünü ve geçici klasör izinlerini kontrol edin."),
            _ when ex is DirectoryNotFoundException => new("Klasör bulunamadı.", "Ayarlar'dan Google Drive klasörünü yeniden seçin."),
            _ => new(msg, "Günlük kaydını inceleyin; sorun sürerse 'Hızlı Kontrol' çalıştırın.")
        };
    }

    static string FriendlyLine(Exception ex) { var f = Friendly(ex); return $"{f.Text} Öneri: {f.Action}"; }

    public List<DbInfo> ListUserDatabases()
    {
        var list = new List<DbInfo>();
        using var con = new SqlConnection(_cfg.ConnectionString);
        con.Open();
        using var cmd = new SqlCommand(
            "SELECT name, state_desc, is_read_only, CAST(CASE WHEN source_database_id IS NULL THEN 0 ELSE 1 END AS bit) " +
            "FROM sys.databases WHERE database_id > 4 ORDER BY name", con);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add(new DbInfo(rd.GetString(0), rd.GetString(1), rd.GetBoolean(2), rd.GetBoolean(3)));
        return list;
    }

    public string CheckPermissions()
    {
        using var con = new SqlConnection(_cfg.ConnectionString);
        con.Open();
        using var cmd = new SqlCommand("SELECT SUSER_SNAME(), IS_SRVROLEMEMBER('sysadmin'), CAST(SERVERPROPERTY('Edition') AS nvarchar(200))", con);
        using var rd = cmd.ExecuteReader();
        rd.Read();
        var sa = rd.IsDBNull(1) ? 0 : rd.GetInt32(1);
        return $"Oturum: {rd.GetString(0)} | sysadmin: {(sa == 1 ? "EVET" : "HAYIR (her veritabanında db_backupoperator gerekir)")} | Sürüm: {rd.GetString(2)}";
    }

    public JobResult Run(CancellationToken ct = default)
    {
        var job = new JobResult();
        Logger.Info($"=== Yedekleme başladı. Sunucu: {_cfg.InstanceName} | Hedef: {_cfg.DestLabel} ({_cfg.DriveTargetFolder}) | Hesap: {Environment.UserName} | Şifreleme: {(_cfg.EncryptEnabled ? "AÇIK (AES-256)" : "kapalı")} ===");
        try
        {
            if (_cfg.RequiresDriveApiUpload)
            {
                Directory.CreateDirectory(_cfg.DestRoot);
                if (string.IsNullOrWhiteSpace(_cfg.DriveApiFolderId))
                    throw new InvalidOperationException("Google Drive API: KlasorId eksik (basit-ayarlar.json).");
                if (string.IsNullOrEmpty(_cfg.GetDriveApiKeyJson())
                    && (string.IsNullOrWhiteSpace(_cfg.DriveApiKeyPath) || !File.Exists(_cfg.DriveApiKeyPath)))
                    throw new InvalidOperationException("Google Drive API: AnahtarDosyasi eksik veya okunamadı.");
            }
            else if (string.IsNullOrWhiteSpace(_cfg.DestRoot) || !Directory.Exists(_cfg.DestRoot))
                throw new DirectoryNotFoundException($"{_cfg.DestLabel} klasörü bulunamadı: '{_cfg.DestRoot}'.");
            if (_cfg.UseSqlAuth && string.IsNullOrEmpty(_cfg.GetSqlPassword()))
                throw new InvalidOperationException("SQL oturum parolası okunamadı. Gelişmiş Ayarlar'dan parolayı yeniden girin.");
            string? pwd = null;
            if (_cfg.EncryptEnabled)
            {
                pwd = _cfg.GetPassword();
                if (string.IsNullOrEmpty(pwd) || pwd.Length < 6)
                    throw new InvalidOperationException("Şifreleme açık ama parola okunamadı. Ayarlar → Gelişmiş Ayarlar'dan parolayı yeniden girin.");
            }
            if (!PathSafety.IsSafeStagingPath(_cfg.StagingFolder))
                throw new InvalidOperationException("Geçici (staging) klasör yolu güvenli değil. Varsayılanı kullanın: " + Paths.DefaultStaging);
            Staging.Ensure(_cfg);
            Directory.CreateDirectory(_cfg.DriveTargetFolder);
            // SQL BACKUP → staging → ZIP → local hedef; GoogleDriveApi ise ardından API upload (SQL asla atlanmaz).
            PathSafety.HardenBackupFolderAcl(_cfg.DriveTargetFolder, _cfg.Destination);

            var dbs = ListUserDatabases();
            foreach (var s in dbs.Where(d => !d.Eligible))
            {
                var why = s.Snapshot ? "snapshot" : s.ReadOnly ? "salt okunur" : "durum: " + s.State;
                Logger.Warn($"[{s.Name}] atlandı ({why}).");
                job.Items.Add(new DbResult(s.Name, false, true, "Atlandı: " + why, null));
            }
            var targets = dbs.Where(d => d.Eligible).ToList();
            Logger.Info($"{targets.Count} kullanıcı veritabanı yedeklenecek: {string.Join(", ", targets.Select(t => t.Name))}");
            var script = LoadScript();

            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var db = targets[i].Name;
                if (!PathSafety.IsSafeDbName(db))
                {
                    Logger.Warn($"[{db}] atlandı (güvenlik: veritabanı adı geçersiz karakter içeriyor).");
                    job.Items.Add(new DbResult(db, false, true, "Atlandı: güvenli olmayan veritabanı adı", null));
                    continue;
                }
                Progress?.Invoke(i, targets.Count, db);
                var r = BackupOne(script, db, pwd);
                job.Items.Add(r);
                _cfg.LastResults[db] = new DbStatus { Time = DateTime.Now, Status = r.Success ? "Başarılı" : "HATA", Message = r.Message };
                if (r.Success) Cleanup(db); // only after a new verified backup exists
            }
            Progress?.Invoke(targets.Count, targets.Count, "");
        }
        catch (Exception ex)
        {
            var line = FriendlyLine(ex);
            Logger.Error("Yedekleme işi başlatılamadı: " + line + " (Ayrıntı: " + ex.Message + ")");
            job.Items.Add(new DbResult("(genel)", false, false, line, null));
        }
        _cfg.LastJobTime = DateTime.Now;
        _cfg.LastJobOk = job.AllOk;
        _cfg.LastJobSummary = job.Summary;
        try { _cfg.Save(); } catch (Exception ex) { Logger.Warn("Ayarlar kaydedilemedi: " + ex.Message); }
        (job.AllOk ? (Action<string>)Logger.Success : Logger.Error)("=== " + job.Summary + " ===");
        // Best-effort mail: never alters job result / exit code (scheduled, --backup, Test Yedeği).
        MailNotifier.NotifyBackup(_cfg, job);
        Logger.Cleanup();
        return job;
    }

    static string Lit(string v) => v.Replace("'", "''");
    public static string SafeName(string db) => string.Concat(db.Select(c => "\\/:*?\"<>|".Contains(c) ? '_' : c));

    DbResult BackupOne(string script, string db, string? pwd)
    {
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string? bak = null, zipPath = null;
        try
        {
            Logger.Info($"[{db}] yedekleniyor (sağlamlık kontrolüyle)...");
            if (!PathSafety.IsSafeStagingPath(_cfg.StagingFolder))
                throw new InvalidOperationException("Güvenli olmayan staging yolu.");
            if (!PathSafety.IsSafeDbName(db))
                throw new InvalidOperationException("Güvenli olmayan veritabanı adı.");
            // Quote-escape only; paths/names also validated above to block '; etc. breakouts.
            var sql = script.Replace("$(StagingDir)", Lit(_cfg.StagingFolder))
                            .Replace("$(Timestamp)", ts)
                            .Replace("$(OnlyDb)", Lit(db));
            string status = "HATA", msg = "Betik sonuç döndürmedi.";
            using (var con = new SqlConnection(_cfg.ConnectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(sql, con) { CommandTimeout = 0 };
                using var rd = cmd.ExecuteReader();
                do
                {
                    while (rd.Read())
                    {
                        if (rd.FieldCount < 4 || rd.GetString(0) != db) continue;
                        bak = rd.IsDBNull(1) ? null : rd.GetString(1);
                        status = rd.GetString(2);
                        msg = rd.IsDBNull(3) ? "" : rd.GetString(3);
                    }
                } while (rd.NextResult());
            }
            if (status == "ATLANDI") { Logger.Warn($"[{db}] {msg}"); return new DbResult(db, false, true, msg, null); }
            if (status != "BASARILI" || bak == null) throw new Exception(msg);
            if (!PathSafety.IsUnderRoot(bak, _cfg.StagingFolder))
                throw new InvalidOperationException("Yedek dosya yolu staging klasörü dışında (reddedildi): " + bak);
            if (!File.Exists(bak)) throw new FileNotFoundException($"Yedek dosyasına erişilemiyor: {bak}. Geçici klasör izinlerini kontrol edin.");
            var bakLen = new FileInfo(bak).Length;
            var bakLeaf = Path.GetFileName(bak);
            // Reject unexpected leaf names (must match our SafeName + timestamp pattern).
            var expectedLeaf = $"{SafeName(db)}_{ts}.bak";
            if (!string.Equals(bakLeaf, expectedLeaf, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Beklenmeyen yedek dosya adı: {bakLeaf}");
            Logger.Info($"[{db}] SQL yedeği doğrulandı (RESTORE VERIFYONLY): {bakLeaf} ({bakLen / 1048576.0:F1} MB)");

            // ZIP name from SafeName(db)+timestamp only — never trust raw bak path leaf blindly for traversal.
            zipPath = Path.Combine(_cfg.DriveTargetFolder, $"{SafeName(db)}_{ts}.zip");
            var tmp = zipPath + ".partial";
            if (File.Exists(tmp)) File.Delete(tmp);
            var entryName = expectedLeaf;
            long crc = WriteZip(bak, tmp, entryName, pwd);

            // Verify BEFORE moving into place and BEFORE deleting the .bak
            string? verr = VerifyZip(tmp, entryName, bakLen, crc, pwd);
            if (verr != null)
            {
                TryDelete(tmp);
                Logger.Error($"[{db}] ZIP doğrulaması BAŞARISIZ: {verr}. Bozuk zip silindi, .bak KORUNDU: {bak}");
                return new DbResult(db, false, false, "ZIP doğrulanamadı; yerel yedek korundu: " + bak, null);
            }
            File.Move(tmp, zipPath, true);
            var zlen = new FileInfo(zipPath).Length;
            if (zlen <= 0)
            {
                Logger.Error($"[{db}] ZIP boş görünüyor: {zipPath}. .bak KORUNDU: {bak}");
                return new DbResult(db, false, false, "ZIP boş; yerel yedek korundu: " + bak, null);
            }
            File.Delete(bak); // safe: zip verified
            AppendManifest(db, bakLen, Path.GetFileName(zipPath));
            Logger.Success($"[{db}] SQL+ZIP tamam: {zipPath} ({zlen / 1048576.0:F1} MB{(pwd != null ? ", AES-256 şifreli" : "")}).");

            // Headless Drive API: local ZIP success is not enough — upload required or job item fails.
            if (_cfg.RequiresDriveApiUpload)
            {
                try
                {
                    using var api = DriveApiClient.Create(_cfg);
                    var leaf = Path.GetFileName(zipPath);
                    api.UploadZip(zipPath, leaf);
                    api.EnforceRetention(SafeName(db), _cfg.RetentionDays);
                    Logger.Success($"[{db}] Drive API yükleme OK → klasör {_cfg.DriveApiFolderId}");
                }
                catch (Exception upEx)
                {
                    Logger.Error($"[{db}] Drive API yükleme BAŞARISIZ (yerel ZIP duruyor: {zipPath}): {upEx.Message}");
                    return new DbResult(db, false, false,
                        "SQL yedeği alındı ama Google Drive API yüklemesi başarısız: " + upEx.Message + " — yerel: " + zipPath, zipPath);
                }
            }

            return new DbResult(db, true, false,
                _cfg.RequiresDriveApiUpload
                    ? "Yedek alındı, doğrulandı ve Google Drive API'ye yüklendi."
                    : "Yedek alındı, doğrulandı ve hedefe kopyalandı.", zipPath);
        }
        catch (Exception ex)
        {
            if (zipPath != null) TryDelete(zipPath + ".partial");
            var line = FriendlyLine(ex);
            Logger.Error($"[{db}] BAŞARISIZ: {line} (Ayrıntı: {ex.Message})" + (bak != null && File.Exists(bak) ? $" — yerel yedek korundu: {bak}" : ""));
            if (ex.Message.Contains("1844")) Logger.Warn($"[{db}] Sıkıştırma desteklenmiyor; betik sıkıştırmasız yedeğe geçer.");
            return new DbResult(db, false, false, Friendly(ex).Text + " " + Friendly(ex).Action, null);
        }
    }

    static long WriteZip(string src, string dest, string entryName, string? pwd)
    {
        var crc = new Crc32();
        using var fs = File.Create(dest);
        using var zos = new ZipOutputStream(fs) { IsStreamOwner = true };
        zos.SetLevel(6);
        if (pwd != null) zos.Password = pwd;
        var entry = new ZipEntry(entryName) { DateTime = DateTime.Now, Size = new FileInfo(src).Length };
        if (pwd != null) entry.AESKeySize = 256;
        zos.PutNextEntry(entry);
        var buf = new byte[1 << 20];
        using (var inp = File.OpenRead(src))
        {
            int n;
            while ((n = inp.Read(buf, 0, buf.Length)) > 0) { crc.Update(new ArraySegment<byte>(buf, 0, n)); zos.Write(buf, 0, n); }
        }
        zos.CloseEntry();
        zos.Finish();
        return crc.Value;
    }

    /// <summary>Re-open the zip (with password if set), decompress the entry fully, compare length + CRC32.</summary>
    static string? VerifyZip(string path, string entryName, long expectedLen, long expectedCrc, string? pwd)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 0) return "zip dosyası yok veya boş";
            using var zf = new ZipFile(path);
            if (pwd != null) zf.Password = pwd;
            if (zf.Count != 1) return $"beklenmeyen giriş sayısı ({zf.Count})";
            var e = zf.GetEntry(entryName);
            if (e == null) return "giriş bulunamadı";
            if (pwd != null && !e.IsCrypted) return "giriş şifrelenmemiş";
            if (pwd == null && !zf.TestArchive(true)) return "arşiv CRC testi başarısız";
            var crc = new Crc32();
            long total = 0;
            var buf = new byte[1 << 20];
            using (var s = zf.GetInputStream(e))
            {
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) { crc.Update(new ArraySegment<byte>(buf, 0, n)); total += n; }
            }
            if (total != expectedLen) return $"boyut uyuşmuyor ({total} ≠ {expectedLen})";
            if (crc.Value != expectedCrc) return "CRC uyuşmuyor";
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    void AppendManifest(string db, long bakLen, string zipName)
    {
        try
        {
            var f = Path.Combine(_cfg.StagingFolder, "manifest.csv");
            if (!File.Exists(f)) File.WriteAllText(f, "timestamp,database,bak_bytes,zip_name,verified,encrypted" + Environment.NewLine);
            string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{Q(db)},{bakLen},{Q(zipName)},OK,{(_cfg.EncryptEnabled ? "AES256" : "no")}{Environment.NewLine}");
        }
        catch (Exception ex) { Logger.Warn("manifest.csv yazılamadı: " + ex.Message); }
    }

    static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

    /// <summary>SAFETY: deletes ONLY files named exactly "{DB}_yyyyMMdd_HHmmss.zip", ONLY directly inside
    /// the Drive "SQLBackup" folder and the staging folder (no recursion). Keeps newest N.</summary>
    void Cleanup(string db)
    {
        // Local cache / sync-folder retention (Drive API also enforces cloud retention after upload).
        var rx = new Regex("^" + Regex.Escape(SafeName(db)) + @"_\d{8}_\d{6}\.zip$", RegexOptions.IgnoreCase);
        foreach (var dirRaw in new[] { _cfg.DriveTargetFolder, _cfg.StagingFolder })
        {
            string dir;
            try { dir = Path.GetFullPath(dirRaw).TrimEnd('\\'); } catch { continue; }
            // guard: never operate on a drive root or a folder not ending with our known names
            if (!Directory.Exists(dir) || Path.GetPathRoot(dir)?.TrimEnd('\\') == dir) continue;
            var leaf = Path.GetFileName(dir);
            if (!leaf.Equals("SQLBackup", StringComparison.OrdinalIgnoreCase) && !leaf.Equals("Staging", StringComparison.OrdinalIgnoreCase))
            { Logger.Warn($"Güvenlik: '{dir}' bilinen yedek klasörü değil, temizlik atlandı."); continue; }
            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.zip", SearchOption.TopDirectoryOnly)
                    .Where(f => rx.IsMatch(Path.GetFileName(f)) &&
                                string.Equals(Path.GetDirectoryName(Path.GetFullPath(f))?.TrimEnd('\\'), dir, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) { Logger.Warn($"[{db}] {dir} listelenemedi: {ex.Message}"); continue; }
            foreach (var old in files.Skip(_cfg.RetentionDays))
            {
                try { File.Delete(old); Logger.Info($"[{db}] eski yedek silindi (saklama {_cfg.RetentionDays}): {old}"); }
                catch (Exception ex) { Logger.Error($"[{db}] eski yedek SİLİNEMEDİ: {old} — {ex.Message}"); }
            }
        }
    }
}
