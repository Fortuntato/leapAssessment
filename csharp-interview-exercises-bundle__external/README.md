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
5. **Single process only.** Separate service instances don't share locks, so double spending would become possible.
6. **The lock map never shrinks.** One semaphore stays in memory for every account ever used.
7. **Transfers on a busy account wait in line.** Every transfer touching the same account waits its turn.
8. **No currency or account-status checks** (frozen or closed accounts). `Account` has no fields for them.
