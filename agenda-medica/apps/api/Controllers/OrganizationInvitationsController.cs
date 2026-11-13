using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain;
using AgendaMedica.Api.Domain.Identity;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Infrastructure.Email;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgendaMedica.Api.Controllers;

[ApiController]
[Authorize]
public sealed class OrganizationInvitationsController(
    AgendaDbContext database,
    IAntiforgery antiforgery,
    IEmailSender emailSender,
    IHostEnvironment environment,
    ILogger<OrganizationInvitationsController> logger,
    IOptions<SmtpEmailOptions> smtpOptions,
    IConfiguration configuration,
    TimeProvider timeProvider,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(48);

    [HttpPost("api/v1/organizations/{organizationId:guid}/invitations")]
    public async Task<IActionResult> Create(
        Guid organizationId,
        CreateOrganizationInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        var creator = await database.Users
            .SingleOrDefaultAsync(user => user.Id == UserId, cancellationToken);
        if (creator is null)
        {
            return Unauthorized();
        }

        var isAdmin = await database.OrganizationMemberships.AnyAsync(
            membership => membership.OrganizationId == organizationId &&
                membership.UserId == creator.Id &&
                membership.Role == OrganizationRole.ClinicAdmin,
            cancellationToken);
        if (!isAdmin)
        {
            return Forbid();
        }

        var smtp = smtpOptions.Value;
        if (!smtp.Enabled)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "El envío de invitaciones no está configurado.");
        }

        if (!TryCreateInvitationUri(out var invitationBaseUri))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "La URL de aceptación de invitaciones no está configurada correctamente.");
        }

        var email = request.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();
        var now = timeProvider.GetUtcNow();
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        var organization = await database.Organizations
            .SingleOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
        if (organization is null)
        {
            return NotFound();
        }

        var role = request.Role switch
        {
            "Practitioner" => OrganizationRole.Practitioner,
            "Secretary" => OrganizationRole.Secretary,
            _ => (OrganizationRole?)null,
        };
        if (role is null)
        {
            return BadRequest(new { message = "El rol de invitación no es válido." });
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var existingUser = await database.Users.AnyAsync(
            user => user.NormalizedEmail == normalizedEmail,
            cancellationToken);
        if (existingUser)
        {
            return Conflict(new { message = "No se puede invitar esa cuenta a este consultorio." });
        }

        var existingPendingInvitation = await database.OrganizationInvitations
            .SingleOrDefaultAsync(
                item => item.OrganizationId == organizationId &&
                    item.EmailNormalized == normalizedEmail &&
                    item.AcceptedAt == null &&
                    item.RevokedAt == null,
                cancellationToken);
        if (existingPendingInvitation is not null)
        {
            if (existingPendingInvitation.IsPendingAt(now))
            {
                return Conflict(new { message = "Ya existe una invitación pendiente para esa cuenta." });
            }

            existingPendingInvitation.Revoke(now);
        }

        var invitation = OrganizationInvitation.Create(
            organizationId,
            creator.Id,
            email,
            normalizedEmail,
            role.Value,
            tokenHash,
            now,
            now.Add(InvitationLifetime));
        database.OrganizationInvitations.Add(invitation);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var invitationUri = new UriBuilder(invitationBaseUri)
        {
            Fragment = $"token={Uri.EscapeDataString(token)}",
        }.Uri;

        try
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    email,
                    "Invitación para unirte a Agenda Médica",
                    $"Te invitaron a colaborar en {organization.Name} con el rol {role.Value}. " +
                    $"Para aceptar, abre este enlace antes de {invitation.ExpiresAt:yyyy-MM-dd HH:mm} UTC: " +
                    invitationUri),
                cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            logger.LogWarning(
                "Email delivery failed for invitation {InvitationId}.",
                invitation.Id);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    message = "No se pudo entregar el correo. Revoca esta invitación y vuelve a intentarlo.",
                    invitationId = invitation.Id,
                });
        }

        return Ok(new OrganizationInvitationResponse(invitation.Id, invitation.ExpiresAt));
    }

    [HttpDelete("api/v1/organizations/{organizationId:guid}/invitations/{invitationId:guid}")]
    public async Task<IActionResult> Revoke(
        Guid organizationId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        var creatorId = UserId;
        var isAdmin = await database.OrganizationMemberships.AnyAsync(
            membership => membership.OrganizationId == organizationId &&
                membership.UserId == creatorId &&
                membership.Role == OrganizationRole.ClinicAdmin,
            cancellationToken);
        if (!isAdmin)
        {
            return Forbid();
        }

        var invitation = await database.OrganizationInvitations.SingleOrDefaultAsync(
            item => item.Id == invitationId && item.OrganizationId == organizationId,
            cancellationToken);
        if (invitation is null)
        {
            return NotFound();
        }

        if (!invitation.IsPendingAt(timeProvider.GetUtcNow()))
        {
            return Conflict(new { message = "La invitación ya no está pendiente." });
        }

        invitation.Revoke(timeProvider.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("invitation-accept")]
    [HttpPost("api/v1/invitations/accept")]
    public async Task<IActionResult> Accept(
        AcceptOrganizationInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        var now = timeProvider.GetUtcNow();
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Token));
        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var invitation = await database.OrganizationInvitations
            .SingleOrDefaultAsync(
                item => item.TokenHash == tokenHash,
                cancellationToken);
        if (invitation is null || !invitation.IsPendingAt(now))
        {
            return InvalidInvitation();
        }

        var userExists = await database.Users.AnyAsync(
            user => user.NormalizedEmail == invitation.EmailNormalized,
            cancellationToken);
        if (userExists)
        {
            return InvalidInvitation();
        }

        var user = new ApplicationUser
        {
            UserName = invitation.Email,
            Email = invitation.Email,
            EmailConfirmed = true,
        };
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                createResult.Errors
                    .GroupBy(error => error.Code)
                    .ToDictionary(
                    errors => errors.Key,
                    errors => errors.Select(error => error.Description).ToArray())));
        }

        var organization = await database.Organizations.SingleAsync(
            item => item.Id == invitation.OrganizationId,
            cancellationToken);
        database.OrganizationMemberships.Add(
            OrganizationMembership.Create(user, organization, invitation.Role));
        invitation.Accept(now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return NoContent();
    }

    private Guid UserId =>
        Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId)
            ? userId
            : Guid.Empty;

    private bool TryCreateInvitationUri(out Uri invitationBaseUri)
    {
        invitationBaseUri = null!;
        var configuredUrl = configuration["Invitation:AcceptUrl"];
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var parsed) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            (parsed.Scheme != Uri.UriSchemeHttps &&
                !(environment.IsDevelopment() && parsed.IsLoopback && parsed.Scheme == Uri.UriSchemeHttp)))
        {
            return false;
        }

        invitationBaseUri = parsed;
        return true;
    }

    private BadRequestObjectResult InvalidInvitation() =>
        BadRequest(new { message = "La invitación no es válida o venció." });
}

public sealed record CreateOrganizationInvitationRequest(
    [param: Required]
    [param: EmailAddress]
    [param: StringLength(254)]
    string Email,
    [param: Required]
    [param: RegularExpression("^(Practitioner|Secretary)$")]
    string Role);

public sealed record AcceptOrganizationInvitationRequest(
    [param: Required]
    [param: StringLength(128, MinimumLength = 40)]
    string Token,
    [param: Required]
    [param: StringLength(128, MinimumLength = 12)]
    string Password);

public sealed record OrganizationInvitationResponse(Guid Id, DateTimeOffset ExpiresAt);
