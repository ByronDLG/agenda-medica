using AgendaMedica.Api.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Data;

public static class PractitionerScheduleLock
{
    public static Task<OrganizationMembership?> AcquireAsync(
        AgendaDbContext database,
        Guid organizationId,
        Guid practitionerUserId,
        CancellationToken cancellationToken) =>
        database.OrganizationMemberships
            .FromSqlInterpolated(
                $"SELECT * FROM [OrganizationMemberships] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [UserId] = {practitionerUserId}")
            .SingleOrDefaultAsync(cancellationToken);
}
