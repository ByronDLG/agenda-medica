using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Infrastructure.Appointments;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/secretary-agenda-assignments")]
public sealed class SecretaryAgendaAssignmentsController(
    AgendaDbContext database,
    IAntiforgery antiforgery,
    TimeProvider timeProvider) : ControllerBase
{
    private const int MaximumPractitionerAssignments = 64;

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryGetUserId(out var userId) ||
            !await IsClinicAdministratorAsync(organizationId, userId, cancellationToken))
        {
            return Forbid();
        }

        var practitioners = await database.OrganizationMemberships
            .AsNoTracking()
            .Where(item =>
                item.OrganizationId == organizationId &&
                item.Role == OrganizationRole.Practitioner)
            .OrderBy(item => item.User.Email)
            .Select(item => new AssignmentPractitionerResponse(
                item.UserId,
                item.User.Email))
            .ToArrayAsync(cancellationToken);
        var secretaries = await database.OrganizationMemberships
            .AsNoTracking()
            .Where(item =>
                item.OrganizationId == organizationId &&
                item.Role == OrganizationRole.Secretary)
            .OrderBy(item => item.User.Email)
            .Select(item => new
            {
                item.UserId,
                item.User.Email,
            })
            .ToArrayAsync(cancellationToken);
        var assignments = await database.SecretaryPractitionerAssignments
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .Select(item => new
            {
                item.SecretaryUserId,
                item.PractitionerUserId,
            })
            .ToListAsync(cancellationToken);

        return Ok(new SecretaryAgendaAssignmentsResponse(
            practitioners,
            secretaries.Select(secretary => new AssignmentSecretaryResponse(
                secretary.UserId,
                secretary.Email,
                assignments
                    .Where(item => item.SecretaryUserId == secretary.UserId)
                    .Select(item => item.PractitionerUserId)
                    .Order()
                    .ToArray()))
                .ToArray()));
    }

    [HttpPut("secretaries/{secretaryUserId:guid}")]
    public async Task<IActionResult> Replace(
        Guid organizationId,
        Guid secretaryUserId,
        ReplaceSecretaryAgendaAssignmentsRequest request,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!TryGetUserId(out var actorUserId))
        {
            return Forbid();
        }

        var practitionerIds = request.PractitionerUserIds;
        if (practitionerIds is null ||
            practitionerIds.Count > MaximumPractitionerAssignments ||
            practitionerIds.Any(id => id == Guid.Empty) ||
            practitionerIds.Distinct().Count() != practitionerIds.Count)
        {
            return BadRequest(new { message = "La selección de agendas no es válida." });
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var administrator = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            actorUserId,
            cancellationToken);
        if (administrator?.Role != OrganizationRole.ClinicAdmin)
        {
            return Forbid();
        }

        var secretary = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            secretaryUserId,
            cancellationToken);
        if (secretary?.Role != OrganizationRole.Secretary)
        {
            return NotFound();
        }

        var existingAssignments = await database.SecretaryPractitionerAssignments
            .Where(item =>
                item.OrganizationId == organizationId &&
                item.SecretaryUserId == secretaryUserId)
            .ToListAsync(cancellationToken);
        var practitionerIdsToLock = existingAssignments
            .Select(item => item.PractitionerUserId)
            .Concat(practitionerIds)
            .Distinct()
            .Order()
            .ToArray();
        foreach (var practitionerId in practitionerIdsToLock)
        {
            var practitioner = await PractitionerScheduleLock.AcquireAsync(
                database,
                organizationId,
                practitionerId,
                cancellationToken);
            if (practitioner?.Role != OrganizationRole.Practitioner)
            {
                return BadRequest(new
                {
                    message = "Todas las agendas seleccionadas deben pertenecer a profesionales del consultorio.",
                });
            }
        }

        var existingIds = existingAssignments
            .Select(item => item.PractitionerUserId)
            .ToHashSet();
        var requestedIds = practitionerIds.ToHashSet();
        var changedAtUtc = timeProvider.GetUtcNow();
        foreach (var assignment in existingAssignments.Where(
                     item => !requestedIds.Contains(item.PractitionerUserId)))
        {
            database.SecretaryPractitionerAssignments.Remove(assignment);
            database.SecretaryAgendaAssignmentAuditEvents.Add(
                SecretaryAgendaAssignmentAuditEvent.Create(
                    organizationId,
                    secretaryUserId,
                    assignment.PractitionerUserId,
                    actorUserId,
                    SecretaryAgendaAssignmentChange.Revoked,
                    changedAtUtc));
        }

        foreach (var practitionerId in requestedIds.Where(id => !existingIds.Contains(id)))
        {
            database.SecretaryPractitionerAssignments.Add(
                SecretaryPractitionerAssignment.Create(
                    organizationId,
                    secretaryUserId,
                    practitionerId));
            database.SecretaryAgendaAssignmentAuditEvents.Add(
                SecretaryAgendaAssignmentAuditEvent.Create(
                    organizationId,
                    secretaryUserId,
                    practitionerId,
                    actorUserId,
                    SecretaryAgendaAssignmentChange.Granted,
                    changedAtUtc));
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new SecretaryAgendaAssignmentUpdateResponse(
            secretaryUserId,
            requestedIds.Order().ToArray()));
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private Task<bool> IsClinicAdministratorAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        database.OrganizationMemberships.AnyAsync(
            membership => membership.OrganizationId == organizationId &&
                membership.UserId == userId &&
                membership.Role == OrganizationRole.ClinicAdmin,
            cancellationToken);
}

public sealed record SecretaryAgendaAssignmentsResponse(
    IReadOnlyList<AssignmentPractitionerResponse> Practitioners,
    IReadOnlyList<AssignmentSecretaryResponse> Secretaries);

public sealed record AssignmentPractitionerResponse(Guid UserId, string? Email);

public sealed record AssignmentSecretaryResponse(
    Guid UserId,
    string? Email,
    IReadOnlyList<Guid> PractitionerUserIds);

public sealed record ReplaceSecretaryAgendaAssignmentsRequest(
    [param: Required]
    IReadOnlyList<Guid> PractitionerUserIds);

public sealed record SecretaryAgendaAssignmentUpdateResponse(
    Guid SecretaryUserId,
    IReadOnlyList<Guid> PractitionerUserIds);
