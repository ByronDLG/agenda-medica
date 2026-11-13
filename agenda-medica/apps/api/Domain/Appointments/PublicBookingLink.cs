namespace AgendaMedica.Api.Domain.Appointments;

public sealed class PublicBookingLink
{
    public Guid OrganizationId { get; private set; }

    public Guid PractitionerUserId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] Version { get; private set; } = [];

    public static PublicBookingLink Create(
        Guid organizationId,
        Guid practitionerUserId,
        byte[] tokenHash,
        DateTimeOffset now)
    {
        Validate(organizationId, practitionerUserId, tokenHash, now);
        return new PublicBookingLink
        {
            OrganizationId = organizationId,
            PractitionerUserId = practitionerUserId,
            TokenHash = tokenHash,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public void Rotate(byte[] tokenHash, DateTimeOffset now)
    {
        Validate(OrganizationId, PractitionerUserId, tokenHash, now);
        TokenHash = tokenHash;
        UpdatedAtUtc = now;
    }

    private static void Validate(
        Guid organizationId,
        Guid practitionerUserId,
        byte[] tokenHash,
        DateTimeOffset now)
    {
        if (organizationId == Guid.Empty || practitionerUserId == Guid.Empty)
        {
            throw new ArgumentException("Booking link identifiers are required.");
        }

        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length != 32)
        {
            throw new ArgumentException("Booking link token hashes must be SHA-256.", nameof(tokenHash));
        }

        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Booking link timestamps must be UTC.", nameof(now));
        }
    }
}
