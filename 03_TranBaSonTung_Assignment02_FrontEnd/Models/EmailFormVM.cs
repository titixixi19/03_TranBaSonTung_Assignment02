using System.ComponentModel.DataAnnotations;

namespace FrontEnd.Models;

// Used by "Forgot password" and "Resend confirmation email"
public class EmailFormVM
{
    [Display(Name = "Email")]
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    [StringLength(70)]
    public string Email { get; set; } = string.Empty;
}
