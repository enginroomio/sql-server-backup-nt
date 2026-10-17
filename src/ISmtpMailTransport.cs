using System.Net;
using System.Net.Mail;

namespace SqlBackupTool;

/// <summary>SMTP send abstraction — production uses <see cref="NetworkSmtpTransport"/>; unit tests inject a sink.</summary>
public interface ISmtpMailTransport
{
    void Send(MailMessage message, string host, int port, bool enableSsl, NetworkCredential? credentials);
}

/// <summary>Real network SMTP via <see cref="SmtpClient"/>.</summary>
public sealed class NetworkSmtpTransport : ISmtpMailTransport
{
    public void Send(MailMessage message, string host, int port, bool enableSsl, NetworkCredential? credentials)
    {
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30000,
            UseDefaultCredentials = false
        };
        if (credentials != null)
            client.Credentials = credentials;
        client.Send(message);
    }
}
