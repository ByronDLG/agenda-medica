namespace AgendaMedica.Api.Infrastructure.Email;

public sealed record EmailMessage(
    string RecipientAddress,
    string Subject,
    string TextBody,
    string? HtmlBody = null,
    string? RecipientName = null);
