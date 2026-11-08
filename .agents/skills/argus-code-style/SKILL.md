---
name: argus-code-style
description: Format or check C# and Next.js code in Argus after editing source or when asked for a formatting pass.
---

Use the Code style section of [AGENTS.md](../../../AGENTS.md). Check the worktree
first and limit feature cleanup to files you changed. A repository-wide cleanup
belongs in a dedicated style change.

## C#

From `backend/`, use `argus.Server.csproj` for server files and
`tests/argus.Server.Tests/argus.Server.Tests.csproj` for test files.

```text
dotnet format <project> --include <paths>
dotnet format style <project> --diagnostics IDE0005 --severity info --include <paths>
```

Paths are relative to `backend/`. Omit `--include` for a full pass or add
`--verify-no-changes` for a check. Resolve naming diagnostics with a symbol
rename and review long parameter declarations.

## Next.js

From `frontend/`, run `npm exec --no -- prettier --write -- <paths>` on changed
TS or TSX files. Use `npm run format` for a full pass. Run
`npm run format:check` and `npm run lint` to verify formatting and zero lint
warnings.

Inspect the diff for unintended changes, especially whitespace inside C# raw
strings or TSX text. Run the verification commands in AGENTS.md for the affected
projects and report any check you could not run.
