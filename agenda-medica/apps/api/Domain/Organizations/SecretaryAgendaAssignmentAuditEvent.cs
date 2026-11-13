namespace AgendaMedica.Api.Domain.Organizations;

public enum SecretaryAgendaAssignmentChange
{
    Granted,
    Revoked,
}

public sealed class SecretaryAgendaAssignmentAuditEvent
{
    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid SecretaryUserId { get; private set; }

    public Guid PractitionerUserId { get; private set; }

    public Guid ActorUserId { get; private set; }

    public SecretaryAgendaAssignmentChange Change { get; private set; }

    public DateTimeOffset ChangedAtUtc { get; private set; }

    public static SecretaryAgendaAssignmentAuditEvent Create(
        Guid organizationId,
        Guid secretaryUserId,
        Guid practitionerUserId,
        Guid actorUserId,
        SecretaryAgendaAssignmentChange change,
        DateTimeOffset changedAtUtc)
    {
        if (organizationId == Guid.Empty ||
            secretaryUserId == Guid.Empty ||
            practitionerUserId == Guid.Empty ||
            actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Secretary assignment audit identifiers are required.");
        }

        if (secretaryUserId == practitionerUserId || !Enum.IsDefined(change))
        {
            throw new ArgumentOutOfRangeException(nameof(change));
        }

        if (changedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Audit timestamps must be UTC.", nameof(changedAtUtc));
        }

        return new SecretaryAgendaAssignmentAuditEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            SecretaryUserId = secretaryUserId,
            PractitionerUserId = practitionerUserId,
            ActorUserId = actorUserId,
            Change = change,
            ChangedAtUtc = changedAtUtc,
        };
    }
}
