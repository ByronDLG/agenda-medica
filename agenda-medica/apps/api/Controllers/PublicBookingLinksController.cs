using System.Security.Claims;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Infrastructure.Appointments;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/public-booking-link")]
public sealed class PublicBookingLinksController(
    AgendaDbContext database,
    IAntiforgery antiforgery,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryGetUserId(out var userId) ||
            !await IsPractitionerAsync(organizationId, userId, cancellationToken))
        {
            return Forbid();
        }

        var link = await database.PublicBookingLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OrganizationId == organizationId &&
                    item.PractitionerUserId == userId,
                cancellationToken);
        return Ok(new PublicBookingLinkStatusResponse(
            link is not null,
            link?.CreatedAtUtc));
    }

    [HttpPut]
    public async Task<IActionResult> Rotate(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var membership = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            userId,
            cancellationToken);
        if (membership is null || membership.Role != OrganizationRole.Practitioner)
        {
            return Forbid();
        }

        var (token, tokenHash) = PublicBookingToken.Create();
        var link = await database.PublicBookingLinks.SingleOrDefaultAsync(
            item => item.OrganizationId == organizationId &&
                item.PractitionerUserId == userId,
            cancellationToken);
        if (link is null)
        {
            database.PublicBookingLinks.Add(PublicBookingLink.Create(
                organizationId,
                userId,
                tokenHash,
                timeProvider.GetUtcNow()));
        }
        else
        {
            link.Rotate(tokenHash, timeProvider.GetUtcNow());
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new RotatedPublicBookingLinkResponse(token));
    }

    [HttpDelete]
    public async Task<IActionResult> Revoke(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var membership = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            userId,
            cancellationToken);
        if (membership is null || membership.Role != OrganizationRole.Practitioner)
        {
            return Forbid();
        }

        var link = await database.PublicBookingLinks.SingleOrDefaultAsync(
            item => item.OrganizationId == organizationId &&
                item.PractitionerUserId == userId,
            cancellationToken);
        if (link is not null)
        {
            database.PublicBookingLinks.Remove(link);
            await database.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private Task<bool> IsPractitionerAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        database.OrganizationMemberships.AnyAsync(
            membership => membership.OrganizationId == organizationId &&
                membership.UserId == userId &&
                membership.Role == OrganizationRole.Practitioner,
            cancellationToken);
}

public sealed record PublicBookingLinkStatusResponse(bool IsEnabled, DateTimeOffset? CreatedAtUtc);

public sealed record RotatedPublicBookingLinkResponse(string Token);
