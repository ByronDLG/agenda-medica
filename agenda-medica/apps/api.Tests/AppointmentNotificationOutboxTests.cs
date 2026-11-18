using AgendaMedica.Api.Domain.Appointments;

namespace AgendaMedica.Api.Tests;

public sealed class AppointmentNotificationOutboxTests
{
    [Fact]
    public void EventQueuesOnlyConsentedPatientEmailAndPractitionerEmail()
    {
        var (appointment, auditEvent, createdAtUtc) = CreateEvent();

        var messages = AppointmentNotificationOutboxMessage.CreateForEvent(
            appointment,
            auditEvent,
            practitionerEmail: "professional@example.test",
            createdAtUtc);

        Assert.Equal(3, messages.Count);
        Assert.Contains(
            messages,
            message => message.Recipient == AppointmentNotificationRecipient.Patient &&
                message.RecipientAddress == "patient@example.test" &&
                message.Kind == AppointmentNotificationKind.EventUpdate);
        Assert.Contains(
            messages,
            message => message.Recipient == AppointmentNotificationRecipient.Patient &&
                message.Kind == AppointmentNotificationKind.Reminder24Hours);
        Assert.Contains(
            messages,
            message => message.Recipient == AppointmentNotificationRecipient.Practitioner &&
                message.RecipientAddress == "professional@example.test" &&
                message.Kind == AppointmentNotificationKind.EventUpdate);
    }

    [Fact]
    public void AppointmentWithinOneDayDoesNotQueueAReminder()
    {
        var (appointment, auditEvent, createdAtUtc) =
            CreateEvent(timeUntilAppointment: TimeSpan.FromHours(23));

        var messages = AppointmentNotificationOutboxMessage.CreateForEvent(
            appointment,
            auditEvent,
            practitionerEmail: null,
            createdAtUtc);

        var message = Assert.Single(messages);
        Assert.Equal(AppointmentNotificationKind.EventUpdate, message.Kind);
    }

    [Fact]
    public void RetryUsesBackoffAndStopsAfterMaximumAttempts()
    {
        var (appointment, auditEvent, createdAtUtc) = CreateEvent();
        var message = Assert.Single(
            AppointmentNotificationOutboxMessage.CreateForEvent(
                appointment,
                auditEvent,
                practitionerEmail: null,
                createdAtUtc),
            item => item.Kind == AppointmentNotificationKind.EventUpdate);
        var failedAtUtc = createdAtUtc;

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            message.StartAttempt(failedAtUtc);
            message.MarkFailedAttempt(failedAtUtc, "provider-failure");
            Assert.Equal(AppointmentNotificationDeliveryStatus.Pending, message.Status);
            Assert.Equal(attempt, message.AttemptCount);
            failedAtUtc = message.NextAttemptAtUtc;
        }

        message.StartAttempt(failedAtUtc);
        message.MarkFailedAttempt(failedAtUtc, "provider-failure");

        Assert.Equal(5, message.AttemptCount);
        Assert.Equal(AppointmentNotificationDeliveryStatus.Failed, message.Status);
        Assert.False(message.IsReadyToAttempt(failedAtUtc.AddDays(1)));
    }

    [Fact]
    public void ExpiredProcessingLeaseDoesNotExceedMaximumAttempts()
    {
        var (appointment, auditEvent, createdAtUtc) = CreateEvent();
        var message = Assert.Single(
            AppointmentNotificationOutboxMessage.CreateForEvent(
                appointment,
                auditEvent,
                practitionerEmail: null,
                createdAtUtc),
            item => item.Kind == AppointmentNotificationKind.EventUpdate);
        var attemptAtUtc = createdAtUtc;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            message.StartAttempt(attemptAtUtc);
            if (attempt < 5)
            {
                message.MarkFailedAttempt(attemptAtUtc, "provider-failure");
                attemptAtUtc = message.NextAttemptAtUtc;
            }
        }

        message.MarkProcessingLeaseExpired(attemptAtUtc.AddMinutes(11));

        Assert.Equal(AppointmentNotificationDeliveryStatus.Failed, message.Status);
        Assert.Equal(5, message.AttemptCount);
        Assert.Equal("worker-timeout", message.LastFailureCode);
        Assert.Empty(message.RecipientAddress);
    }

    [Fact]
    public void CompletedAppointmentDoesNotNotifyPatient()
    {
        var (appointment, _, createdAtUtc) = CreateEvent(AppointmentStatus.Confirmed);
        appointment.ChangeStatus(AppointmentStatus.Completed);
        var auditEvent = AppointmentAuditEvent.Create(
            appointment.OrganizationId,
            appointment.Id,
            Guid.NewGuid(),
            AppointmentStatus.Confirmed,
            AppointmentStatus.Completed,
            createdAtUtc);

        var messages = AppointmentNotificationOutboxMessage.CreateForEvent(
            appointment,
            auditEvent,
            practitionerEmail: "professional@example.test",
            createdAtUtc);

        var message = Assert.Single(messages);
        Assert.Equal(AppointmentNotificationRecipient.Practitioner, message.Recipient);
    }

    private static (
        Appointment Appointment,
        AppointmentAuditEvent AuditEvent,
        DateTimeOffset CreatedAtUtc) CreateEvent(
        AppointmentStatus status = AppointmentStatus.Requested,
        TimeSpan? timeUntilAppointment = null)
    {
        var organizationId = Guid.NewGuid();
        var practitionerId = Guid.NewGuid();
        var appointment = Appointment.Create(
            organizationId,
            practitionerId,
            Guid.NewGuid(),
            "Paciente de prueba",
            "patient@example.test",
            null,
            contactConsent: true,
            DateTimeOffset.UtcNow.Add(timeUntilAppointment ?? TimeSpan.FromDays(2)),
            durationMinutes: 30,
            preparationBufferMinutes: 5,
            status,
            DateTimeOffset.UtcNow);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var auditEvent = AppointmentAuditEvent.Create(
            organizationId,
            appointment.Id,
            practitionerId,
            previousStatus: null,
            status,
            createdAtUtc);
        return (appointment, auditEvent, createdAtUtc);
    }
}
