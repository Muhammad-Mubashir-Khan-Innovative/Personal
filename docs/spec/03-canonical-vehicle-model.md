# Canonical Vehicle Model

Implements [D5](02-decisions.md#d5--full-export-trade-canonical-model). Extends master prompt §7
and the `Vehicles`/`VehicleListings` tables in [`01-sql-schema.md`](01-sql-schema.md) §3.

The organising rule is master prompt §7's separation, restated:

> **A `Vehicle` is a physical car. A `VehicleListing` is one source's offer of that car.**

Anything intrinsic to the car (steering side, engine, chassis number) belongs on `Vehicle`.
Anything that varies by who is selling it (price, incoterm, freight, availability) belongs on
`VehicleListing`. When in doubt, ask whether two sources could disagree about it: if they can,
it is listing data.

All new columns are nullable. Sources populate them inconsistently, and master prompt §8 requires
the POC to measure data completeness — that measurement tells us which fields are real.

---

## 1. `Vehicles` — new columns

| Column | Type | Notes |
| --- | --- | --- |
| `SteeringSide` | `tinyint` | Enum. The most-used filter in this trade; not derivable from any existing column. |
| `AuctionGrade` | `nvarchar(8)` | Overall grade from the auction sheet. Free-ish text — see [§4](#4-auction-grades). |
| `InteriorGrade` | `nvarchar(4)` | Interior letter grade, graded separately from the body. |
| `InspectionScore` | `decimal(3,1)` | Numeric form of the overall grade where parseable, for range filtering. |
| `RegistrationDate` | `date` | First registration. **Distinct from `ModelYear`** — see [§3](#3-registrationdate-vs-modelyear). |
| `LotNumber` | `nvarchar(64)` | Auction lot / dealer stock number. Also a strong dedup identifier ([D3](02-decisions.md#d3--deduplication-strong-id-only-auto-merge)). |
| `Doors` | `tinyint` | |
| `Seats` | `tinyint` | |
| `MakeId` | `int` FK → `Makes` | Normalized. `Make` nvarchar is retained as the raw source value. |
| `ModelId` | `int` FK → `Models` | Normalized. `Model` nvarchar is retained as the raw source value. |

`Vehicles.TenantId` also becomes nullable under
[D1](02-decisions.md#d1--global-vehicle-catalog-with-tenant-overlay) — see
[`04-schema-delta.md`](04-schema-delta.md).

## 2. `VehicleListings` — new columns

| Column | Type | Notes |
| --- | --- | --- |
| `PriceType` | `tinyint` | Enum, now enumerated. See [§5](#5-price-type-incoterms). |
| `FreightCost` | `decimal(18,2)` | Quoted freight to `PortOfDischarge`, when the source provides it. |
| `FreightCurrencyCode` | `char(3)` | May differ from `CurrencyCode`. |
| `PortOfLoading` | `nvarchar(64)` | Typically a Japanese port. |
| `PortOfDischarge` | `nvarchar(64)` | Destination port the freight quote applies to. |
| `PriceBaseCurrency` | `decimal(18,2)` | Denormalized converted price ([D6](02-decisions.md#d6--cross-currency-pricing-via-a-normalized-base-price)). |
| `BaseCurrencyCode` | `char(3)` | Currency `PriceBaseCurrency` is expressed in. |
| `ExchangeRateId` | `bigint` FK → `ExchangeRates` | Pins the rate used, so historical reports stay stable. |

A freight quote is only meaningful together with its destination. `FreightCost` without
`PortOfDischarge` must be treated as unknown, not as zero.

## 3. `RegistrationDate` vs `ModelYear`

These are different facts and the distinction has legal force.

`ModelYear` is the model designation. `RegistrationDate` is when the car was first registered,
and it is what destination import-age rules actually key on. A vehicle can be a 2017 model first
registered in 2018, which under an eight-year rule remains importable for a full year after the
2017 model year would suggest otherwise.

Several destination markets enforce age limits of this kind. The specific rules are **not**
encoded here — they change by country and by year, they are a compliance question rather than a
schema question, and getting them wrong in a hard filter would hide valid stock. The schema's job
is to carry `RegistrationDate` so that a rules table can be added later. Until that table exists,
destination eligibility is not a hard filter.

Recorded as an open item in [`05-open-items.md`](05-open-items.md).

## 4. Auction grades

Japanese auction houses grade the body overall and the interior separately, and conventions vary
between houses. Overall grades commonly run `S`, `6`, `5`, `4.5`, `4`, `3.5`, `3`, `2`, `1`, with
`R` / `RA` marking a repaired vehicle. Interior grades are letters, commonly `A` through `D`.

Because the vocabulary is not uniform across houses, `AuctionGrade` and `InteriorGrade` are
stored as **short strings, not enums** — the source value is preserved verbatim. `InspectionScore`
holds the numeric interpretation where one can be parsed, so range filters ("grade 4 and above")
work without forcing every house's vocabulary into one enum.

`R`/`RA` do not map to a number. They must not silently become `NULL` in a way that lets a
repaired vehicle pass a "grade 4 and above" filter — leave `InspectionScore` NULL and exclude
NULLs from numeric grade filters.

## 5. Price type (incoterms)

`PriceType` was specified but never enumerated. Comparing an FOB price against a CIF price
without knowing which is which is comparing two different numbers — they differ by the entire
cost of shipping and insurance.

| Value | Code | Meaning |
| --- | --- | --- |
| 1 | `EXW` | Ex works — price at the seller's yard. |
| 2 | `FOB` | Free on board — loaded at the port of loading. |
| 3 | `CFR` | Cost and freight — includes freight, excludes insurance. Also written C&F. |
| 4 | `CIF` | Cost, insurance and freight — includes both. |

Search and ranking must never compare prices across different `PriceType` values without
normalizing first. Where `FreightCost` is known, an FOB price can be brought to a CFR-comparable
figure; where it is not, the listings are not comparable and must not be ranked against each
other on price alone.

## 6. Enumerations

Stored as `tinyint`. Value `0` is reserved for `Unknown` throughout — sources routinely omit these
fields, and a missing value must be distinguishable from a real one.

### SteeringSide
| Value | Name |
| --- | --- |
| 0 | Unknown |
| 1 | RHD |
| 2 | LHD |

### VehicleStatus
Canonical statuses, from master prompt §7.

| Value | Name | Meaning |
| --- | --- | --- |
| 0 | Unknown | |
| 1 | Active | Available. |
| 2 | Reserved | Held against a specific customer. |
| 3 | Sold | Confirmed sold. |
| 4 | Unavailable | Temporarily not purchasable; may return. |
| 5 | Expired | Absent from the source past its grace period (schema §11). |
| 6 | Archived | Retained for history; excluded from search. |

`Unavailable` and `Expired` are distinct on purpose. `Unavailable` is a statement by the source;
`Expired` is an inference we drew from the listing's absence. Only `Expired` is set by the sync
job's grace-period rule.

### MileageUnit
| Value | Name |
| --- | --- |
| 0 | Unknown |
| 1 | Kilometers |
| 2 | Miles |

Never compare or range-filter mileage without normalizing the unit first.

### Transmission
| Value | Name |
| --- | --- |
| 0 | Unknown |
| 1 | Manual |
| 2 | Automatic |
| 3 | CVT |
| 4 | SemiAutomatic |
| 5 | DualClutch |

### FuelType
| Value | Name |
| --- | --- |
| 0 | Unknown |
| 1 | Petrol |
| 2 | Diesel |
| 3 | Hybrid |
| 4 | PluginHybrid |
| 5 | Electric |
| 6 | LPG |
| 7 | CNG |
| 8 | Hydrogen |

Hybrid and plug-in hybrid are separated because destination emissions and tax rules frequently
treat them differently.

### Drivetrain
| Value | Name |
| --- | --- |
| 0 | Unknown |
| 1 | FWD |
| 2 | RWD |
| 3 | AWD |
| 4 | FourWD |

## 7. Make/model normalization

Schema §7 indexes `Make` and `Model` as free-text `nvarchar`. Source data will arrive as
`TOYOTA`, `Toyota`, `toyota` and `トヨタ` for the same manufacturer. That fragments the index,
breaks faceted search, splits demand reporting across spellings, and weakens dedup.

Three reference tables fix it:

| Table | Purpose |
| --- | --- |
| `Makes` | Canonical manufacturer list. |
| `Models` | Canonical model list, FK to `Makes`. |
| `SourceMakeModelAliases` | Maps a raw source string (per source) to a canonical `MakeId`/`ModelId`. |

The raw `Make`/`Model` strings stay on `Vehicles` as received, for attribution and debugging.
`MakeId`/`ModelId` carry the normalized values and are what search, facets and reporting use.

This is where Japanese→English normalization lives. An unmapped alias must **not** silently drop
the vehicle from the catalog — normalize what maps, leave `MakeId`/`ModelId` NULL otherwise, and
surface the unmapped values as a review queue so the alias table can be extended. Vehicles with
NULL `MakeId` remain searchable by raw text.

## 8. What is deliberately not modeled

- **Destination import-eligibility rules.** Carried data (`RegistrationDate`), not encoded rules.
  See [§3](#3-registrationdate-vs-modelyear).
- **Vehicle features/options** (master prompt §7). Deferred to Phase 1 under
  [D7](02-decisions.md#d7--phase-0-blocker-tables-now-the-rest-at-their-phase); needs a
  `VehicleFeatures` table plus a controlled vocabulary, and neither is Phase 0 work.
- **Total landed cost.** Requires destination duties and taxes per country. Out of scope for the
  documented phases.
