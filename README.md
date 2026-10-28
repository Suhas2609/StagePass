# StagePass

## 1. What StagePass Is

StagePass is a live-event ticket booking MVP built with C#, ASP.NET Core Web API (`net10.0`), Entity Framework Core, SQL Server, and a zero-dependency vanilla HTML/CSS/JavaScript frontend. It implements the complete reservation hold, booking confirmation, background hold expiry, and organizer management lifecycle under high concurrency.

## 2. The Engineering Problem It Demonstrates

Flash-sale ticketing systems fail when inventory is checked in application memory (`SELECT Available` → C# `if (available >= quantity)` → `UPDATE Available`), because concurrent threads read the same stale count and oversell capacity. StagePass solves this by delegating inventory allocation to a single atomic, conditional SQL `UPDATE` inside a database transaction (`WHERE TicketTypeId = @id AND Available >= @qty`), relying on SQL Server's row-level exclusive (`U`/`X`) lock serialization and database `CHECK` constraints (`Available >= 0 AND Available <= Capacity`) to guarantee zero overselling even when 500 concurrent requests compete for 100 tickets.

## 3. Prerequisites

- **.NET SDK**: .NET 10 SDK (`10.0.x`)
- **Database**: SQL Server (Developer/Evaluation/Express edition on `localhost` or SQL Server LocalDB)
- **IDE / Editor**: Visual Studio Code with the C# Dev Kit extension (or Visual Studio 2022+)

## 4. Setup

### a. Clone the Repository

```bash
git clone <repository-url>
cd StagePass
dotnet tool restore
```

### b. Initialize User Secrets for the Development Connection String

Credentials and development connection strings are never committed to source control. Configure the `DefaultConnection` secret for `StagePass.Api`:

```bash
dotnet user-secrets init --project StagePass.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=StagePassDb;Trusted_Connection=True;TrustServerCertificate=True;" --project StagePass.Api
```

*(If using SQL Server LocalDB instead of a default `localhost` instance, use `Server=(localdb)\\MSSQLLocalDB;Database=StagePassDb;Trusted_Connection=True;TrustServerCertificate=True;`.)*

### c. Apply the EF Core Migration

```bash
dotnet tool run dotnet-ef database update --project StagePass.Api
```

### d. Run the Application

```bash
dotnet run --project StagePass.Api
```

Navigate to the local URL printed in the console (e.g. `https://localhost:7142/` or `http://localhost:5189/`) to use the customer booking UI, or `/organizer.html` for the organizer dashboard (default demo key in `appsettings.json`: `REPLACE_FOR_DEMO`).

## 5. Seeding

When `StagePass.Api` starts in the `Development` environment with a valid non-system database configured in `ConnectionStrings:DefaultConnection`, `Program.cs` automatically applies any pending EF Core migrations and invokes `DbInitializer.SeedAsync`. Seeding is idempotent (`if (await context.Venues.AnyAsync()) return;`) and populates:
- **1 Organizer**: Midnight Sun Productions
- **2 Venues**: Aurora Concert Hall (Brightmoor), Harbor Lights Theatre (Port Elyse)
- **2 Events**: *Symphony Under the Stars* (Music), *The Lighthouse Keeper* (Theatre)
- **3 Upcoming Performances** across those events
- **6 Ticket Types** (Standard and Premium tiers for each performance)

## 6. Running Tests

The test project (`StagePass.Tests`) uses a separate database (`StagePassTestDb`) so running tests never modifies or wipes development data in `StagePassDb`.

### a. Non-Concurrency / Standard Test Suite

Runs input validation unit tests, seed verification, reservation lifecycle HTTP integration tests, idempotency tests, and API smoke tests:

```bash
dotnet test --filter "Category!=RequiresSqlServer"
```

To run only the pure in-memory input validation unit tests (no database connection required):

```bash
dotnet test --filter "FullyQualifiedName~ReservationInputTests"
```

### b. Integration Tests (Test Database Configuration)

By default, `appsettings.Testing.json` points to `Server=localhost;Database=StagePassTestDb;Trusted_Connection=True;TrustServerCertificate=True;`. You can override the test database connection string via the `STAGEPAS_TEST_CONNECTIONSTRING` environment variable:

```powershell
$env:STAGEPAS_TEST_CONNECTIONSTRING="Server=localhost;Database=StagePassTestDb;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test
```

### c. High-Concurrency Verification Suite

Runs the 500-concurrent-request oversell test (`ConcurrentReservations_FiveHundredRequests_OneHundredTickets`) and confirm/cancel vs. expiry race tests against SQL Server:

```bash
dotnet test --filter "Category=RequiresSqlServer"
```

## 7. Key Design Decisions

### a. Atomic Conditional `UPDATE` (Oversell Prevention)
Inventory allocation executes a single parameterized SQL statement inside an explicit transaction:
```sql
UPDATE TicketTypes
SET Available = Available - @quantity
WHERE TicketTypeId = @ticketTypeId
  AND Available >= @quantity
  AND PerformanceId IN (
      SELECT PerformanceId FROM Performances WHERE Status = 'Upcoming'
  )
```
SQL Server acquires an exclusive row lock on the target `TicketTypes` row when evaluating the `WHERE` predicate and decrementing `Available`. If `rowsAffected == 0`, the transaction rolls back immediately and returns `409 Conflict` (`InsufficientInventory` or `PerformanceCancelled`).

### b. Inventory Invariant Definition
At every committed point in time, for every `TicketType` row:
$$\text{Available} = \text{Capacity} - \sum \left(\text{Quantity} \;\middle|\; \text{ReservationHold.Status} \in \{\text{'Pending'}, \text{'Confirmed'}\}\right)$$

### c. Idempotency Mechanism
Every `POST /api/reservations` request includes a client-generated `IdempotencyKey` (`Guid`, non-empty).
1. **Pre-check**: A read-only lookup checks if a `ReservationHold` with `IdempotencyKey` already exists. If found with matching `TicketTypeId` and `Quantity`, the existing hold is returned (`200 OK`); if parameters differ, `409 IdempotencyKeyReused` is returned.
2. **Unique Index & Constraint Fallback**: The database enforces a unique index `IX_ReservationHolds_IdempotencyKey`. If 50 concurrent requests with the same key pass the pre-check simultaneously, only one `INSERT` succeeds; the remaining 49 catch SQL Server error `2601`/`2627` (`DbUpdateException`), roll back their inventory decrement, re-query the winning hold, and return `200 OK`.

### d. Expiry Worker Three-Step Transaction
`HoldExpiryWorker` periodically reclaims inventory from unconfirmed holds whose `ExpiresAt <= GETUTCDATE()` inside a single database transaction:
1. **Atomic State Transition with `OUTPUT`**: Transitions `Status` from `'Pending'` to `'Expired'` and captures the exact rows transitioned using `UPDATE ReservationHolds SET Status = 'Expired' OUTPUT inserted.HoldId, inserted.TicketTypeId, inserted.Quantity INTO @Expired WHERE Status = 'Pending' AND ExpiresAt <= GETUTCDATE()`.
2. **In-Memory Aggregation**: Groups the captured rows by `TicketTypeId` and sums `Quantity` (sorted by `TicketTypeId` to ensure deterministic lock ordering).
3. **Inventory Restoration**: Executes `UPDATE TicketTypes SET Available = Available + @total WHERE TicketTypeId = @ticketTypeId` for each group and commits the transaction.

## 8. What Is Not Production-Ready

StagePass is an architectural MVP focused on transactional concurrency and inventory correctness. The following aspects are intentionally simplified and **not** ready for public production use:

- **Organizer API key is a demo mechanism, not production auth**: All `/api/organizer/*` endpoints share a single static key compared in `OrganizerApiKeyAttribute`. There is no organizer identity, per-organizer ownership enforcement, key rotation, or audit logging.
- **No real payment processing**: Confirming a hold (`POST /api/reservations/{holdId}/confirm`) transitions the hold to `Confirmed` immediately without integrating with a payment gateway (e.g., Stripe/Adyen) or handling asynchronous payment webhooks.
- **No email or notification delivery**: Customer email addresses (`CustomerIdentifier`) are stored for ownership verification on confirmation, but no confirmation or cancellation emails are sent.
- **No horizontal scaling tested**: Running multiple instances of `StagePass.Api` simultaneously without a distributed lock or external job scheduler causes `HoldExpiryWorker` instances to compete on the same database tables.
- **No HTTPS certificate management documented**: Relies on the local ASP.NET Core development certificate (`dotnet dev-certs https`) rather than production TLS termination / automated certificate renewal.
- **No monitoring or alerting**: Uses standard console logging without OpenTelemetry metrics, distributed tracing, dead-letter queues, or health-check alerting.
