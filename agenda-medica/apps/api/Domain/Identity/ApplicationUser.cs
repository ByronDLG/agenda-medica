using AgendaMedica.Api.Domain.Organizations;
using Microsoft.AspNetCore.Identity;

namespace AgendaMedica.Api.Domain.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ICollection<OrganizationMembership> OrganizationMemberships { get; private set; } = [];
}
