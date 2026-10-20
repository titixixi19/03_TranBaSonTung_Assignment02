using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BackEnd.BusinessObjects;

// Identity tables (AspNetUsers, AspNetUserLogins, ...) live in the same FUNewsManagement database
public class AuthDbContext : IdentityDbContext<ApplicationUser>
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.AccountID)
            .IsUnique()
            .HasFilter("[AccountID] IS NOT NULL");
    }
}
