namespace AgendaMedica.Api.Domain.Appointments;

public sealed class AppointmentAuditEvent
{
    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public Guid ActorUserId { get; private set; }

    public AppointmentStatus? PreviousStatus { get; private set; }

    public AppointmentStatus NewStatus { get; private set; }

    public DateTimeOffset ChangedAtUtc { get; private set; }

    public static AppointmentAuditEvent Create(
        Guid organizationId,
        Guid appointmentId,
        Guid actorUserId,
        AppointmentStatus? previousStatus,
        AppointmentStatus newStatus,
        DateTimeOffset changedAtUtc)
    {
        if (organizationId == Guid.Empty ||
            appointmentId == Guid.Empty ||
            actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Appointment audit identifiers are required.");
        }

        if (!Enum.IsDefined(newStatus) ||
            (previousStatus.HasValue && !Enum.IsDefined(previousStatus.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(newStatus));
        }

        if (changedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Audit timestamps must be UTC.", nameof(changedAtUtc));
        }

        return new AppointmentAuditEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AppointmentId = appointmentId,
            ActorUserId = actorUserId,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            ChangedAtUtc = changedAtUtc,
        };
    }
}
