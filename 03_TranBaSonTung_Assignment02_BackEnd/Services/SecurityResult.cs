using BackEnd.BusinessObjects;

namespace BackEnd.Services;

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    EmailNotConfirmed,
    LockedOut,
    NotAllowed
}

public class SecurityResult
{
    public bool Succeeded { get; init; }
    public LoginStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public SystemAccount? Account { get; init; }

    public static SecurityResult Ok(string message = "", SystemAccount? account = null) =>
        new() { Succeeded = true, Status = LoginStatus.Success, Message = message, Account = account };

    public static SecurityResult Fail(string message, LoginStatus status = LoginStatus.NotAllowed) =>
        new() { Succeeded = false, Status = status, Message = message };
}
