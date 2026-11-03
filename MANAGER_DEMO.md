# StagePass — Project Panel Demo Guide

## What you're demonstrating

StagePass solves a real engineering problem faced by every ticketing platform:
when thousands of users click "Buy" simultaneously for the last few tickets, most
systems oversell. StagePass prevents this mathematically using an atomic SQL Server
UPDATE that makes overselling structurally impossible.

## Pre-demo setup (5 minutes before)

```bash
cd StagePass
dotnet run --project StagePass.Api
```

Open five browser tabs:
1. Tab 1: https://localhost:<port>/           (Home — Discovery)
2. Tab 2: https://localhost:<port>/performance.html?id=1 (Booking flow)
3. Tab 3: https://localhost:<port>/demo.html  (Concurrency proof)
4. Tab 4: https://localhost:<port>/dashboard.html (Organizer)
5. Tab 5: https://localhost:<port>/login.html (Auth)

Pre-login as organizer in Tab 4 (admin@stagepass.local / StagePass2026!).

## The 12-minute demo flow

### Act 1: The product (3 min) — Tab 1

Open Tab 1. Show the home page.

Say: "StagePass is a live event discovery and ticketing platform."

Point out:
- "Each section shows a different category — Music, Theatre, Comedy, Dance."
- "The event cards show real data from SQL Server."
- Click a card: "The performance detail page shows live ticket availability."
- In Tab 2: Select a tier, enter a quantity, enter your email, click Reserve.
- Show the countdown ring: "The server holds these tickets for exactly 5 minutes."
- Click Confirm → show the booking confirmation page with the SP-XXXXXXXX code and email confirmation notice.
- "This is an end-to-end real booking against a real SQL Server database, triggering transactional confirmation emails."

### Act 2: The problem (1 min) — Tab 3

Switch to Tab 3.

Say: "Every ticketing platform faces this: a popular event goes on sale, and 
10,000 people click at exactly the same second. The naive implementation reads 
available tickets into memory, checks the count in code, then issues a separate 
write. Under load, two threads both see 1 ticket available and both proceed. 
You get 2 sales for 1 ticket — overselling."

### Act 3: The live proof (5 min) — Tab 3

Point to the config: "100 available tickets. I'm about to send 300 simultaneous requests with a controlled concurrency pool."

Click Run Test.

Point to the live Request Storm Visualizer:
- "Each dot represents an in-flight buyer request arcing from the user cluster to the SQL Server."
- "Green explosions indicate allocated tickets (201 Created)."
- "Amber explosions indicate sold-out rejections (409 Conflict)."
- "Red indicates any rate-limit or network errors."

The progress bar and metric counters fill in real-time.

When it completes:
- Point to the green number (should be exactly 100): "100 tickets sold."
- Point to the amber number (should be 200): "200 rejected — correct behavior."
- Point to the red number (should be 0): "Zero server errors."
- Point to the invariant box: "The database formula: Available + Pending + Confirmed = Capacity.
  It holds. No oversell. No negative inventory."

Say: "This runs against the real SQL Server database. Not a simulation. Not mocked."

### Act 4: The engineering (2 min) — Tab 3 scroll down

Scroll to the explainer cards.

Point to the SQL snippet:
"This single SQL statement is the entire concurrency solution. SQL Server locks 
the row before evaluating the WHERE clause. The second request finds Available 
already reduced and gets zero rows affected — rejected before any state changes."

Point to the test table:
"88 automated tests run against a real SQL Server database. The concurrency test 
fires 500 requests and asserts exactly 100 succeed."

### Act 5: The organizer view (2 min) — Tab 4

Show Tab 4 (already logged in).

"Organizers log in with email and password. They can create events, schedule 
performances, and view real-time inventory."

Show the inventory bars: "This shows capacity used per ticket tier."

If time allows: create a new event and performance live.

## Key questions and answers

**Q: What breaks under scale?**
"Two things: (1) The background worker that expires holds runs in-process. Multiple 
app instances need a distributed lock or an external scheduler. (2) Rate limiting uses 
RemoteIpAddress — behind a load balancer you'd add ForwardedHeadersMiddleware."

**Q: Is this production-ready?**
"The booking engine is production-correct. The organizer auth uses email/password for
this MVP — production would add password reset, OAuth, and per-organizer tenant 
isolation. No real payment processing yet."

**Q: Why not use a queue?**
"A queue still needs to decide who gets the last ticket when it drains. The atomic SQL 
UPDATE solves it at the data layer with zero external dependencies and consistent 
sub-millisecond response."

**Q: How did you test concurrency without Postman or k6?**
"xUnit + WebApplicationFactory + Task.WhenAll. 500 concurrent HTTP requests from the 
test process directly to the API, all assertions against the actual SQL Server database."

## What impresses at each level

| Audience | Emphasise |
|---|---|
| Junior reviewer | Working product, clean UI, complete booking flow |
| Mid-level engineer | Atomic UPDATE, idempotency key, transaction boundaries |
| Senior architect | Invariant formula, failure modes, scaling analysis |
| Non-technical panel | "300 simultaneous buyers, 100 tickets — exact count, no doubles, zero crashes" |
