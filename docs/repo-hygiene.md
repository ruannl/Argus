# Repository hygiene

What must not be committed, and what to check before every push.

## Never commit

- `.env` files or any file containing a real secret, token, or password. The
  repository ignores `.env` and `.env.*` except `.env.example` files.
- Private keys or certificates (`.pem`, `.key`, `.p12`, `.pfx`, `.p8`,
  `.crt`, `.cer`). The kit archive excludes these extensions as a second
  guard; the repository should never hold them either.
- Customer data, database dumps, or log exports containing account data.
- Build output: `bin/`, `obj/`, `.next/`, `node_modules/`, coverage,
  Playwright reports.
- The kit origin record or any locally generated instantiation output.

## Always commit

- Source, tests, and configuration templates (`.env.example`).
- The frontend lockfile (`frontend/package-lock.json`); `npm ci` depends on
  it being in sync with `package.json`.
- The kit descriptor (`kit.json`) for kit-source work.

## Before pushing

1. Run the verification battery in `AGENTS.md` (format, tests, build, e2e).
2. Confirm `git status` does not list any secret-bearing or generated file.
3. Confirm `.env.example` still carries no real values.
4. Rewrite `frontend/src/lib/landing-content.ts` for the product before
   publishing the product publicly; the shipped copy is kit source copy.
