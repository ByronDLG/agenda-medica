namespace AgendaMedica.Api.Infrastructure.Email;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Agenda Medica";

    public bool UseImplicitTls { get; set; }

    public int TimeoutMilliseconds { get; set; } = 15000;
}
