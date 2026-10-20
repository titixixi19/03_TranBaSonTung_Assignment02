using System.ComponentModel.DataAnnotations;

namespace FrontEnd.Models;

public class ResetPasswordVM
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "New password")]
    [Required(ErrorMessage = "Password is required.")]
    [StringLength(70)]
    [RegularExpression(PasswordPolicy.Pattern, ErrorMessage = PasswordPolicy.Message)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Confirm new password")]
    [Required(ErrorMessage = "Please confirm your password.")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
