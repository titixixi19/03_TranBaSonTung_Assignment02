using System.Text.Json;
using FrontEnd.Models;
using FrontEnd.Services;
using Microsoft.AspNetCore.Mvc;

namespace FrontEnd.Controllers;

public class AuthController : Controller
{
    private readonly ApiClient _api;

    public AuthController(ApiClient api) => _api = api;

    // ----- Login / Logout -----

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(HttpContext.Session.GetString(SessionKeys.Token)))
        {
            return RedirectToHome();
        }
        await LoadExternalProvidersAsync();
        return View(new LoginVM { ReturnUrl = returnUrl });
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginVM model)
    {
        if (!ModelState.IsValid)
        {
            await LoadExternalProvidersAsync();
            return View(model);
        }

        var result = await _api.SendAsync(HttpMethod.Post, "api/Auth/login",
            new { model.Email, model.Password }, throwOnUnauthorized: false);
        if (!result.Success || result.Data == null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            ViewBag.ShowResend = result.Data is { } d && d.TryGetProperty("status", out var s) && s.GetString() == "EmailNotConfirmed";
            await LoadExternalProvidersAsync();
            return View(model);
        }

        var name = SignIn(result.Data.Value);
        TempData["Success"] = $"Welcome, {name}!";
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }
        return RedirectToHome();
    }

    [HttpPost]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        TempData["Success"] = "You have been logged out.";
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied() => View();

    // ----- Register + email confirmation -----

    [HttpGet]
    public IActionResult Register() => View(new RegisterVM());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterVM model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _api.SendAsync(HttpMethod.Post, "api/Auth/register", new
        {
            model.AccountName,
            model.AccountEmail,
            model.Password,
            model.ConfirmPassword
        }, throwOnUnauthorized: false);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        return ShowMessage("Check your email", result.Message,
            "We sent a confirmation link to " + model.AccountEmail + ". Click it to activate your account.");
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? code)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
        {
            return ShowMessage("Email confirmation", "The confirmation link is invalid.", success: false);
        }

        var result = await _api.SendAsync(HttpMethod.Post, "api/Auth/confirm-email",
            new { userId, code }, throwOnUnauthorized: false);
        return ShowMessage("Email confirmation", result.Message, success: result.Success);
    }

    [HttpGet]
    public IActionResult ResendConfirmation() => View("EmailForm", new EmailFormVM());

    [HttpPost]
    public async Task<IActionResult> ResendConfirmation(EmailFormVM model)
    {
        if (!ModelState.IsValid)
        {
            return View("EmailForm", model);
        }
        var result = await _api.SendAsync(HttpMethod.Post, "api/Auth/resend-confirmation",
            new { model.Email }, throwOnUnauthorized: false);
        return ShowMessage("Check your email", result.Message, success: result.Success);
    }

    // ----- Forgot / reset password -----

    [HttpGet]
    public IActionResult ForgotPassword() => View("EmailForm", new EmailFormVM());

    [HttpPost]
    public async Task<IActionResult> ForgotPassword(EmailFormVM model)
    {
        if (!ModelState.IsValid)
        {
            return View("EmailForm", model);
        }
        var result = await _api.SendAsync(HttpMethod.Post, "api/Auth/forgot-password",
            new { model.Email }, throwOnUnauthorized: false);
        return ShowMessage("Check your email", result.Message, success: result.Success);
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? code)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(code))
        {
            return ShowMessage("Reset password", "The reset link is invalid.", success: false);
        }
        return View(new ResetPasswordVM { Email = email, Code = code });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordVM model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _api.SendAsync(HttpMethod.Post, "api/Auth/reset-password", new
        {
            model.Email,
            model.Code,
            model.Password,
            model.ConfirmPassword
        }, throwOnUnauthorized: false);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Login));
    }

    // ----- Google sign-in -----

    // Sends the browser to the Web API, which redirects to Google and back
    [HttpGet]
    public IActionResult ExternalLogin(string provider)
    {
        var callback = Url.Action(nameof(ExternalLoginCallback), "Auth", null, Request.Scheme)!;
        var target = new Uri(_api.BaseAddress,
            $"api/Auth/external-login?provider={Uri.EscapeDataString(provider)}&returnUrl={Uri.EscapeDataString(callback)}");
        return Redirect(target.ToString());
    }

    // The API redirects here with a one-time code; we exchange it for the JWT server-to-server
    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? code, string? error)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            TempData["Error"] = error ?? "External login failed.";
            return RedirectToAction(nameof(Login));
        }

        var result = await _api.SendAsync(HttpMethod.Post, "api/Auth/exchange-code", new { code }, throwOnUnauthorized: false);
        if (!result.Success || result.Data == null)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Login));
        }

        var name = SignIn(result.Data.Value);
        TempData["Success"] = $"Welcome, {name}!";
        return RedirectToHome();
    }

    // ----- Helpers -----

    // Stores the JWT and user info returned by the API in the session; returns the user's name
    private string SignIn(JsonElement data)
    {
        string Get(string name) => data.TryGetProperty(name, out var v) ? v.ToString() : string.Empty;

        HttpContext.Session.SetString(SessionKeys.Token, Get("token"));
        HttpContext.Session.SetString(SessionKeys.AccountId, Get("accountID"));
        HttpContext.Session.SetString(SessionKeys.AccountName, Get("accountName"));
        HttpContext.Session.SetString(SessionKeys.AccountEmail, Get("accountEmail"));
        HttpContext.Session.SetString(SessionKeys.Role, Get("role"));
        return Get("accountName");
    }

    private async Task LoadExternalProvidersAsync()
    {
        try
        {
            ViewBag.ExternalProviders = await _api.GetAsync<List<ExternalProviderVM>>("api/Auth/external-providers")
                                        ?? new List<ExternalProviderVM>();
        }
        catch (ApiException)
        {
            ViewBag.ExternalProviders = new List<ExternalProviderVM>();
        }
    }

    private IActionResult ShowMessage(string title, string message, string? detail = null, bool success = true)
    {
        ViewBag.Title = title;
        ViewBag.Message = message;
        ViewBag.Detail = detail;
        ViewBag.Success = success;
        return View("Message");
    }

    private IActionResult RedirectToHome() =>
        HttpContext.Session.GetString(SessionKeys.Role) switch
        {
            "Admin" => RedirectToAction("Index", "Accounts"),
            "Staff" => RedirectToAction("Index", "NewsArticles"),
            _ => RedirectToAction("Index", "Home")
        };
}
