namespace AgendaMedica.Api.Domain.Organizations;

public sealed class SecretaryPractitionerAssignment
{
    public Guid OrganizationId { get; private set; }

    public Guid SecretaryUserId { get; private set; }

    public Guid PractitionerUserId { get; private set; }

    public static SecretaryPractitionerAssignment Create(
        Guid organizationId,
        Guid secretaryUserId,
        Guid practitionerUserId)
    {
        if (organizationId == Guid.Empty ||
            secretaryUserId == Guid.Empty ||
            practitionerUserId == Guid.Empty ||
            secretaryUserId == practitionerUserId)
        {
            throw new ArgumentException("Distinct organization member identifiers are required.");
        }

        return new SecretaryPractitionerAssignment
        {
            OrganizationId = organizationId,
            SecretaryUserId = secretaryUserId,
            PractitionerUserId = practitionerUserId,
        };
    }
}
