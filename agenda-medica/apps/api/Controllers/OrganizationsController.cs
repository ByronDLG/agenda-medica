using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain;
using AgendaMedica.Api.Domain.Organizations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/settings")]
public sealed class OrganizationsController(
    AgendaDbContext database,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!await IsClinicAdministratorAsync(organizationId, cancellationToken))
        {
            return Forbid();
        }

        var organization = await database.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
        if (organization is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(organization));
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        Guid organizationId,
        UpdateOrganizationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!await IsClinicAdministratorAsync(organizationId, cancellationToken))
        {
            return Forbid();
        }

        var organization = await database.Organizations
            .SingleOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
        if (organization is null)
        {
            return NotFound();
        }

        if (!Enum.TryParse(
                request.AppointmentConfirmationMode,
                ignoreCase: false,
                out AppointmentConfirmationMode mode) ||
            !Enum.IsDefined(mode))
        {
            return BadRequest(new { message = "La política de confirmación no es válida." });
        }

        organization.UpdateAppointmentConfirmationMode(mode);
        await database.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(organization));
    }

    private async Task<bool> IsClinicAdministratorAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return false;
        }

        return await database.OrganizationMemberships.AnyAsync(
            membership => membership.OrganizationId == organizationId &&
                membership.UserId == userId &&
                membership.Role == OrganizationRole.ClinicAdmin,
            cancellationToken);
    }

    private static OrganizationSettingsResponse ToResponse(Organization organization) =>
        new(
            organization.Id,
            organization.Name,
            organization.CountryCode,
            organization.TimeZoneId,
            organization.AppointmentConfirmationMode.ToString());
}

public sealed record UpdateOrganizationSettingsRequest(
    [param: Required]
    [param: RegularExpression("^(ManualApproval|Automatic)$")]
    string AppointmentConfirmationMode);

public sealed record OrganizationSettingsResponse(
    Guid Id,
    string Name,
    string CountryCode,
    string TimeZoneId,
    string AppointmentConfirmationMode);
