using System.ComponentModel.DataAnnotations;

namespace BackEnd.DTOs;

// Used by "forgot password" and "resend confirmation email"
public class EmailRequest
{
    [Required, EmailAddress, StringLength(70)]
    public string Email { get; set; } = string.Empty;
}
