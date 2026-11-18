namespace AgendaMedica.Api.Infrastructure.Email;

public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string failureCode = "provider-failure")
        : base(failureCode == "smtp-disabled"
            ? "SMTP email delivery is disabled. Configure Email:Smtp through a secret provider."
            : "The SMTP provider did not accept the email.")
    {
        FailureCode = failureCode;
    }

    public string FailureCode { get; }
}
