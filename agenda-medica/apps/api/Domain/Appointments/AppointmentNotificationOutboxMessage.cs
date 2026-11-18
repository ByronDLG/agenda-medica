namespace AgendaMedica.Api.Domain.Appointments;

public enum AppointmentNotificationRecipient
{
    Patient,
    Practitioner,
}

public enum AppointmentNotificationKind
{
    EventUpdate,
    Reminder24Hours,
}

public enum AppointmentNotificationDeliveryStatus
{
    Pending,
    Processing,
    Delivered,
    Failed,
    Skipped,
}

public sealed class AppointmentNotificationOutboxMessage
{
    public const int MaximumAttempts = 5;

    private static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(24);

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public Guid AuditEventId { get; private set; }

    public AppointmentNotificationKind Kind { get; private set; }

    public AppointmentNotificationRecipient Recipient { get; private set; }

    public string RecipientAddress { get; private set; } = string.Empty;

    public string? RecipientName { get; private set; }

    public AppointmentNotificationDeliveryStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? LastAttemptAtUtc { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    public string? LastFailureCode { get; private set; }

    public byte[] Version { get; private set; } = [];

    public static IReadOnlyList<AppointmentNotificationOutboxMessage> CreateForEvent(
        Appointment appointment,
        AppointmentAuditEvent auditEvent,
        string? practitionerEmail,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (appointment.OrganizationId != auditEvent.OrganizationId ||
            appointment.Id != auditEvent.AppointmentId ||
            appointment.Status != auditEvent.NewStatus ||
            createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Notification event context is inconsistent.");
        }

        var messages = new List<AppointmentNotificationOutboxMessage>(3);
        if (appointment.ContactConsent &&
            !string.IsNullOrWhiteSpace(appointment.ContactEmail) &&
            auditEvent.NewStatus is not (AppointmentStatus.Completed or AppointmentStatus.NoShow))
        {
            messages.Add(Create(
                appointment,
                auditEvent,
                AppointmentNotificationKind.EventUpdate,
                AppointmentNotificationRecipient.Patient,
                appointment.ContactEmail,
                appointment.PatientName,
                createdAtUtc,
                createdAtUtc));
        }

        if (auditEvent.PreviousStatus is null &&
            (appointment.Status is AppointmentStatus.Requested or AppointmentStatus.Confirmed) &&
            appointment.ContactConsent &&
            !string.IsNullOrWhiteSpace(appointment.ContactEmail))
        {
            var reminderAtUtc = appointment.StartsAtUtc.Subtract(ReminderLeadTime);
            if (reminderAtUtc > createdAtUtc)
            {
                messages.Add(Create(
                    appointment,
                    auditEvent,
                    AppointmentNotificationKind.Reminder24Hours,
                    AppointmentNotificationRecipient.Patient,
                    appointment.ContactEmail,
                    appointment.PatientName,
                    createdAtUtc,
                    reminderAtUtc));
            }
        }

        if (!string.IsNullOrWhiteSpace(practitionerEmail))
        {
            messages.Add(Create(
                appointment,
                auditEvent,
                AppointmentNotificationKind.EventUpdate,
                AppointmentNotificationRecipient.Practitioner,
                practitionerEmail,
                recipientName: null,
                createdAtUtc: createdAtUtc,
                nextAttemptAtUtc: createdAtUtc));
        }

        return messages;
    }

    public bool IsReadyToAttempt(DateTimeOffset nowUtc) =>
        Status == AppointmentNotificationDeliveryStatus.Pending &&
        NextAttemptAtUtc <= nowUtc;

    public bool IsStaleProcessing(DateTimeOffset staleBeforeUtc) =>
        Status == AppointmentNotificationDeliveryStatus.Processing &&
        LastAttemptAtUtc <= staleBeforeUtc;

    public void StartAttempt(DateTimeOffset nowUtc)
    {
        if (nowUtc.Offset != TimeSpan.Zero ||
            AttemptCount >= MaximumAttempts ||
            (!IsReadyToAttempt(nowUtc) &&
                !IsStaleProcessing(nowUtc.AddMinutes(-10))))
        {
            throw new InvalidOperationException("Notification is not ready for delivery.");
        }

        Status = AppointmentNotificationDeliveryStatus.Processing;
        AttemptCount++;
        LastAttemptAtUtc = nowUtc;
    }

    public void MarkDelivered(DateTimeOffset deliveredAtUtc)
    {
        if (Status != AppointmentNotificationDeliveryStatus.Processing ||
            deliveredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("Notification cannot be marked delivered.");
        }

        Status = AppointmentNotificationDeliveryStatus.Delivered;
        DeliveredAtUtc = deliveredAtUtc;
        LastFailureCode = null;
        ClearRecipientDetails();
    }

    public void MarkFailedAttempt(DateTimeOffset failedAtUtc, string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(failureCode.Length, 64);
        if (Status != AppointmentNotificationDeliveryStatus.Processing ||
            failedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("Notification is not being processed.");
        }

        LastFailureCode = failureCode;
        if (AttemptCount >= MaximumAttempts)
        {
            Status = AppointmentNotificationDeliveryStatus.Failed;
            ClearRecipientDetails();
            return;
        }

        var delay = AttemptCount switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromHours(1),
        };
        NextAttemptAtUtc = failedAtUtc.Add(delay);
        Status = AppointmentNotificationDeliveryStatus.Pending;
    }

    public void MarkProcessingLeaseExpired(DateTimeOffset nowUtc)
    {
        if (nowUtc.Offset != TimeSpan.Zero ||
            !IsStaleProcessing(nowUtc.AddMinutes(-10)))
        {
            throw new InvalidOperationException("Notification processing lease has not expired.");
        }

        Status = AppointmentNotificationDeliveryStatus.Failed;
        LastFailureCode = "worker-timeout";
        ClearRecipientDetails();
    }

    public void MarkSkipped(string skipCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skipCode);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(skipCode.Length, 64);
        if (Status != AppointmentNotificationDeliveryStatus.Processing)
        {
            throw new InvalidOperationException("Notification is not being processed.");
        }

        Status = AppointmentNotificationDeliveryStatus.Skipped;
        LastFailureCode = skipCode;
        ClearRecipientDetails();
    }

    private static AppointmentNotificationOutboxMessage Create(
        Appointment appointment,
        AppointmentAuditEvent auditEvent,
        AppointmentNotificationKind kind,
        AppointmentNotificationRecipient recipient,
        string recipientAddress,
        string? recipientName,
        DateTimeOffset createdAtUtc,
        DateTimeOffset nextAttemptAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = appointment.OrganizationId,
            AppointmentId = appointment.Id,
            AuditEventId = auditEvent.Id,
            Kind = kind,
            Recipient = recipient,
            RecipientAddress = recipientAddress.Trim(),
            RecipientName = string.IsNullOrWhiteSpace(recipientName)
                ? null
                : recipientName.Trim(),
            Status = AppointmentNotificationDeliveryStatus.Pending,
            CreatedAtUtc = createdAtUtc,
            NextAttemptAtUtc = nextAttemptAtUtc,
        };

    private void ClearRecipientDetails()
    {
        RecipientAddress = string.Empty;
        RecipientName = null;
    }
}
