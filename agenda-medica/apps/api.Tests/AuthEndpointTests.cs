using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgendaMedica.Api.Tests;

public sealed class AuthEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public AuthEndpointTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task LoginRequiresAnAntiforgeryToken()
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Email = "nobody@example.invalid", Password = "Dummy-Password-12345" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CurrentUserRequiresAuthentication()
    {
        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvitationCreationRequiresAuthentication()
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/organizations/00000000-0000-0000-0000-000000000001/invitations",
            new { Email = "staff@example.invalid", Role = "Secretary" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OrganizationSettingsRequireAuthentication()
    {
        var response = await client.GetAsync(
            "/api/v1/organizations/00000000-0000-0000-0000-000000000001/settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OrganizationSettingsUpdateRequiresAuthentication()
    {
        var response = await client.PutAsJsonAsync(
            "/api/v1/organizations/00000000-0000-0000-0000-000000000001/settings",
            new { AppointmentConfirmationMode = "Automatic" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WeeklyScheduleRequiresAuthentication()
    {
        var response = await client.GetAsync(
            "/api/v1/organizations/00000000-0000-0000-0000-000000000001/schedule");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WeeklyScheduleUpdateRequiresAuthentication()
    {
        var response = await client.PutAsJsonAsync(
            "/api/v1/organizations/00000000-0000-0000-0000-000000000001/schedule",
            new { Windows = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SchedulePreviewRequiresAuthentication()
    {
        var response = await client.GetAsync(
            "/api/v1/organizations/00000000-0000-0000-0000-000000000001/schedule/preview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ScheduleExceptionCreationRequiresAuthentication()
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/organizations/00000000-0000-0000-0000-000000000001/schedule/exceptions",
            new { LocalDate = "2026-12-25" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AppointmentReadCreateAndStatusUpdatesRequireAuthentication()
    {
        var organizationId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var collectionPath = $"/api/v1/organizations/{organizationId}/appointments";

        var readResponse = await client.GetAsync(collectionPath);
        Assert.Equal(HttpStatusCode.Unauthorized, readResponse.StatusCode);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, collectionPath)
        {
            Content = JsonContent.Create(new
            {
                LocalDate = "2026-12-25",
                StartTime = "09:00",
                PatientName = "Persona de prueba",
                ContactEmail = "persona@example.test",
                ContactPhone = (string?)null,
                ContactConsent = true,
            }),
        };
        createRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);

        var updateResponse = await client.PatchAsJsonAsync(
            $"{collectionPath}/{appointmentId}/status",
            new { Status = "Confirmed" });
        Assert.Equal(HttpStatusCode.Unauthorized, updateResponse.StatusCode);
    }

    [Fact]
    public async Task PublicBookingLinkManagementRequiresAuthentication()
    {
        var path = "/api/v1/organizations/00000000-0000-0000-0000-000000000001/public-booking-link";

        var readResponse = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, readResponse.StatusCode);

        var rotateResponse = await client.PutAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, rotateResponse.StatusCode);
    }

    [Fact]
    public async Task SecretaryAgendaAssignmentManagementRequiresAuthentication()
    {
        var path = "/api/v1/organizations/00000000-0000-0000-0000-000000000001/secretary-agenda-assignments";

        var readResponse = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, readResponse.StatusCode);

        var updateResponse = await client.PutAsJsonAsync(
            $"{path}/secretaries/00000000-0000-0000-0000-000000000002",
            new { PractitionerUserIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Unauthorized, updateResponse.StatusCode);
    }

    [Fact]
    public async Task PublicAvailabilityRequiresAValidLinkToken()
    {
        var response = await client.GetAsync("/api/v1/public/booking/availability");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicBookingRequestRequiresAntiforgeryToken()
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/public/booking/requests",
            new
            {
                LocalDate = "2026-12-25",
                StartTime = "09:00",
                PatientName = "Persona de prueba",
                ContactEmail = "persona@example.test",
                ContactPhone = (string?)null,
                ContactConsent = true,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvitationAcceptanceRequiresAntiforgeryToken()
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new
            {
                Token = "not-a-valid-invitation-token-which-is-long-enough",
                Password = "Secure-Example-123!",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CsrfEndpointIssuesBrowserTokensWithoutCaching()
    {
        var response = await client.GetAsync("/api/v1/auth/csrf");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var cookies = string.Join(
            ";",
            response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("XSRF-TOKEN=", cookies);
        Assert.Contains("AgendaMedica.Antiforgery=", cookies);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }
}
