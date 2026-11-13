using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Domain;
using AgendaMedica.Api.Domain.Identity;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Domain.Schedules;

namespace AgendaMedica.Api.Tests;

public class AppointmentConfirmationPolicyTests
{
    [Theory]
    [InlineData(
        AppointmentConfirmationMode.ManualApproval,
        AppointmentStatus.Requested)]
    [InlineData(
        AppointmentConfirmationMode.Automatic,
        AppointmentStatus.Confirmed)]
    public void GetInitialStatusReflectsOrganizationMode(
        AppointmentConfirmationMode mode,
        AppointmentStatus expected)
    {
        Assert.Equal(expected, AppointmentConfirmationPolicy.GetInitialStatus(mode));
    }

    [Fact]
    public void OrganizationDefaultsToGuatemalaAndManualApproval()
    {
        var organization = Organization.Create("Consultorio de prueba");

        Assert.Equal("GT", organization.CountryCode);
        Assert.Equal("America/Guatemala", organization.TimeZoneId);
        Assert.Equal(
            AppointmentConfirmationMode.ManualApproval,
            organization.AppointmentConfirmationMode);
    }

    [Fact]
    public void OrganizationCanChangeItsAppointmentConfirmationMode()
    {
        var organization = Organization.Create("Consultorio de prueba");

        organization.UpdateAppointmentConfirmationMode(AppointmentConfirmationMode.Automatic);

        Assert.Equal(
            AppointmentConfirmationMode.Automatic,
            organization.AppointmentConfirmationMode);
    }

    [Fact]
    public void OrganizationRejectsUndefinedAppointmentConfirmationMode()
    {
        var organization = Organization.Create("Consultorio de prueba");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => organization.UpdateAppointmentConfirmationMode((AppointmentConfirmationMode)99));
    }

    [Fact]
    public void ScheduleRulesAllowAdjacentWindowsButRejectOverlapsOnTheSameDay()
    {
        var organizationId = Guid.NewGuid();
        var practitionerId = Guid.NewGuid();
        var schedule = new[]
        {
            ScheduleRule.Create(
                organizationId,
                practitionerId,
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0)),
            ScheduleRule.Create(
                organizationId,
                practitionerId,
                DayOfWeek.Monday,
                new TimeOnly(12, 0),
                new TimeOnly(17, 0)),
        };
        var overlapping = schedule.Append(
            ScheduleRule.Create(
                organizationId,
                practitionerId,
                DayOfWeek.Monday,
                new TimeOnly(11, 30),
                new TimeOnly(13, 0)));

        Assert.False(ScheduleRule.HasOverlappingWindows(schedule));
        Assert.True(ScheduleRule.HasOverlappingWindows(overlapping));
    }

    [Fact]
    public void AppointmentTransitionsFollowTheAllowedLifecycle()
    {
        var now = DateTimeOffset.UtcNow;
        var appointment = Appointment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Persona de prueba",
            "persona@example.test",
            null,
            contactConsent: true,
            now.AddDays(1),
            durationMinutes: 30,
            preparationBufferMinutes: 5,
            AppointmentStatus.Requested,
            now);

        Assert.True(appointment.CanTransitionTo(AppointmentStatus.Confirmed));
        Assert.False(appointment.CanTransitionTo(AppointmentStatus.Completed));

        appointment.ChangeStatus(AppointmentStatus.Confirmed);

        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.True(appointment.CanTransitionTo(AppointmentStatus.NoShow));
        Assert.False(appointment.CanTransitionTo(AppointmentStatus.Rejected));
        Assert.Throws<InvalidOperationException>(
            () => appointment.ChangeStatus(AppointmentStatus.Rejected));
    }

    [Fact]
    public void PractitionerSchedulePreferencesDefaultToThirtyMinutesAndFiveMinuteBuffer()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        var organization = Organization.Create("Consultorio de prueba");
        var membership = OrganizationMembership.Create(
            user,
            organization,
            OrganizationRole.Practitioner);

        Assert.Equal(30, membership.AppointmentDurationMinutes);
        Assert.Equal(5, membership.PreparationBufferMinutes);
    }

    [Fact]
    public void PractitionerSchedulePreferencesAcceptOnlySupportedValues()
    {
        var membership = OrganizationMembership.Create(
            new ApplicationUser { Id = Guid.NewGuid() },
            Organization.Create("Consultorio de prueba"),
            OrganizationRole.Practitioner);

        membership.UpdateSchedulePreferences(45, 10);

        Assert.Equal(45, membership.AppointmentDurationMinutes);
        Assert.Equal(10, membership.PreparationBufferMinutes);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => membership.UpdateSchedulePreferences(35, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => membership.UpdateSchedulePreferences(30, 7));
    }

    [Fact]
    public void ScheduleRuleRejectsAnEmptyOrReversedWindow()
    {
        Assert.Throws<ArgumentException>(() =>
            ScheduleRule.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(9, 0)));
    }

    [Fact]
    public void SchedulePreviewAppliesClosuresToLocalCalendarDates()
    {
        var organizationId = Guid.NewGuid();
        var practitionerId = Guid.NewGuid();
        var rules = new[]
        {
            ScheduleRule.Create(
                organizationId,
                practitionerId,
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0)),
            ScheduleRule.Create(
                organizationId,
                practitionerId,
                DayOfWeek.Wednesday,
                new TimeOnly(13, 0),
                new TimeOnly(16, 0)),
        };
        var monday = new DateOnly(2026, 11, 16);

        var preview = SchedulePreviewCalculator.Calculate(
            monday,
            days: 3,
            rules,
            new HashSet<DateOnly> { monday },
            appointmentDurationMinutes: 30,
            preparationBufferMinutes: 5);

        Assert.Equal(3, preview.Count);
        Assert.True(preview[0].IsException);
        Assert.Empty(preview[0].Windows);
        Assert.Empty(preview[0].PotentialSlots);
        Assert.Empty(preview[1].Windows);
        Assert.Equal(new TimeOnly(13, 0), preview[2].Windows[0].StartTime);
        Assert.Equal(5, preview[2].PotentialSlots.Count);
        Assert.Equal(new TimeOnly(13, 0), preview[2].PotentialSlots[0].StartTime);
        Assert.Equal(new TimeOnly(13, 30), preview[2].PotentialSlots[0].EndTime);
        Assert.Equal(new TimeOnly(13, 35), preview[2].PotentialSlots[1].StartTime);
    }

    [Fact]
    public void SchedulePreviewRejectsMoreThanOneMonth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SchedulePreviewCalculator.Calculate(
                new DateOnly(2026, 11, 16),
                days: 32,
                Array.Empty<ScheduleRule>(),
                new HashSet<DateOnly>(),
                appointmentDurationMinutes: 30,
                preparationBufferMinutes: 5));
    }
}
