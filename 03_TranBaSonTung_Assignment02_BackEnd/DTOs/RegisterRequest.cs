using System.ComponentModel.DataAnnotations;

namespace BackEnd.DTOs;

public class RegisterRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string AccountName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(70)]
    public string AccountEmail { get; set; } = string.Empty;

    [Required, StringLength(70, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
