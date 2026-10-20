using BackEnd.BusinessObjects;
using BackEnd.DTOs;
using BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackEnd.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = AccountRoles.AdminName)]
public class AccountController : ControllerBase
{
    private readonly IAccountSecurityService _security;

    public AccountController(IAccountSecurityService security) => _security = security;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AccountCreateRequest request)
    {
        var result = await _security.CreateAccountAsync(
            request.AccountName, request.AccountEmail, request.AccountRole, request.AccountPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.Message });
        }
        return Ok(new { result.Account!.AccountID, message = result.Message });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(short id, [FromBody] AccountUpdateRequest request)
    {
        var result = await _security.UpdateAccountAsync(
            id, request.AccountName, request.AccountEmail, request.AccountRole, request.AccountPassword);
        if (!result.Succeeded)
        {
            return result.Message == "Account not found."
                ? NotFound(new { message = result.Message })
                : BadRequest(new { message = result.Message });
        }
        return Ok(new { message = result.Message });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(short id)
    {
        await _security.DeleteAccountAsync(id);
        return Ok(new { message = "Account deleted successfully." });
    }
}
