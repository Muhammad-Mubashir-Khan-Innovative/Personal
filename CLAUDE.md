# Car Dealer SaaS — Project Context

Multi-tenant enterprise SaaS for international used-car dealers/importers, focused initially on
Japanese vehicle export. Aggregates vehicle inventory from multiple permitted sources into one
workspace, with dealer CRM, customer requirements, messaging, AI recommendations and reporting.

**Repository:** `mushikhan2212-create/Personal` (renamed from
`Muhammad-Mubashir-Khan-Innovative/Personal`; the old URL still redirects to the same repo).

---

## Read these first, in this order

| Document | What it is |
| --- | --- |
| [`docs/spec/00-master-prompt.md`](docs/spec/00-master-prompt.md) | Product + architecture spec |
| [`docs/spec/01-sql-schema.md`](docs/spec/01-sql-schema.md) | SQL Server schema + ERD spec |
| [`docs/spec/02-decisions.md`](docs/spec/02-decisions.md) | **D1–D11 — locked decisions.** Read before designing anything |
| [`docs/spec/03-canonical-vehicle-model.md`](docs/spec/03-canonical-vehicle-model.md) | Export-trade vehicle model, all enums |
| [`docs/spec/04-schema-delta.md`](docs/spec/04-schema-delta.md) | DDL-level changes; **authoritative over `01-`** |
| [`docs/spec/05-open-items.md`](docs/spec/05-open-items.md) | 13 unresolved items with owner slots |
| [`docs/spec/06-phase-0-acceptance.md`](docs/spec/06-phase-0-acceptance.md) | Phase 0 pass/fail checklist |
| [`backend/README.md`](backend/README.md) | How to run, test, and log in |

**Authority order:** the two `.docx` files are the signed-off originals; the Markdown copies are
the working source of truth. `04-schema-delta.md` overrides `01-sql-schema.md` where they differ.
Decisions extend the master prompt — they never silently contradict it, and where they amend it
the master prompt is annotated in place.

## Current state

**Phase 0 is complete, pushed, and awaiting the user's own testing sign-off.** The sign-off table
at the bottom of `06-phase-0-acceptance.md` is deliberately blank — that is the user's gate, not
ours. Do not start Phase 0.5 until they confirm.

- 41 automated tests pass (13 unit, 28 integration) against real SQL Server
- 29 live acceptance checks pass against a running API
- Both container images build; the API image was run end to end
- Branch: `claude/car-dealer-saas-prompt-access-1rv1ig`

**Next up: Phase 0.5** — Carapis adapter behind `IVehicleSourceProvider`, raw payload storage,
canonical normalization, strong-ID dedup with a review queue, search behind `ISearchProvider`,
and the first React/Vite screen. Schema for it is already specified in
[`04-schema-delta.md` §8](docs/spec/04-schema-delta.md).

## The decisions that constrain new code

Full rationale in `02-decisions.md`. In brief:

- **D1 Global vehicle catalog + tenant overlay.** `Vehicles.TenantId` is nullable: NULL = global
  catalog, non-null = a tenant's private inventory. Per-tenant state lives in `TenantVehicles`.
- **D2 Multi-tenant identity.** One global user; membership and roles resolve per tenant. A token
  is valid for exactly one tenant; switching issues a new one.
- **D3 Conservative dedup.** Auto-merge ONLY on exact strong identifier (VIN → chassis → lot).
  Fuzzy matches go to a human review queue. Never auto-merge on a similarity score.
- **D4 Search behind `ISearchProvider`.** SQL Server first; the POC's p95 measurement is the gate
  for adding a search engine.
- **D5 Export-trade model.** Steering side, auction grade, registration date, lot number, and
  enumerated incoterms (EXW/FOB/CFR/CIF) are first-class.
- **D6 Cross-currency.** `ExchangeRates` table plus denormalized `PriceBaseCurrency`, with the
  rate pinned per listing so historical reports stay stable.
