using System.Text;
using System.Text.Json.Serialization;
using BackEnd.BusinessObjects;
using BackEnd.Common;
using BackEnd.Repositories;
using BackEnd.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
// dùng để tạo mô hình EDM (Entity Data Model) cho OData
static IEdmModel GetEdmModel()
{
    var odataBuilder = new ODataConventionModelBuilder();
    odataBuilder.EntitySet<NewsArticle>("NewsArticles").EntityType.HasKey(n => n.NewsArticleID);
    odataBuilder.EntitySet<Category>("Categories").EntityType.HasKey(c => c.CategoryID);
    odataBuilder.EntitySet<Tag>("Tags").EntityType.HasKey(t => t.TagID);
    var accounts = odataBuilder.EntitySet<SystemAccount>("SystemAccounts").EntityType;
    accounts.HasKey(a => a.AccountID);
    accounts.Ignore(a => a.AccountPassword); // never expose passwords
    return odataBuilder.GetEdmModel();
}

// Database: connection string "MyCnn" from appsettings.json, 
builder.Services.AddDbContext<FUNewsManagementDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("MyCnn")));

// Repository layer (DAOs behind them are singletons), Đăng ký Repository vào DI để controller nhận qua constructor
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<INewsArticleRepository, NewsArticleRepository>();
builder.Services.AddScoped<ISystemAccountRepository, SystemAccountRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();

// ===== Security (Assignment 2): ASP.NET Core Identity =====
// Identity tables are stored in the same database (connection string "MyCnn")
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("MyCnn")));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;          // must click the confirmation link first

        options.Password.RequiredLength = 6;                  // + digit, lowercase, uppercase, symbol (defaults)

        options.Lockout.AllowedForNewUsers = true;            // brute-force protection
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddSignInManager()
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();                              // email confirmation / password reset tokens

// Confirmation and reset links expire after 2 hours
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(2));

builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IAccountSecurityService, AccountSecurityService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddMemoryCache();                            // one-time codes after Google sign-in

builder.Services
    // AddControllers() để đăng ký các controller và cấu hình các options cho JSON serialization và OData
    .AddControllers(options => options.Filters.Add<ApiExceptionFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles)
    // dùng để cấu hình OData cho các endpoint với các options như Select, Filter, OrderBy, Expand, Count và SetMaxTop
    .AddOData(options => options
        .Select().Filter().OrderBy().Expand().Count().SetMaxTop(100)
        .AddRouteComponents("odata", GetEdmModel()));

var authentication = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
        options.Events = new JwtBearerEvents
        {
            // A staff token stays valid only while the account still exists and still has the Staff role
            OnTokenValidated = context =>
            {
                if (context.Principal?.IsInRole(AccountRoles.StaffName) == true)
                {
                    var repository = context.HttpContext.RequestServices.GetRequiredService<ISystemAccountRepository>();
                    var account = repository.GetAccountById(context.Principal.GetAccountId());
                    if (account == null || account.AccountRole != AccountRoles.Staff)
                    {
                        context.Fail("The account no longer exists or is no longer a staff member.");
                    }
                }
                return Task.CompletedTask;
            }
        };
    });

// Temporary cookie that holds the Google identity between the Google callback and our callback endpoint
authentication.AddCookie(IdentityConstants.ExternalScheme, options =>
{
    options.Cookie.Name = "FUNews.External";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
});

// Google sign-in is enabled only when a Client ID is configured (dotnet user-secrets, never in git)
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        // Allow the OAuth correlation cookie on http://localhost during development
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
}

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FUNewsManagement API", Version = "v1" });
    c.ResolveConflictingActions(apis => apis.First());
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token returned by POST /api/Auth/login"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Create the Identity tables if needed and give every existing SystemAccount an Identity login
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.Migrate();
    await scope.ServiceProvider.GetRequiredService<IAccountSecurityService>().SyncIdentityUsersAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
