using System.Security.Cryptography;
using BackEnd.BusinessObjects;
using BackEnd.DTOs;
using BackEnd.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace BackEnd.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAccountSecurityService _security;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly JwtTokenService _jwt;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;

    public AuthController(
        IAccountSecurityService security,
        SignInManager<ApplicationUser> signInManager,
        JwtTokenService jwt,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _security = security;
        _signInManager = signInManager;
        _jwt = jwt;
        _cache = cache;
        _configuration = configuration;
    }

    private LoginResponse StaffResponse(SystemAccount account) =>
        _jwt.CreateLoginResponse(account.AccountID, account.AccountName ?? "", account.AccountEmail ?? "", AccountRoles.StaffName);

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        // Default admin account comes from appsettings.json
        var adminEmail = _configuration["AdminAccount:Email"];
        var adminPassword = _configuration["AdminAccount:Password"];
        if (string.Equals(request.Email, adminEmail, StringComparison.OrdinalIgnoreCase) && request.Password == adminPassword)
        {
            return Ok(_jwt.CreateLoginResponse(0, "Administrator", adminEmail!, AccountRoles.AdminName));
        }

        // Staff accounts are verified by ASP.NET Core Identity (hashed password, lockout, confirmed email)
        var result = await _security.PasswordLoginAsync(request.Email, request.Password);
        if (result.Succeeded)
        {
            return Ok(StaffResponse(result.Account!));
        }

        var body = new { message = result.Message, status = result.Status.ToString() };
        return result.Status == LoginStatus.NotAllowed
            ? StatusCode(StatusCodes.Status403Forbidden, body)
            : Unauthorized(body);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _security.RegisterAsync(request.AccountName, request.AccountEmail, request.Password);
        return result.Succeeded ? Ok(new { message = result.Message }) : BadRequest(new { message = result.Message });
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request)
    {
        var result = await _security.ConfirmEmailAsync(request.UserId, request.Code);
        return result.Succeeded ? Ok(new { message = result.Message }) : BadRequest(new { message = result.Message });
    }

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation([FromBody] EmailRequest request)
    {
        await _security.ResendConfirmationEmailAsync(request.Email);
        return Ok(new { message = "If the account exists and is not confirmed yet, a new confirmation email has been sent." });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] EmailRequest request)
    {
        await _security.ForgotPasswordAsync(request.Email);
        return Ok(new { message = "If the email is registered, a password reset link has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _security.ResetPasswordAsync(request.Email, request.Code, request.Password);
        return result.Succeeded ? Ok(new { message = result.Message }) : BadRequest(new { message = result.Message });
    }

    // ----- External login (Google) -----

    [HttpGet("external-providers")]
    public async Task<IActionResult> ExternalProviders()
    {
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        return Ok(schemes.Select(s => new { name = s.Name, displayName = s.DisplayName }));
    }

    // Only redirect back to the FrontEnd origins listed in appsettings.json (prevents open redirects)
    private bool IsAllowedReturnUrl(string? returnUrl)
    {
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var target)) return false;
        var origins = _configuration.GetSection("FrontEnd:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        return origins.Any(o => Uri.TryCreate(o, UriKind.Absolute, out var allowed)
                                && allowed.Scheme == target.Scheme && allowed.Host == target.Host && allowed.Port == target.Port);
    }

    private static string AppendQuery(string url, string key, string value) =>
        url + (url.Contains('?') ? "&" : "?") + key + "=" + Uri.EscapeDataString(value);

    // The browser is sent here by the FrontEnd; we challenge Google and come back to the callback below
    [HttpGet("external-login")]
    public async Task<IActionResult> ExternalLogin([FromQuery] string provider, [FromQuery] string returnUrl)
    {
        if (!IsAllowedReturnUrl(returnUrl)) return BadRequest(new { message = "Invalid return URL." });

        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        if (!schemes.Any(s => s.Name == provider)) return BadRequest(new { message = "Unknown login provider." });

        var callbackUrl = Url.Action(nameof(ExternalLoginCallback), new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, callbackUrl);
        return Challenge(properties, provider);
    }

    [HttpGet("external-login-callback")]
    public async Task<IActionResult> ExternalLoginCallback([FromQuery] string returnUrl, [FromQuery] string? remoteError = null)
    {
        if (!IsAllowedReturnUrl(returnUrl)) return BadRequest(new { message = "Invalid return URL." });

        if (remoteError != null)
            return Redirect(AppendQuery(returnUrl, "error", "External login was cancelled or failed."));

        var info = await _signInManager.GetExternalLoginInfoAsync();
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (info == null)
            return Redirect(AppendQuery(returnUrl, "error", "Could not read the external login information."));

        var result = await _security.ExternalLoginAsync(info);
        if (!result.Succeeded)
            return Redirect(AppendQuery(returnUrl, "error", result.Message));

        // Never put the JWT in the URL: hand out a short-lived one-time code instead
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _cache.Set("external-login:" + code, StaffResponse(result.Account!), TimeSpan.FromMinutes(2));
        return Redirect(AppendQuery(returnUrl, "code", code));
    }

    // Called by the FrontEnd server to swap the one-time code for the JWT
    [HttpPost("exchange-code")]
    public ActionResult<LoginResponse> ExchangeCode([FromBody] ExchangeCodeRequest request)
    {
        var key = "external-login:" + request.Code;
        if (!_cache.TryGetValue(key, out LoginResponse? response) || response == null)
        {
            return Unauthorized(new { message = "The login code is invalid or has expired." });
        }
        _cache.Remove(key);
        return Ok(response);
    }
}