- **D7** Phase 0 blocker tables now; the rest via migrations at their phase.
- **D8 TypeScript** on the frontend (amends the master prompt's JavaScript).
- **D9 Ant Design** as the component library.
- **D10 Phase 0 ships no frontend.** React arrives at Phase 0.5.
- **D11 .NET 10 / EF Core 10** (amends the master prompt's .NET 8, which leaves support Nov 2026).

## Working agreements

- **Phase gate.** Build a phase, the user tests it, then proceed. Master prompt §1 requires a
  hard stop after Phase 0.5 for a report.
- **Branch.** Develop on `claude/car-dealer-saas-prompt-access-1rv1ig`. Never push elsewhere
  without explicit permission.
- **Migrations for every schema change.** CI fails if the model and migrations disagree.
- **Tests with each module.** Integration tests must use real SQL Server, never the EF in-memory
  provider — it ignores unique indexes, filtered indexes and computed columns, which is exactly
  what tenant-scope uniqueness is built from.
- **Never commit secrets.** Local dev placeholders are fine and are documented as such.
- **Report honestly.** If something could not be verified, say so rather than marking it green.

## Local development

```bash
cd backend
docker compose up -d --build      # SQL Server + Redis + API + Worker
# Swagger: http://localhost:5080/swagger
dotnet test                        # needs SQL Server running
```

Seeded accounts all use `Dev_Passw0rd!`. Start with `multi@example.test` — Admin in
`nihon-motors`, ReadOnly in `karachi-auto`; it exercises most of the tenancy design in one login.

## Gotchas already paid for — do not rediscover these

- **JwtBearer claim mapping.** `options.MapInboundClaims = false` is required. With mapping on,
  `sub` and `email` are rewritten to WS-Federation URIs and every lookup returns null, which
  surfaces as a spurious 401 while custom claims like `tenant_id` keep working. Very easy to
  misdiagnose. There is a regression test for it.
- **Nullable `TenantId` breaks unique constraints.** SQL Server treats NULLs as distinct, so
  `UNIQUE(TenantId, ...)` silently stops preventing duplicates on exactly the global rows. Fixed
  with a persisted computed `TenantScope = ISNULL(TenantId, 0)` column. Any new nullable-tenant
  table must do the same.
- **Visibility is not mutability.** The query filter admits `TenantId IS NULL` rows, which also
  permits *updates* to them. Global rows must be write-guarded separately, and tested.
- **Unresolved tenant must fail closed.** `TenantIdOrZero` returns 0, which matches nothing.
  Never let an unresolved context mean "all data".
- **`Newtonsoft.Json` is pinned to 13.x** in the two projects referencing Hangfire, because
  Hangfire.SqlServer drags in 11.0.1 which carries a high-severity advisory. Remove the pin only
  when Hangfire's own floor moves past 13.0.1.
- **Integration test classes share a fixture.** Any test that mutates seeded state must restore
  it, or it breaks whichever test happens to run next.
- **xUnit parallelism is disabled** in the integration assembly; parallel database provisioning
  against one SQL Server produced flaky "cannot open database" failures.

## Open items that block later phases

Tracked with owner slots in `05-open-items.md`. The two most urgent:

- **O2 — Carapis licensing.** Phase 0.5 is built entirely on this provider and its commercial
  terms are unresolved. The spec currently only asks the POC to *document* the question; it
  should be *resolved* before Phase 1, with a named fallback (direct partner feeds + dealer
  CSV/XML/FTP). The adapter abstraction means a "no" costs a pivot, not a rewrite.
- **O1 — Media rights.** Whether third-party images may be copied or only hot-linked is a legal
  question, and it blocks the Phase 1 media pipeline.

Also open and unverified locally: the Redis-backed cache path (the image was unreachable in the
build sandbox) and migrating from a previous migration version (only one migration exists so far).
