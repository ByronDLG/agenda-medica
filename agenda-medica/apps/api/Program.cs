using System.Threading.RateLimiting;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain.Identity;
using AgendaMedica.Api.Infrastructure;
using AgendaMedica.Api.Infrastructure.Appointments;
using AgendaMedica.Api.Infrastructure.Email;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var connectionString = SqlServerConnection.Create(
    builder.Configuration,
    builder.Environment);

builder.Services.AddDbContext<AgendaDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IValidateOptions<SmtpEmailOptions>, SmtpEmailOptionsValidator>();
builder.Services.AddOptions<SmtpEmailOptions>()
    .Bind(builder.Configuration.GetSection(SmtpEmailOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddTransient<IEmailSender, MailKitEmailSender>();
builder.Services.AddScoped<AppointmentBookingService>();
builder.Services.AddScoped<AppointmentNotificationProcessor>();
builder.Services.AddScoped<PublicBookingAvailabilityService>();
builder.Services.AddScoped<PractitionerAgendaAccessService>();
builder.Services.AddHostedService<AppointmentNotificationBackgroundService>();
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AgendaDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "AgendaMedica.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "AgendaMedica.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddControllers();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("invitation-accept", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.AddPolicy("public-booking-read", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.AddPolicy("public-booking-write", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();
if (args is ["--bootstrap-admin"])
{
    var exitCode = await FirstAdministratorBootstrapper.RunAsync(app.Services);
    await app.DisposeAsync();
    return exitCode;
}

var api = app.MapGroup("/api/v1");

api.MapGet("/health", async (
        AgendaDbContext database,
        CancellationToken cancellationToken) =>
    {
        if (!await database.Database.CanConnectAsync(cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new { status = "ok" });
    })
    .AllowAnonymous()
    .WithName("GetHealth");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
return 0;

public partial class Program { }
