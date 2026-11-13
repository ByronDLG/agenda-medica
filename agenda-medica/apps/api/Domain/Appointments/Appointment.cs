namespace AgendaMedica.Api.Domain.Appointments;

public sealed class Appointment
{
    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid PractitionerUserId { get; private set; }

    public Guid IdempotencyKey { get; private set; }

    public string PatientName { get; private set; } = string.Empty;

    public string? ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    public bool ContactConsent { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public DateTimeOffset EndsAtUtc { get; private set; }

    public DateTimeOffset OccupiedUntilUtc { get; private set; }

    public int DurationMinutes { get; private set; }

    public int PreparationBufferMinutes { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public byte[] Version { get; private set; } = [];

    public bool OccupiesTime => Status is AppointmentStatus.Requested or AppointmentStatus.Confirmed;

    public static Appointment Create(
        Guid organizationId,
        Guid practitionerUserId,
        Guid idempotencyKey,
        string patientName,
        string? contactEmail,
        string? contactPhone,
        bool contactConsent,
        DateTimeOffset startsAtUtc,
        int durationMinutes,
        int preparationBufferMinutes,
        AppointmentStatus initialStatus,
        DateTimeOffset createdAtUtc)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization ID is required.", nameof(organizationId));
        }

        if (practitionerUserId == Guid.Empty)
        {
            throw new ArgumentException("Practitioner user ID is required.", nameof(practitionerUserId));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(patientName);
        if (string.IsNullOrWhiteSpace(contactEmail) && string.IsNullOrWhiteSpace(contactPhone))
        {
            throw new ArgumentException("At least one contact method is required.");
        }

        if (!contactConsent)
        {
            throw new ArgumentException("Contact consent is required.", nameof(contactConsent));
        }

        if (startsAtUtc.Offset != TimeSpan.Zero || createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Appointment timestamps must be UTC.");
        }

        if (durationMinutes is not (15 or 20 or 30 or 45 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        }

        if (preparationBufferMinutes is not (0 or 5 or 10 or 15))
        {
            throw new ArgumentOutOfRangeException(nameof(preparationBufferMinutes));
        }

        if (!Enum.IsDefined(initialStatus) ||
            initialStatus is not (AppointmentStatus.Requested or AppointmentStatus.Confirmed))
        {
            throw new ArgumentOutOfRangeException(nameof(initialStatus));
        }

        var trimmedEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();
        var trimmedPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim();
        var endAtUtc = startsAtUtc.AddMinutes(durationMinutes);

        return new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PractitionerUserId = practitionerUserId,
            IdempotencyKey = idempotencyKey,
            PatientName = patientName.Trim(),
            ContactEmail = trimmedEmail,
            ContactPhone = trimmedPhone,
            ContactConsent = contactConsent,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endAtUtc,
            OccupiedUntilUtc = endAtUtc.AddMinutes(preparationBufferMinutes),
            DurationMinutes = durationMinutes,
            PreparationBufferMinutes = preparationBufferMinutes,
            Status = initialStatus,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public bool HasSameRequest(
        DateTimeOffset startsAtUtc,
        string patientName,
        string? contactEmail,
        string? contactPhone,
        bool contactConsent) =>
        StartsAtUtc == startsAtUtc &&
        string.Equals(PatientName, patientName.Trim(), StringComparison.Ordinal) &&
        string.Equals(ContactEmail, NormalizeOptional(contactEmail), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ContactPhone, NormalizeOptional(contactPhone), StringComparison.Ordinal) &&
        ContactConsent == contactConsent;

    public void ChangeStatus(AppointmentStatus newStatus)
    {
        if (!CanTransitionTo(newStatus))
        {
            throw new InvalidOperationException(
                $"Appointment cannot transition from {Status} to {newStatus}.");
        }

        Status = newStatus;
    }

    public bool CanTransitionTo(AppointmentStatus newStatus) =>
        Status switch
        {
            AppointmentStatus.Requested =>
                newStatus is AppointmentStatus.Confirmed or
                    AppointmentStatus.Rejected or
                    AppointmentStatus.Cancelled,
            AppointmentStatus.Confirmed =>
                newStatus is AppointmentStatus.Cancelled or
                    AppointmentStatus.Completed or
                    AppointmentStatus.NoShow,
            _ => false,
        };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
