# FUNewsManagementSystem – PRN232 Assignment 02

Tran Ba Son Tung – ASP.NET Core Web API (OData) + ASP.NET Core MVC client, .NET 8, SQL Server.

Assignment 02 continues Assignment 01 and adds **Security with ASP.NET Core Identity**:
email confirmation, password reset, hashed passwords, account lockout and Google sign-in.

## Solution structure

Two Visual Studio solutions, as required (`StudentName_ClassCode_A02_BE.sln`, `StudentName_ClassCode_A02_FE.sln`):

```
TranBaSonTung_SE1933_A02_BE.sln
└── 03_TranBaSonTung_Assignment02_BackEnd    ASP.NET Core Web API + OData + JWT + Identity
    ├── BusinessObjects/   Entities, FUNewsManagementDbContext, Identity (ApplicationUser, AuthDbContext)
    ├── DataAccess/        DAOs – Singleton pattern
    ├── Repositories/      Repository pattern (interfaces + implementations, call the DAOs)
    ├── Services/          Security (Identity), JWT, email
    ├── DTOs/              Request / response models with data-annotation validation
    ├── Migrations/        Identity tables
    └── Controllers/       OData (read/search) + REST (create/update/delete, auth, report)

TranBaSonTung_SE1933_A02_FE.sln
└── 03_TranBaSonTung_Assignment02_FrontEnd   ASP.NET Core MVC – calls the Web API through HttpClient

Database/FUNewsManagement.sql
```

Controllers never touch the database directly: Controller → Repository → DAO → DbContext.

## Run

1. Run `Database/FUNewsManagement.sql` on SQL Server 2012+ (Identity tables are created automatically on first run).
2. Edit `ConnectionStrings:MyCnn` in `03_TranBaSonTung_Assignment02_BackEnd/appsettings.json` if needed.
3. Open `TranBaSonTung_SE1933_A02_BE.sln` and press F5, then open `TranBaSonTung_SE1933_A02_FE.sln` in a second Visual Studio window and press F5 (profile `http`).
   Or from a terminal:
   ```
   dotnet run --project 03_TranBaSonTung_Assignment02_BackEnd --launch-profile http
   dotnet run --project 03_TranBaSonTung_Assignment02_FrontEnd --launch-profile http
   ```
4. API + Swagger: http://localhost:5203/swagger — Web app: http://localhost:5236

The FrontEnd reads the API address from `ApiSettings:BaseUrl` in its `appsettings.json`.

## Accounts

| Role  | Email                              | Password     |
|-------|------------------------------------|--------------|
| Admin | admin@FUNewsManagementSystem.org   | @@abc123@@ (from appsettings.json) |
| Staff | IsabellaDavid@FUNewsManagement.org | @1           |

Staff = role 1, Lecturer = role 2 (lecturers cannot log in).

## API

| Endpoint | Access | Description |
|---|---|---|
| `GET /odata/NewsArticles` | Public (active only) / Staff, Admin (all) | `$filter`, `$orderby`, `$expand`, `$select`, `$top`, `$count` |
| `GET /odata/Categories`, `/odata/Tags` | Public | OData queries |
| `POST/PUT/DELETE /api/Tag` | Staff | Delete is refused if the tag is used by any news article |
| `GET /odata/SystemAccounts` | Admin | OData queries (password is not exposed) |
| `POST /api/Auth/login` | Public | Returns a JWT (Identity: hashed password, lockout, confirmed email) |
| `POST /api/Auth/register` | Public | Creates a Staff account and emails a confirmation link |
| `POST /api/Auth/confirm-email` | Public | Confirms the email (`userId`, `code` from the link) |
| `POST /api/Auth/resend-confirmation` | Public | Sends a new confirmation link |
| `POST /api/Auth/forgot-password` | Public | Emails a password reset link |
| `POST /api/Auth/reset-password` | Public | Sets a new password (`email`, `code`, `password`) |
| `GET /api/Auth/external-providers` | Public | Configured external providers (Google) |
| `GET /api/Auth/external-login` | Public | Redirects the browser to Google |
| `POST /api/Auth/exchange-code` | Public | Swaps the one-time code after Google sign-in for a JWT |
| `POST/PUT/DELETE /api/Account` | Admin | Delete is refused if the account created any news article |
| `POST/PUT/DELETE /api/Category` | Staff | Delete is refused if the category is used by any news article |
| `POST/PUT/DELETE /api/NewsArticle` | Staff | Includes tags (`tagIds`) |
| `GET/PUT /api/Profile` | Staff | Own profile |
| `GET /api/Report?startDate=&endDate=` | Admin | Statistics by created date, sorted descending |

