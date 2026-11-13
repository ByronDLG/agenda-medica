namespace AgendaMedica.Api.Infrastructure.Email;

public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException()
        : base("The SMTP provider did not accept the email.")
    {
    }
}
