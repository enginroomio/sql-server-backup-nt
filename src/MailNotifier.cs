using System.Net;
using System.Net.Mail;
using System.Text;

namespace SqlBackupTool;

/// <summary>
/// Best-effort SMTP notification after backup. Failures are logged only —
/// they never change JobResult / exit codes.
/// </summary>
public static class MailNotifier
{
    public static bool IsConfigured(AppConfig cfg) =>
        cfg.MailEnabled
        && !string.IsNullOrWhiteSpace(cfg.SmtpHost)
        && !string.IsNullOrWhiteSpace(cfg.MailFrom)
        && !string.IsNullOrWhiteSpace(cfg.MailTo)
        && cfg.SmtpPort is >= 1 and <= 65535;

    /// <summary>Send success and/or failure mail according to config. Never throws.</summary>
    public static void NotifyBackup(AppConfig cfg, JobResult job)
    {
        try
        {
            if (!IsConfigured(cfg))
            {
                if (cfg.MailEnabled)
                    Logger.Warn("E-posta açık ama SMTP/Kimden/Kime eksik; bildirim atlandı.");
                return;
            }
            bool want = job.AllOk ? cfg.MailOnSuccess : cfg.MailOnFailure;
            if (!want)
            {
                Logger.Info(job.AllOk
                    ? "Yedekleme başarılı; başarı e-postası kapalı (MailOnSuccess=false)."
                    : "Yedekleme başarısız; hata e-postası kapalı (MailOnFailure=false).");
                return;
            }
            Send(cfg, job);
        }
        catch (Exception ex)
        {
            Logger.Warn("E-posta bildirimi başarısız (yedekleme sonucu değişmedi): " + SafeMsg(ex));
        }
    }

    static void Send(AppConfig cfg, JobResult job)
    {
        var ok = job.AllOk;
        var subject = ok
            ? "SQL Yedekleme Aracı — yedekleme başarılı"
            : "SQL Yedekleme Aracı — yedekleme BAŞARISIZ";
        var body = new StringBuilder();
        if (ok)
        {
            body.AppendLine("yedekleme başarılı");
            body.AppendLine();
            body.AppendLine(job.Summary);
        }
        else
        {
            body.AppendLine("Yedekleme tamamlanamadı veya kısmen başarısız.");
            body.AppendLine();
            body.AppendLine(job.Summary);
            foreach (var i in job.Items.Where(x => !x.Success && !x.Skipped))
                body.AppendLine($"- {i.Db}: {i.Message}");
        }
        body.AppendLine();
        body.AppendLine($"Sunucu: {cfg.InstanceName}");
        body.AppendLine($"Hedef: {cfg.DestLabel} — {cfg.DriveTargetFolder}");
        body.AppendLine($"Zaman: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        body.AppendLine($"Makine: {Environment.MachineName}");

        using var msg = new MailMessage
        {
            From = new MailAddress(cfg.MailFrom.Trim()),
            Subject = subject,
            Body = body.ToString(),
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        foreach (var addr in cfg.MailTo.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            msg.To.Add(addr.Trim());

        using var client = new SmtpClient(cfg.SmtpHost.Trim(), cfg.SmtpPort)
        {
            EnableSsl = cfg.SmtpSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30000
        };

        if (!string.IsNullOrWhiteSpace(cfg.SmtpUser))
        {
            var pwd = cfg.GetSmtpPassword() ?? "";
            client.Credentials = new NetworkCredential(cfg.SmtpUser.Trim(), pwd);
            // do not log pwd / credential object
        }
        else
        {
            client.UseDefaultCredentials = false;
        }

        Logger.Info($"E-posta gönderiliyor → {cfg.MailTo} (SMTP {cfg.SmtpHost}:{cfg.SmtpPort}, SSL={cfg.SmtpSsl})…");
        client.Send(msg);
        Logger.Success(ok
            ? "E-posta bildirimi gönderildi: yedekleme başarılı."
            : "E-posta bildirimi gönderildi: yedekleme hatası.");
    }

    static string SafeMsg(Exception ex)
    {
        // Strip anything that might accidentally include connection strings; keep short.
        var m = ex.Message ?? "";
        if (m.Length > 300) m = m[..300] + "…";
        return m;
    }
}
