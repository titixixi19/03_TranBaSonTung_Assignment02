using BackEnd.BusinessObjects;
using Microsoft.AspNetCore.Identity;

namespace BackEnd.Services;

// Security operations built on ASP.NET Core Identity, kept in sync with SystemAccount
public interface IAccountSecurityService
{
    Task<SecurityResult> PasswordLoginAsync(string email, string password);
    Task<SecurityResult> RegisterAsync(string name, string email, string password);
    Task<SecurityResult> ConfirmEmailAsync(string userId, string code);
    Task ResendConfirmationEmailAsync(string email);
    Task ForgotPasswordAsync(string email);
    Task<SecurityResult> ResetPasswordAsync(string email, string code, string password);
    Task<SecurityResult> ExternalLoginAsync(ExternalLoginInfo info);

    Task<SecurityResult> CreateAccountAsync(string name, string email, int role, string password);
    Task<SecurityResult> UpdateAccountAsync(short accountId, string name, string email, int role, string? newPassword);
    Task DeleteAccountAsync(short accountId);

    // Creates Identity users for existing SystemAccount rows (run at startup)
    Task SyncIdentityUsersAsync();
}
