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
   leave it unset and set the Auth.js public URL so mismatched hosts are
   rejected.
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
