using System.ComponentModel.DataAnnotations;

namespace BackEnd.DTOs;

public class ConfirmEmailRequest
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    // Base64Url-encoded token from the confirmation link
    [Required]
    public string Code { get; set; } = string.Empty;
}
