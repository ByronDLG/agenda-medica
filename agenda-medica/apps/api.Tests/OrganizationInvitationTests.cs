using AgendaMedica.Api.Domain.Organizations;

namespace AgendaMedica.Api.Tests;

public sealed class OrganizationInvitationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InvitationIsPendingUntilAccepted()
    {
        var invitation = CreateInvitation();

        Assert.True(invitation.IsPendingAt(CreatedAt.AddHours(1)));
        invitation.Accept(CreatedAt.AddHours(1));
        Assert.False(invitation.IsPendingAt(CreatedAt.AddHours(2)));
    }

    [Fact]
    public void ExpiredInvitationIsNotPending()
    {
        var invitation = CreateInvitation();

        Assert.False(invitation.IsPendingAt(CreatedAt.AddHours(49)));
    }

    [Fact]
    public void RevokedInvitationCannotBeAccepted()
    {
        var invitation = CreateInvitation();
        invitation.Revoke(CreatedAt.AddHours(1));

        Assert.False(invitation.IsPendingAt(CreatedAt.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() =>
            invitation.Accept(CreatedAt.AddHours(2)));
    }

    [Fact]
    public void InvitationCannotGrantClinicAdministratorRole()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OrganizationInvitation.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "person@example.test",
                "PERSON@EXAMPLE.TEST",
                OrganizationRole.ClinicAdmin,
                new byte[32],
                CreatedAt,
                CreatedAt.AddHours(48)));
    }

    private static OrganizationInvitation CreateInvitation() =>
        OrganizationInvitation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "person@example.test",
            "PERSON@EXAMPLE.TEST",
            OrganizationRole.Secretary,
            new byte[32],
            CreatedAt,
            CreatedAt.AddHours(48));
}
