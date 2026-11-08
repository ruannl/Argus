---
name: argus-api-change
description: Change an Argus backend REST endpoint and its Next.js server-side caller together, including authorization and response compatibility.
---

Read the product boundary in [AGENTS.md](../../../AGENTS.md) and the affected route in [the repository layout](../../../docs/repository-layout.md).

1. Trace the route from the frontend page or server action through `frontend/src/lib/server/backend-client.ts` to the backend endpoint. Identify callers and the statuses and payloads they depend on.
2. Reuse the backend's application-secret, session, and account-ownership checks where the route requires them. Keep backend credentials and calls on the server. Check how the frontend handles expired sessions, denied access, and invalid input.
3. Exercise success and rejection behavior with the appropriate backend and frontend tests. Update the route documentation when the public contract changes. Use `argus-verify` for database and browser checks.
