using BackEnd.BusinessObjects;
using BackEnd.Common;
using BackEnd.DTOs;
using BackEnd.Repositories;
using BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackEnd.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = AccountRoles.StaffName)]
public class ProfileController : ControllerBase
{
    private readonly ISystemAccountRepository _repository;
    private readonly IAccountSecurityService _security;

    public ProfileController(ISystemAccountRepository repository, IAccountSecurityService security)
    {
        _repository = repository;
        _security = security;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var account = _repository.GetAccountById(User.GetAccountId());
        if (account == null)
        {
            return NotFound(new { message = "Account not found." });
        }
        return Ok(new { account.AccountID, account.AccountName, account.AccountEmail, account.AccountRole });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ProfileUpdateRequest request)
    {
        var id = User.GetAccountId();
        var account = _repository.GetAccountById(id);
        if (account == null)
        {
            return NotFound(new { message = "Account not found." });
        }

        // Staff cannot change their own role; email/password changes go through Identity
        var result = await _security.UpdateAccountAsync(
            id, request.AccountName, request.AccountEmail, account.AccountRole ?? AccountRoles.Staff, request.AccountPassword);
        return result.Succeeded
            ? Ok(new { message = "Profile updated successfully." })
            : BadRequest(new { message = result.Message });
    }
}
