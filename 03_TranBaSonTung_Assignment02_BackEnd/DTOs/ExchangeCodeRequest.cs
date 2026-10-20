using System.ComponentModel.DataAnnotations;

namespace BackEnd.DTOs;

// One-time code issued after a Google sign-in; the FrontEnd exchanges it for a JWT (server to server)
public class ExchangeCodeRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;
}
