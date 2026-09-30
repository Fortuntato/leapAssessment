# Exercise 1 — Bank Transfer Service

Implementation of `ITransferService.TransferAsync`, which moves money between two accounts safely when several transfers run at once, and runs each `requestId` only once.

### Design choices

- **The `requestId` is recorded after the account lookup but before the balance check.**
  - A request with a wrong account id can be corrected and retried with the same id.
  - A repeat of a successful transfer does nothing, even if the balance has dropped since.
- **The lock provider is injected, not a `static` field.** This keeps `TransferService` testable, and a distributed lock can replace the in-memory one without changing the service. 

## Assumptions

1. The service runs as a **single process**, so in-memory locks are enough.
2. Amounts use one currency with **at most 2 decimal places**.
3. A repeated `requestId` is assumed to carry the **same details** (accounts and amount) and therefore unique.
4. The provided `IAccountStore` and `IIdempotencyStore` interfaces are unchanged.

## Known limitations

1. **A request repeated after a fail reports success.** `IIdempotencyStore` only records that an id was seen, not how the request ended. If a transfer fails after its id is recorded (insufficient funds or a storage error), a retry with the same id returns normally without moving money. Clients must use a new `requestId` after a failure, this is linked to the first assumption.
2. **Atomicity is best effort.** `IAccountStore` saves each account in a separate call. If writing back the source balance also fails, or the process crashes between the debit and the credit, the balances end up wrong.
3. **A repeat with different details is silently ignored** (see assumption 4).
4. **A repeat after an account is deleted** throws `AccountNotFoundException` instead of doing nothing, because accounts are loaded before the id check.
5. **Single process only.** Separate processes don't share the in-memory locks, so double spending would become possible without a distributed lock provider.
6. **The lock map never shrinks.** One semaphore stays in memory for every account ever used.
7. **Transfers on a busy account wait in line.** Every transfer touching the same account waits its turn.
8. **No currency or account-status checks** (frozen or closed accounts). `Account` has no fields for them.


# Exercise 2 — Async HTTP Client Review

# Async HTTP Client Review – `UserClient`

## Problems identified in the original code

1. **Blocking calls (`.Result`)**
   `SendAsync(...).Result` and `ReadAsStringAsync().Result` wait synchronously on async work. This can deadlock where a synchronization context exists (UI apps, classic ASP.NET) and wastes thread-pool threads under load.
   `return await Task.FromResult(name);` added nothing and hid the fact that the method was really synchronous.

2. **Cancellation token ignored**
   `GetUserName` accepted a `CancellationToken` but never passed it on, so callers could not cancel a request.

3. **No difference between "not found" and real errors**
   Every non-success status, including `404 Not Found`, threw an exception. A missing user is a normal outcome, not a failure.

4. **Exception had no status code**
   `HttpRequestException` was created without `StatusCode`, so callers had to parse the message text to find out what went wrong.

5. **Wrong JSON parsing**
   `JsonDocument.Parse(json).RootElement.GetString()` assumes the body is a JSON string. The API returns an object (`{"id":1,"name":"..."}`), so this throws `InvalidOperationException`.

6. **Resources not disposed**
   `HttpRequestMessage`, `HttpResponseMessage` and `JsonDocument` (which uses pooled buffers) were never disposed.

7. **Silent `new HttpClient()` fallback**
   `_http = http ?? new HttpClient();` hides configuration mistakes, ignores the configured `BaseAddress` (so the relative URL fails), and creating clients this way can exhaust sockets.

8. **No resilience**
   A single temporary failure (network error, `503`, slow server) failed the whole call, and there was no timeout of its own.

## Known limitations

- `Retry-After` given as a date is ignored, and a server-provided delay is not capped.
- Retries are not logged.

# Exercise 3 — Product Search with LINQ and Paging

## How the query avoids loading too much data into memory

`ProductService.SearchAsync` builds a single `IQueryable<Product>` and lets the database do the work. Nothing is materialized until the final `ToListAsync`.

- **Filtering in SQL:** the search term and category are `Where` clauses on the `IQueryable`. They translate to a SQL `WHERE`, so unmatched rows never leave the database.
- **Sorting in SQL:** `OrderBy`/`OrderByDescending` (with `Id` as a tie-breaker) become `ORDER BY`. No sorting happens in application memory.
- **Paging in SQL:** `Skip`/`Take` become `OFFSET`/`FETCH`. Only one page is read, and `PageSize` is capped at 100 no matter what the caller asks for.
- **Projection to `ProductDto`:** `Select` runs before `ToListAsync`, so only `Id`, `Sku`, `Name` and `Price` are selected. `Category` and `CreatedUtc` aren't fetched.
- **No change tracking:** `AsNoTracking()` stops EF from building tracking entries for read-only results.
- **Cheap total count:** `CountAsync` runs on the filtered query and returns one integer. If the total is 0, or the requested page is past the end, the page query is skipped.

Result: at most 100 small DTOs are held in memory per request, whatever the table size.