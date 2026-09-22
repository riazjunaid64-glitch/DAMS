# AGENTS.md — rules for every AI agent working in this repo

This file is the **single source of truth** for how any AI coding agent must work in
DAMS. It applies to Claude Code, Kilo Code, Cursor, Copilot, Codex, and any model or
tool that comes later. Tool-specific files (`CLAUDE.md`, `.kilocode/rules/`,
`.github/copilot-instructions.md`) exist only to point back here — if they ever
disagree with this file, **this file wins**.

If you are an agent reading this: follow the workflow in §2 on every task, without
being asked. It is not optional and it is not "only for big changes".

---

## 1. What this repo is

A real-estate / property management system (DAMS).

| Part | Path | Stack |
| --- | --- | --- |
| API | `BACKEND/DAMS.Api` | ASP.NET Core 8, controllers, Swagger |
| Business logic | `BACKEND/DAMS.Application` | Services, DTOs, Interfaces |
| Entities | `BACKEND/DAMS.Domain` | POCOs, enums |
| Data access | `BACKEND/DAMS.Infrastructure` | EF Core — `Data/`, `Migrations/`, `Sql/` |
| Tests | `BACKEND/DAMS.Application.Tests` | xUnit, EF InMemory + real SQL Server |
| Web app | `frontend` | React 19, TypeScript, Vite, Tailwind 4, Vitest |

Solution file: `BACKEND/DAMS.sln`. The frontend is a separate npm project rooted at
`frontend/` — run npm commands from there, not from the repo root.

---

## 2. The working loop — mandatory

Every task runs through these phases in order. Do not skip ahead to editing.

### Phase 1 — Inspect before you touch anything
- Read the actual code path end to end: controller → service → EF query → entity →
  migration; on the frontend, page → hook → api client → types.
- Find where the behaviour **already** exists. This codebase has near-duplicate looking
  code that is deliberately separate; adding another copy is a defect.
- State what the current behaviour is and what is actually wrong. If you cannot
  describe the bug in one sentence, keep reading.
- Check `git log` / `git diff` for recent related work so you do not undo a deliberate fix.

### Phase 2 — Plan
- Decide the smallest change that fixes the real cause, not the symptom.
- Name the files you will touch, and why, before touching them.
- If two readings of the request lead to materially different work, ask. Otherwise pick
  the sensible one, say which you picked, and continue.

### Phase 3 — Change
- Match the surrounding code's naming, structure, comment density and idiom.
- Keep the change scoped to the request. No drive-by refactors, no reformatting of
  untouched lines, no "while I was here" renames.
- Leave no dead code, commented-out blocks or TODOs behind.

### Phase 4 — Review your own change
Re-read the full diff (`git diff`) as if someone else wrote it, hunting specifically for:
- Missing or wrong `await`, swallowed exceptions, silent `catch`.
- Broken null handling, off-by-one, inverted comparison.
- EF traps: client-side evaluation, N+1, missing `AsNoTracking`, reuse of a tracked
  entity, a query the in-memory provider accepts but SQL Server cannot translate.
- Money and dates — see §4. These are where bugs are most expensive.
- Authorization: anything touching customer data must be scoped by the authenticated
  user's identity, never by an email or a client-supplied id (§4).
- Frontend: stale closures in hooks, wrong dependency arrays, unmemoized heavy work,
  `any` creeping into typed boundaries.

### Phase 5 — Verify
Run what the change touched (§3). Never report success on unrun code.

### Phase 6 — Re-review for regressions
After the fix and after tests pass, walk the diff **one more time** and ask:
- Did this break another caller of what I changed? Grep for every usage.
- Did I change a shared DTO, contract or enum the frontend also consumes?
- Did I change a query a report, dashboard or export also depends on?
- Did I introduce a new failure mode — an irreversible migration, or behaviour that only
  shows up under concurrent requests or retries?

If phase 6 finds anything, fix it and run phases 4–6 again. Repeat until a full pass is
clean. Only then report done.

