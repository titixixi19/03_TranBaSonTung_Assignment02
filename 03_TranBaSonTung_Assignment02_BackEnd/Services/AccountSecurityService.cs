using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using BackEnd.BusinessObjects;
using BackEnd.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BackEnd.Services;

public class AccountSecurityService : IAccountSecurityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ISystemAccountRepository _accounts;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AccountSecurityService> _logger;

    public AccountSecurityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ISystemAccountRepository accounts,
        IEmailService emailService,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<AccountSecurityService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _accounts = accounts;
        _emailService = emailService;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    private string FrontEndUrl => (_configuration["FrontEnd:BaseUrl"] ?? "http://localhost:5236/").TrimEnd('/');

    private bool IsAdminEmail(string email) =>
        string.Equals(email, _configuration["AdminAccount:Email"], StringComparison.OrdinalIgnoreCase);

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static string? Decode(string code)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return null; }
    }

    private static ApplicationUser NewUser(SystemAccount account, bool emailConfirmed) => new()
    {
        UserName = account.AccountEmail,
        Email = account.AccountEmail,
        EmailConfirmed = emailConfirmed,
        AccountID = account.AccountID
    };

    private Task<ApplicationUser?> FindByAccountIdAsync(short accountId) =>
        _userManager.Users.FirstOrDefaultAsync(u => u.AccountID == accountId);

    // An email is taken if it is the admin email, another SystemAccount's email, or another Identity user's email
    private async Task<bool> EmailInUseAsync(string email, short? excludeAccountId = null)
    {
        if (IsAdminEmail(email) || _accounts.EmailExists(email, excludeAccountId)) return true;
        var user = await _userManager.FindByEmailAsync(email);
        return user != null && (!excludeAccountId.HasValue || user.AccountID != excludeAccountId);
    }

    // Runs the Identity password policy before anything is written to the database
    private async Task<IdentityResult> ValidatePasswordAsync(ApplicationUser user, string password)
    {
        var errors = new List<IdentityError>();
        foreach (var validator in _userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_userManager, user, password);
            if (!result.Succeeded) errors.AddRange(result.Errors);
        }
        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }

    private async Task SendLinkAsync(string email, string subject, string intro, string linkText, string link)
    {
        if (_environment.IsDevelopment())
        {
            _logger.LogInformation("{Subject} link for {Email}: {Link}", subject, email, link);
        }
        var body = $"<p>{HtmlEncoder.Default.Encode(intro)}</p>" +
                   $"<p><a href=\"{HtmlEncoder.Default.Encode(link)}\">{HtmlEncoder.Default.Encode(linkText)}</a></p>" +
                   "<p>If you did not request this, you can ignore this email.</p>";
        await _emailService.SendAsync(email, subject, body);
    }

    private async Task SendConfirmationEmailAsync(ApplicationUser user)
    {
        var code = Encode(await _userManager.GenerateEmailConfirmationTokenAsync(user));
        var link = $"{FrontEndUrl}/Auth/ConfirmEmail?userId={Uri.EscapeDataString(user.Id)}&code={code}";
        await SendLinkAsync(user.Email!, "Confirm your FU News account",
            "Thank you for registering. Please confirm your email address:", "Confirm my email", link);
    }

    public async Task<SecurityResult> PasswordLoginAsync(string email, string password)
    {
        const string invalid = "Invalid email or password.";
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return SecurityResult.Fail(invalid, LoginStatus.InvalidCredentials);

        // Checks lockout and email confirmation; wrong passwords count towards the lockout
        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return SecurityResult.Fail("This account is locked because of too many failed attempts. Please try again later.", LoginStatus.LockedOut);
        if (result.IsNotAllowed)
        {
            // Only reveal the "not confirmed" state to someone who knows the password
            return await _userManager.CheckPasswordAsync(user, password)
                ? SecurityResult.Fail("Please confirm your email address before logging in.", LoginStatus.EmailNotConfirmed)
                : SecurityResult.Fail(invalid, LoginStatus.InvalidCredentials);
        }
        if (!result.Succeeded) return SecurityResult.Fail(invalid, LoginStatus.InvalidCredentials);

        var account = user.AccountID.HasValue ? _accounts.GetAccountById(user.AccountID.Value) : null;
        if (account == null) return SecurityResult.Fail(invalid, LoginStatus.InvalidCredentials);
        if (account.AccountRole != AccountRoles.Staff)
            return SecurityResult.Fail("You do not have permission to access this system.");

        return SecurityResult.Ok(account: account);
    }

    public async Task<SecurityResult> RegisterAsync(string name, string email, string password)
    {
        email = email.Trim();
        if (await EmailInUseAsync(email)) return SecurityResult.Fail("Email already exists.");

        var probe = new ApplicationUser { UserName = email, Email = email };
        var passwordCheck = await ValidatePasswordAsync(probe, password);
        if (!passwordCheck.Succeeded) return SecurityResult.Fail(Describe(passwordCheck));

        // Self-registered users become Staff (they still have to confirm their email before logging in)
        var account = _accounts.AddAccount(new SystemAccount
        {
            AccountName = name.Trim(),
            AccountEmail = email,
            AccountRole = AccountRoles.Staff
        });

        var user = NewUser(account, emailConfirmed: false);
        var created = await _userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            _accounts.DeleteAccount(account.AccountID);
            return SecurityResult.Fail(Describe(created));
        }

        await SendConfirmationEmailAsync(user);
        return SecurityResult.Ok("Registration successful. Please check your email to confirm your account.", account);
    }

    public async Task<SecurityResult> ConfirmEmailAsync(string userId, string code)
    {
        const string invalidLink = "The confirmation link is invalid or has expired.";
        var user = await _userManager.FindByIdAsync(userId);
        var token = Decode(code);
        if (user == null || token == null) return SecurityResult.Fail(invalidLink);

        var result = await _userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded
            ? SecurityResult.Ok("Your email has been confirmed. You can now log in.")
            : SecurityResult.Fail(invalidLink);
    }

    public async Task ResendConfirmationEmailAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user != null && !user.EmailConfirmed)
        {
            await SendConfirmationEmailAsync(user);
        }
    }

    public async Task ForgotPasswordAsync(string email)
    {
        // Same response whether or not the email exists, so accounts cannot be discovered
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || !user.EmailConfirmed) return;

        var code = Encode(await _userManager.GeneratePasswordResetTokenAsync(user));
        var link = $"{FrontEndUrl}/Auth/ResetPassword?email={Uri.EscapeDataString(user.Email!)}&code={code}";
        await SendLinkAsync(user.Email!, "Reset your FU News password",
            "We received a request to reset your password. Click the link below to choose a new one:", "Reset my password", link);
    }

    public async Task<SecurityResult> ResetPasswordAsync(string email, string code, string password)
    {
        const string done = "Your password has been reset. You can now log in.";
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return SecurityResult.Ok(done);

        var token = Decode(code);
        if (token == null) return SecurityResult.Fail("The reset link is invalid or has expired.");

        var result = await _userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            var invalidToken = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken));
            return SecurityResult.Fail(invalidToken ? "The reset link is invalid or has expired." : Describe(result));
        }

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
        return SecurityResult.Ok(done);
    }

    public async Task<SecurityResult> ExternalLoginAsync(ExternalLoginInfo info)
    {
        var user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user == null)
        {
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return SecurityResult.Fail($"{info.ProviderDisplayName} did not provide an email address.");
            if (IsAdminEmail(email))
                return SecurityResult.Fail("The administrator account cannot sign in with an external provider.");

            user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                // First external sign-in: reuse a matching SystemAccount or create a new Staff account
                var existing = _accounts.GetAccounts()
                    .FirstOrDefault(a => string.Equals(a.AccountEmail, email, StringComparison.OrdinalIgnoreCase));
                var account = existing ?? _accounts.AddAccount(new SystemAccount
                {
                    AccountName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email,
                    AccountEmail = email,
                    AccountRole = AccountRoles.Staff
                });

                user = NewUser(account, emailConfirmed: true); // the provider has verified the email
                var created = await _userManager.CreateAsync(user);
                if (!created.Succeeded)
                {
                    if (existing == null) _accounts.DeleteAccount(account.AccountID);
                    return SecurityResult.Fail(Describe(created));
                }
            }
            else if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }

            var linked = await _userManager.AddLoginAsync(user, info);
            if (!linked.Succeeded) return SecurityResult.Fail(Describe(linked));
        }

        if (await _userManager.IsLockedOutAsync(user))
            return SecurityResult.Fail("This account is locked. Please try again later.", LoginStatus.LockedOut);

        var staff = user.AccountID.HasValue ? _accounts.GetAccountById(user.AccountID.Value) : null;
        if (staff == null || staff.AccountRole != AccountRoles.Staff)
            return SecurityResult.Fail("You do not have permission to access this system.");

        return SecurityResult.Ok(account: staff);
    }

    public async Task<SecurityResult> CreateAccountAsync(string name, string email, int role, string password)
    {
        email = email.Trim();
        if (await EmailInUseAsync(email)) return SecurityResult.Fail("Email already exists.");

        var passwordCheck = await ValidatePasswordAsync(new ApplicationUser { UserName = email, Email = email }, password);
        if (!passwordCheck.Succeeded) return SecurityResult.Fail(Describe(passwordCheck));

        var account = _accounts.AddAccount(new SystemAccount
        {
            AccountName = name.Trim(),
            AccountEmail = email,
            AccountRole = role
        });

        // Accounts created by the admin do not need email confirmation
        var created = await _userManager.CreateAsync(NewUser(account, emailConfirmed: true), password);
        if (!created.Succeeded)
        {
            _accounts.DeleteAccount(account.AccountID);
            return SecurityResult.Fail(Describe(created));
        }
        return SecurityResult.Ok("Account created successfully.", account);
    }

    public async Task<SecurityResult> UpdateAccountAsync(short accountId, string name, string email, int role, string? newPassword)
    {
        email = email.Trim();
        var account = _accounts.GetAccountById(accountId);
        if (account == null) return SecurityResult.Fail("Account not found.");
        if (await EmailInUseAsync(email, accountId)) return SecurityResult.Fail("Email already exists.");

        var user = await FindByAccountIdAsync(accountId);
        if (!string.IsNullOrEmpty(newPassword))
        {
            var passwordCheck = await ValidatePasswordAsync(user ?? new ApplicationUser { UserName = email, Email = email }, newPassword);
            if (!passwordCheck.Succeeded) return SecurityResult.Fail(Describe(passwordCheck));
        }

        if (user == null)
        {
            // Account created outside Identity: create its login now
            user = NewUser(new SystemAccount { AccountID = accountId, AccountEmail = email }, emailConfirmed: true);
            var created = string.IsNullOrEmpty(newPassword)
                ? await _userManager.CreateAsync(user)
                : await _userManager.CreateAsync(user, newPassword);
            if (!created.Succeeded) return SecurityResult.Fail(Describe(created));
        }
        else
        {
            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                user.Email = email;
                user.UserName = email;
                var updated = await _userManager.UpdateAsync(user);
                if (!updated.Succeeded) return SecurityResult.Fail(Describe(updated));
            }

            if (!string.IsNullOrEmpty(newPassword))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var reset = await _userManager.ResetPasswordAsync(user, token, newPassword);
                if (!reset.Succeeded) return SecurityResult.Fail(Describe(reset));
                await _userManager.SetLockoutEndDateAsync(user, null);
            }
        }

        _accounts.UpdateAccount(new SystemAccount
        {
            AccountID = accountId,
            AccountName = name.Trim(),
            AccountEmail = email,
            AccountRole = role
        });
        return SecurityResult.Ok("Account updated successfully.");
    }

    public async Task DeleteAccountAsync(short accountId)
    {
        // Business rule (no news articles) is enforced by the DAO before anything is removed
        _accounts.DeleteAccount(accountId);

        var user = await FindByAccountIdAsync(accountId);
        if (user != null)
        {
            await _userManager.DeleteAsync(user);
        }
    }

    public async Task SyncIdentityUsersAsync()
    {
        foreach (var account in _accounts.GetAccounts())
        {
            if (string.IsNullOrWhiteSpace(account.AccountEmail)) continue;
            if (await FindByAccountIdAsync(account.AccountID) != null) continue;
            if (await _userManager.FindByEmailAsync(account.AccountEmail) != null) continue;

            var user = NewUser(account, emailConfirmed: true);
            if (!string.IsNullOrEmpty(account.AccountPassword))
            {
                // Existing sample passwords are hashed as-is (they predate the password policy)
                user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, account.AccountPassword);
            }

            var result = await _userManager.CreateAsync(user);
            if (result.Succeeded)
                _logger.LogInformation("Created Identity user for {Email}", account.AccountEmail);
            else
                _logger.LogWarning("Could not create Identity user for {Email}: {Errors}", account.AccountEmail, Describe(result));
        }
    }
}
