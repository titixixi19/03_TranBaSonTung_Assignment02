namespace FrontEnd.Models;

// Mirrors the ASP.NET Core Identity password policy configured in the BackEnd
public static class PasswordPolicy
{
    public const string Pattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).{6,}$";

    public const string Message =
        "Password must be at least 6 characters and contain an uppercase letter, a lowercase letter, a digit and a symbol.";
}