### Phase 7 — Report honestly
- Say exactly what changed, what you ran, and what passed or failed.
- If you skipped something, say so and why. Never claim verification you did not do.
- If tests fail, show the output. A failing test is a result, not something to hide.

---

## 3. Commands

Backend (from repo root):

```bash
dotnet restore BACKEND/DAMS.sln
dotnet build   BACKEND/DAMS.sln -c Release
dotnet test    BACKEND/DAMS.Application.Tests/DAMS.Application.Tests.csproj
dotnet run --project BACKEND/DAMS.Api --launch-profile http   # http://localhost:5219
```

Frontend (from `frontend/`):

```bash
npm ci
npm run lint
npm test          # vitest run
npm run build     # tsc -b && vite build
npm run dev
```

Real SQL Server invariant tests — the in-memory provider does **not** enforce unique or
filtered indexes or check constraints, and it client-evaluates LINQ that SQL Server
would reject. Anything touching migrations, indexes, constraints or derived finance
queries must be proven here:

```bash
DAMS_SQLSERVER_TEST_CONNECTION="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=true;Encrypt=false" \
dotnet test BACKEND/DAMS.Application.Tests/DAMS.Application.Tests.csproj \
  --filter "FullyQualifiedName~SqlServerProductionInvariantTests|FullyQualifiedName~ClientIdentitySqlTests"
```

With that variable unset those tests silently skip — a green run means nothing. If your
change is in that area and you could not run them against a real server, say so
explicitly in your report.

CI (`.github/workflows/pr-validation.yml`) runs: backend build + in-memory tests on
Windows, the SQL Server suite on Linux, and frontend `lint` + `test` + `build`. Your
change must pass all four; match CI locally before declaring done.

---

## 4. Domain rules that must not be broken

Invariants, not preferences. Breaking one is a production incident.

**Identity and authorization**
- Email is **never** an authorization key. A customer's records are reachable only
  through the authenticated user's id (`Customer.UserId`). Never look up or authorize by
  email, phone, or a client-supplied customer id.
- Do not add a new code path that writes `Customer.UserId`. The existing, deliberate
  ways it gets written are the only ones.

**Money**
- Amounts are `decimal`. Never `float`/`double`, never a rounded intermediate.
- Cost is recognised when it is **agreed**, not when it is paid (accrual). A payout
  settles a liability; it must not move profit a second time.
- Gross vs net stays consistent — withholding tax is deducted from a payment, it does
  not reduce the recorded expense.
- There is exactly **one** Net Profit figure across the system. If a new screen needs a
  different number, that is a disclosure, not a second definition of profit.
- Every mutable finance record carries a `RowVersion` concurrency token; any update path
  must round-trip it. Dropping it is a silent lost-update bug.
- Any operation that writes money must be safe to retry — no double write on replay.
  Check the existing idempotency guards before adding a new write path.

**Dates**
- Every financial / business date is a **Pakistan business date**, not UTC. Do not put
  `DateTime.UtcNow` into a business-date column, and do not compare a business date
  against a UTC value.

**Data layer**
- A schema change means a real EF migration committed with the code, plus an updated
  model snapshot. Never hand-edit an already-applied migration.
- Report and dashboard queries must produce gapless buckets, and drill-downs that add up
  to their totals.

**Notifications**
- One central notification system. Do not send from a new ad-hoc place, and do not send
  when the required settings are not configured.

---

## 5. General conduct

- **Do not commit or push unless asked.** Never `push --force`, never rewrite shared
  history, never skip hooks (`--no-verify`). If you are on `main`, branch first.
- Do not create files that were not needed — no stray scripts, notes or summary `.md`
  files in the repo. Use a temp directory for scratch work.
- Never commit secrets, connection strings, tokens or real customer data.
- Do not add a dependency for something the existing code already does. If a new package
  is genuinely needed, say why before adding it.
- Prefer deleting code over adding flags; prefer fixing the cause over guarding the
  symptom.
- If you disagree with the request, say so once in a sentence or two, then do the work as
  asked under stated assumptions. Scope is the user's call.
