using System.IO;
using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AgendaMedica.Api.Infrastructure.Email;

public sealed class MailKitEmailSender(IOptions<SmtpEmailOptions> options) : IEmailSender
{
    public async Task SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.RecipientAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.TextBody);

        var smtpOptions = options.Value;
        if (!smtpOptions.Enabled)
        {
            throw new EmailDeliveryException("smtp-disabled");
        }

        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(smtpOptions.FromName, smtpOptions.FromAddress));
        email.To.Add(new MailboxAddress(message.RecipientName ?? string.Empty, message.RecipientAddress));
        email.Subject = message.Subject;
        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            email.MessageId = message.MessageId;
        }

        email.Body = new BodyBuilder
        {
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();

        using var client = new SmtpClient
        {
            Timeout = smtpOptions.TimeoutMilliseconds,
        };
        var tlsMode = smtpOptions.UseImplicitTls
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        try
        {
            await client.ConnectAsync(
                smtpOptions.Host,
                smtpOptions.Port,
                tlsMode,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(smtpOptions.UserName))
            {
                await client.AuthenticateAsync(
                    smtpOptions.UserName,
                    smtpOptions.Password,
                    cancellationToken);
            }

            var result = await client.SendAsync(
                FormatOptions.Default,
                new SmtpSendRequest(email),
                cancellationToken);
            if (result.AcceptedRecipients.Count != 1 || result.RejectedRecipients.Count != 0)
            {
                throw new EmailDeliveryException();
            }

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception exception) when (
            exception is MailKit.CommandException or
                MailKit.ProtocolException or
                MailKit.ServiceNotAuthenticatedException or
                MailKit.ServiceNotConnectedException or
                System.Security.Authentication.AuthenticationException or
                MailKit.Security.AuthenticationException or
                IOException or
                SocketException or
                TimeoutException)
        {
            throw new EmailDeliveryException();
        }
    }
}
