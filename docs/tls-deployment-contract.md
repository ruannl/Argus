# TLS and public deployment contract

The kit is a foundation, not a deployment platform. This contract states what
must hold before anything is exposed publicly. The operator owns the hosting,
the certificates, and the network boundary.

## Required conditions

1. **TLS terminates in front of both services.** The frontend and the backend
   must be reachable only over HTTPS in production. The backend should not be
   exposed directly to the internet; put it behind the same ingress or a
   private network as the frontend server.
2. **Exact origins are configured.** `ARGUS_FRONTEND_URL` must be the
   exact public frontend origin. It is used for the backend's origin rule,
   the CORS allowlist, and the links in verification and reset emails. A
   mismatch fails closed with `403`.
3. **Cookies are secure in production.** The Auth.js session cookie is
   `httpOnly`, `sameSite=lax`, and `secure` when `NODE_ENV=production`. Do not
   disable that.
4. **Host trust is explicit.** `ARGUS_AUTH_TRUST_HOST=true` is for local
   development and trusted proxies that set `X-Forwarded-Host`. In production,
   leave it unset: `ARGUS_FRONTEND_URL` is pinned as the Auth.js public URL, and
   incoming request origins are rewritten to it, so a spoofed `Host` header
   cannot change callbacks or cookies. Setting the variable to `false` rejects
   every host and is not the production setting.
5. **The database requires a password and TLS off-localhost.** A non-local
   `ARGUS_DATABASE_URL` without a password, or without
   `sslmode=require`/`verify-full`, is rejected at startup.
6. **Secrets live in the platform's secret store**, not in committed files,
   image layers, or environment defaults. The container reads configuration
   from environment variables only.
7. **Health checks and non-root runtime.** The backend image runs as uid
   10001 and exposes `GET /health` for the platform's health probe.
8. **Email is real before launch.** Replace the bootstrap's development
   Resend placeholder with a real key and a verified sender; otherwise users
   never receive verification or reset links.

## Statement import deployment requirements

**Current scope:** Standard Bank PDF import is deferred by Plan 2 revision 5.
The backend rejects `standard_bank_pdf` before launching the parser child, so
the child-process requirements below are dormant but remain part of the
deployment contract for the follow-up PDF thread.

9. **The proxy admits the bounded import envelope.** Allow multipart requests up
   to 10,100,000 bytes and JSON review mutations up to 256 KiB. Do not raise
   these limits at the trust boundary to compensate for a larger file; the
   importer rejects anything beyond its frozen service bounds.
10. **Request and parser timeouts are compatible.** The backend enforces a
    30-second parser deadline for CSV and PDF parsing. The ingress request
    timeout must be longer than the parser deadline plus normal database work,
    and the runtime must allow the backend to launch the
    `--parse-statement-pdf` child of its own assembly when PDF is re-enabled.
11. **Published output is self-contained.** Deploy the published backend output,
    including the server runtime configuration. The PDF child is launched from
    that output, not only from `dotnet run`.
12. **Parsers are bounded.** Expect at most two concurrent parsers per backend
    process, with CSV and PDF sharing the same admission slots. Each PDF child
    has a 256 MiB managed-heap target, a 384 MiB resident-memory ceiling
    enforced by the parent, bounded stdin and stdout, and whole-tree termination
    on timeout or disconnect. A platform that forbids child processes cannot
    serve the Standard Bank PDF preset when it is re-enabled.
13. **Import privacy survives infrastructure.** Raw uploads are not persisted.
    Ensure proxy access logs, APM traces, error trackers, and backups do not
    capture request bodies, multipart filenames, or financial source text.
    Database backups may retain committed financial rows; backup retention and
    restore drills are operator responsibilities.

## Operator responsibilities

TLS certificates and renewal, network isolation, backups and restore drills,
PostgreSQL upgrades and tuning, secret storage, log retention and shipping,
alerting, and rate-limit placement at the edge. The kit deliberately keeps
in-process rate limiting and a mock billing provider; a real deployment adds
its own provider integration and edge protections.

## Verification before exposure

- `curl -fsS https://<frontend>/` serves the product with HSTS present.
- `curl -fsS https://<backend-host>/health` returns `{"status":"ok"}` only
  from inside the private network.
- A request with a foreign `Origin` receives `403`.
- Sign-up, verification, login, and reset complete against real email.
- The billing webhook accepts a correctly signed provider event and rejects
  an unsigned one with `401`.
