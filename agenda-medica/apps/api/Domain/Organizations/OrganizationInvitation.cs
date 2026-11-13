namespace AgendaMedica.Api.Domain.Organizations;

public sealed class OrganizationInvitation
{
    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Organization Organization { get; private set; } = null!;

    public Guid CreatedByUserId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public string Email { get; private set; } = string.Empty;

    public string EmailNormalized { get; private set; } = string.Empty;

    public OrganizationRole Role { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public byte[] Version { get; private set; } = [];

    public static OrganizationInvitation Create(
        Guid organizationId,
        Guid createdByUserId,
        string email,
        string normalizedEmail,
        OrganizationRole role,
        byte[] tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length != 32)
        {
            throw new ArgumentException("Invitation token hashes must be SHA-256.", nameof(tokenHash));
        }

        if (role is not (OrganizationRole.Practitioner or OrganizationRole.Secretary))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        return new OrganizationInvitation
        {
            OrganizationId = organizationId,
            CreatedByUserId = createdByUserId,
            Email = email.Trim(),
            EmailNormalized = normalizedEmail,
            Role = role,
            TokenHash = tokenHash,
            CreatedAt = createdAt.ToUniversalTime(),
            ExpiresAt = expiresAt.ToUniversalTime(),
        };
    }

    public bool IsPendingAt(DateTimeOffset now) =>
        AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    public void Accept(DateTimeOffset acceptedAt)
    {
        if (!IsPendingAt(acceptedAt))
        {
            throw new InvalidOperationException("The invitation is no longer pending.");
        }

        AcceptedAt = acceptedAt.ToUniversalTime();
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (AcceptedAt is not null || RevokedAt is not null)
        {
            throw new InvalidOperationException("The invitation is no longer pending.");
        }

        RevokedAt = revokedAt.ToUniversalTime();
    }
}
