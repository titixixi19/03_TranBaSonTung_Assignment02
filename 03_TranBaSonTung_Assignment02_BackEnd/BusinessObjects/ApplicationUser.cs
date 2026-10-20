using Microsoft.AspNetCore.Identity;

namespace BackEnd.BusinessObjects;

// ASP.NET Core Identity user (login credentials, email confirmation, lockout, external logins).
// Business data (name, role, news ownership) stays in SystemAccount, linked by AccountID.
public class ApplicationUser : IdentityUser
{
    public short? AccountID { get; set; }
}
