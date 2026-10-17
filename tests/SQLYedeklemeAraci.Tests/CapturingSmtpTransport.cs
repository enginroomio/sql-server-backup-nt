using System.Net;
using System.Net.Mail;

namespace SqlBackupTool.Tests;

/// <summary>SMTP sink for unit tests — captures messages, never opens a socket.</summary>
public sealed class CapturingSmtpTransport : ISmtpMailTransport
{
    public List<CapturedMail> Sent { get; } = new();
    public Exception? Fault { get; set; }

    public void Send(MailMessage message, string host, int port, bool enableSsl, NetworkCredential? credentials)
    {
        if (Fault != null) throw Fault;
        Sent.Add(new CapturedMail(
            Host: host,
            Port: port,
            EnableSsl: enableSsl,
            From: message.From?.Address ?? "",
            To: string.Join(";", message.To.Select(a => a.Address)),
            Subject: message.Subject ?? "",
            Body: message.Body ?? "",
            UserName: credentials?.UserName));
    }

    public sealed record CapturedMail(
        string Host, int Port, bool EnableSsl,
        string From, string To, string Subject, string Body, string? UserName);
}
