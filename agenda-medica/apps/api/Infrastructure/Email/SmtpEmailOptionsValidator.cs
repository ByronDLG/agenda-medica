using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace AgendaMedica.Api.Infrastructure.Email;

public sealed class SmtpEmailOptionsValidator : IValidateOptions<SmtpEmailOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpEmailOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add("Email:Smtp:Host is required when SMTP is enabled.");
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add("Email:Smtp:Port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress) ||
            !new EmailAddressAttribute().IsValid(options.FromAddress))
        {
            failures.Add("Email:Smtp:FromAddress must be a valid email address.");
        }

        var hasUserName = !string.IsNullOrWhiteSpace(options.UserName);
        var hasPassword = !string.IsNullOrWhiteSpace(options.Password);
        if (hasUserName != hasPassword)
        {
            failures.Add("Email:Smtp:UserName and Password must both be configured or both be empty.");
        }

        if (options.TimeoutMilliseconds is < 1000 or > 300000)
        {
            failures.Add("Email:Smtp:TimeoutMilliseconds must be between 1000 and 300000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
