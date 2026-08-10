# Schema Delta

Every change to [`01-sql-schema.md`](01-sql-schema.md) implied by
[`02-decisions.md`](02-decisions.md). **Where this document and `01-sql-schema.md` conflict, this
document is authoritative** — `01-` records the schema as originally specified.

Written to be sufficient for an EF Core model without inventing requirements. Types are SQL
Server. Conventions from schema §2 hold throughout: UTC `datetime2(3)`, `nvarchar` for
multilingual text, `decimal(18,2)` for money, and FKs/indexes are mandatory.

Which of these belong to Phase 0 versus later is in [§8](#8-phasing).

---

## 1 — Global catalog + tenant overlay

Implements [D1](02-decisions.md#d1--global-vehicle-catalog-with-tenant-overlay).

### 1.1 Nullable `TenantId`

`NULL` = global catalog row, sourced from a shared `VehicleSource`.
Non-null = a tenant's own private inventory (manual, CSV, Excel, XML, JSON, FTP).

```sql
ALTER TABLE Vehicles       ALTER COLUMN TenantId bigint NULL;
ALTER TABLE VehicleListings ALTER COLUMN TenantId bigint NULL;
ALTER TABLE VehicleImages   ALTER COLUMN TenantId bigint NULL;
```

`VehicleImages` is included deliberately. It FKs to `VehicleId`, so an image of a global vehicle
must itself be global — leaving it non-nullable would make images of global vehicles
unrepresentable.

`Customers`, `CustomerRequirements`, `Conversations`, `Messages`, `Tasks`, `Imports`,
`Integrations` and `VehicleRecommendations` stay non-nullable. Nothing about them is ever shared.

### 1.2 `TenantScope` — the nullable-unique-constraint fix

SQL Server treats `NULL` values as **distinct** in unique constraints and unique indexes. Once
`TenantId` is nullable, `UNIQUE(TenantId, VehicleSourceId, ExternalListingId)` stops preventing
duplicates on exactly the rows that matter most: the global ones. Every sync run would insert
another copy of the same listing, silently.

Fix with a deterministic persisted computed column, which is indexable:

```sql
ALTER TABLE VehicleListings
    ADD TenantScope AS ISNULL(TenantId, 0) PERSISTED;

CREATE UNIQUE INDEX UX_VehicleListings_Scope_Source_External
    ON VehicleListings (TenantScope, VehicleSourceId, ExternalListingId)
    WHERE ExternalListingId IS NOT NULL;
```

Tenant IDs are positive, so `0` is a safe sentinel for "global". Add the same computed column to
`Vehicles` and `VehicleImages` for consistent index keys.

**The same latent bug already exists in the original schema.** `VehicleSources.TenantId` is
nullable per schema §3, and §6 specifies `UNIQUE(TenantId, Code)` — which therefore permits
duplicate shared-source codes today. Fix it the same way:

```sql
ALTER TABLE VehicleSources
    ADD TenantScope AS ISNULL(TenantId, 0) PERSISTED;

CREATE UNIQUE INDEX UX_VehicleSources_Scope_Code
    ON VehicleSources (TenantScope, Code);
```

### 1.3 `TenantVehicles` (new)

Per-tenant commercial state over a global vehicle. Nothing here is ever visible to another tenant.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `bigint` identity | no | PK |
| `TenantId` | `bigint` | no | FK → `Tenants` |
| `VehicleId` | `bigint` | no | FK → `Vehicles` |
| `TenantPrice` | `decimal(18,2)` | yes | The tenant's own retail price |
| `TenantCurrencyCode` | `char(3)` | yes | |
| `TenantStatus` | `tinyint` | yes | Overrides canonical status for this tenant only |
| `IsHidden` | `bit` | no | Default 0; excludes from this tenant's search |
| `IsPinned` | `bit` | no | Default 0 |
| `InternalNotes` | `nvarchar(max)` | yes | |
| `CreatedAtUtc` | `datetime2(3)` | no | |
| `UpdatedAtUtc` | `datetime2(3)` | no | |

- `UNIQUE(TenantId, VehicleId)`
- Index `TenantVehicles(TenantId, IsHidden)`

### 1.4 Query filter and isolation tests

```csharp
modelBuilder.Entity<Vehicle>().HasQueryFilter(
    v => v.TenantId == null || v.TenantId == _tenantContext.TenantId);
```

Same filter on `VehicleListings` and `VehicleImages`.

This is a **weaker guard than flat equality**, so schema §9's isolation tests are mandatory and
must additionally prove:

1. Tenant A cannot read tenant B's private (non-null `TenantId`) vehicles.
2. Tenant A can read global (`NULL`) vehicles.
3. **Tenant A cannot write to or delete a global vehicle row.** Writes to the global catalog come
   only from sync jobs, never from a tenant-scoped request path.
4. Tenant A's `TenantVehicles` overlay is invisible to tenant B.

Test 3 is the one the query filter does not give for free. A read filter permitting `NULL` also
permits *updates* to those rows unless writes are separately guarded.

---

## 2 — Multi-tenant identity

Implements [D2](02-decisions.md#d2--multi-tenant-user-membership).

### 2.1 `TenantUsers` (new)

Membership is not the same as holding a role. `UserRoles` cannot express "invited, no role yet"
or "suspended in this tenant only", and `Users.Status` is global — using it for per-tenant
suspension would lock the user out of every tenant they belong to.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `bigint` identity | no | PK |
| `TenantId` | `bigint` | no | FK → `Tenants` |
| `UserId` | `bigint` | no | FK → `Users` |
| `MembershipStatus` | `tinyint` | no | 1 Invited, 2 Active, 3 Suspended |
| `InvitedByUserId` | `bigint` | yes | FK → `Users` |
| `JoinedAtUtc` | `datetime2(3)` | yes | |
| `CreatedAtUtc` | `datetime2(3)` | no | |
| `UpdatedAtUtc` | `datetime2(3)` | no | |

- `UNIQUE(TenantId, UserId)`
- Index `TenantUsers(UserId, MembershipStatus)` — drives the tenant picker at login

### 2.2 Tenant-defined roles

`Roles.Name` is currently globally unique, which prevents two tenants from each having a role
called "Sales Manager".

```sql
ALTER TABLE Roles ADD TenantId bigint NULL;   -- NULL = system role
ALTER TABLE Roles ADD TenantScope AS ISNULL(TenantId, 0) PERSISTED;
DROP INDEX /* existing UNIQUE on Roles(Name) */;
CREATE UNIQUE INDEX UX_Roles_Scope_Name ON Roles (TenantScope, Name);
```

### 2.3 Assignment guard

`Customers.AssignedUserId` has no constraint tying the assignee to the customer's tenant. A
customer can currently be assigned to a user with no membership in the owning tenant.

Enforce in the application layer: an assignee must have a `TenantUsers` row for the customer's
`TenantId` with `MembershipStatus = Active`. Cover with an integration test. The same guard
applies to `Conversations.AssignedUserId` and `Tasks.AssignedUserId`.

A composite FK could enforce this in the database, but it requires carrying `TenantId` into the
`Users` key, which contradicts D2's global identity. Application-layer enforcement plus a test is
the accepted trade.

### 2.4 Auth

- JWTs carry the active `TenantId`. A token is valid for one tenant at a time.
- Tenant switching is an explicit endpoint that issues a new token and writes an `AuditLogs` row.
- Never accept a client-supplied `TenantId` without checking `TenantUsers` (schema §9).

---

## 3 — Deduplication

Implements [D3](02-decisions.md#d3--deduplication-strong-id-only-auto-merge).

### 3.1 `CanonicalHash` defined

Currently unspecified and, notably, the only unindexed lookup key in the design.

Composition, in strict precedence — the **first** available strong identifier wins:

1. Normalized `Vin` (uppercase, strip whitespace and hyphens)
2. else normalized `ChassisNumber`
3. else `VehicleSourceId` + normalized `LotNumber`

If none is present, `CanonicalHash` is `NULL`. **A NULL hash never matches anything**, including
another NULL. Those vehicles enter the catalog as distinct rows and are only ever consolidated
through the review queue.

```sql
ALTER TABLE Vehicles ADD CanonicalHashSource tinyint NULL;  -- 1 Vin, 2 Chassis, 3 Lot
CREATE INDEX IX_Vehicles_CanonicalHash
    ON Vehicles (CanonicalHash) WHERE CanonicalHash IS NOT NULL;
```

`CanonicalHashSource` records which rule produced the hash, so a VIN-based match can be trusted
more than a lot-number match when reviewing.

### 3.2 `VehicleMatchCandidates` (new)

Fuzzy similarity writes here. It never merges.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `bigint` identity | no | PK |
| `VehicleId` | `bigint` | no | FK → `Vehicles` |
| `CandidateVehicleId` | `bigint` | no | FK → `Vehicles` |
| `Score` | `decimal(5,4)` | no | 0.0000–1.0000 |
| `SignalsJson` | `nvarchar(max)` | yes | Which signals fired and their weights |
| `Status` | `tinyint` | no | 1 Pending, 2 Merged, 3 Rejected |
| `ReviewedByUserId` | `bigint` | yes | FK → `Users` |
| `ReviewedAtUtc` | `datetime2(3)` | yes | |
| `CreatedAtUtc` | `datetime2(3)` | no | |

- `UNIQUE(VehicleId, CandidateVehicleId)`
- Index `VehicleMatchCandidates(Status, Score DESC)` — the review queue

Store each pair once. Normalize so `VehicleId < CandidateVehicleId` before insert, otherwise every
pair is recorded twice and the unique constraint does not catch it.

`SignalsJson` exists so a reviewer can see *why* two vehicles were suggested. A bare score is not
reviewable.

### 3.3 `VehicleMergeHistory` (new)

Merges must be reversible. Under D1 a bad merge is visible to every tenant.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `bigint` identity | no | PK |
| `SurvivingVehicleId` | `bigint` | no | FK → `Vehicles` |
| `MergedVehicleId` | `bigint` | no | FK → `Vehicles` |
| `MergedByUserId` | `bigint` | yes | NULL = automatic strong-ID merge |
| `ReasonsJson` | `nvarchar(max)` | yes | Identifier matched, or reviewer note |
| `RepointedListingIdsJson` | `nvarchar(max)` | yes | For reversal |
| `MergedAtUtc` | `datetime2(3)` | no | |
| `RevertedAtUtc` | `datetime2(3)` | yes | |
| `RevertedByUserId` | `bigint` | yes | |

- Index `VehicleMergeHistory(SurvivingVehicleId)`, `VehicleMergeHistory(MergedVehicleId)`

On merge, `VehicleListings`, `VehicleImages` and `VehicleRecommendations` are repointed to the
surviving vehicle and the merged vehicle is set to `Archived` — **never deleted**. Recording the
repointed listing IDs is what makes reversal possible.

### 3.4 Merge rule

- Auto-merge **only** on exact `CanonicalHash` equality where the hash is non-NULL.
- All other similarity produces a `VehicleMatchCandidates` row and nothing else.
- No threshold auto-merges. No exceptions until real multi-source data exists to tune against.

---

## 4 — Search abstraction

Implements [D4](02-decisions.md#d4--search-behind-an-abstraction-sql-server-first).

- `ISearchProvider` in Application. `SqlServerSearchProvider` in Infrastructure. Per master
  prompt §5, business logic depends only on the interface.
- The POC report records p95 latency for the five realistic searches required by master prompt §8,
  at the catalog size reached during the POC.
- That measurement is the gate for adding a dedicated search engine. No engine is added on
  speculation.

---

## 5 — Export-trade fields

Implements [D5](02-decisions.md#d5--full-export-trade-canonical-model). Columns, types and all
enumerations are in [`03-canonical-vehicle-model.md`](03-canonical-vehicle-model.md); not repeated
here.

New reference tables:

### `Makes`
`Id` int identity PK; `Name` nvarchar(64); `CountryCode` char(2) null; `IsActive` bit.
`UNIQUE(Name)`.

### `Models`
`Id` int identity PK; `MakeId` int FK → `Makes`; `Name` nvarchar(64); `IsActive` bit.
`UNIQUE(MakeId, Name)`.

### `SourceMakeModelAliases`
`Id` bigint identity PK; `VehicleSourceId` bigint FK null (NULL = applies to all sources);
`RawMake` nvarchar(128); `RawModel` nvarchar(128) null; `MakeId` int FK null; `ModelId` int FK
null; `CreatedAtUtc`.
`UNIQUE(VehicleSourceId, RawMake, RawModel)`; index on `(RawMake, RawModel)`.

An unmapped alias leaves `Vehicles.MakeId`/`ModelId` NULL. The vehicle **stays in the catalog** and
remains searchable by its raw text — it is simply absent from facets until an alias is added.
Dropping unmapped vehicles would silently lose inventory.

Replace the §7 index `Vehicles(TenantId, Make, Model, ModelYear, Status)` with:

```sql
CREATE INDEX IX_Vehicles_Scope_Make_Model
    ON Vehicles (TenantScope, MakeId, ModelId, ModelYear, Status);
```

---

## 6 — Currency

Implements [D6](02-decisions.md#d6--cross-currency-pricing-via-a-normalized-base-price).

### `ExchangeRates` (new)

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `bigint` identity | no | PK |
| `BaseCurrencyCode` | `char(3)` | no | |
| `QuoteCurrencyCode` | `char(3)` | no | |
| `Rate` | `decimal(18,8)` | no | Wider than money — FX needs the precision |
| `AsOfUtc` | `datetime2(3)` | no | |
| `Source` | `nvarchar(64)` | no | Rate provider |
| `CreatedAtUtc` | `datetime2(3)` | no | |

- `UNIQUE(BaseCurrencyCode, QuoteCurrencyCode, AsOfUtc)`
- Index `ExchangeRates(BaseCurrencyCode, QuoteCurrencyCode, AsOfUtc DESC)`

Rates are **append-only**. Never update a rate row — historical listings reference it.

### Listing columns

`PriceBaseCurrency`, `BaseCurrencyCode` and `ExchangeRateId` are added to `VehicleListings` (see
`03-`), populated at sync time.

```sql
CREATE INDEX IX_VehicleListings_Scope_BasePrice
    ON VehicleListings (TenantScope, PriceBaseCurrency, IsActive)
    INCLUDE (VehicleId, CurrencyCode, PriceType);
```

Rules:

- `ExchangeRateId` **pins** the rate used. Reports read the pinned rate, so a price-trend report
  does not silently rewrite itself when rates move.
- A background job refreshes `PriceBaseCurrency` on active listings when new rates land. Cadence
  is a configuration value, not a constant in code.
- If FX is unavailable at sync time, store the listing with `PriceBaseCurrency` NULL rather than
  guessing. NULL is excluded from base-currency range filters.
- Never compare prices across different `PriceType` values without normalizing first — see
  [`03-canonical-vehicle-model.md`](03-canonical-vehicle-model.md#5-price-type-incoterms).

---

## 7 — Phase 0 tables

Implements [D7](02-decisions.md#d7--phase-0-blocker-tables-now-the-rest-at-their-phase).

### `RefreshTokens` (new)
Master prompt §14 requires a token revocation strategy; there is currently nowhere to record one.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `bigint` identity | no | PK |
| `UserId` | `bigint` | no | FK → `Users` |
| `TokenHash` | `varbinary(32)` | no | SHA-256. **Never store the token itself** |
| `ExpiresAtUtc` | `datetime2(3)` | no | |
| `RevokedAtUtc` | `datetime2(3)` | yes | |
| `ReplacedByTokenId` | `bigint` | yes | FK → self; rotation chain |
| `CreatedByIp` | `nvarchar(45)` | yes | IPv6-length |
| `CreatedAtUtc` | `datetime2(3)` | no | |

- `UNIQUE(TokenHash)`; index `RefreshTokens(UserId, ExpiresAtUtc)`
- Revoking a token revokes its whole rotation chain — this is what makes reuse detection possible.
- Per master prompt §14, never log the token value.

### `Permissions` (new)
`Id` int identity PK; `Code` nvarchar(128) UNIQUE (e.g. `vehicles.read`); `Description`
nvarchar(256).

### `RolePermissions` (new)
`RoleId` int FK; `PermissionId` int FK; composite PK.

Seeded deterministically per schema §12.

### Deferred to their phase

Added via migrations when their phase begins, per schema §10:

| Table | Phase | Requirement |
| --- | --- | --- |
| `WebhookEvents` | 1 | §10 idempotency — provider event ID dedupe store |
| `CustomerOptIns` | 1 | §10 opt-in records, auditable |
| `MessageTemplates` | 1 | §10 approved templates |
| `SavedSearches` | 1 | §15 |
| `Tags`, `CustomerTags` | 1 | §9 |
| `CustomerActivities` | 1 | §9 activity timeline (distinct from `AuditLogs`) |
| `VehicleFeatures` | 1 | §7 features/options |
| `AiContentApprovals` | 2 | §11 human approval before external AI messaging |

`CustomerActivities` and `AuditLogs` are different things. `AuditLogs` is a technical security
record; the activity timeline is a user-facing CRM feature. Do not conflate them.

---

## 8 — Phasing

**Phase 0** — `TenantUsers`, `RefreshTokens`, `Permissions`, `RolePermissions`, the `Roles`
tenant-scoping change ([§2](#2--multi-tenant-identity), [§7](#7--phase-0-tables)), and the
isolation tests in [§1.4](#14-query-filter-and-isolation-tests).

**Phase 0.5** — everything in [§1](#1--global-catalog--tenant-overlay),
[§3](#3--deduplication), [§4](#4--search-abstraction), [§5](#5--export-trade-fields) and
[§6](#6--currency). These land with the Carapis POC because normalization, dedup and pricing are
exactly what the POC exercises.

**Phase 1+** — the deferred table list in [§7](#7--phase-0-tables).

Schema §10's POC minimum table set expands accordingly: it must now also include `TenantUsers`,
`RefreshTokens`, `Permissions` and `RolePermissions`, because Phase 0 cannot be called complete
without working authorization and token revocation.
