namespace SqlBackupTool;

/// <summary>
/// Aze Backup-like single screen: Drive mail+şifre, SQL, saat, test, kaydet, Windows ile başlat.
/// Drive password field = Google uygulama şifresi (SMTP onay maili). Drive API = one-time browser OAuth.
/// </summary>
public class AzeSimpleForm : Form
{
    readonly AppConfig _cfg;
    readonly TextBox _driveMail = new() { Width = 320 };
    readonly TextBox _drivePwd = new() { Width = 320, UseSystemPasswordChar = true };
    readonly Label _driveStatus = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Color.DimGray };
    readonly TextBox _sqlServer = new() { Width = 320 };
    readonly TextBox _sqlUser = new() { Width = 200 };
    readonly TextBox _sqlPwd = new() { Width = 200, UseSystemPasswordChar = true };
    readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    readonly NumericUpDown _retention = new() { Minimum = 1, Maximum = 365, Value = 7, Width = 70 };
    readonly CheckBox _chkStartup = new() { Text = "Windows açılınca uygulamayı tepside çalıştır", AutoSize = true, Checked = true };
    readonly CheckBox _chkAlwaysTask = new() { Text = "Her gün seçilen saatte otomatik yedek (Görev Zamanlayıcı)", AutoSize = true, Checked = true };
    readonly Label _msg = new() { AutoSize = true, MaximumSize = new Size(560, 0), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
    readonly ProgressBar _bar = new() { Width = 520, Height = 18, Visible = false };

    public AzeSimpleForm(AppConfig cfg)
    {
        _cfg = cfg;
        Text = "SQL Yedekleme — Basit Ayarlar (Aze tarzı)";
        Size = new Size(620, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(16)
        };

        root.Controls.Add(H("1) Google Drive"));
        root.Controls.Add(Tip("E-posta + uygulama şifresi (onay maili). Drive dosya yüklemesi için bir kez 'Google Drive'ı Bağla' (tarayıcıda Google şifrenizi yazarsınız)."));
        root.Controls.Add(L("Google e-posta:"));
        root.Controls.Add(_driveMail);
        root.Controls.Add(L("Google uygulama şifresi (onay maili için):"));
        root.Controls.Add(_drivePwd);
        root.Controls.Add(Tip("Uygulama şifresi: Google Hesabı → Güvenlik → 2 Adımlı Doğrulama → Uygulama şifreleri. Normal hesap şifresi Drive API için çalışmaz."));
        var btnBind = new Button { Text = "Google Drive'ı Bağla (bir kez)", AutoSize = true, Height = 32 };
        btnBind.Click += async (_, _) => await BindDriveAsync();
        root.Controls.Add(btnBind);
        root.Controls.Add(_driveStatus);

        root.Controls.Add(H("2) SQL Server"));
        root.Controls.Add(L("SQL sunucu adresi:"));
        root.Controls.Add(_sqlServer);
        root.Controls.Add(L("SQL kullanıcı (boş = Windows kimliği):"));
        root.Controls.Add(_sqlUser);
        root.Controls.Add(L("SQL şifre:"));
        root.Controls.Add(_sqlPwd);

        root.Controls.Add(H("3) Zamanlama"));
        root.Controls.Add(Row(L("Yedek saati:"), _time, L("  Saklama adedi:"), _retention));
        root.Controls.Add(_chkAlwaysTask);
        root.Controls.Add(_chkStartup);

        var btnTest = new Button { Text = "Test Et (bağlantı + yedek)", Width = 220, Height = 36, BackColor = Color.SteelBlue, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var btnSave = new Button { Text = "Ayarları Kaydet", Width = 160, Height = 36, BackColor = Color.ForestGreen, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnTest.Click += async (_, _) => await TestAsync();
        btnSave.Click += (_, _) => Save(close: true);
        root.Controls.Add(Row(btnTest, btnSave));
        root.Controls.Add(_bar);
        root.Controls.Add(_msg);

        Controls.Add(root);
        LoadUi();
    }

    static Label H(string t) => new() { Text = t, AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Padding = new Padding(0, 12, 0, 4) };
    static Label L(string t) => new() { Text = t, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    static Label Tip(string t) => new() { Text = t, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Color.DimGray };
    static FlowLayoutPanel Row(params Control[] cs)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        p.Controls.AddRange(cs);
        return p;
    }

    void LoadUi()
    {
        _driveMail.Text = _cfg.DriveMail;
        _drivePwd.PlaceholderText = string.IsNullOrEmpty(_cfg.DriveAppPasswordEnc) ? "uygulama şifresi" : "(kayıtlı — değiştirmek için yazın)";
        _sqlServer.Text = _cfg.InstanceName;
        _sqlUser.Text = _cfg.SqlUser;
        _sqlPwd.PlaceholderText = string.IsNullOrEmpty(_cfg.SqlPasswordEnc) ? "" : "(kayıtlı)";
        try { _time.Value = DateTime.Today.Add(TimeSpan.Parse(_cfg.BackupTime)); } catch { _time.Value = DateTime.Today.AddHours(2); }
        _retention.Value = Math.Clamp(_cfg.RetentionDays, 1, 365);
        _chkStartup.Checked = _cfg.StartWithWindows;
        _chkAlwaysTask.Checked = _cfg.ScheduleEnabled;
        RefreshDriveStatus();
    }

    void RefreshDriveStatus()
    {
        if (_cfg.HasDriveUserOAuth)
            _driveStatus.Text = "✓ Google Drive bağlı (OAuth jetonu kayıtlı — oturum kapalıyken de yükleme çalışır).";
        else if (_cfg.HasDriveServiceAccount)
            _driveStatus.Text = "✓ Service account anahtarı kayıtlı.";
        else
            _driveStatus.Text = "⚠ Drive henüz bağlı değil — 'Google Drive'ı Bağla'ya basın.";
        _driveStatus.ForeColor = (_cfg.HasDriveUserOAuth || _cfg.HasDriveServiceAccount) ? Color.ForestGreen : Color.DarkOrange;
    }

    void PullFromUi(bool requireDrivePwdIfMail)
    {
        _cfg.DriveMail = _driveMail.Text.Trim();
        if (_drivePwd.Text.Length > 0) _cfg.SetDriveAppPassword(_drivePwd.Text);
        else if (requireDrivePwdIfMail && !string.IsNullOrEmpty(_cfg.DriveMail) && string.IsNullOrEmpty(_cfg.DriveAppPasswordEnc))
            throw new InvalidOperationException("Onay maili için Google uygulama şifresini girin.");
        _cfg.InstanceName = string.IsNullOrWhiteSpace(_sqlServer.Text) ? @".\SQLEXPRESS" : _sqlServer.Text.Trim();
        if (!string.IsNullOrWhiteSpace(_sqlUser.Text))
        {
            _cfg.UseSqlAuth = true;
            _cfg.SqlUser = _sqlUser.Text.Trim();
            if (_sqlPwd.Text.Length > 0) _cfg.SetSqlPassword(_sqlPwd.Text);
            if (string.IsNullOrEmpty(_cfg.GetSqlPassword()))
                throw new InvalidOperationException("SQL kullanıcısı için şifre girin.");
        }
        else { _cfg.UseSqlAuth = false; _cfg.SqlUser = ""; }
        _cfg.BackupTime = _time.Value.ToString("HH:mm");
        _cfg.RetentionDays = (int)_retention.Value;
        _cfg.ScheduleEnabled = _chkAlwaysTask.Checked;
        _cfg.StartWithWindows = _chkStartup.Checked;
        _cfg.ApplyAzeMailFromDrive();
        // Prefer API upload when OAuth/SA present; else sync folder if available.
        if (_cfg.HasDriveUserOAuth || _cfg.HasDriveServiceAccount)
        {
            _cfg.Destination = DestKind.GoogleDriveApi;
            Directory.CreateDirectory(_cfg.ApiLocalCacheRoot);
        }
        else
        {
            _cfg.Destination = DestKind.GoogleDrive;
            if (string.IsNullOrWhiteSpace(_cfg.DriveFolder) || !Directory.Exists(_cfg.DriveFolder))
                _cfg.DriveFolder = DriveDetector.Detect() ?? "";
        }
    }

    async Task BindDriveAsync()
    {
        try
        {
            _msg.ForeColor = Color.DimGray;
            _msg.Text = "Tarayıcı açılıyor — Google hesabınızla giriş yapın…";
            _cfg.DriveMail = _driveMail.Text.Trim();
            var refresh = await DriveOAuth.AuthorizeInteractiveAsync(_cfg);
            _cfg.SetDriveRefreshToken(refresh);
            _cfg.Destination = DestKind.GoogleDriveApi;
            try
            {
                var uc = DriveOAuth.FromRefreshToken(_cfg)!;
                var svc = new Google.Apis.Drive.v3.DriveService(new Google.Apis.Services.BaseClientService.Initializer
                {
                    HttpClientInitializer = uc, ApplicationName = "SQLYedeklemeAraci"
                });
                _cfg.DriveApiFolderId = DriveApiClient.EnsureFolder(svc, "SQLBackup");
            }
            catch (Exception folderEx)
            {
                Logger.Warn("Drive SQLBackup klasörü şimdi oluşturulamadı (sonraki yedekte denenecek): " + folderEx.Message);
            }
            _cfg.Save();
            RefreshDriveStatus();
            _msg.ForeColor = Color.ForestGreen;
            _msg.Text = "✓ Google Drive bağlandı. Ayarları Kaydet'e basın.";
        }
        catch (Exception ex)
        {
            _msg.ForeColor = Color.Firebrick;
            _msg.Text = "Drive bağlantısı başarısız: " + ex.Message;
        }
    }

    async Task TestAsync()
    {
        try
        {
            PullFromUi(requireDrivePwdIfMail: false);
            _bar.Visible = true; _bar.Style = ProgressBarStyle.Marquee;
            _msg.ForeColor = Color.DimGray; _msg.Text = "Test ediliyor…";
            await Task.Run(() =>
            {
                // SQL ping
                _ = new BackupEngine(_cfg).CheckPermissions();
            });
            if (_cfg.HasDriveUserOAuth || _cfg.HasDriveServiceAccount)
            {
                await Task.Run(() =>
                {
                    using var api = DriveApiClient.Create(_cfg);
                });
            }
            var job = await Task.Run(() => new BackupEngine(_cfg).Run());
            _msg.ForeColor = job.AllOk ? Color.ForestGreen : Color.Firebrick;
            _msg.Text = (job.AllOk ? "✓ Test yedeği başarılı. " : "✗ ") + job.Summary;
        }
        catch (Exception ex)
        {
            _msg.ForeColor = Color.Firebrick;
            _msg.Text = "Test başarısız: " + ex.Message;
        }
        finally { _bar.Visible = false; _bar.Style = ProgressBarStyle.Blocks; }
    }

    void Save(bool close)
    {
        try
        {
            PullFromUi(requireDrivePwdIfMail: !string.IsNullOrWhiteSpace(_driveMail.Text));
            if (!_cfg.HasDriveUserOAuth && !_cfg.HasDriveServiceAccount
                && (string.IsNullOrWhiteSpace(_cfg.DriveFolder) || !Directory.Exists(_cfg.DriveFolder)))
            {
                MessageBox.Show(
                    "Google Drive bağlı değil ve senkron klasörü bulunamadı.\n\n'Google Drive'ı Bağla'ya basın (önerilen, oturum kapalı sunucu için),\nveya Google Drive for Desktop kurun.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _cfg.SetupCompleted = true;
            _cfg.StartMinimizedToTray = true;
            _cfg.Save();
            StartupHelper.Apply(_cfg.StartWithWindows);
            if (_cfg.ScheduleEnabled)
            {
                // Prefer NoLogon-capable interactive token for user account; Interactive works when logged on.
                // For always-on without session: user should use OAuth API + NoLogon or leave machine logged on with tray.
                var (ok, msg) = Scheduler.Register(_cfg.BackupTime, TaskMode.Interactive, null);
                if (!ok) MessageBox.Show("Zamanlanmış görev kaydı: " + msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _msg.ForeColor = Color.ForestGreen;
            _msg.Text = "✓ Ayarlar kaydedildi. Yeniden başlatmadan sonra tepsi + günlük görev çalışır.";
            if (close) DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
