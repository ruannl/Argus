# Secret rotation

## Inventory

| Secret | Where it lives | Protects |
| --- | --- | --- |
| Application secret (`ARGUS_API_SECRET`) | `backend/.env`, `frontend/.env` | Proves the caller is the trusted frontend |
| Auth.js secret (`ARGUS_AUTH_SECRET`) | `frontend/.env` | Encrypts the frontend session cookie |
| Billing webhook secret (`ARGUS_BILLING_WEBHOOK_SECRET`) | `backend/.env`, provider dashboard | Verifies provider webhook signatures |
| Resend API key (`ARGUS_RESEND_API_KEY`) | `backend/.env`, Resend dashboard | Sends verification and reset email |
| Database password | `ARGUS_DATABASE_URL` | PostgreSQL access |

No secret is ever committed. `.env` files are git-ignored; the packed kit
archive excludes `.env` and key/certificate material. The origin record
contains project identity only, never secrets.

## Application secret

1. Generate a new value: `openssl rand -hex 32`.
2. Update `ARGUS_API_SECRET` in `backend/.env` and `frontend/.env` to
   the same value.
3. Restart the backend and the frontend. Sessions are unaffected; a mismatch
   between the two fails closed with `401 unauthorized`.

Rotate after any suspected exposure, and whenever the frontend's server
environment may have leaked.

## Auth.js secret

1. Generate a new value: `openssl rand -hex 32`.
2. Update `ARGUS_AUTH_SECRET` in `frontend/.env` and restart the
   frontend.
3. Every frontend session cookie becomes unreadable; users sign in again.
   Backend sessions remain valid until their own expiry or revocation.

## Billing webhook secret

1. Generate and set the new secret in the provider's webhook settings.
2. Update `ARGUS_BILLING_WEBHOOK_SECRET` and restart the backend.
3. Any webhook signed with the old secret receives `401` and is retried by
   the provider with the new signature. Apply both changes together to avoid
   a window of rejected deliveries.

## Resend API key

1. Create a new key in Resend and verify the sender domain.
2. Update `ARGUS_RESEND_API_KEY` and `ARGUS_RESEND_FROM` and
   restart the backend.
3. Delete the old key. Delivery failures are logged, never surfaced to
   account flows, so monitor backend logs after rotating.

## Database password

1. Change the password in PostgreSQL.
2. Update `ARGUS_DATABASE_URL` and restart the backend. A non-local URL
   must include the password and `sslmode=require` or `sslmode=verify-full`.
