namespace SqlBackupTool;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Any(a => a.Equals("--backup", StringComparison.OrdinalIgnoreCase)))
            return RunHeadless();
        var applyIdx = Array.FindIndex(args, a => a.Equals("--apply-basit", StringComparison.OrdinalIgnoreCase));
        if (applyIdx >= 0)
            return ApplyBasit(applyIdx + 1 < args.Length ? args[applyIdx + 1] : "basit-ayarlar.json");

        using var mutex = new Mutex(true, @"Local\SQLYedeklemeAraci_UI", out bool first);
        if (!first)
        {
            MessageBox.Show("SQL Yedekleme Aracı zaten çalışıyor (sistem tepsisini kontrol edin).", "SQL Yedekleme Aracı",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        Application.Run(new MainForm(args.Contains("--tray")));
        return 0;
    }

    /// <summary>Headless: merge basit-ayarlar.json (4 fields) into config and register daily task (interactive mode).</summary>
    static int ApplyBasit(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("basit ayar dosyası bulunamadı: " + path);
                return 2;
            }
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            string Get(params string[] keys)
            {
                foreach (var k in keys)
                    if (r.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                        return v.GetString() ?? "";
                return "";
            }
            int GetInt(params string[] keys)
            {
                foreach (var k in keys)
                    if (r.TryGetProperty(k, out var v) && v.TryGetInt32(out var n)) return n;
                return 7;
            }
            var cfg = AppConfig.Load();
            // Aze-like flat fields (preferred)
            var sqlServer = Get("SqlSunucu", "Sunucu", "Server", "InstanceName");
            var saat = Get("Saat", "BackupTime", "Time");
            cfg.ApplySimple(sqlServer,
                Get("YedekKlasoru", "BackupFolder", "LocalFolder"),
                GetInt("SaklamaAdedi", "Retention", "RetentionDays"),
                string.IsNullOrWhiteSpace(saat) ? "02:00" : saat);

            var driveMail = Get("DriveMail", "GoogleMail", "Email");
            var driveSifre = Get("DriveSifre", "DrivePassword", "UygulamaSifresi");
            if (!string.IsNullOrWhiteSpace(driveMail)) cfg.DriveMail = driveMail;
            if (!string.IsNullOrEmpty(driveSifre)) cfg.SetDriveAppPassword(driveSifre);
            var sqlUser = Get("SqlKullanici", "SqlUser", "User");
            var sqlPwd = Get("SqlSifre", "SqlPassword", "Password");
            if (!string.IsNullOrWhiteSpace(sqlUser))
            {
                cfg.UseSqlAuth = true;
                cfg.SqlUser = sqlUser;
                if (!string.IsNullOrEmpty(sqlPwd)) cfg.SetSqlPassword(sqlPwd);
            }
            if (r.TryGetProperty("WindowsIleBaslat", out var sw) && sw.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                cfg.StartWithWindows = sw.GetBoolean();

            // Hedef: GoogleDrive | GoogleDriveApi | Yerel
            var hedef = Get("Hedef", "Destination", "Dest");
            if (string.IsNullOrWhiteSpace(hedef))
            {
                var hasApi = !string.IsNullOrWhiteSpace(Get("AnahtarDosyasi", "DriveApiKeyPath", "KeyFile"))
                             || !string.IsNullOrWhiteSpace(Get("KlasorId", "DriveApiFolderId", "FolderId"))
                             || cfg.HasDriveUserOAuth;
                hedef = hasApi ? "GoogleDriveApi" : "GoogleDrive";
            }
            var destErr = cfg.ApplyDestination(
                hedef,
                Get("GoogleDriveKlasoru", "DriveFolder", "DriveKlasoru"),
                Get("YedekKlasoru", "BackupFolder", "LocalFolder"),
                Get("AnahtarDosyasi", "DriveApiKeyPath", "KeyFile"),
                Get("KlasorId", "DriveApiFolderId", "FolderId"),
                Get("Impersonate", "DriveApiImpersonate", "Email"));
            if (destErr != null) { Console.Error.WriteLine(destErr); return 1; }
            ApplyMailFromBasit(cfg, r);
            if (!string.IsNullOrWhiteSpace(cfg.DriveMail)) cfg.ApplyAzeMailFromDrive();
            Directory.CreateDirectory(cfg.DestRoot);
            if (cfg.Destination != DestKind.GoogleDriveApi)
            {
                var err = DestCheck.WriteTest(cfg.DestRoot);
                if (err != null) { Console.Error.WriteLine(err); return 1; }
            }
            else Directory.CreateDirectory(cfg.DriveTargetFolder);
            cfg.SetupCompleted = true;
            cfg.Save();
            StartupHelper.Apply(cfg.StartWithWindows);
            if (cfg.ScheduleEnabled)
            {
                var (ok, msg) = Scheduler.Register(cfg.BackupTime, TaskMode.Interactive, null);
                Console.WriteLine(ok ? $"Ayarlar kaydedildi. Görev: her gün {cfg.BackupTime}" : "Ayarlar kaydedildi; görev: " + msg);
            }
            else Console.WriteLine("Ayarlar kaydedildi (zamanlama kapalı).");
            Console.WriteLine(cfg.Destination switch
            {
                DestKind.GoogleDriveApi => $"Hedef: Google Drive API (kaynak: SQL BACKUP)",
                DestKind.GoogleDrive => $"Hedef: Google Drive senkron → {cfg.DriveTargetFolder}",
                _ => $"Hedef: Yerel/Ağ → {cfg.DriveTargetFolder}"
            });
            if (cfg.MailEnabled)
                Console.WriteLine(MailNotifier.IsConfigured(cfg)
                    ? $"Onay maili → {cfg.MailTo}"
                    : "E-posta yapılandırması eksik.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    static void ApplyMailFromBasit(AppConfig cfg, System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("Eposta", out var mail) && !root.TryGetProperty("Mail", out mail))
            return;
        static string S(System.Text.Json.JsonElement e, params string[] keys)
        {
            foreach (var k in keys)
                if (e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                    return v.GetString() ?? "";
            return "";
        }
        static bool? B(System.Text.Json.JsonElement e, params string[] keys)
        {
            foreach (var k in keys)
                if (e.TryGetProperty(k, out var v))
                {
                    if (v.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                    if (v.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                }
            return null;
        }
        static int? I(System.Text.Json.JsonElement e, params string[] keys)
        {
            foreach (var k in keys)
                if (e.TryGetProperty(k, out var v) && v.TryGetInt32(out var n)) return n;
            return null;
        }
        cfg.ApplyMailSimple(
            B(mail, "Acik", "Enabled"),
            S(mail, "SmtpSunucu", "SmtpHost", "Host"),
            I(mail, "SmtpPort", "Port"),
            B(mail, "Ssl", "SmtpSsl"),
            S(mail, "Kimden", "From", "MailFrom"),
            S(mail, "Kime", "To", "MailTo"),
            S(mail, "SmtpKullanici", "SmtpUser", "User"),
            S(mail, "SmtpParola", "SmtpPassword", "Password"),
            B(mail, "BasaridaGonder", "MailOnSuccess", "OnSuccess"),
            B(mail, "HatadaGonder", "MailOnFailure", "OnFailure"));
    }

    static int RunHeadless()
    {
        using var mutex = new Mutex(true, @"Local\SQLYedeklemeAraci_Job", out bool first);
        if (!first) { Logger.Warn("Başka bir yedekleme işi zaten çalışıyor; bu çalıştırma atlandı."); return 3; }
        try
        {
            Logger.Info("Zamanlanmış/komut satırı yedeklemesi başlatıldı (--backup).");
            var cfg = AppConfig.Load();
            var job = new BackupEngine(cfg).Run(); // includes best-effort MailNotifier
            EventLogWriter.Write(job.AllOk, job.AllOk
                ? $"SQL yedekleme başarılı: {job.Ok} veritabanı. Hedef: {cfg.DriveTargetFolder}"
                : "SQL yedekleme BAŞARISIZ. " + job.Summary + Environment.NewLine +
                  string.Join(Environment.NewLine, job.Items.Where(i => !i.Success && !i.Skipped).Select(i => $"- {i.Db}: {i.Message}")));
            ShowBalloon(job.AllOk ? "Yedekleme tamamlandı" : "Yedekleme HATASI", job.Summary,
                job.AllOk ? ToolTipIcon.Info : ToolTipIcon.Error);
            return job.AllOk ? 0 : 1;
        }
        catch (Exception ex)
        {
            Logger.Error("Beklenmeyen hata: " + ex);
            EventLogWriter.Write(false, "SQL yedekleme beklenmeyen hata: " + ex.Message);
            try
            {
                var cfg = AppConfig.Load();
                var fail = new JobResult();
                fail.Items.Add(new DbResult("(genel)", false, false, ex.Message, null));
                MailNotifier.NotifyBackup(cfg, fail);
            }
            catch { /* mail must never mask primary error */ }
            ShowBalloon("Yedekleme HATASI", ex.Message, ToolTipIcon.Error);
            return 2;
        }
    }

    static void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        if (Paths.IsSystem || !Environment.UserInteractive) return; // no desktop in SYSTEM/no-logon runs
        try
        {
            using var ni = new NotifyIcon { Icon = SystemIcons.Application, Visible = true, Text = "SQL Yedekleme Aracı" };
            ni.ShowBalloonTip(10000, title, text.Length > 250 ? text[..250] + "…" : text, icon);
            var until = DateTime.Now.AddSeconds(8);
            while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(100); }
            ni.Visible = false;
        }
        catch (Exception ex) { Logger.Warn("Bildirim gösterilemedi: " + ex.Message); }
    }
}
