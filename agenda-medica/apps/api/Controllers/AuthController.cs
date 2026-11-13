using System.ComponentModel.DataAnnotations;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/auth")]
public sealed class AuthController(
    IAntiforgery antiforgery,
    AgendaDbContext database,
    IHostEnvironment environment,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";
        Response.Cookies.Append(
            "XSRF-TOKEN",
            tokens.RequestToken
                ?? throw new InvalidOperationException("Could not issue an antiforgery token."),
            new CookieOptions
            {
                HttpOnly = false,
                IsEssential = true,
                Path = "/",
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps || !environment.IsDevelopment(),
            });

        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        var result = await signInManager.PasswordSignInAsync(
            request.Email.Trim(),
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Unauthorized(new { message = "Correo o contraseña no válidos." });
        }

        return NoContent();
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var organizations = await database.OrganizationMemberships
            .Where(membership => membership.UserId == user.Id)
            .Select(membership => new OrganizationAccess(
                membership.OrganizationId,
                membership.Organization.Name,
                membership.Role.ToString()))
            .ToListAsync(cancellationToken);

        return Ok(new CurrentUserResponse(user.Email, organizations));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        await signInManager.SignOutAsync();
        return NoContent();
    }
}

public sealed record LoginRequest(
    [param: Required]
    [param: EmailAddress]
    [param: StringLength(254)]
    string Email,
    [param: Required]
    [param: StringLength(128, MinimumLength = 12)]
    string Password);

public sealed record OrganizationAccess(Guid Id, string Name, string Role);

public sealed record CurrentUserResponse(
    string? Email,
    IReadOnlyList<OrganizationAccess> Organizations);
