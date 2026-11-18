using System.Globalization;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Infrastructure.Appointments;

public sealed class AppointmentNotificationProcessor(
    AgendaDbContext database,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<AppointmentNotificationProcessor> logger)
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(10);

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var staleBeforeUtc = nowUtc.Subtract(ProcessingLease);
        var message = await database.AppointmentNotificationOutboxMessages
            .Where(item =>
                (item.Status == AppointmentNotificationDeliveryStatus.Pending &&
                    item.NextAttemptAtUtc <= nowUtc) ||
                (item.Status == AppointmentNotificationDeliveryStatus.Processing &&
                    item.LastAttemptAtUtc <= staleBeforeUtc))
            .OrderBy(item => item.NextAttemptAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (message is null)
        {
            return false;
        }

        if (message.AttemptCount >= 5 &&
            message.IsStaleProcessing(staleBeforeUtc))
        {
            message.MarkProcessingLeaseExpired(nowUtc);
            await database.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Appointment notification {NotificationId} failed because its delivery lease expired after the maximum attempts.",
                message.Id);
            return true;
        }

        message.StartAttempt(nowUtc);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.Entry(message).State = EntityState.Detached;
            return true;
        }

        var details = await (
            from outbox in database.AppointmentNotificationOutboxMessages.AsNoTracking()
            join audit in database.AppointmentAuditEvents.AsNoTracking()
                on outbox.AuditEventId equals audit.Id
            join appointment in database.Appointments.AsNoTracking()
                on new { outbox.OrganizationId, outbox.AppointmentId }
                equals new { appointment.OrganizationId, AppointmentId = appointment.Id }
            join organization in database.Organizations.AsNoTracking()
                on outbox.OrganizationId equals organization.Id
            where outbox.Id == message.Id
            select new NotificationDetails(
                outbox.Id,
                outbox.Recipient,
                outbox.Kind,
                outbox.RecipientAddress,
                outbox.RecipientName,
                appointment.StartsAtUtc,
                appointment.Status,
                audit.NewStatus,
                organization.Name,
                organization.TimeZoneId))
            .SingleAsync(cancellationToken);

        var skipCode = details.Kind switch
        {
            AppointmentNotificationKind.Reminder24Hours
                when details.CurrentAppointmentStatus is not (
                        AppointmentStatus.Requested or AppointmentStatus.Confirmed) ||
                    details.StartsAtUtc <= nowUtc =>
                "appointment-inactive",
            AppointmentNotificationKind.EventUpdate
                when details.CurrentAppointmentStatus != details.Status =>
                "appointment-status-changed",
            _ => null,
        };
        if (skipCode is not null)
        {
            message.MarkSkipped(skipCode);
            await database.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Appointment notification {NotificationId} was skipped with code {SkipCode}.",
                message.Id,
                skipCode);
            return true;
        }

        try
        {
            await emailSender.SendAsync(CreateEmail(details), cancellationToken);
            message.MarkDelivered(timeProvider.GetUtcNow());
        }
        catch (EmailDeliveryException exception)
        {
            message.MarkFailedAttempt(
                timeProvider.GetUtcNow(),
                exception.FailureCode);
            logger.LogWarning(
                "Appointment notification {NotificationId} delivery failed with code {FailureCode}; retry count {AttemptCount}, status {Status}.",
                message.Id,
                exception.FailureCode,
                message.AttemptCount,
                message.Status);
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static EmailMessage CreateEmail(NotificationDetails details)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(details.TimeZoneId);
        var localStart = TimeZoneInfo.ConvertTime(details.StartsAtUtc, timeZone);
        var date = localStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var time = localStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        var status = details.Status switch
        {
            AppointmentStatus.Requested => "pendiente de confirmación",
            AppointmentStatus.Confirmed => "confirmada",
            AppointmentStatus.Rejected => "rechazada",
            AppointmentStatus.Cancelled => "cancelada",
            AppointmentStatus.Completed => "actualizada",
            AppointmentStatus.NoShow => "actualizada",
            _ => throw new InvalidOperationException(
                $"Unsupported appointment status: {details.Status}."),
        };
        var subject = details.Kind switch
        {
            AppointmentNotificationKind.Reminder24Hours => "Recordatorio de tu cita",
            _ when details.Recipient == AppointmentNotificationRecipient.Patient =>
                "Actualización sobre tu cita",
            _ => "Actualización de agenda",
        };
        var body = details.Kind == AppointmentNotificationKind.Reminder24Hours
            ? $"Hola {details.RecipientName},\n\nTe recordamos tu cita en {details.OrganizationName}, " +
              $"programada para el {date} a las {time} ({details.TimeZoneId}).\n\n" +
              "Si necesitas ayuda, comunícate directamente con el consultorio."
            : details.Recipient == AppointmentNotificationRecipient.Patient
                ? $"Hola {details.RecipientName},\n\nTu cita en {details.OrganizationName} está {status} " +
                  $"para el {date} a las {time} ({details.TimeZoneId}).\n\n" +
                  "Si necesitas ayuda, comunícate directamente con el consultorio."
                : $"Se actualizó una cita en {details.OrganizationName}. La cita programada para el " +
                  $"{date} a las {time} ({details.TimeZoneId}) está {status}. " +
                  "Consulta la agenda para ver los detalles.";

        return new EmailMessage(
            details.RecipientAddress,
            subject,
            body,
            RecipientName: details.RecipientName,
            MessageId: $"{details.Id:N}@agenda-medica.local");
    }

    private sealed record NotificationDetails(
        Guid Id,
        AppointmentNotificationRecipient Recipient,
        AppointmentNotificationKind Kind,
        string RecipientAddress,
        string? RecipientName,
        DateTimeOffset StartsAtUtc,
        AppointmentStatus CurrentAppointmentStatus,
        AppointmentStatus Status,
        string OrganizationName,
        string TimeZoneId);
}
