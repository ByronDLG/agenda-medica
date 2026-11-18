using AgendaMedica.Api.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace AgendaMedica.Api.Tests;

public sealed class SmtpEmailOptionsTests
{
    private readonly SmtpEmailOptionsValidator validator = new();

    [Fact]
    public void DisabledSmtpDoesNotRequireCredentials()
    {
        var result = validator.Validate(
            Options.DefaultName,
            new SmtpEmailOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void EnabledSmtpRequiresACompleteConfiguration()
    {
        var result = validator.Validate(
            Options.DefaultName,
            new SmtpEmailOptions
            {
                Enabled = true,
                Host = "smtp.example.test",
                FromAddress = "agenda@example.test",
                UserName = "agenda",
            });

        Assert.False(result.Succeeded);
        var failures = result.Failures ?? Array.Empty<string>();
        Assert.Contains(failures, failure => failure.Contains("both be configured"));
    }

    [Fact]
    public void EnabledSmtpRequiresASenderAddress()
    {
        var result = validator.Validate(
            Options.DefaultName,
            new SmtpEmailOptions
            {
                Enabled = true,
                Host = "smtp.example.test",
            });

        Assert.False(result.Succeeded);
        var failures = result.Failures ?? Array.Empty<string>();
        Assert.Contains(failures, failure => failure.Contains("FromAddress"));
    }

    [Fact]
    public void EnabledSmtpAcceptsValidTlsConfiguration()
    {
        var result = validator.Validate(
            Options.DefaultName,
            new SmtpEmailOptions
            {
                Enabled = true,
                Host = "smtp.example.test",
                FromAddress = "agenda@example.test",
                UserName = "agenda",
                Password = "not-a-real-credential",
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SenderReportsWhenSmtpIsDisabled()
    {
        var sender = new MailKitEmailSender(
            Options.Create(new SmtpEmailOptions()));

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendAsync(new EmailMessage(
                "recipient@example.test",
                "Subject",
                "Message")));

        Assert.Contains("disabled", exception.Message);
        Assert.Equal("smtp-disabled", exception.FailureCode);
    }
}
