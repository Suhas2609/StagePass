# StagePass — Project Panel Demo Guide

## What You're Demonstrating
A real engineering solution to a real problem. Ticket platforms fail under concurrent load
when the booking logic lives in application code. StagePass pushes the critical check into
an atomic SQL statement, making overselling mathematically impossible.

## Pre-Demo Setup (5 minutes before)

1. dotnet run --project StagePass.Api
2. Open these four browser tabs:
   - Tab 1: https://localhost:<port>/index.html        (Customer view)
   - Tab 2: https://localhost:<port>/performance.html?id=1  (Performance detail)
   - Tab 3: https://localhost:<port>/demo.html         (Concurrency demo)
   - Tab 4: https://localhost:<port>/organizer.html    (Organizer portal)
3. In Tab 4, enter the Organizer API key configured in your local User Secrets (`dotnet user-secrets set "OrganizerApiKey" "<your-dev-organizer-key>" --project StagePass.Api`) → Connect.

## Demo Flow (10–12 minutes)

### Act 1: The Product (3 min)
- Show Tab 1: "This is StagePass. Customers discover performances here."
- Point out: dark/light theme, category filtering, date picker, pagination.
- Click Symphony Under the Stars → Tab 2 loads.
- Show: ticket tiers (Standard vs Premium), real availability counts, reservation form.
- Say: "This is a live connection to SQL Server. The available count is live data."

### Act 2: The Problem (1 min)
- Say: "Eventbrite, BookMyShow, Ticketmaster — they all face this problem."
- "When a popular show goes on sale and 10,000 people click Buy at the same moment,
  how do you ensure exactly the right number of tickets are sold?"
- "The naive implementation reads the count, checks it in code, then writes.
  Under concurrent load, two threads both read 1 ticket remaining, both proceed,
  and you end up with 2 sold — one valid booking and one oversell."

### Act 3: The Solution — Live Demo (5 min)
- Switch to Tab 3 (demo.html).
- Say: "I'm about to send 300 simultaneous booking requests for 100 tickets."
- Set tickets = 100, requests = 300.
- Click Run Concurrency Test.
- Watch the progress bars fill in real-time.
- Point to the invariant box: "This is the mathematical proof."
- Expected result: exactly 100 green (allocated), 200 amber (sold out), 0 red (server errors).
- Point to the formula: "Available + Pending + Confirmed = Capacity. Always. No exceptions."
- Say: "This runs against the real SQL Server database. Not mocked. Not in-memory. Real."

### Act 4: The Engineering (2 min)
- Show the "How It Works" explainer cards on demo.html.
- Point to the SQL: "This single statement is the entire concurrency solution."
- "SQL Server's row-level locking means only one concurrent UPDATE can reduce Available
  from 1 to 0. Every other competing request finds 0 remaining and is rejected cleanly."
- "38 automated tests verify this, including a test that sends exactly 500 concurrent
  requests and asserts exactly 100 succeed and 400 receive a correct rejection."

### Act 5: The Organizer View (2 min)
- Switch to Tab 4.
- Show the Dashboard: stat cards, inventory progress bars.
- If time: create a new event/performance live.
- Say: "Organizers can see real-time inventory. When they cancel a performance,
  all pending holds are released atomically in the same transaction."

## Key Technical Talking Points

### If asked "What would break at scale?"
"Two things require attention before horizontal scaling:
1. The background hold-expiry worker runs in-process. Two application instances
   would compete. Fix: a distributed lock via SQL Server's sp_getapplock, or extract
   to a separate job scheduler like Hangfire.
2. Rate limiting is keyed on RemoteIpAddress. Behind a load balancer, add
   ForwardedHeadersMiddleware to use the real client IP."

### If asked "Is this production-ready?"
"The core booking guarantee is production-correct. The organizer authentication
uses a shared demo key — production would replace this with JWT Bearer tokens
and per-organizer identity. Payment processing, email notifications, and monitoring
are documented as future work."

### If asked "How did you test concurrency?"
"38 automated tests. The key test fires 500 concurrent HTTP requests against the
live API with a real SQL Server database. It asserts exactly 100 succeed,
400 receive InsufficientInventory, zero server errors occur, and the database
invariant holds after all requests complete."

### If asked "Why not use a queue?"
"A message queue like Kafka would add operational complexity without solving the
core problem — you still need to decide who gets the last ticket when the queue
drains. The atomic SQL UPDATE solves it at the data layer with zero external
dependencies and consistent sub-millisecond guarantees."

## What Impresses at Each Level
| Audience | What to Emphasise |
|---|---|
| Junior reviewer | The working product, clean UI, booking flow |
| Mid-level engineer | The atomic UPDATE, idempotency key, transaction boundaries |
| Senior architect | The invariant formula, failure modes, scaling analysis, test coverage |
| Non-technical manager | "100 out of 100 tickets sold to 300 simultaneous buyers — no doubles, no errors" |