## Web app features

- Public: browse and search active news, view details.
- Admin: account management (CRUD + search), statistics report by period.
- Staff: category management, tag management, news article management (with tags), profile, own news history.
- Create/Update run in popup dialogs; every Delete asks for confirmation.
- Security: Register, Confirm email, Resend confirmation, Forgot password, Reset password, Login with Google.

## Security (Assignment 02)

Based on the Microsoft samples *"Create a secure ASP.NET MVC 5 web app with log in, email confirmation and password reset"*
and *"MVC 5 App with Facebook and Google OAuth2 sign-on"*, implemented with **ASP.NET Core Identity** in the Web API.

| Feature | Where |
|---|---|
| Identity tables (`AspNetUsers`, `AspNetUserLogins`, ...) in the same database | `BusinessObjects/AuthDbContext.cs`, `Migrations/` (applied automatically at startup) |
| Identity user linked to `SystemAccount` by `AccountID` | `BusinessObjects/ApplicationUser.cs` |
| Password policy, lockout (5 attempts / 5 min), required email confirmation, token lifetime 2h | `Program.cs` (`AddIdentityCore`) |
| Register, confirm email, forgot / reset password, Google sign-in | `Services/AccountSecurityService.cs`, `Controllers/AuthController.cs` |
| Email sending (SMTP or local `.eml` files) | `Services/SmtpEmailService.cs` |
| FrontEnd pages | `FrontEnd/Controllers/AuthController.cs`, `Views/Auth/` |

- Passwords are stored as Identity hashes (PBKDF2). Existing sample accounts are migrated at startup and keep their password (`@1`).
- New passwords: at least 6 characters with uppercase, lowercase, digit and symbol (e.g. `Demo@123`).
- Accounts created by the Admin do not need email confirmation; self-registered accounts do.
- Forgot password always returns the same message, so registered emails cannot be discovered.
- After Google sign-in the JWT is never put in the URL: the API hands the FrontEnd a one-time code (valid 2 minutes) that the FrontEnd exchanges server-to-server.

### Email
Without SMTP settings, each email is saved as an `.eml` file in `03_TranBaSonTung_Assignment02_BackEnd/EmailPickup/`
and the link is also printed in the BackEnd console (Development). To send real emails with Gmail, create an
*App password* (Google Account → Security → 2-Step Verification → App passwords) and store it with user-secrets:

```
cd 03_TranBaSonTung_Assignment02_BackEnd
dotnet user-secrets set "Email:SmtpHost" "smtp.gmail.com"
dotnet user-secrets set "Email:From" "your.name@gmail.com"
dotnet user-secrets set "Email:UserName" "your.name@gmail.com"
dotnet user-secrets set "Email:Password" "<16-character app password>"
```

### Google sign-in
1. Google Cloud Console → APIs & Services → Credentials → *Create OAuth client ID* (type **Web application**).
2. Authorized redirect URIs: `http://localhost:5203/signin-google` and `https://localhost:7218/signin-google`.
3. Store the keys with user-secrets (never commit them):
   ```
   cd 03_TranBaSonTung_Assignment02_BackEnd
   dotnet user-secrets set "Authentication:Google:ClientId" "<client id>"
   dotnet user-secrets set "Authentication:Google:ClientSecret" "<client secret>"
   ```
4. Restart the BackEnd: the **Login with Google** button appears on the login page.
