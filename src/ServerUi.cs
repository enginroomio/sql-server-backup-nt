namespace SqlBackupTool;

/// <summary>Shared server-mode helpers used by MainForm and FirstRunForm.</summary>
public static class ServerUi
{
    public static readonly string[] ModeNames =
    {
        "Oturum açıkken çalıştır (varsayılan)",
        "Oturum açılmadan çalıştır (sunucu; yönetici gerekir)",
        "SYSTEM hesabıyla çalıştır (yalnızca SQL oturum bilgisi ile)"
    };
    public static readonly string[] DestNames = { "Google Drive (senkron klasörü)", "Yerel / Ağ klasörü (Windows Server)" };

    /// <summary>Masked one-time prompt for the Windows password. Returned value is never persisted.</summary>
    public static string? AskWindowsPassword(IWin32Window? owner)
    {
        using var f = new Form
        {
            Text = "Windows oturum parolası", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(440, 150), Font = new Font("Segoe UI", 9.5f)
        };
        var lbl = new Label { Text = $"Windows oturum parolanız ({Environment.UserDomainName}\\{Environment.UserName})\n(yalnızca Görev Zamanlayıcı'ya kaydedilir, saklanmaz)", Left = 12, Top = 10, Width = 420, Height = 40 };
        var tb = new TextBox { Left = 12, Top = 56, Width = 410, UseSystemPasswordChar = true };
        var ok = new Button { Text = "Tamam", Left = 250, Top = 100, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "İptal", Left = 340, Top = 100, Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        var r = f.ShowDialog(owner);
        var pwd = tb.Text;
        tb.Text = "";
        return r == DialogResult.OK && pwd.Length > 0 ? pwd : null;
    }

    /// <summary>Validate mode prerequisites, re-encrypt secrets for the needed DPAPI scope, save config, register task.
    /// Returns true on success. Falls back to Interactive mode on user request.</summary>
    public static bool ApplyMode(AppConfig cfg, TaskMode mode, IWin32Window? owner)
    {
        const string T = "SQL Yedekleme Aracı";
        if (mode == TaskMode.SystemAccount && (!cfg.UseSqlAuth || string.IsNullOrWhiteSpace(cfg.SqlUser) || string.IsNullOrEmpty(cfg.SqlPasswordEnc)))
        {
            MessageBox.Show(owner, "SYSTEM modu yalnızca SQL Server oturum bilgisiyle çalışır.\nÖnce Gelişmiş Ayarlar'da 'SQL Server oturum açma bilgisi kullan' seçeneğini, kullanıcı ve parolayı girin.",
                T, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (mode != TaskMode.Interactive && !OsInfo.IsAdmin)
        {
            var r = MessageBox.Show(owner, "Bu mod için yönetici yetkisi gerekir. Devam ederseniz Windows yönetici onayı (UAC) isteyecek.\n\n" +
                "Evet = yönetici onayıyla devam et\nHayır = 'Oturum açıkken çalıştır' modunda kal", T, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.No) mode = TaskMode.Interactive;
        }
        string? winPwd = null;
        if (mode == TaskMode.NoLogon)
        {
            winPwd = AskWindowsPassword(owner);
            if (winPwd == null) return false;
        }
        var prevMode = cfg.Mode;
        cfg.Mode = mode;
        cfg.EnsureSecretScope();
        var (ok, msg) = Scheduler.Register(cfg.BackupTime, mode, winPwd);
        winPwd = null;
        if (!ok)
        {
            cfg.Mode = prevMode;
            cfg.EnsureSecretScope();
            cfg.Save();
            var r = MessageBox.Show(owner, "Görev kaydedilemedi:\n" + msg + "\n\n'Oturum açıkken çalıştır' (yönetici gerektirmez) moduna geçilsin mi?",
                T, MessageBoxButtons.YesNo, MessageBoxIcon.Error);
            if (r == DialogResult.Yes && mode != TaskMode.Interactive) return ApplyMode(cfg, TaskMode.Interactive, owner);
            return false;
        }
        cfg.ScheduleEnabled = true;
        cfg.Save();
        MessageBox.Show(owner, $"Her gün {cfg.BackupTime}'de otomatik yedek alınacak ✓\nMod: {ModeNames[(int)mode]}", T, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }
}
