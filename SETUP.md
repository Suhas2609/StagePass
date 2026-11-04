# StagePass: Local Setup and Environment Guide

> **Target Audience**: Teammates setting up StagePass on a fresh development machine.  
> **Repository Root**: `StagePass/`  
> **Target Framework**: .NET 10.0 (`net10.0`), C# 13, ASP.NET Core Web API, EF Core 10.0.14, Microsoft SQL Server  

---

## 1. Project Overview

**StagePass** is a high-concurrency live-event ticket reservation and booking MVP. It addresses the flash-sale overselling problem: when thousands of concurrent buyers attempt to reserve tickets simultaneously, naive in-memory application checks (`if (available >= quantity)`) fail under race conditions, causing overselling and negative inventory.

StagePass solves this by pushing the inventory check and decrement into a single atomic, conditional SQL Server `UPDATE` statement inside a database transaction (`UPDATE TicketTypes SET Available = Available - @qty WHERE TicketTypeId = @id AND Available >= @qty`). SQL Server serializes concurrent updates with row-level locks, mathematically guaranteeing zero overselling without external distributed locks or message queues.

### Stack Summary
- **Backend**: C# 13, .NET 10 Web API (controller-based), Entity Framework Core 10.0.14
- **Database**: Microsoft SQL Server (Development: `StagePassDb`, Isolated Testing: `StagePassTestDb`)
- **Background Service**: In-process `HoldExpiryWorker` (`PeriodicTimer`, 60-second intervals)
- **Frontend**: Zero-dependency Vanilla HTML5, modern CSS3 (custom properties, responsive grid, light/dark themes), and Vanilla JavaScript (Fetch API). Static files are served directly by ASP.NET Core via `UseStaticFiles()`. No Node.js, npm, Vite, or external CDNs are required.

### What Runs Locally
1. **ASP.NET Core Web API Host** (`StagePass.Api`) on Kestrel (serves API endpoints and static frontend pages).
2. **Local SQL Server Instance** hosting the application database (`StagePassDb`) and test database (`StagePassTestDb`).
3. **Web Browser** accessing `https://localhost:7135` or `http://localhost:5047`.

---

## 2. Prerequisites

### Required Software

