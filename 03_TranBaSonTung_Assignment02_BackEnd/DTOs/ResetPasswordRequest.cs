using System.ComponentModel.DataAnnotations;

namespace BackEnd.DTOs;

public class ResetPasswordRequest
{
    [Required, EmailAddress, StringLength(70)]
    public string Email { get; set; } = string.Empty;

    // Base64Url-encoded token from the reset link
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(70, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
