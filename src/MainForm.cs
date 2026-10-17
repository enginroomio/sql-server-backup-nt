using System.Diagnostics;

namespace SqlBackupTool;

public class MainForm : Form
{
    AppConfig _cfg;
    readonly NotifyIcon _tray;
    bool _exiting, _running;
    readonly bool _startInTray;

    // Status strip
    readonly Label[] _ind = new Label[4];
    readonly ToolTip _tip = new();

    // Yedekleme tab
    readonly ListView _dbList = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, GridLines = true };
    readonly Button _btnBackup = new() { Text = "🛡 Şimdi Güvenli Yedek Al", Width = 240, Height = 40, Font = new Font("Segoe UI", 11f, FontStyle.Bold), BackColor = Color.SteelBlue, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly Button _btnRefresh = new() { Text = "Listeyi Yenile", AutoSize = true, Height = 40 };
    readonly ProgressBar _progress = new() { Width = 300, Height = 22 };
    readonly Label _lblCurrent = new() { AutoSize = true, Text = "Hazır.", Padding = new Padding(0, 4, 0, 0) };

    // Ayarlar tab (simple)
    readonly TextBox _txtDrive = new() { Width = 360 };
    readonly NumericUpDown _numRetention = new() { Minimum = 1, Maximum = 365, Width = 70 };
    readonly DateTimePicker _dtTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    readonly CheckBox _chkSchedule = new() { Text = "Otomatik yedekleme açık", AutoSize = true };
    readonly Label _lblTask = new() { AutoSize = true, MaximumSize = new Size(560, 0) };
    readonly TextBox _txtHealth = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 640, Height = 90 };
    // Gelişmiş
    readonly Panel _advanced = new() { AutoSize = true, Visible = false };
    readonly ComboBox _txtInstance = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDown };
    readonly ComboBox _cmbDest = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly Label _lblDestNote = new() { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Color.DimGray };
    readonly ComboBox _cmbMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    readonly CheckBox _chkSqlAuth = new() { Text = "SQL Server oturum açma bilgisi kullan (şifreli)", AutoSize = true };
    readonly TextBox _txtSqlUser = new() { Width = 160, PlaceholderText = "örn. yedek_kullanici" };
    readonly TextBox _txtSqlPwd = new() { Width = 160, UseSystemPasswordChar = true };
    string _driveVal = "", _localVal = "";
    readonly CheckBox _chkEncrypt = new() { Text = "Yedeklerimi şifrele (önerilen)", AutoSize = true };
    readonly TextBox _txtPwd = new() { Width = 200, UseSystemPasswordChar = true, PlaceholderText = "en az 6 karakter" };
    readonly CheckBox _chkShowPwd = new() { Text = "Göster", AutoSize = true };

    // Günlükler tab
    readonly RichTextBox _txtLog = new() { ReadOnly = true, Dock = DockStyle.Fill, WordWrap = false, Font = new Font("Consolas", 9f), BackColor = Color.White };
    readonly ComboBox _cmbLevel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    readonly TextBox _txtManifest = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Bottom, Height = 90, Font = new Font("Consolas", 8.5f) };
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    public MainForm(bool startInTray)
    {
        _startInTray = startInTray;
        _cfg = AppConfig.Load();
        Text = "SQL Yedekleme Aracı";
        Size = new Size(800, 620);
        MinimumSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;
        Font = new Font("Segoe UI", 9.5f);

        _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "SQL Yedekleme Aracı", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Şimdi Güvenli Yedek Al", null, (_, _) => StartBackup());
        menu.Items.Add("Ayarları Aç", null, (_, _) => { RestoreWindow(); _tabs.SelectedIndex = 1; });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Kapat", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreWindow();

        _tabs.TabPages.Add(BuildBackupTab());
        _tabs.TabPages.Add(BuildSettingsTab());
        _tabs.TabPages.Add(BuildLogTab());
        _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedIndex == 2) LoadLog(); if (_tabs.SelectedIndex == 1) RefreshTaskStatus(); };
        Controls.Add(_tabs);
        Controls.Add(BuildStatusStrip());

        LoadSettingsToUi();
        Logger.LineWritten += line => { if (IsHandleCreated) BeginInvoke(() => { if (_tabs.SelectedIndex == 2 && PassesFilter(line)) AppendColored(line); }); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        Shown += OnShown;
    }

    async void OnShown(object? s, EventArgs e)
    {
        if (_startInTray && _cfg.SetupCompleted) { Hide(); return; }
        await Task.Run(() => Staging.Ensure(_cfg));
        RefreshDbList(silent: true);
        if (!_cfg.SetupCompleted) { await Task.Delay(200); OpenWizard(); }
        else await RefreshStatusAsync();
    }

    void OpenWizard()
    {
        using var w = new FirstRunForm(AppConfig.Load());
        w.ShowDialog(this);
        _cfg = AppConfig.Load();
        LoadSettingsToUi();
        RefreshDbList(silent: true);
        _ = RefreshStatusAsync();
    }

    void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(3000, "SQL Yedekleme Aracı", "Uygulama sistem tepsisinde çalışıyor. Otomatik yedekleme uygulama kapalıyken de çalışır.", ToolTipIcon.Info);
            return;
        }
        if (_running && MessageBox.Show("Yedekleme sürüyor. Yine de çıkılsın mı?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        { e.Cancel = true; _exiting = false; return; }
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    // ---------------- Durum şeridi ----------------
    Control BuildStatusStrip()
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 4, Padding = new Padding(6, 6, 6, 2), BackColor = Color.WhiteSmoke };
        for (int i = 0; i < 4; i++)
        {
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            _ind[i] = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, BackColor = Color.Gray, Margin = new Padding(3), Font = new Font("Segoe UI", 9f, FontStyle.Bold), Text = "…" };
            p.Controls.Add(_ind[i], i, 0);
        }
        return p;
    }

    async Task RefreshStatusAsync()
    {
        var cfg = _cfg;
        var items = await Task.Run(() => new[] { HealthCheck.Sql(cfg), HealthCheck.Drive(cfg), HealthCheck.LastBackup(cfg), HealthCheck.Task_() });
        for (int i = 0; i < 4; i++)
        {
            var it = items[i];
            _ind[i].BackColor = HealthCheck.ColorOf(it.Level);
            _ind[i].Text = $"{HealthCheck.Mark(it.Level)} {it.Name}";
            _tip.SetToolTip(_ind[i], it.Message + (it.Fix.Length > 0 ? "\n→ " + it.Fix : ""));
        }
    }

    // ---------------- Yedekleme ----------------
    TabPage BuildBackupTab()
    {
        var page = new TabPage("Yedekleme");
        _dbList.Columns.Add("Veritabanı", 200);
        _dbList.Columns.Add("Durum", 120);
        _dbList.Columns.Add("Son Yedek", 130);
        _dbList.Columns.Add("Son Sonuç", 280);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(6) };
        top.Controls.AddRange(new Control[] { _btnBackup, _btnRefresh,
            new Label { Text = "Yedek alınır, sağlamlığı kontrol edilir, sonra Drive'a kopyalanır.", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(6, 12, 0, 0) } });
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6) };
        bottom.Controls.AddRange(new Control[] { _progress, _lblCurrent });
        page.Controls.Add(_dbList);
        page.Controls.Add(top);
        page.Controls.Add(bottom);
        _btnBackup.Click += (_, _) => StartBackup();
        _btnRefresh.Click += (_, _) => { RefreshDbList(false); _ = RefreshStatusAsync(); };
        return page;
    }

    void RefreshDbList(bool silent)
    {
        _dbList.Items.Clear();
        try
        {
            foreach (var d in new BackupEngine(_cfg).ListUserDatabases())
            {
                var state = d.Eligible ? "Yedeklenecek" : d.Snapshot ? "Atlanır (snapshot)" : d.ReadOnly ? "Atlanır (salt okunur)" : "Atlanır (" + d.State + ")";
                _cfg.LastResults.TryGetValue(d.Name, out var last);
                var it = new ListViewItem(new[] { d.Name, state, last?.Time.ToString("dd.MM.yyyy HH:mm") ?? "-", last == null ? "-" : $"{last.Status}: {last.Message}" });
                if (last?.Status == "HATA") it.ForeColor = Color.Firebrick;
                else if (last?.Status == "Başarılı") it.ForeColor = Color.ForestGreen;
                _dbList.Items.Add(it);
            }
            _lblCurrent.Text = $"{_dbList.Items.Count} veritabanı bulundu.";
        }
        catch (Exception ex)
        {
            var f = BackupEngine.Friendly(ex);
            _lblCurrent.Text = $"✗ {f.Text} → {f.Action}";
            if (!silent) MessageBox.Show($"{f.Text}\n\nNe yapmalı: {f.Action}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    async void StartBackup()
    {
        if (_running) return;
        _running = true;
        _btnBackup.Enabled = false;
        _progress.Value = 0;
        var engine = new BackupEngine(_cfg);
        engine.Progress += (i, n, db) => BeginInvoke(() =>
        {
            _progress.Maximum = Math.Max(n, 1);
            _progress.Value = Math.Min(i, _progress.Maximum);
            _lblCurrent.Text = db.Length > 0 ? $"Yedekleniyor ({i + 1}/{n}): {db}" : "Tamamlandı.";
        });
        try
        {
            var job = await Task.Run(() => engine.Run());
            _lblCurrent.Text = job.Summary;
            var text = job.Summary.Length > 250 ? job.Summary[..250] + "…" : job.Summary;
            _tray.ShowBalloonTip(8000, job.AllOk ? "Yedekleme tamamlandı ✓" : "Yedekleme sorunu ✗", text, job.AllOk ? ToolTipIcon.Info : ToolTipIcon.Error);
        }
        finally
        {
            _running = false;
            _btnBackup.Enabled = true;
            _cfg = AppConfig.Load();
            RefreshDbList(true);
            _ = RefreshStatusAsync();
        }
    }

    // ---------------- Ayarlar ----------------
    static Label Help(string t) => new() { Text = "ℹ " + t, AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(4, 6, 0, 0) };

    TabPage BuildSettingsTab()
    {
        var page = new TabPage("Ayarlar") { AutoScroll = true };
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10) };

        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        void Row(TableLayoutPanel tl, string label, params Control[] cs)
        {
            tl.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) });
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            f.Controls.AddRange(cs);
            tl.Controls.Add(f);
        }
        var btnBrowse = new Button { Text = "Gözat...", AutoSize = true };
        btnBrowse.Click += (_, _) => BrowseDrive(_txtDrive);
        var btnDetect = new Button { Text = "Otomatik Bul", AutoSize = true };
        btnDetect.Click += (_, _) =>
        {
            var d = DriveDetector.Detect();
            if (d != null) _txtDrive.Text = d;
            else MessageBox.Show("Google Drive klasörü bulunamadı. Google Drive for Desktop kurulu ve giriş yapılmış mı?", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        var btnSave = new Button { Text = "Ayarları Kaydet", AutoSize = true };
        btnSave.Click += (_, _) => { if (SaveUiToSettings()) { MessageBox.Show("Ayarlar kaydedildi ✓", Text); _ = RefreshStatusAsync(); } };
        var btnTask = new Button { Text = "Zamanlanmış görevi kaydet", AutoSize = true };
        btnTask.Click += (_, _) => { if (SaveUiToSettings()) { ApplySchedule(true); _ = RefreshStatusAsync(); } };
        var btnHealth = new Button { Text = "Hızlı Kontrol", AutoSize = true };
        btnHealth.Click += async (_, _) =>
        {
            _txtHealth.Text = "Kontrol ediliyor...";
            var cfg = _cfg;
            var items = await Task.Run(() => HealthCheck.RunAll(cfg));
            _txtHealth.Text = HealthCheck.Format(items);
            _ = RefreshStatusAsync();
        };

        _cmbDest.Items.AddRange(ServerUi.DestNames);
        _cmbDest.SelectedIndexChanged += (_, _) => OnDestChanged(btnDetect);
        var btnWrite = new Button { Text = "Yazma Testi", AutoSize = true };
        btnWrite.Click += (_, _) =>
        {
            var err = DestCheck.WriteTest(_txtDrive.Text.Trim());
            _txtHealth.ForeColor = err == null ? Color.ForestGreen : Color.Firebrick;
            _txtHealth.Text = err == null ? "✓ Klasör yazılabiliyor." : "✗ " + err;
        };
        Row(t, "Yedek hedefi:", _cmbDest);
        Row(t, "Hedef klasör:", _txtDrive, btnBrowse, btnDetect, btnWrite);
        Row(t, "", _lblDestNote);
        Row(t, "Yedekleri kaç gün saklayayım?", _numRetention, Help("7 — önerilen. Her veritabanı için son N yedek tutulur."));
        Row(t, "Yedekleme saati:", _dtTime, _chkSchedule, Help("Bu saatte bilgisayar açık olmalı."));
        Row(t, "", btnSave, btnTask, btnHealth);
        _cmbMode.Items.AddRange(ServerUi.ModeNames);
        var btnMode = new Button { Text = "Modu Uygula", AutoSize = true };
        btnMode.Click += (_, _) =>
        {
            if (!SaveUiToSettings()) return;
            ServerUi.ApplyMode(_cfg, (TaskMode)_cmbMode.SelectedIndex, this);
            _cfg = AppConfig.Load(); LoadSettingsToUi(); RefreshTaskStatus(); _ = RefreshStatusAsync();
        };
        Row(t, "Sunucu Modu:", _cmbMode, btnMode);
        Row(t, "Otomatik görev:", _lblTask);
        Row(t, "Kontrol sonucu:", _txtHealth);
        root.Controls.Add(t);

        root.Controls.Add(new Label { BorderStyle = BorderStyle.Fixed3D, Height = 2, Width = 720, Margin = new Padding(0, 8, 0, 4) });
        var toggle = new Button { Text = "▶ Gelişmiş Ayarlar", AutoSize = true, FlatStyle = FlatStyle.Flat };
        toggle.FlatAppearance.BorderSize = 0;
        toggle.Click += (_, _) => { _advanced.Visible = !_advanced.Visible; toggle.Text = (_advanced.Visible ? "▼" : "▶") + " Gelişmiş Ayarlar"; };
        root.Controls.Add(toggle);

        var a = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        var btnTest = new Button { Text = "Bağlantıyı Test Et", AutoSize = true };
        btnTest.Click += async (_, _) =>
        {
            _txtHealth.Text = "Bağlanılıyor...";
            var c = AppConfig.Load();
            c.InstanceName = _txtInstance.Text.Trim();
            c.UseSqlAuth = _chkSqlAuth.Checked; c.SqlUser = _txtSqlUser.Text.Trim();
            if (_txtSqlPwd.Text.Length > 0) c.SetSqlPassword(_txtSqlPwd.Text);
            var item = await Task.Run(() => HealthCheck.Sql(c));
            _txtHealth.Text = HealthCheck.Format(new[] { item });
        };
        _chkShowPwd.CheckedChanged += (_, _) => _txtPwd.UseSystemPasswordChar = !_chkShowPwd.Checked;
        _chkEncrypt.CheckedChanged += (_, _) => _txtPwd.Enabled = _chkShowPwd.Enabled = _chkEncrypt.Checked;
        var btnScript = new Button { Text = "SQL Betiğini Dışa Aktar", AutoSize = true };
        btnScript.Click += (_, _) =>
        {
            using var sfd = new SaveFileDialog { FileName = "backup.master.sql", Filter = "SQL dosyası|*.sql" };
            if (sfd.ShowDialog(this) == DialogResult.OK) File.WriteAllText(sfd.FileName, BackupEngine.LoadScript());
        };
        var wizard = new LinkLabel { Text = "Kurulum Sihirbazını Tekrar Aç", AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        wizard.LinkClicked += (_, _) => OpenWizard();

        Row(a, "Sunucu adı (örnek):", _txtInstance, btnTest, Help("Genelde .\\SQLEXPRESS — değiştirmeyin."));
        _chkSqlAuth.CheckedChanged += (_, _) => _txtSqlUser.Enabled = _txtSqlPwd.Enabled = _chkSqlAuth.Checked;
        Row(a, "SQL oturumu:", _chkSqlAuth, Help("SYSTEM modu için gerekli. sa yerine yalnızca db_backupoperator yetkili bir kullanıcı önerilir."));
        Row(a, "Kullanıcı / Parola:", _txtSqlUser, _txtSqlPwd);
        Row(a, "Şifreleme:", _chkEncrypt, Help("Bu ne işe yarar? Drive'daki yedekleri parolasız kimse açamaz (AES-256)."));
        Row(a, "Parola:", _txtPwd, _chkShowPwd, Help("Unutmayın! Parola olmadan yedek geri yüklenemez."));
        Row(a, "Doğrulama:", Help("Her yedek RESTORE VERIFYONLY ve ZIP CRC kontrolüyle doğrulanır — yedeklerin sağlam olduğunu doğrular."));
        Row(a, "", btnScript, wizard);
        _advanced.Controls.Add(a);
        root.Controls.Add(_advanced);
        page.Controls.Add(root);
        return page;
    }

    void LoadSettingsToUi()
    {
        InstanceDetector.Fill(_txtInstance, _cfg.InstanceName);
        _driveVal = _cfg.DriveFolder; _localVal = _cfg.LocalFolder;
        _cmbDest.Tag = null;
        _cmbDest.SelectedIndex = (int)_cfg.Destination;
        _cmbDest.Tag = _cmbDest.SelectedIndex;
        _txtDrive.Text = _cfg.DestRoot;
        UpdateDestNote();
        _cmbMode.SelectedIndex = (int)_cfg.Mode;
        _chkSqlAuth.Checked = _cfg.UseSqlAuth;
        _txtSqlUser.Text = _cfg.SqlUser;
        _txtSqlPwd.Text = "";
        _txtSqlPwd.PlaceholderText = string.IsNullOrEmpty(_cfg.SqlPasswordEnc) ? "parola" : "(kayıtlı)";
        _txtSqlUser.Enabled = _txtSqlPwd.Enabled = _chkSqlAuth.Checked;
        _numRetention.Value = Math.Clamp(_cfg.RetentionDays, 1, 365);
        _dtTime.Value = DateTime.Today.Add(TimeSpan.Parse(_cfg.BackupTime));
        _chkSchedule.Checked = _cfg.ScheduleEnabled;
        _chkEncrypt.Checked = _cfg.EncryptEnabled;
        _txtPwd.Text = "";
        _txtPwd.PlaceholderText = string.IsNullOrEmpty(_cfg.EncryptedPassword) ? "en az 6 karakter" : "(kayıtlı — değiştirmek için yazın)";
        _txtPwd.Enabled = _chkShowPwd.Enabled = _chkEncrypt.Checked;
    }

    bool SaveUiToSettings()
    {
        var destErr = DestCheck.WriteTest(_txtDrive.Text.Trim());
        if (destErr != null)
        {
            _txtHealth.ForeColor = Color.Firebrick; _txtHealth.Text = "✗ " + destErr;
            MessageBox.Show("Hedef klasör kullanılamıyor:\n" + destErr + "\n\nLütfen 'Gözat' ile yazılabilir bir klasör seçin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        var cfg = AppConfig.Load();
        if (_chkSqlAuth.Checked)
        {
            if (_txtSqlUser.Text.Trim().Length == 0 || (_txtSqlPwd.Text.Length == 0 && string.IsNullOrEmpty(cfg.SqlPasswordEnc)))
            { MessageBox.Show("SQL oturumu için kullanıcı adı ve parola girin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            if (_txtSqlPwd.Text.Length > 0) cfg.SetSqlPassword(_txtSqlPwd.Text);
        }
        cfg.UseSqlAuth = _chkSqlAuth.Checked;
        cfg.SqlUser = _txtSqlUser.Text.Trim();
        cfg.Destination = (DestKind)Math.Max(0, _cmbDest.SelectedIndex);
        if (cfg.Destination == DestKind.LocalOrNetwork) cfg.LocalFolder = _txtDrive.Text.Trim(); else cfg.DriveFolder = _txtDrive.Text.Trim();
        if (_chkEncrypt.Checked)
        {
            if (_txtPwd.Text.Length > 0 && _txtPwd.Text.Length < 6)
            { MessageBox.Show("Parola en az 6 karakter olmalı.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            if (_txtPwd.Text.Length == 0 && string.IsNullOrEmpty(cfg.EncryptedPassword))
            { MessageBox.Show("Şifreleme için bir parola girin (en az 6 karakter).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            if (_txtPwd.Text.Length >= 6) cfg.SetPassword(_txtPwd.Text);
        }
        cfg.EncryptEnabled = _chkEncrypt.Checked;
        cfg.InstanceName = _txtInstance.Text.Trim();
        cfg.RetentionDays = (int)_numRetention.Value;
        cfg.BackupTime = _dtTime.Value.ToString("HH:mm");
        cfg.ScheduleEnabled = _chkSchedule.Checked;
        cfg.Save();
        _cfg = cfg;
        LoadSettingsToUi();
        Logger.Info($"Ayarlar kaydedildi (şifreleme: {(cfg.EncryptEnabled ? "açık" : "kapalı")}).");
        return true;
    }

    void OnDestChanged(Button detect)
    {
        // remember the path typed for the previous destination
        if (_cmbDest.Tag is int prev) { if (prev == 1) _localVal = _txtDrive.Text; else _driveVal = _txtDrive.Text; }
        _cmbDest.Tag = _cmbDest.SelectedIndex;
        _txtDrive.Text = _cmbDest.SelectedIndex == 1 ? _localVal : _driveVal;
        detect.Visible = _cmbDest.SelectedIndex == 0;
        UpdateDestNote();
    }

    void UpdateDestNote() => _lblDestNote.Text = _cmbDest.SelectedIndex == 1
        ? "ℹ Yerel disk veya ağ paylaşımı (\\\\sunucu\\paylasim\\...). Yedekler <klasör>\\SQLBackup içine yazılır. Etkileşimli Drive oturumu olmayan sunucular için önerilir."
        : "ℹ " + (OsInfo.IsServer ? OsInfo.DriveServerNote : "Google Drive for Desktop kurulu ve kişisel hesapla girişli olmalı.");

    void ApplySchedule(bool showMsg)
    {
        if (_cfg.ScheduleEnabled && _cfg.Mode != TaskMode.Interactive) { ServerUi.ApplyMode(_cfg, _cfg.Mode, this); RefreshTaskStatus(); return; }
        var (ok, msg) = _cfg.ScheduleEnabled ? Scheduler.Register(_cfg.BackupTime) : Scheduler.Unregister();
        RefreshTaskStatus();
        if (showMsg)
            MessageBox.Show(ok ? (_cfg.ScheduleEnabled ? $"Her gün {_cfg.BackupTime}'de otomatik yedek alınacak ✓" : "Otomatik yedekleme kapatıldı.")
                               : "İşlem başarısız:\n" + msg, Text, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }

    void RefreshTaskStatus() => _lblTask.Text = Scheduler.Status();

    internal static void BrowseDrive(TextBox target)
    {
        using var fbd = new FolderBrowserDialog { Description = "Yedek hedef klasörünü seçin (Google Drive 'Drive'ım' veya yerel/ağ klasörü)", UseDescriptionForTitle = true };
        if (Directory.Exists(target.Text)) fbd.SelectedPath = target.Text;
        if (fbd.ShowDialog() == DialogResult.OK) target.Text = fbd.SelectedPath;
    }

    // ---------------- Günlükler ----------------
    TabPage BuildLogTab()
    {
        var page = new TabPage("Günlükler");
        _cmbLevel.Items.AddRange(new object[] { "Tümü", "Başarılar", "Uyarılar", "Hatalar", "Bilgi" });
        _cmbLevel.SelectedIndex = 0;
        _cmbLevel.SelectedIndexChanged += (_, _) => LoadLog();
        var btnReload = new Button { Text = "Yenile", AutoSize = true };
        btnReload.Click += (_, _) => LoadLog();
        var btnOpen = new Button { Text = "Günlük Klasörünü Aç", AutoSize = true };
        btnOpen.Click += (_, _) =>
        {
            Directory.CreateDirectory(Paths.LogDir);
            Process.Start(new ProcessStartInfo(Paths.LogDir) { UseShellExecute = true });
        };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6) };
        top.Controls.AddRange(new Control[] { new Label { Text = "Göster:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _cmbLevel, btnReload, btnOpen,
            new Label { Text = "kırmızı=hata, turuncu=uyarı, yeşil=başarı", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(6, 6, 0, 0) } });
        page.Controls.Add(_txtLog);
        page.Controls.Add(new Label { Text = "Doğrulanmış yedek listesi (manifest.csv, son satırlar):", Dock = DockStyle.Bottom, Height = 20 });
        page.Controls.Add(_txtManifest);
        page.Controls.Add(top);
        return page;
    }

    static readonly string[] LevelTags = { "", "[BASARI]", "[UYARI]", "[HATA]", "[BILGI]" };
    bool PassesFilter(string line) => _cmbLevel.SelectedIndex <= 0 || line.Contains(LevelTags[_cmbLevel.SelectedIndex]);

    void AppendColored(string line)
    {
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.SelectionColor = line.Contains("[HATA]") ? Color.Firebrick : line.Contains("[UYARI]") ? Color.DarkOrange
                               : line.Contains("[BASARI]") ? Color.ForestGreen : Color.Black;
        _txtLog.AppendText(line + "\n");
        _txtLog.ScrollToCaret();
    }

    void LoadLog()
    {
        _txtLog.Clear();
        try
        {
            var files = Directory.Exists(Paths.LogDir) ? Directory.GetFiles(Paths.LogDir, "SQLBackup_*.log").OrderByDescending(f => f).Take(3).Reverse() : Enumerable.Empty<string>();
            var lines = new List<string>();
            foreach (var f in files) lines.AddRange(ReadShared(f).Where(PassesFilter));
            _txtLog.SuspendLayout();
            foreach (var l in lines.TakeLast(2000)) AppendColored(l);
            _txtLog.ResumeLayout();
        }
        catch (Exception ex) { _txtLog.Text = "Günlük okunamadı: " + ex.Message; }
        try
        {
            var m = Path.Combine(_cfg.StagingFolder, "manifest.csv");
            _txtManifest.Text = File.Exists(m) ? string.Join(Environment.NewLine, ReadShared(m).TakeLast(15)) : "(henüz doğrulanmış yedek yok)";
        }
        catch (Exception ex) { _txtManifest.Text = "manifest.csv okunamadı: " + ex.Message; }
    }

    static List<string> ReadShared(string f)
    {
        using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var r = new StreamReader(fs);
        var list = new List<string>(); string? l;
        while ((l = r.ReadLine()) != null) list.Add(l);
        return list;
    }
}
