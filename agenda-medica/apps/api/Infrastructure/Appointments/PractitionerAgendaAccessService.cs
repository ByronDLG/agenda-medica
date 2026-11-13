using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Infrastructure.Appointments;

public sealed class PractitionerAgendaAccessService(AgendaDbContext database)
{
    public Task<OrganizationRole?> GetRoleAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        database.OrganizationMemberships
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.UserId == userId)
            .Select(item => (OrganizationRole?)item.Role)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> CanAccessPractitionerAgendaAsync(
        Guid organizationId,
        Guid userId,
        Guid practitionerUserId,
        CancellationToken cancellationToken)
    {
        var role = await GetRoleAsync(organizationId, userId, cancellationToken);
        if (role == OrganizationRole.Practitioner)
        {
            return userId == practitionerUserId;
        }

        return role == OrganizationRole.Secretary &&
            await database.SecretaryPractitionerAssignments
                .AsNoTracking()
                .Where(
                    item => item.OrganizationId == organizationId &&
                        item.SecretaryUserId == userId &&
                        item.PractitionerUserId == practitionerUserId)
                .Join(
                    database.OrganizationMemberships.AsNoTracking(),
                    assignment => new
                    {
                        assignment.OrganizationId,
                        UserId = assignment.PractitionerUserId,
                    },
                    membership => new { membership.OrganizationId, membership.UserId },
                    (_, membership) => membership)
                .AnyAsync(
                    membership => membership.Role == OrganizationRole.Practitioner,
                    cancellationToken);
    }

    public async Task<IReadOnlyList<AuthorizedPractitionerAgenda>> GetAuthorizedAgendasAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var role = await GetRoleAsync(organizationId, userId, cancellationToken);
        if (role == OrganizationRole.Practitioner)
        {
            return await database.OrganizationMemberships
                .AsNoTracking()
                .Where(item =>
                    item.OrganizationId == organizationId &&
                    item.UserId == userId &&
                    item.Role == OrganizationRole.Practitioner)
                .Select(item => new AuthorizedPractitionerAgenda(
                    item.UserId,
                    item.User.Email))
                .ToArrayAsync(cancellationToken);
        }

        if (role != OrganizationRole.Secretary)
        {
            return [];
        }

        return await database.SecretaryPractitionerAssignments
            .AsNoTracking()
            .Where(item =>
                item.OrganizationId == organizationId &&
                item.SecretaryUserId == userId)
            .Join(
                database.OrganizationMemberships.AsNoTracking(),
                assignment => new
                {
                    assignment.OrganizationId,
                    UserId = assignment.PractitionerUserId,
                },
                membership => new { membership.OrganizationId, membership.UserId },
                (_, membership) => membership)
            .Where(membership => membership.Role == OrganizationRole.Practitioner)
            .OrderBy(membership => membership.User.Email)
            .Select(membership => new AuthorizedPractitionerAgenda(
                membership.UserId,
                membership.User.Email))
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record AuthorizedPractitionerAgenda(Guid UserId, string? Email);
