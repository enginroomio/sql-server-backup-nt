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
            cfg.ApplySimple(
                Get("Sunucu", "Server", "InstanceName"),
                Get("YedekKlasoru", "BackupFolder", "LocalFolder"),
                GetInt("SaklamaAdedi", "Retention", "RetentionDays"),
                Get("Saat", "BackupTime", "Time"));
            if (string.IsNullOrWhiteSpace(cfg.LocalFolder)) cfg.LocalFolder = @"C:\SQLBackup";
            cfg.Destination = DestKind.LocalOrNetwork;
            Directory.CreateDirectory(cfg.LocalFolder);
            var err = DestCheck.WriteTest(cfg.LocalFolder);
            if (err != null) { Console.Error.WriteLine(err); return 1; }
            cfg.SetupCompleted = true;
            cfg.Save();
            if (cfg.ScheduleEnabled)
            {
                var (ok, msg) = Scheduler.Register(cfg.BackupTime, TaskMode.Interactive, null);
                Console.WriteLine(ok ? $"Ayarlar kaydedildi. Görev: her gün {cfg.BackupTime}" : "Ayarlar kaydedildi; görev: " + msg);
            }
            else Console.WriteLine("Ayarlar kaydedildi (zamanlama kapalı).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    static int RunHeadless()
    {
        using var mutex = new Mutex(true, @"Local\SQLYedeklemeAraci_Job", out bool first);
        if (!first) { Logger.Warn("Başka bir yedekleme işi zaten çalışıyor; bu çalıştırma atlandı."); return 3; }
        try
        {
            Logger.Info("Zamanlanmış/komut satırı yedeklemesi başlatıldı (--backup).");
            var cfg = AppConfig.Load();
            var job = new BackupEngine(cfg).Run();
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