| Software | Required Version | Verification Command | Notes / Download Link |
| :--- | :--- | :--- | :--- |
| **.NET SDK** | `10.0.100` or higher | `dotnet --version` | Download from [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). |
| **Git** | `2.40+` | `git --version` | Download from [git-scm.com](https://git-scm.com/). |
| **Microsoft SQL Server** | 2022, 2025, Express, or LocalDB | Check SQL Server service status (see Section 5) | [SQL Server Developer Edition](https://www.microsoft.com/sql-server/sql-server-downloads) or [SQL Server Express](https://www.microsoft.com/sql-server/sql-server-downloads). |
| **EF Core CLI Tool** | `10.0.14` (local tool) | `dotnet tool run dotnet-ef --version` | Restored locally via `dotnet tool restore` from `.config/dotnet-tools.json`. |

### Optional Software
- **Database Management Tool**: [SQL Server Management Studio (SSMS)](https://learn.microsoft.com/sql/ssms/download-sql-server-management-studio-ssms), [Azure Data Studio](https://learn.microsoft.com/azure-data-studio/), or the **mssql** extension in VS Code.
- **IDE / Editor**: Visual Studio 2022/2026 (v17.12+), Visual Studio Code (with the *C# Dev Kit* extension), or JetBrains Rider.

---

## 3. Clone the Repository

Clone the repository from GitHub and switch to the project root:

```bash
# Clone the repository (replace <repository-url> with your clone URL or fork)
git clone https://github.com/Suhas2609/StagePass.git

# Enter the repository root
cd StagePass
```

### Verify Working Directory Structure
Confirm that you are in the directory containing the solution file:

```text
StagePass/
├── .config/
│   └── dotnet-tools.json
├── StagePass.slnx
├── StagePass.Api/
│   ├── StagePass.Api.csproj
│   ├── Program.cs
│   └── wwwroot/
├── StagePass.Tests/
│   ├── StagePass.Tests.csproj
│   └── TestWebApplicationFactory.cs
├── README.md
├── SETUP.md
└── CLAUDE_HANDOFF.md
```

---

## 4. Restore and Build

From the repository root (`StagePass/`), restore the local tools, restore NuGet dependencies, and build the solution:

```bash
# 1. Restore local .NET tools (installs dotnet-ef 10.0.14 locally)
dotnet tool restore

# 2. Restore NuGet packages for the solution
dotnet restore

# 3. Build the solution (compiles StagePass.Api and StagePass.Tests)
dotnet build
```

### Expected Output
A successful build produces zero warnings and zero errors:
```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Common Build Failures
- **Error CS0234 / SDK mismatch**: Ensure `dotnet --version` outputs `10.0.x`. If multiple SDKs are installed, verify that .NET 10 is on your `PATH`.
- **Tool restore failure**: If `dotnet tool restore` fails, verify that `.config/dotnet-tools.json` exists in the current folder.

---

## 5. Configure SQL Server

StagePass requires access to a running Microsoft SQL Server instance. You can use any of the following setups:

### 1. Identify Your Local SQL Server Instance

| Setup Type | Typical Server Name in Connection String | Authentication |
| :--- | :--- | :--- |
| **Default Local Instance** (Developer Edition) | `localhost` or `.` | Windows Integrated (`Trusted_Connection=True;`) |
| **SQL Server Express** | `localhost\SQLEXPRESS` | Windows Integrated (`Trusted_Connection=True;`) |
| **SQL Server LocalDB** (Installed with Visual Studio) | `(localdb)\MSSQLLocalDB` | Windows Integrated (`Trusted_Connection=True;`) |
| **SQL Server in Docker / SQL Authentication** | `localhost,1433` | SQL Login (`User Id=sa;Password=<YourPassword>;`) |

### 2. Verify SQL Server Is Running

**On Windows (PowerShell)**:
```powershell
# For default SQL Server:
Get-Service -Name "MSSQLSERVER"

# For SQL Server Express:
Get-Service -Name "MSSQL$SQLEXPRESS"

# For LocalDB:
sqllocaldb info MSSQLLocalDB
# If stopped, start it with: sqllocaldb start MSSQLLocalDB
```

Ensure the service status is **Running**.

> [!NOTE]
> You do **not** need to manually create the `StagePassDb` database in SSMS before starting. ASP.NET Core automatically creates the database and runs migrations on first launch in `Development` mode when a valid server connection string is configured.

---

## 6. Configure User Secrets

To prevent credentials and machine-specific database server names from leaking into version control, connection strings and demonstration keys are excluded from `appsettings.json`. They must be set in your local **.NET User Secrets** store.

The User Secrets ID for `StagePass.Api` is configured in `StagePass.Api/StagePass.Api.csproj` as `ae409454-bb1b-4b30-9405-ed0c33e73782`.

### Step 1: Initialize User Secrets (if not already initialized)

```bash
dotnet user-secrets init --project StagePass.Api
```

### Step 2: Set Your Database Connection String

Choose the connection string that matches your SQL Server instance:

**For Standard Local SQL Server (Default):**
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=StagePassDb;Trusted_Connection=True;TrustServerCertificate=True;" --project StagePass.Api
```

**For SQL Server Express:**
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost\SQLEXPRESS;Database=StagePassDb;Trusted_Connection=True;TrustServerCertificate=True;" --project StagePass.Api
```

**For SQL Server LocalDB:**
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=StagePassDb;Trusted_Connection=True;TrustServerCertificate=True;" --project StagePass.Api
```

**For SQL Server Authentication (Username / Password):**
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=StagePassDb;User Id=your_username;Password=your_password;TrustServerCertificate=True;" --project StagePass.Api
```

### Step 3: Organizer Login (No Secret Required)

Organizer endpoints use database-backed session authentication (register/login). When the app starts in `Development` (or the test suite runs), the seed creates a demo organizer account:

| Field | Value |
| :--- | :--- |
| **Email** | `admin@stagepass.local` |
| **Password** | `StagePass2026!` |

> [!WARNING]
> These are **development/test-only demo credentials** created by `DbInitializer`. Never seed them in a real deployment.

The demo account is linked to the seeded `Organizer` record by **email** (see Section 10). To use another organizer account, it must be registered with `isOrganizer = true` *and* an `Organizers` row with the same email must exist.

Session lifetime is controlled by `SessionDurationDays` in `appsettings.json` (default `7`).

### Step 4: Verify User Secrets Are Set

Verify that the connection string exists in your User Secrets store without printing sensitive passwords:

```bash
dotnet user-secrets list --project StagePass.Api
```

Expected output:
```text
ConnectionStrings:DefaultConnection = Server=...;Database=StagePassDb;...
```

---

## 7. Create and Initialize the Database

StagePass includes full EF Core migrations in `StagePass.Api/Data/Migrations/` (`20261027104645_InitialCreate.cs`).

### Automatic Migration & Seeding on Startup
When `StagePass.Api` runs in the `Development` environment, `Program.cs` automatically:
1. Validates that `ConnectionStrings:DefaultConnection` is configured and does not point to a system database (`master`, `model`, `msdb`, `tempdb`).
2. Executes `await db.Database.MigrateAsync()`, creating `StagePassDb` and applying all pending migrations.
3. Invokes `DbInitializer.SeedAsync(app.Services)` to populate demo data.

### Manual Alternative (Pre-flight Database Creation)
If you prefer to apply migrations manually before starting the web server, run:

```bash
dotnet tool run dotnet-ef database update --project StagePass.Api
```

> [!WARNING]
> Do **NOT** run `dotnet ef migrations add` on a fresh machine. The initial migration already exists in the repository. Running `migrations add` would generate a duplicate or empty migration file.

### What Gets Seeded
The seeder (`DbInitializer.cs`) is idempotent (`if (await db.Venues.AnyAsync()) return;`) and seeds:
- **1 Organizer**: `Midnight Sun Productions` (`contact@midnightsun.example`)
- **2 Venues**: `Aurora Concert Hall` (Brightmoor), `Harbor Lights Theatre` (Port Elyse)
- **2 Events**: *Symphony Under the Stars* (Music), *The Lighthouse Keeper* (Theatre)
- **3 Performances**: Upcoming shows scheduled 1, 3, and 7 days from the current date
- **6 Ticket Types**: Standard and Premium tiers with initial `Available = Capacity`

---

## 8. Run the Application

Start the web application from the repository root:

```bash
dotnet run --project StagePass.Api
```

### Application URLs
From `StagePass.Api/Properties/launchSettings.json`, Kestrel listens on:
- **HTTPS**: `https://localhost:7135`
- **HTTP**: `http://localhost:5047`

Console startup output:
```text
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7135
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5047
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Development
info: Microsoft.Hosting.Lifetime[0]
      Content root path: ...\StagePass\StagePass.Api
```

*(If prompted to trust the ASP.NET Core HTTPS development certificate, run `dotnet dev-certs https --trust` or accept the browser prompt).*

### Open the Application Pages

Open your web browser and navigate to:

1. **Customer Event Discovery**: [`https://localhost:7135/`](https://localhost:7135/) (or [`https://localhost:7135/index.html`](https://localhost:7135/index.html))
   - Browse upcoming performances, search by title, filter by category (`Music`, `Theatre`), filter by date, and paginate through results.
2. **Performance Detail & Seat Selection**: [`https://localhost:7135/performance.html?id=1`](https://localhost:7135/performance.html?id=1)
   - View live ticket availability, select ticket tiers (Standard/Premium), choose a quantity (1–10), and place a 5-minute reservation hold.
   - Watch the real-time countdown timer and click **Confirm Booking**.
3. **Booking Voucher**: [`https://localhost:7135/booking.html`](https://localhost:7135/booking.html)
   - Displays the confirmed booking receipt with a copyable reference (`SP-XXXXXXXX`).
4. **Organizer Dashboard**: [`https://localhost:7135/dashboard.html`](https://localhost:7135/dashboard.html)
   - Sign in at [`https://localhost:7135/login.html`](https://localhost:7135/login.html) using `admin@stagepass.local` / `StagePass2026!`.
   - Create new events (with the populated venue selector), schedule performances, and add ticket tiers; view confirmed bookings with real-time inventory progress bars; or cancel performances.
5. **Live Concurrency Demo**: [`https://localhost:7135/demo.html`](https://localhost:7135/demo.html)
   - *Development only*: Fire concurrent reservation requests against a test scenario to witness atomic oversell prevention in real-time.

### Verify API Responds
In another terminal or browser tab, test the public API:
```bash
curl -k https://localhost:7135/api/performances
```
Expected output: A JSON array returning the 3 seeded performances.

---

## 9. Run Automated Tests

StagePass includes a comprehensive test suite (88 tests) in `StagePass.Tests/` verifying unit input validation, seed idempotency, reservation lifecycle, idempotency race conditions, live 500-request SQL Server concurrency, API smoke tests, authentication/session handling, confirmation emails, and security controls.

### Test Database Isolation (`StagePassTestDb`)
- Integration tests execute against an **isolated database** named `StagePassTestDb`.
- `TestWebApplicationFactory.cs` pins test connections to `StagePassTestDb` and verifies `context.Database.GetDbConnection().Database == "StagePassTestDb"` before resetting tables.
- **`StagePassDb` is NEVER touched or modified by running tests.**

### Test Connection String
By default, tests connect using:
`Server=localhost;Database=StagePassTestDb;Trusted_Connection=True;TrustServerCertificate=True;`

If your SQL Server uses an instance name (e.g. `SQLEXPRESS` or LocalDB), set the `STAGEPASS_TEST_CONNECTIONSTRING` environment variable before running tests:

**PowerShell**:
```powershell
$env:STAGEPASS_TEST_CONNECTIONSTRING="Server=(localdb)\MSSQLLocalDB;Database=StagePassTestDb;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test
```

**Bash**:
```bash
export STAGEPASS_TEST_CONNECTIONSTRING="Server=localhost;Database=StagePassTestDb;User Id=sa;Password=<password>;TrustServerCertificate=True;"
dotnet test
```

### Running the Tests

```bash
# 1. Run the entire test suite (all 88 tests)
dotnet test --logger "console;verbosity=minimal"

# 2. Run only non-concurrency tests (Unit, Lifecycle, Smoke, Security)
dotnet test --filter "Category!=RequiresSqlServer"

# 3. Run the 500-concurrent-request oversell & race condition suite against SQL Server
dotnet test --filter "Category=RequiresSqlServer"

# 4. Run pure in-memory unit tests (no database required)
dotnet test --filter "FullyQualifiedName~ReservationInputTests"
```

### Expected Test Result
```text
Passed!  - Failed: 0, Passed: 86, Skipped: 0, Total: 86
```

---

## 10. Authentication and Development Demo

### How Authentication Works
- `POST /api/auth/register`, `POST /api/auth/login` return `{ token, userId, name, email, isOrganizer, expiresAt }`. The `token` is an opaque GUID stored in the `AppSessions` table (no JWT).
- Send it on later requests as `Authorization: Bearer <token>`. `SessionAuthMiddleware` resolves it to the user; unknown/expired tokens are simply treated as anonymous.
- `GET /api/auth/me` and `POST /api/auth/logout` require a valid session (`401 AuthRequired` otherwise). Logout deletes that one session.
- `/api/organizer/*` requires `RequireOrganizer`: `401 AuthRequired` without a valid session, `403 OrganizerRequired` for non-organizer users.
- Passwords are hashed with PBKDF2-SHA256 (100,000 iterations, 32-byte random salt) using built-in `System.Security.Cryptography`.

**Demo organizer login (development/test seed):** `admin@stagepass.local` / `StagePass2026!`

```powershell
# PowerShell: log in and call an organizer endpoint
$login = Invoke-RestMethod -Method Post -Uri https://localhost:7135/api/auth/login -ContentType application/json `
  -Body (@{ email = "admin@stagepass.local"; password = "StagePass2026!" } | ConvertTo-Json)
Invoke-RestMethod -Uri https://localhost:7135/api/organizer/performances/1/bookings -Headers @{ Authorization = "Bearer $($login.token)" }
```

### AppUser ↔ Organizer Link
The domain model maintains an `Organizers` table for event ownership. An `AppUser` is linked to an `Organizer` **by matching email**. When an organizer registers via `/register.html` (or an authenticated organizer creates their first event), StagePass auto-provisions their corresponding `Organizer` domain record so self-registered organizers can create and manage events immediately. Creating an event binds the event to the caller's `Organizer` record (`organizerId` in the request body is safely overridden by the server).

> [!IMPORTANT]
> **MVP Limitations**: (1) `isOrganizer` is accepted on public registration, gated only by the email link above; (2) there is no per-event ownership check on performances, ticket types, bookings, or cancellation, so any organizer can manage any performance; (3) no login rate limiting, password reset, email verification, or expired-session cleanup. A production system would use a single user model, server-assigned roles, and ownership checks (`Event.OrganizerId == currentOrganizer`).

### Concurrency Demo (`demo.html`) & Rate Limiting
- The concurrency demonstration page (`demo.html` / `demo.js`) calls `/api/demo/setup-scenario` and `/api/demo/snapshot/{id}`.
- **Environment Gated**: In `Program.cs`, `ExcludeControllerConvention` removes `DemoController` from the route table in `Production` and `Testing`. Attempting to access `/api/demo/*` outside `Development` returns `404 Not Found`.
- **Rate-Limiting Interaction**: `Program.cs` applies a rate limit policy (`10 requests per 60 seconds per client IP`) to `POST /api/reservations`.
  - When running `demo.html` with `requests = 300` on localhost in `Development`, requests beyond the 10th will receive `429 Too Many Requests` (visible under `✗ HTTP Errors` on the demo card) if `RateLimiting:BookingEnabled` is `true`.
  - To test maximum concurrency from a single browser without rate limiting, set `"RateLimiting:BookingEnabled": false` in `StagePass.Api/appsettings.Development.json` or via User Secrets:
    ```bash
    dotnet user-secrets set "RateLimiting:BookingEnabled" "false" --project StagePass.Api
    ```

---

## 11. Troubleshooting

### 1. `dotnet: command not found` or SDK Version Mismatch
- **Symptom**: `The current .NET SDK does not support targeting .NET 10.0`.
- **Cause**: .NET 10 SDK is not installed or an older SDK is active on your `PATH`.
- **Fix**: Run `dotnet --list-sdks`. Download and install [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

### 2. SQL Server Connection Failed (Error 26 / Error 40)
- **Symptom**: `Microsoft.Data.SqlClient.SqlException: A network-related or instance-specific error occurred while establishing a connection to SQL Server`.
- **Cause**: SQL Server service is not running or the server name in `ConnectionStrings:DefaultConnection` is incorrect.
- **Fix**:
  - Verify SQL Server is running: `Get-Service -Name "MSSQLSERVER"` or `sqllocaldb info MSSQLLocalDB`.
  - Check your server name. If using SQL Server Express, the server is `localhost\SQLEXPRESS`. If using LocalDB, it is `(localdb)\MSSQLLocalDB`. Update via `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." --project StagePass.Api`.

### 3. Login Failed for User (Error 18456)
- **Symptom**: `Cannot open database "StagePassDb" requested by the login. The login failed.`
- **Cause**: Windows Authentication permissions or SQL credentials invalid.
- **Fix**: If using SQL Authentication (Docker / sa), verify the password. If using Windows Authentication on a local instance, ensure your current Windows user has `sysadmin` rights on the SQL instance.

### 4. `401 Unauthorized` on Organizer Dashboard
- **Symptom**: `/api/organizer/*` returns `401 {"error":"AuthRequired"}` or `403 {"error":"OrganizerRequired"}`.
- **Cause**: Missing/expired/invalid `Authorization: Bearer <token>` header, or the logged-in user is not an organizer.
- **Fix**: Log in via `/login.html` or `POST /api/auth/login` (demo: `admin@stagepass.local` / `StagePass2026!`) and send the returned `token` as a Bearer token. If login fails with `InvalidCredentials` on an older database, restart the app in `Development` so the seed creates the demo account (or run `dotnet tool run dotnet-ef database update --project StagePass.Api` first to create the `AppUsers`/`AppSessions` tables).

### 5. `404 Not Found` on `/api/demo/*`
- **Symptom**: Visiting `demo.html` displays `Demo setup failed: Not Found. Is the server running in Development mode?`.
- **Cause**: The application was launched in `Production` or `Testing` mode where `DemoController` is excluded from the route table.
- **Fix**: Ensure `ASPNETCORE_ENVIRONMENT=Development` is set (this is the default in `launchSettings.json`).

### 6. Test Suite Reset Failure (`Refusing to run test reset against non-test database`)
- **Symptom**: `InvalidOperationException: Refusing to run test reset against non-test database 'StagePassDb'. Expected 'StagePassTestDb'`.
- **Cause**: The test runner was pointed to the development database instead of the test database.
- **Fix**: Do not override the test connection with `StagePassDb`. Unset `STAGEPASS_TEST_CONNECTIONSTRING` or point it to a database named `StagePassTestDb`.

### 7. Port Already in Use (7135 / 5047)
- **Symptom**: `System.IO.IOException: Failed to bind to address https://localhost:7135: address already in use`.
- **Cause**: An earlier instance of `dotnet run` is still active in the background.
- **Fix**: Stop the running process (`Get-Process -Name "StagePass.Api" | Stop-Process` on Windows, or find the PID using `netstat -ano | findstr 7135`).

---

## 12. Quick Start Checklist

Follow this quick checklist to confirm your environment is fully operational:

- [ ] **1. Prerequisites**: `dotnet --version` returns `10.0.x`, `git --version` returns `2.x`, and SQL Server service is running.
- [ ] **2. Clone & Tools**: Cloned repo, ran `dotnet tool restore`, and `dotnet build` succeeded with `0 Warning(s), 0 Error(s)`.
- [ ] **3. User Secrets**: Initialized secrets and set `ConnectionStrings:DefaultConnection`.
- [ ] **4. Database**: Ran `dotnet tool run dotnet-ef database update --project StagePass.Api` (or let the app auto-migrate on first run).
- [ ] **5. Run Web App**: Ran `dotnet run --project StagePass.Api`.
- [ ] **6. Browser Verification**:
  - [ ] Opened `https://localhost:7135/` and observed seeded events.
  - [ ] Opened `https://localhost:7135/performance.html?id=1` and tested a ticket hold.
  - [ ] Logged in via `POST /api/auth/login` as `admin@stagepass.local` and called an organizer endpoint with the Bearer token.
  - [ ] Opened `https://localhost:7135/demo.html` and verified the concurrency demo.
- [ ] **7. Run Tests**: Ran `dotnet test` and confirmed all **88 tests passed**.

---

## 13. Email Confirmation Setup (Optional)

Booking confirmation emails are disabled by default (`Email:Enabled = false`). To enable them:

### Option A: Gmail SMTP (recommended for local development)

1. Enable 2-Step Verification on your Google Account:
   https://myaccount.google.com/security

2. Create an App Password:
   https://myaccount.google.com/apppasswords
   - Select "Mail" and "StagePass Server" (or "Windows Computer")
   - Copy the 16-character password (e.g. `abcd efgh ijkl mnop`)

3. Add credentials via User Secrets:
   ```bash
   dotnet user-secrets set "Email:From"     "yourname@gmail.com" --project StagePass.Api
   dotnet user-secrets set "Email:Password" "abcdefghijklmnop"   --project StagePass.Api
   dotnet user-secrets set "Email:Enabled"  "true"               --project StagePass.Api
   ```

4. Run the application and complete a booking:
   - Use a real email address as the Customer Email when reserving
   - Confirm the booking
   - A confirmation email will arrive within 30 seconds

### Testing Email

Email sending is tested by the booking confirmation flow.
Automated integration tests run with `Email:Enabled = false` so no actual emails are sent during automated testing.

To manually test email delivery:
1. Configure credentials (steps 1–3 above).
2. Run the app: `dotnet run --project StagePass.Api`
3. Open `performance.html?id=1`, reserve tickets with your real email, and confirm.
4. Check your inbox for the StagePass confirmation email (arrives within 30–60 seconds).
5. If SMTP fails (e.g. invalid credentials or blocked port), the server logs the error, but the booking itself completes successfully with an `SP-XXXXXXXX` reference.

### Option B: Gmail API with OAuth2 (Google Cloud Console)

Use only if SMTP port 587 is blocked in your network environment.
- Create a project in Google Cloud Console.
- Enable the Gmail API.
- Create an OAuth 2.0 Client ID.
- *Note*: StagePass implements Option A (SMTP) first; Option B can be configured if SMTP traffic is blocked.

