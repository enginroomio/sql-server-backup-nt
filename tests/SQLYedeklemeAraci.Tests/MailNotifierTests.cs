using Xunit;

namespace SqlBackupTool.Tests;

public class MailNotifierTests : IDisposable
{
    readonly ISmtpMailTransport _prev;

    public MailNotifierTests()
    {
        _prev = MailNotifier.Transport;
    }

    public void Dispose()
    {
        MailNotifier.Transport = _prev;
    }

    static AppConfig MailCfg() => new()
    {
        MailEnabled = true,
        SmtpHost = "smtp.test.local",
        SmtpPort = 587,
        SmtpSsl = true,
        MailFrom = "from@test.local",
        MailTo = "to@test.local; other@test.local",
        SmtpUser = "from@test.local",
        MailOnSuccess = true,
        MailOnFailure = true,
        InstanceName = @".\SQLEXPRESS",
        Destination = DestKind.LocalOrNetwork,
        LocalFolder = @"C:\SQLBackup"
    };

    static JobResult OkJob()
    {
        var j = new JobResult();
        j.Items.Add(new DbResult("DemoDb", true, false, "ok", @"C:\SQLBackup\DemoDb_20261017_120000.zip"));
        return j;
    }

    static JobResult FailJob()
    {
        var j = new JobResult();
        j.Items.Add(new DbResult("DemoDb", false, false, "backup failed", null));
        return j;
    }

    [Fact]
    public void IsConfigured_requires_host_from_to()
    {
        var cfg = MailCfg();
        Assert.True(MailNotifier.IsConfigured(cfg));
        cfg.SmtpHost = "";
        Assert.False(MailNotifier.IsConfigured(cfg));
    }

    [Fact]
    public void NotifyBackup_success_sends_via_transport_without_network()
    {
        var sink = new CapturingSmtpTransport();
        MailNotifier.Transport = sink;
        var cfg = MailCfg();

        MailNotifier.NotifyBackup(cfg, OkJob());

        Assert.Single(sink.Sent);
        var m = sink.Sent[0];
        Assert.Equal("smtp.test.local", m.Host);
        Assert.Equal(587, m.Port);
        Assert.True(m.EnableSsl);
        Assert.Contains("başarılı", m.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("yedekleme başarılı", m.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("to@test.local", m.To);
        Assert.Contains("other@test.local", m.To);
        Assert.Equal("from@test.local", m.UserName);
    }

    [Fact]
    public void NotifyBackup_failure_sends_error_mail()
    {
        var sink = new CapturingSmtpTransport();
        MailNotifier.Transport = sink;

        MailNotifier.NotifyBackup(MailCfg(), FailJob());

        Assert.Single(sink.Sent);
        Assert.Contains("BAŞARISIZ", sink.Sent[0].Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DemoDb", sink.Sent[0].Body);
    }

    [Fact]
    public void NotifyBackup_skips_when_MailOnSuccess_false()
    {
        var sink = new CapturingSmtpTransport();
        MailNotifier.Transport = sink;
        var cfg = MailCfg();
        cfg.MailOnSuccess = false;

        MailNotifier.NotifyBackup(cfg, OkJob());
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void NotifyBackup_transport_fault_does_not_throw()
    {
        var sink = new CapturingSmtpTransport { Fault = new InvalidOperationException("smtp down") };
        MailNotifier.Transport = sink;

        var ex = Record.Exception(() => MailNotifier.NotifyBackup(MailCfg(), OkJob()));
        Assert.Null(ex);
    }

    [Fact]
    public void NotifyBackup_disabled_sends_nothing()
    {
        var sink = new CapturingSmtpTransport();
        MailNotifier.Transport = sink;
        var cfg = MailCfg();
        cfg.MailEnabled = false;

        MailNotifier.NotifyBackup(cfg, OkJob());
        Assert.Empty(sink.Sent);
    }
}
