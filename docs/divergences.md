# Documented divergences from the Rust sibling kit

The C# kit preserves the customer-visible behaviour and safety outcomes of
the NextJS and Rust Argus (frozen at commit
`6396b1ad3b5fdf5578669f999a67c78e2b6d4dc9`). The differences below are
deliberate, C#-appropriate mechanisms, not behaviour changes. No known Rust
defect or unsafe behaviour was found during implementation, so none was
corrected.

## Mechanism differences

1. **Web stack.** ASP.NET Core minimal APIs instead of Axum. Routes, methods,
   status codes, JSON field names, error kinds, and header names are the
   frozen product contract and are unchanged.
2. **Data access.** Raw SQL through Npgsql instead of sqlx, with embedded
   migrations recorded in `schema_migrations`. The table shapes and
   transaction boundaries match the Rust schema.
3. **Password hash encoding.** Argon2id (m=64 MiB, t=3, p=1) through
   Konscious.Security.Cryptography with a self-describing
   `argon2id$v=19$m=65536,t=3,p=1$…` value instead of a PHC string. Hash
   strength and verification semantics are equivalent. Existing Rust hashes
   are not migrated; the two kits do not share a database.
4. **Project naming.** The C# project and namespaces use the kit's
   `{slugSnake}.Server` token so instantiation brands them legally in C#; the
   project file sits at `backend/` root so `dotnet run` works from that
   directory, with tests in `backend/tests/` excluded from its compile globs.
5. **Timestamps.** API timestamps are RFC3339 UTC with fixed 7-digit
   fractional seconds (`2026-10-01T12:00:00.0000000Z`). The Rust kit emits
   equivalent RFC3339 values with variable fractional precision; clients
   treat both as opaque strings.
6. **Audit stream.** The same `audit.*` event names are emitted through
   .NET structured logging; JSON console formatting is selected with
   `ARGUS_LOG_FORMAT=json`.
7. **Rate limiting.** The same buckets, windows, and thresholds are
   implemented as an in-process C# sliding window. It remains per-process,
   exactly as the Rust kit's documented residual limit.
8. **First-run commands.** `dotnet run` replaces `cargo run`; the bootstrap
   checks for `dotnet` instead of the Rust toolchain. The journey, safety
   properties, and idempotence are unchanged.
9. **Environment prefix.** The kit source writes `ARGUS_*` so the CLI
   can replace it with the customer's chosen prefix at instantiation.
