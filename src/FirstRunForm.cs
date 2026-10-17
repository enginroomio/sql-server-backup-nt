using System.Diagnostics;

namespace SqlBackupTool;

/// <summary>Simple 3-step wizard: 1 Bağlantı → 2 Google Drive → 3 Bitir. Skippable, re-openable.</summary>
public class FirstRunForm : Form
{
    const string DriveUrl = "https://www.google.com/drive/download/";
    readonly AppConfig _cfg;
    readonly Panel[] _steps = new Panel[3];
    int _step;
    readonly Label _header = new() { Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI", 13f, FontStyle.Bold), Padding = new Padding(12, 8, 0, 0) };
    readonly Button _back = new() { Text = "< Geri", Width = 90, Height = 32 };
    readonly Button _next = new() { Text = "İleri >", Width = 90, Height = 32 };
    readonly Button _skip = new() { Text = "Şimdilik Atla", Width = 110, Height = 32 };

    // step 1
    readonly ComboBox _instance = new() { Width = 220, Font = new Font("Segoe UI", 10f), DropDownStyle = ComboBoxStyle.DropDown };
    readonly ComboBox _dest = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _mode = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
    string _driveVal = "", _localVal = "";
    readonly Label _connResult = new() { AutoSize = true, MaximumSize = new Size(600, 0), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    readonly ListBox _dbNames = new() { Width = 600, Height = 130 };
    int _dbCount = -1;
    // step 2
    readonly TextBox _drive = new() { Width = 420, Font = new Font("Segoe UI", 10f) };
    readonly Label _driveState = new() { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    // step 3
    readonly Label _summary = new() { AutoSize = true, MaximumSize = new Size(620, 0), Font = new Font("Segoe UI", 10f) };
    readonly ProgressBar _progress = new() { Width = 600, Height = 20 };
    readonly Label _progressText = new() { AutoSize = true, MaximumSize = new Size(620, 0) };
    readonly TextBox _health = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 600, Height = 90 };
    readonly Button _testBackup = new() { Text = "Test Yedeği Al", AutoSize = true, Height = 34 };
    readonly Button _finish = new() { Text = "Kurulumu Tamamla ✓", AutoSize = true, Height = 34, BackColor = Color.ForestGreen, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    bool _busy;

    public FirstRunForm(AppConfig cfg)
    {
        _cfg = cfg;
        Text = "SQL Yedekleme Aracı - Kurulum Sihirbazı";
        Size = new Size(680, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);

        InstanceDetector.Fill(_instance, cfg.InstanceName);
        _driveVal = Directory.Exists(cfg.DriveFolder) ? cfg.DriveFolder : DriveDetector.Detect() ?? "";
        _localVal = !string.IsNullOrWhiteSpace(cfg.LocalFolder) ? cfg.LocalFolder
            : (OsInfo.IsServer ? @"C:\SQLBackup" : "");
        _dest.Items.AddRange(ServerUi.DestNames);
        // Sunucuda yerel/ağ klasörü varsayılan (tak-çalıştır).
        var destIdx = OsInfo.IsServer && cfg.Destination == DestKind.GoogleDrive && string.IsNullOrWhiteSpace(cfg.DriveFolder)
            ? 1 : (int)cfg.Destination;
        if (OsInfo.IsServer && destIdx == 0 && string.IsNullOrWhiteSpace(_driveVal)) destIdx = 1;
        _dest.SelectedIndex = destIdx;
        _dest.Tag = _dest.SelectedIndex;
        _drive.Text = _dest.SelectedIndex == 1
            ? (!string.IsNullOrWhiteSpace(_localVal) ? _localVal : @"C:\SQLBackup")
            : _driveVal;
        if (_dest.SelectedIndex == 1 && string.IsNullOrWhiteSpace(_localVal)) _localVal = _drive.Text;
        _dest.SelectedIndexChanged += (_, _) =>
        {
            if (_dest.Tag is int prev) { if (prev == 1) _localVal = _drive.Text; else _driveVal = _drive.Text; }
            _dest.Tag = _dest.SelectedIndex;
            _drive.Text = _dest.SelectedIndex == 1
                ? (!string.IsNullOrWhiteSpace(_localVal) ? _localVal : @"C:\SQLBackup")
                : _driveVal;
        };
        _mode.Items.AddRange(ServerUi.ModeNames);
        _mode.SelectedIndex = OsInfo.IsServer && cfg.Mode == TaskMode.Interactive
            ? (int)TaskMode.NoLogon : (int)cfg.Mode;

        _steps[0] = BuildStep1(); _steps[1] = BuildStep2(); _steps[2] = BuildStep3();
        var nav = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        nav.Controls.AddRange(new Control[] { _next, _back, _skip });
        foreach (var p in _steps) Controls.Add(p);
        Controls.Add(nav);
        Controls.Add(_header);

        _back.Click += (_, _) => Go(_step - 1);
        _next.Click += (_, _) => Go(_step + 1);
        _skip.Click += (_, _) =>
        {
            if (MessageBox.Show("Sihirbazı atlamak istiyor musunuz? Daha sonra Ayarlar → Gelişmiş Ayarlar → 'Kurulum Sihirbazını Tekrar Aç' ile dönebilirsiniz.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) DialogResult = DialogResult.Cancel;
        };
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
        Go(0);
    }

    void Go(int i)
    {
        _step = Math.Clamp(i, 0, 2);
        for (int k = 0; k < 3; k++) _steps[k].Visible = k == _step;
        _header.Text = $"Adım {_step + 1}/3 — " + new[] { "Bağlantı", "Yedek hedefi", "Bitir" }[_step];
        _back.Enabled = _step > 0;
        _next.Visible = _step < 2;
        if (_step == 1) UpdateDriveState();
        if (_step == 2) UpdateSummary();
    }

    static Label Tip(string t) => new() { Text = "💡 " + t, AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Color.DimGray, Padding = new Padding(0, 4, 0, 6) };
    static Panel Page(params Control[] cs)
    {
        var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 8, 16, 8), AutoScroll = true };
        f.Controls.AddRange(cs);
        return f;
    }
    static FlowLayoutPanel Row(params Control[] cs) { var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; p.Controls.AddRange(cs); return p; }

    Panel BuildStep1()
    {
        var test = new Button { Text = "Test Et", Width = 140, Height = 36, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
        test.Click += async (_, _) =>
        {
            test.Enabled = false; _connResult.ForeColor = Color.DimGray; _connResult.Text = "Bağlanılıyor..."; _dbNames.Items.Clear();
            var c = AppConfig.Load(); c.InstanceName = _instance.Text.Trim();
            try
            {
                var dbs = await Task.Run(() => new BackupEngine(c).ListUserDatabases());
                var ok = dbs.Where(d => d.Eligible).ToList();
                _dbCount = ok.Count;
                foreach (var d in dbs) _dbNames.Items.Add(d.Eligible ? "✓ " + d.Name : $"– {d.Name} (atlanacak)");
                _connResult.ForeColor = ok.Count > 0 ? Color.ForestGreen : Color.DarkOrange;
                _connResult.Text = ok.Count > 0 ? $"{ok.Count} veritabanı bulundu ✓" : "Bağlandı ama yedeklenecek veritabanı yok ⚠";
                _cfg.InstanceName = c.InstanceName;
            }
            catch (Exception ex)
            {
                _dbCount = -1;
                var f = BackupEngine.Friendly(ex);
                _connResult.ForeColor = Color.Firebrick;
                _connResult.Text = $"✗ {f.Text}\n→ {f.Action}";
            }
            test.Enabled = true;
        };
        return Page(
            new Label { Text = "Önce bilgisayarınızdaki SQL Server'a bağlanalım.", AutoSize = true },
            Tip("SQL Server Express bilgisayarınızda kurulu olmalı. Sunucu adını genelde değiştirmeniz gerekmez."),
            Row(new Label { Text = "Sunucu adı:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, _instance, test),
            _connResult, new Label { Text = "Bulunan veritabanları:", AutoSize = true }, _dbNames);
    }

    Panel BuildStep2()
    {
        var browse = new Button { Text = "Gözat...", AutoSize = true };
        browse.Click += (_, _) => { MainForm.BrowseDrive(_drive); UpdateDriveState(); };
        var detect = new Button { Text = "Otomatik Bul", AutoSize = true };
        detect.Click += (_, _) => { var d = DriveDetector.Detect(); if (d != null) _drive.Text = d; UpdateDriveState(); };
        _drive.Leave += (_, _) => UpdateDriveState();
        var link = new LinkLabel { Text = "Google Drive for Desktop'ı indir: " + DriveUrl, AutoSize = true };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(DriveUrl) { UseShellExecute = true });
        return Page(
            new Label { Text = "Yedekler seçilen klasörün içindeki 'SQLBackup' klasörüne kaydedilecek.", AutoSize = true },
            Row(new Label { Text = "Hedef:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _dest),
            Tip("Google Drive: ücretsiz Drive for Desktop kurulu ve kişisel hesapla girişli olmalı. " +
                "Yerel/Ağ klasörü: sunucular için önerilir; \\\\sunucu\\paylasim gibi ağ yolları desteklenir."),
            OsInfo.IsServer ? Tip(OsInfo.DriveServerNote) : new Label { Height = 1 },
            link,
            Row(new Label { Text = "Klasör:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _drive),
            Row(browse, detect), _driveState);
    }

    void UpdateDriveState()
    {
        var err = DestCheck.WriteTest(_drive.Text.Trim());
        _driveState.ForeColor = err == null ? Color.ForestGreen : Color.Firebrick;
        _driveState.Text = err == null ? "✓ Klasör bulundu ve yazılabiliyor" : "✗ " + err + (_dest.SelectedIndex == 0 ? " — Drive uygulamasını kurup giriş yapın veya 'Gözat' ile seçin" : "");
    }

    Panel BuildStep3()
    {
        var check = new Button { Text = "Hızlı Kontrol", AutoSize = true, Height = 34 };
        check.Click += async (_, _) =>
        {
            if (!ApplyToCfg(false)) return;
            _health.Text = "Kontrol ediliyor...";
            var items = await Task.Run(() => HealthCheck.RunAll(_cfg));
            _health.Text = HealthCheck.Format(items);
        };
        _testBackup.Click += async (_, _) =>
        {
            if (!ApplyToCfg(true)) return;
            _busy = true; _testBackup.Enabled = _finish.Enabled = false;
            var eng = new BackupEngine(_cfg);
            eng.Progress += (i, n, db) => BeginInvoke(() =>
            {
                _progress.Maximum = Math.Max(n, 1); _progress.Value = Math.Min(i, _progress.Maximum);
                _progressText.Text = db.Length > 0 ? $"Yedekleniyor ({i + 1}/{n}): {db}" : "Tamamlandı.";
            });
            var job = await Task.Run(() => eng.Run());
            _progressText.ForeColor = job.AllOk ? Color.ForestGreen : Color.Firebrick;
            _progressText.Text = (job.AllOk ? "✓ " : "✗ ") + job.Summary;
            _busy = false; _testBackup.Enabled = _finish.Enabled = true;
        };
        _finish.Click += (_, _) =>
        {
            if (!ApplyToCfg(true)) return;
            var ok = ServerUi.ApplyMode(_cfg, (TaskMode)_mode.SelectedIndex, this);
            _cfg.SetupCompleted = true;
            _cfg.Save();
            Logger.Info("Kurulum sihirbazı tamamlandı.");
            if (!ok) MessageBox.Show("Ayarlar kaydedildi ancak otomatik görev kaydedilmedi. Ayarlar sekmesinden tekrar deneyin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.OK;
        };
        return Page(new Label { Text = "Seçimleriniz:", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) }, _summary,
            Row(new Label { Text = "Sunucu Modu:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _mode),
            Row(_testBackup, check, _finish), _progress, _progressText, _health);
    }

    void UpdateSummary()
    {
        string M(bool b) => b ? "✓" : "⚠";
        _summary.Text =
            $"{M(_dbCount >= 0)} Sunucu: {_instance.Text}\n" +
            $"{M(_dbCount > 0)} Veritabanı sayısı: {(_dbCount >= 0 ? _dbCount.ToString() : "test edilmedi (Adım 1'de 'Test Et')")}\n" +
            $"{M(Directory.Exists(_drive.Text))} {ServerUi.DestNames[Math.Max(0, _dest.SelectedIndex)]}: {_drive.Text}\\SQLBackup\n" +
            $"✓ Her gün saat {_cfg.BackupTime}'de otomatik yedek\n" +
            $"✓ Her veritabanı için son {_cfg.RetentionDays} yedek saklanır, eskiler silinir\n" +
            $"✓ Her yedek sağlamlık kontrolünden geçer\n" +
            (_cfg.EncryptEnabled ? "✓ Yedekler şifreli (AES-256)" : "• Şifreleme kapalı (Ayarlar → Gelişmiş Ayarlar'dan açabilirsiniz)");
    }

    bool ApplyToCfg(bool save)
    {
        var err = DestCheck.WriteTest(_drive.Text.Trim());
        if (err != null)
        {
            MessageBox.Show("Hedef klasör kullanılamıyor (Adım 2):\n" + err, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Go(1); return false;
        }
        _cfg.InstanceName = _instance.Text.Trim();
        _cfg.Destination = (DestKind)Math.Max(0, _dest.SelectedIndex);
        if (_cfg.Destination == DestKind.LocalOrNetwork) _cfg.LocalFolder = _drive.Text.Trim(); else _cfg.DriveFolder = _drive.Text.Trim();
        if (save) _cfg.Save();
        return true;
    }
}
