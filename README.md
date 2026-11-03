# StagePass

StagePass is a live event discovery and ticket booking platform built with C# 13, ASP.NET Core Web API (.NET 10), Entity Framework Core, Microsoft SQL Server, and a zero-dependency vanilla HTML/CSS/JavaScript frontend. Its defining engineering feature is an atomic conditional SQL `UPDATE` inside an explicit database transaction that prevents ticket overselling under high concurrent load without external locks or queues. The platform includes PBKDF2-SHA256 database-backed session authentication for customers and event organizers, an in-process background worker that expires stale 5-minute reservation holds and restores inventory in batch, and a client-driven idempotency mechanism safe against network retries and concurrent race conditions.

## What it demonstrates

- Atomic SQL Server inventory allocation with zero overselling under concurrent load
- PBKDF2-SHA256 session-based authentication with no JWT dependency
- Background reservation expiry using `OUTPUT INTO` with correct inventory aggregation
- Transactional booking confirmation emails via Gmail SMTP with complete failure isolation
- 88 automated tests including a 500-concurrent-request load test against real SQL Server

## Prerequisites

- .NET 10 SDK (`dotnet --version` should show `10.x`)
- SQL Server (Developer, Express, or LocalDB) running on `localhost`
- VS Code with C# Dev Kit extension (or Visual Studio 2022+)

## Setup

### 1. Clone and restore tools

```bash
git clone <url>
cd StagePass
dotnet tool restore
```

### 2. Configure the development database

Use .NET User Secrets (credentials never go in source files):

```bash
dotnet user-secrets init --project StagePass.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=localhost;Database=StagePassDb;Trusted_Connection=True;TrustServerCertificate=True;" \
  --project StagePass.Api
```

*(If using SQL Server LocalDB, replace `Server=localhost` with `Server=(localdb)\MSSQLLocalDB`.)*

### 3. Apply migrations and seed data

```bash
dotnet ef database update --project StagePass.Api
```

The application seeds development data automatically on first start in the `Development` environment. This creates venues, events, performances, ticket types, and a demo organizer account.

### 4. Run the application

```bash
dotnet run --project StagePass.Api
```

Open `https://localhost:7135/` or `http://localhost:5047/` (or the port shown in the terminal output).

## Demo accounts

| Role | Email | Password |
|---|---|---|
| Organizer | `admin@stagepass.local` | `StagePass2026!` |
| Customer | Register at `/register.html` | Any password ≥ 8 characters |

## Running tests

The test suite runs against an isolated SQL Server database named `StagePassTestDb` so development data in `StagePassDb` is never modified. Configure a custom test connection string via the `STAGEPASS_TEST_CONNECTIONSTRING` environment variable or User Secret if needed:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=localhost;Database=StagePassTestDb;Trusted_Connection=True;TrustServerCertificate=True;" \
  --project StagePass.Tests
```

Apply migrations to the test database:

```bash
dotnet ef database update --project StagePass.Api \
  --connection "Server=localhost;Database=StagePassTestDb;Trusted_Connection=True;TrustServerCertificate=True;"
```

Run all tests:

```bash
dotnet test
```

Run only the high-concurrency tests:

```bash
dotnet test --filter "Category=RequiresSqlServer"
```

Expected: `88 passed, 0 failed, 0 skipped`.

## Application pages

| URL | Purpose |
|---|---|
| `/` (`/index.html`) | Event discovery — hero banner, horizontal carousels, search, and category filters |
| `/performance.html?id=N` | Performance detail, tier selection, 5-minute hold countdown, and session recovery |
| `/booking.html?ref=SP-XXXXXXXX` | Booking confirmation receipt with copyable reference and print layout |
| `/login.html` | Customer and organizer sign-in |
| `/register.html` | Customer and organizer account registration |
| `/dashboard.html` | Organizer portal — event/performance/tier creation, venue selector, inventory bars, and bookings |
| `/demo.html` | Live concurrency demonstration and database invariant verifier (Development only) |

## API overview

Standard REST over JSON. Authentication uses opaque GUID session tokens sent via `Authorization: Bearer <token>`:

- **Authentication**:
  - `POST /api/auth/register` — Register a customer or organizer account and create an active session
  - `POST /api/auth/login` — Authenticate with email and password and receive a session token
  - `GET /api/auth/me` — Return the authenticated user's profile (`[RequireAuth]`)
  - `POST /api/auth/logout` — Revoke the current session token (`[RequireAuth]`)
- **Event Discovery**:
  - `GET /api/performances` — Paginated list of upcoming performances with `search`, `category`, and `date` filters
  - `GET /api/performances/{id}` — Performance details and real-time tier availability
- **Reservations & Bookings**:
  - `POST /api/reservations` — Atomically allocate inventory and create a 5-minute reservation hold (idempotent via `idempotencyKey`)
  - `GET /api/reservations/{holdId}` — Poll current status and expiration timestamp of a hold
  - `POST /api/reservations/{holdId}/confirm` — Confirm a pending hold and issue a `SP-XXXXXXXX` booking reference
  - `GET /api/bookings/{reference}` — Retrieve confirmed booking details by reference code
- **Organizer Management** (requires organizer Bearer token via `[RequireOrganizer]`):
  - `GET /api/organizer/venues` — List available venues ordered by name
  - `POST /api/organizer/events` — Create a new event owned by the authenticated organizer
  - `POST /api/organizer/performances` — Schedule an upcoming performance for an event
  - `POST /api/organizer/performances/{id}/tickettypes` — Add a ticket tier with initial `Available = Capacity`
  - `GET /api/organizer/performances/{id}/bookings` — Retrieve confirmed bookings and per-tier capacity/sold/available summary
  - `PATCH /api/organizer/performances/{id}/cancel` — Cancel a performance and release all pending holds back to inventory
- **Concurrency Demo** (`Development` environment only):
  - `POST /api/demo/setup-scenario` — Provision an isolated demo performance and ticket tier (`1..10000` tickets)
  - `GET /api/demo/snapshot/{ticketTypeId}` — Read live `Capacity`, `Available`, `PendingHeld`, `Confirmed`, and verify the invariant

## Configuration

- `StagePass.Api/appsettings.json`: Base configuration; `RateLimiting:BookingEnabled` is `true` (`10` requests per `60` seconds per client IP on `POST /api/reservations`) and `SessionDurationDays` is `7`.
- `StagePass.Api/appsettings.Development.json`: Sets `RateLimiting:BookingEnabled` to `"false"` so `/demo.html` can fire 300+ concurrent requests from a single browser IP without hitting `429 Too Many Requests`.
- `StagePass.Api/appsettings.Testing.json`: Sets `RateLimiting:BookingEnabled` to `false` and targets `StagePassTestDb` for automated integration and concurrency tests.
- **Credentials**: Connection strings are configured via `.NET User Secrets` or environment variables and are never stored in tracked source files.

## What is intentionally not production-ready

- Organizer auth is email/password with session tokens. No OAuth, OIDC, or password reset.
- No real payment processing. Booking confirmation is a state transition, not a charge.
- The background hold expiry worker is in-process. Horizontal scaling requires a distributed lock (e.g. SQL Server `sp_getapplock`) or an external job scheduler.
- Rate limiting uses `RemoteIpAddress`. Behind a reverse proxy, add `ForwardedHeadersMiddleware`.
- Transactional emails support Gmail SMTP (disabled by default; enabled via User Secrets). Production would use a transactional email gateway (SendGrid, Postmark, AWS SES). No SMS delivery.
- No monitoring, alerting, or deployment infrastructure.
