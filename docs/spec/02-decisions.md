# Architecture Decisions

Decisions taken after review of [`00-master-prompt.md`](00-master-prompt.md) and
[`01-sql-schema.md`](01-sql-schema.md), before any code was written. Each was chosen
deliberately over named alternatives; the rejected options are recorded so a future reader can
tell a decision from an accident.

These extend the two specification documents. They do not override them, and they do not change
the phased build order — master prompt §1 still governs: **Phase 0, then Phase 0.5, then stop and
report.**

Schema consequences are in [`04-schema-delta.md`](04-schema-delta.md).

| # | Decision | Status |
| --- | --- | --- |
| [D1](#d1--global-vehicle-catalog-with-tenant-overlay) | Global vehicle catalog with tenant overlay | Accepted |
| [D2](#d2--multi-tenant-user-membership) | Multi-tenant user membership | Accepted |
| [D3](#d3--deduplication-strong-id-only-auto-merge) | Deduplication: strong-ID-only auto-merge | Accepted |
| [D4](#d4--search-behind-an-abstraction-sql-server-first) | Search behind an abstraction, SQL Server first | Accepted |
| [D5](#d5--full-export-trade-canonical-model) | Full export-trade canonical model | Accepted |
| [D6](#d6--cross-currency-pricing-via-a-normalized-base-price) | Cross-currency pricing via a normalized base price | Accepted |
| [D7](#d7--phase-0-blocker-tables-now-the-rest-at-their-phase) | Phase 0 blocker tables now, the rest at their phase | Accepted |

---

## D1 — Global vehicle catalog with tenant overlay

### Problem

The schema is internally inconsistent. `Vehicles.TenantId` is non-nullable, implying every
vehicle belongs to exactly one tenant. But `VehicleSources` explicitly supports shared sources
(`TenantId` nullable, plus an `IsShared` flag), implying many tenants read the same upstream
data. Both cannot be true. If twenty tenants subscribe to the same shared Carapis source, either
they each store a private copy of every vehicle, or the vehicle rows are global.

### Decision

**Vehicles from shared sources are global.** `Vehicles.TenantId` becomes nullable: `NULL` means
the global catalog, non-null means a tenant's own private inventory (manual, CSV, Excel, XML,
JSON, FTP). Tenant-specific commercial state lives in a new `TenantVehicles` overlay table.

### Why

- Deduplication and Japanese→English normalization run **once** instead of once per tenant. Both
  are expensive and both improve with more data.
- Storage stays linear in the number of real vehicles, not vehicles × tenants.
- It matches what `VehicleSources.IsShared` already implies.

### Cost accepted

- Tenant isolation for vehicles becomes a **read filter**
  (`TenantId == null || TenantId == current`) rather than a hard row-level equality. That is a
  weaker guard, so schema §9's cross-tenant tests move from good practice to mandatory, and must
  additionally prove a tenant cannot *write* to a global row.
- A bad merge is now visible to every tenant simultaneously. This is the direct reason D3 is
  conservative.
- Nullable `TenantId` breaks the §6 unique constraint, because SQL Server treats NULLs as
  distinct. Fixed with a persisted `TenantScope` computed column — see
  [`04-schema-delta.md`](04-schema-delta.md#1--global-catalog--tenant-overlay).

### Rejected

- **Copy per tenant.** Strongest isolation and the simplest security model, but N× storage and
  dedup runs N times over the same data, producing N chances to merge wrongly.
- **Global canonical, tenant-scoped listings.** Close to the chosen option, but it forces every
  tenant to hold its own listing rows even for shared sources, which reintroduces most of the
  duplication without the isolation benefit.

---

## D2 — Multi-tenant user membership

### Problem

The schema puts `TenantId` on `UserRoles` but not on `Users`, and makes `Users.Email` globally
unique. That shape implies one identity can belong to several tenants, but nothing in either
document states it, and several consequences are unhandled.

### Decision

**Intentional: one global user identity, with membership and roles resolved per tenant.**
Confirmed as a requirement — dealer groups and staff working across branches need it.

### Why

- Matches the schema as written; no migration needed later.
- A salesperson at a dealer group can work across branches with one login.

### Cost accepted

- Login needs a tenant-selection step, and JWTs must carry the active `TenantId`. Tenant
  switching gets its own endpoint and writes an `AuditLogs` row.
- `UserRoles` alone cannot express "invited but no role yet" or "suspended in this tenant only",
  and `Users.Status` is global — using it for per-tenant suspension would lock the user out
  everywhere. Requires a `TenantUsers` membership table.
- `Customers.AssignedUserId` has no constraint tying the assignee to that tenant. A customer
  could be assigned to a user with no membership in the owning tenant. Must be guarded in the
  application layer and covered by an integration test.
- `Roles.Name` is globally unique, which prevents tenant-defined roles. Changed to
  `UNIQUE(TenantId, Name)` with `NULL` reserved for system roles.

### Rejected

- **Tenant-scoped users** (`TenantId` on `Users`, `UNIQUE(TenantId, Email)`). Every FK to `Users`
  becomes automatically tenant-safe, which is a real security benefit — but it blocks dealer
  groups, which is a stated requirement.
- **Global identity limited to one tenant.** Simplest for Phase 0 and relaxable later without
  data migration, but defers a requirement that is already known.

---

## D3 — Deduplication: strong-ID-only auto-merge

### Problem

`Vehicles.CanonicalHash` is the entire deduplication mechanism and is specified in one word. The
same car appears on BE FORWARD, SBT and TCV with different photos, differently rounded mileage
and different prices — and master prompt §7 restricts VIN to "where legally and contractually
available", so the strongest identifier is frequently missing. A hash also only does exact
matching, which cannot collapse those listings.

### Decision

**Auto-merge only on an exact strong identifier** — normalized VIN, else chassis number, else
source lot/stock number. Fuzzy similarity **never** auto-merges; it writes a scored suggestion to
a `VehicleMatchCandidates` review queue for human confirmation. All merges are recorded in
`VehicleMergeHistory` and are reversible.

### Why

- Honors master prompt §3's "conservative deduplication".
- Under D1 the catalog is global, so a wrong merge shows a wrong price or wrong availability to
  every tenant at once. The asymmetry is stark: a missed merge shows a duplicate; a wrong merge
  can lose a sale or sell a car twice.
- The review queue still captures the value of fuzzy matching without betting the catalog on a
  similarity threshold nobody has tuned yet.

### Cost accepted

- Visible duplicates remain in the catalog until a human clears the queue.
- Requires human review capacity, and a UI for it in Phase 1.
- `CanonicalHash` needs an index — it is currently the only unindexed lookup key in the design.

### Rejected

- **Exact hash only, as written.** Zero wrong merges, but leaves duplicates permanently and
  undercuts the "one intelligent workspace" USP.
- **Fuzzy auto-merge above a confidence threshold.** Best consolidation, but no threshold can be
  tuned before real multi-source data exists, and the blast radius under D1 is every tenant.

---

## D4 — Search behind an abstraction, SQL Server first

### Problem

Neither document states a scale target — no vehicle count, tenant count, concurrent users or
latency budget — and master prompt §4 lists no search engine. "Advanced vehicle search" over
5,000 rows and over 3,000,000 rows are different systems, and the §7 index list serves only a
narrow set of query shapes.

### Decision

**Put search behind an `ISearchProvider` abstraction and implement SQL Server first.** Master
prompt §8 already requires the POC to measure response time; that measurement becomes the
decision gate for adding a dedicated search engine.

### Why

- Matches the spec's own adapter philosophy (master prompt §5: all external services behind
  interfaces).
- Buys the option without buying the infrastructure. If p95 fails at realistic volume, the
  adapter is swapped without touching business logic.
- Master prompt §18 forbids unlimited synchronization without filters and quotas, so the catalog
  is bounded by design — SQL Server may well be sufficient.

### Cost accepted

- Index design stays provisional until the POC produces numbers.
- If an engine is needed later, an indexing pipeline and reindex strategy are Phase 1 work.

### Rejected

- **Committing to SQL-Server-only now.** Cheapest, but an unrecoverable guess if volume is high.
- **Adding a search engine on day one.** Removes the risk but adds a cluster, an indexing
  pipeline and a sync-lag failure mode before any evidence they are needed.

---

## D5 — Full export-trade canonical model

### Problem

The canonical model is a generic car, not an export-trade vehicle. It has no steering side, no
auction grade, no registration date and no lot number. `PriceType` exists but is never
enumerated. Meanwhile `CustomerRequirements` already carries `DestinationCountryCode` — so the
demand side knows the destination, but the vehicle side carries nothing to filter eligibility
against, and master prompt §12's "deterministic hard filters" step has nothing to filter on.

### Decision

**Extend the canonical model now**, before normalization is written. Add steering side, auction
grade, inspection score, registration date, lot number, doors and seats to `Vehicles`; add
freight cost, freight currency and port of discharge to `VehicleListings`; enumerate `PriceType`
as `FOB | CIF | CFR`. Add `Makes`, `Models` and `SourceMakeModelAliases` reference tables.

Field-by-field detail is in [`03-canonical-vehicle-model.md`](03-canonical-vehicle-model.md).

### Why

- Steering side (RHD/LHD) is arguably the single most-used filter in this trade and cannot be
  derived from any existing column.
- `RegistrationDate` is distinct from `ModelYear`, and destination import-age rules key on
  registration — a car that cannot legally land is not a match.
- `PriceType` without an enumeration makes cross-source price comparison meaningless; FOB and CIF
  differ by the entire cost of shipping.
- `LotNumber` doubles as a strong identifier for D3, directly improving dedup quality.
- Adding these now avoids re-running normalization against stored raw payloads later.

### Cost accepted

- A wider `Vehicles` table before the POC proves which fields sources actually populate. Fields
  are nullable; the POC's completeness measurement (master prompt §8) will show which ones are
  worth keeping.

### Rejected

- **Minimal set now** (steering side, auction grade, registration date, lot number only).
  Defensible, but freight and price type are needed the moment two sources are compared.
- **Keep the generic model.** Lowest upfront work, but guarantees a reprocessing pass.

---

## D6 — Cross-currency pricing via a normalized base price

### Problem

There is no `ExchangeRates` table. Index `VehicleListings(TenantId, Price, CurrencyCode)` serves
only same-currency filtering, so a buyer searching "under $8,000" against JPY-denominated stock
cannot use an index at all. Master prompt §13 also asks for average price and price trends, which
are not computable across mixed currencies.

### Decision

Add an `ExchangeRates` table and maintain a denormalized `PriceBaseCurrency` on
`VehicleListings`, populated at sync time and indexed for range search. **Pin the rate used** by
storing `ExchangeRateId` on the listing.

### Why

- Makes cross-currency range filters index-servable, which is the common search.
- Pinning the rate stops historical reports from silently rewriting themselves as rates move — a
  price-trend report that changes retroactively is worse than no report.

### Cost accepted

- Denormalized data needs a refresh job when rates change, and a decision on refresh cadence.
- Requires choosing an FX rate source and handling its outages.

### Rejected

- **Convert at query time.** Always uses live rates and adds no columns, but no index can serve
  it, so search degrades sharply with catalog size.
- **Single currency for the POC.** Keeps Phase 0.5 small but guarantees reprocessing of every
  price captured during the POC.

---

## D7 — Phase 0 blocker tables now, the rest at their phase

### Problem

Several master-prompt requirements have no storage anywhere in the schema:

| Requirement | Source | Table |
| --- | --- | --- |
| Token revocation strategy | §14 | missing |
| Users, roles **and permissions** | §3 | only `Roles`/`UserRoles` |
| Webhook idempotency via provider event IDs | §10 | missing |
| Opt-ins, templates, messaging windows | §10 | missing |
| Human approval before external AI messaging | §11 | missing |
| Saved searches | §15 | missing |
| Customer tags, notes timeline, activity timeline | §9 | one `Notes` column |
| Vehicle features/options | §7 | missing |

### Decision

Add **only the Phase 0 blockers now**: `RefreshTokens`, `Permissions`, `RolePermissions`. Defer
the rest to their own phase, added via migrations.

### Why

- §14's token revocation and §3's permissions are explicit **Phase 0** deliverables. Shipping
  Phase 0 without them means shipping it incomplete.
- Everything else belongs to Phase 1 or 2, and schema §10 is explicit: "do not create every
  future table solely for the POC; expand through migrations as Phase 1 begins."

### Cost accepted

- More migrations later, by design.

### Rejected

- **Add every table now.** A complete, stable ERD up front, but directly contradicts schema §10
  and creates tables that stay empty for months.
- **Keep §10's POC minimum exactly.** Would ship Phase 0 with no working token revocation and no
  permissions model, requiring §14 and §3 to be formally deferred.

---

## Not decided

The following were identified during review and are **not** resolved. They do not block Phase 0.
Each is tracked in [`05-open-items.md`](05-open-items.md) with an owner slot: media
redistribution rights, the Carapis licensing gate, PII/data-protection obligations, PII redaction
before AI calls, billing and quota enforcement, observability and alerting, the WhatsApp
24-hour messaging window, `PublicId` coverage, Phase 0 acceptance criteria, and saved-search
alerting.
