using Xunit;

namespace SqlBackupTool.Tests;

public class CoreAndEngineTests
{
    [Fact]
    public void JobResult_AllOk_and_Summary()
    {
        var j = new JobResult();
        j.Items.Add(new DbResult("A", true, false, "ok", null));
        Assert.True(j.AllOk);
        Assert.Contains("başarılı", j.Summary, StringComparison.OrdinalIgnoreCase);

        j.Items.Add(new DbResult("B", false, false, "fail", null));
        Assert.False(j.AllOk);
        Assert.Contains("başarısız", j.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Friendly_maps_express_compression_1844()
    {
        var f = BackupEngine.Friendly(new Exception("Hata 1844: compression"));
        Assert.Contains("sıkıştır", f.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyAzeMailFromDrive_wires_gmail_smtp()
    {
        var cfg = new AppConfig { DriveMail = "user@gmail.com" };
        cfg.ApplyAzeMailFromDrive();
        Assert.True(cfg.MailEnabled);
        Assert.Equal("smtp.gmail.com", cfg.SmtpHost);
        Assert.Equal(587, cfg.SmtpPort);
        Assert.Equal("user@gmail.com", cfg.MailFrom);
        Assert.Equal("user@gmail.com", cfg.MailTo);
        Assert.True(MailNotifier.IsConfigured(cfg));
    }

    [Fact]
    public void ApplyMailSimple_sets_fields()
    {
        var cfg = new AppConfig();
        cfg.ApplyMailSimple(true, "smtp.example.com", 465, true,
            "a@ex.com", "b@ex.com", "a@ex.com", null, true, false);
        Assert.True(cfg.MailEnabled);
        Assert.Equal(465, cfg.SmtpPort);
        Assert.False(cfg.MailOnFailure);
        Assert.True(MailNotifier.IsConfigured(cfg));
    }

    [Fact]
    public void ApplyDestination_Yerel_sets_local()
    {
        var cfg = new AppConfig();
        var err = cfg.ApplyDestination("Yerel", null, @"C:\SQLBackup");
        Assert.Null(err);
        Assert.Equal(DestKind.LocalOrNetwork, cfg.Destination);
        Assert.Equal(@"C:\SQLBackup", cfg.LocalFolder);
        Assert.False(cfg.RequiresDriveApiUpload);
    }

    [Fact]
    public void LoadScript_embeds_backup_master()
    {
        var sql = BackupEngine.LoadScript();
        Assert.Contains("BACKUP DATABASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RESTORE VERIFYONLY", sql, StringComparison.OrdinalIgnoreCase);
    }
}
