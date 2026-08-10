# Car Dealer SaaS — Claude Code Master Build Prompt

**Implementation specification**

> **Conversion note.** This is a faithful Markdown conversion of
> `Car_Dealer_SaaS_Claude_Code_Master_Prompt(1).docx`, which remains in the repository as the
> signed-off original. This copy is the working source of truth because it is diffable,
> reviewable and greppable in git. If the two ever disagree, the `.docx` wins until this file
> is corrected.
>
> Decisions taken after this document was written are recorded in
> [`02-decisions.md`](02-decisions.md); schema consequences are in
> [`04-schema-delta.md`](04-schema-delta.md). Neither overrides this document — they extend it.

---

## 1. Instruction to Claude Code

Treat this document as the master implementation specification for the project. Inspect the
existing repository first. Do not replace working code unnecessarily. Implement incrementally,
keep the architecture modular, and do not skip tests, migrations, security controls,
documentation, or provider abstractions. Build Phase 0 first, then Phase 0.5 (Carapis POC),
then stop and report results before implementing later phases unless explicitly instructed.

## 2. Product vision

Build a multi-tenant enterprise SaaS for international used-car dealers/importers, initially
focused on Japanese vehicles. The platform combines vehicle inventory from multiple permitted
sources, dealer CRM, customer requirements, WhatsApp/social conversations, AI
recommendations/follow-up assistance, advertising content, and reporting.

USP: connect a dealer's existing vehicle sources into one intelligent workspace.

- Support authorized APIs, licensed aggregators, direct partner feeds, CSV/Excel, XML/JSON, FTP
  and manual inventory.
- Normalize different sources into one canonical vehicle/search model.
- Use customer requirements and conversations to find and rank suitable vehicles.
- Turn conversations into CRM actions, follow-ups and sales intelligence.
- AI is assistive first; external communication requires human approval initially.

## 3. Locked product scope

### Phase 0 — Foundation

- Enterprise multi-tenancy, authentication, authorization, users, roles and permissions.
- Secure configuration/secrets handling, structured logging, correlation IDs and audit logging.
- API versioning, validation, consistent error contracts, health checks and OpenAPI/Swagger.
- EF Core migrations, seed strategy, background jobs, caching abstraction and file/media storage
  abstraction.
- Local-first deployment with clean path to cloud deployment; automated tests and CI-ready
  structure.

### Phase 0.5 — Carapis Vehicle Data POC

- Implement Carapis behind an isolated vehicle-source adapter.
- Test permitted sources such as BE FORWARD, SBT, TCV and other sources available to the test
  account.
- Retrieve a controlled sample, not a full mirror of third-party inventory.
- Store raw payloads separately from canonical data.
- Normalize records, preserve source attribution, and perform conservative deduplication.
- Provide basic search API and a simple React/Vite test screen.
- Measure data completeness, freshness, response time, duplicates, images, quotas and cost.
- Do not assume commercial redistribution rights; document licensing questions and keep the
  provider replaceable.

### Phase 1 — Basic Features

- Multi-source vehicle aggregation through provider adapters.
- Excel/CSV vehicle and customer import with validation and error reporting.
- XML/JSON/FTP feed support through the same abstraction.
- Manual vehicle creation/editing and advanced vehicle search.
- Vehicle detail/media/source attribution.
- Customer CRM, requirements, tags, notes, assignment and activity timeline.
- WhatsApp Business, Facebook/Meta and Instagram messaging integrations where permitted.
- Webhook synchronization, unified conversations, tasks and follow-up scheduling.

### Phase 2 — AI Features

- AI vehicle recommendation against structured customer requirements.
- AI extraction of requirements from WhatsApp/messages.
- Conversation summarization, intent detection and follow-up suggestions.
- AI communication assistant; human approval before external sending.
- AI ad copy/content generation.
- AI suggestions for vehicles to source based on customer demand and inventory gaps.
- AI explanation of recommendation reasons.
- Provider-agnostic AI layer.

### Phase 3 — Reporting & Analytics

- Inventory, source, pricing, availability and synchronization reports.
- Customer, lead, conversion, follow-up and salesperson reports.
- Conversation/channel reports.
- Demand by make/model/year/budget/country.
- Unmet-demand and sourcing-opportunity reports.
- AI recommendation and business KPI dashboards.
- CSV/Excel report export initially.

## 4. Technology stack

| Concern | Choice |
| --- | --- |
| Frontend | JavaScript, React, Vite |
| Backend | ASP.NET Core 8 Web API, C# |
| Database | Microsoft SQL Server |
| ORM | Entity Framework Core 8 with migrations |
| Cache | Redis abstraction; safe in-memory fallback only for development |
| Background jobs | Hangfire or equivalent durable job system behind a replaceable abstraction |
| Validation | FluentValidation or equivalent |
| Logging | Structured Serilog or NLog; include tenant/user/correlation context |
| API docs | OpenAPI/Swagger |
| Testing | xUnit; Vitest/React Testing Library |
| Local environment | Docker Compose preferred for SQL Server/Redis/supporting services |
| Cloud | Cloud-agnostic enough to deploy later to a cloud server |

## 5. Architecture

- Use a modular monolith initially; keep module boundaries strong enough for future extraction.
- Separate API, Application, Domain, Infrastructure and Integrations concerns.
- Business logic must never call vendor SDKs directly.
- All external services use interfaces/adapters.
- Keep raw source data separate from canonical business data.
- Every tenant-owned operation is tenant-scoped from authenticated context.
- Webhook handlers and synchronization jobs must be idempotent and observable.
- Long-running imports/sync/AI/media tasks run asynchronously.

```
Backend:   Api / Application / Domain / Infrastructure / Integrations / Worker
Frontend:  React/Vite
Tests:     Unit / Integration / Frontend
Docs:      architecture / api / integrations / database
```

## 6. Vehicle Source Framework

```
IVehicleSourceProvider
IVehicleSourceSearchProvider
IVehicleSourceSyncProvider
IVehicleSourceDetailProvider
IVehicleSourceMediaProvider
IVehicleSourceAvailabilityProvider
```

**Initial adapters**

- `CarapisVehicleProvider` — POC only until commercial/legal approval.
- `DealerCsvProvider` / `DealerExcelProvider`.
- `DealerXmlProvider` / `DealerJsonProvider`.
- `ManualVehicleProvider`.

**Future adapters**

- BE FORWARD direct authorized provider.
- SBT direct authorized provider.
- TCV direct authorized provider.
- Goo-net/direct authorized provider.
- USS/auction provider through an authorized/licensed channel.

Never bypass authentication, CAPTCHAs, anti-bot controls or other protections. Do not make
scraping the default integration strategy.

## 7. Canonical vehicle model

- Make, model, variant/trim, model year, body type, engine/displacement, fuel, transmission,
  drivetrain.
- Mileage/unit, exterior/interior color.
- Price, currency and price type.
- Country/city/port where available.
- VIN/chassis only where legally and contractually available.
- Condition/auction/inspection metadata where available.
- Features/options and media.
- Canonical status: active, reserved, sold, unavailable, expired, archived.
- Source listings remain separate; one canonical vehicle may have multiple listings only when
  matching is sufficiently reliable.

## 8. Carapis POC acceptance criteria

- Credentials are environment/secrets configuration, never hard-coded.
- Filtered listings can be retrieved for permitted sources.
- Raw responses are stored for debugging/reprocessing.
- Source fields map to canonical fields.
- Pagination, timeouts, transient errors and HTTP 429 are handled.
- At least five realistic vehicle searches are tested.
- Source attribution is visible in the UI.
- Sync logs show counts, duration, errors and provider status.
- Carapis can be disabled without breaking the rest of the platform.
- A short POC report records technical results, cost/limits and unresolved licensing questions.

## 9. CRM and customer requirements

- Customer: name, phone, email, country, city, language, source, status, owner, tags, notes.
- Multiple active requirements per customer.
- Requirement fields: make/model/variant, year range, budget/currency, mileage, transmission,
  fuel, body, colors, destination and free-text request.
- Excel customer import with duplicate detection and row-level errors.
- Activity timeline, notes, tasks, due dates, priorities and reminders.

## 10. Messaging

- Use official/approved WhatsApp Business and Meta APIs.
- Webhook ingestion with provider event IDs and idempotency.
- Map external contacts/conversations/messages to internal entities.
- Persist message direction, type, text, media metadata and provider status where available.
- Unified inbox linked to customer profiles.
- Respect provider policies, opt-ins, templates and messaging windows.

## 11. AI architecture

```
IAIProvider
  extractCustomerRequirement()
  summarizeConversation()
  recommendVehicles()
  generateAdContent()
  suggestFollowUp()
  generateText()
```

- OpenAI may be the first implementation, but application logic depends only on `IAIProvider`.
- Use structured JSON/schema outputs.
- Hard-filter candidates first; do not send millions of vehicles directly to an LLM.
- Recommendations include score/reasons and traceability.
- Never invent availability, price, specifications or customer facts.
- Human approval is required before external AI-generated messaging in the initial release.

## 12. Recommendation pipeline

```
Conversation / Customer
 -> Requirement Extraction
 -> Structured Requirement
 -> Deterministic Hard Filters
 -> Candidate Vehicles
 -> Ranking
 -> AI Explanation
 -> Salesperson Review
 -> Recommendation
```

## 13. Reporting

- Inventory/source/status counts, new/expired/sold listings.
- Average price and price trends.
- Demand by make/model/year/budget/country.
- Unmet customer requirements.
- Lead funnel, salesperson activity and follow-up performance.
- Conversation volume by channel.
- Recommendation engagement/outcomes.
- Top sourced and slow-moving vehicles.
- Source freshness/reliability and synchronization performance.

## 14. Security

- Strict tenant isolation and least privilege.
- Secure password/token handling and token revocation strategy.
- Encrypt integration credentials at rest where feasible.
- Never log secrets, access tokens or webhook secrets.
- Validate file uploads, size/type limits.
- Verify webhook signatures where provided.
- Rate-limit authentication/public endpoints.
- Use parameterized SQL/EF Core.
- Audit critical actions.
- HTTPS outside local development.

## 15. Frontend pages

- Dashboard.
- Advanced vehicle search and saved searches.
- Vehicle detail/gallery/source information.
- Customers list/detail.
- Customer requirements.
- Unified inbox/conversation detail.
- Tasks/follow-ups.
- AI suggestions/recommendations.
- Source/integration administration.
- Users/roles/tenant administration.
- Reports/analytics.

## 16. Development rules for Claude Code

- Inspect the repository before coding.
- Read this master prompt and the SQL schema document first.
- Implement Phase 0, then Phase 0.5 only.
- Do not prematurely implement later phases.
- Write tests with each major module.
- Use migrations for all schema changes.
- Use mock providers for external integrations.
- Run build, tests and static checks before completion.
- Document setup, environment variables, migrations, provider configuration and troubleshooting.
- Do not commit secrets.

## 17. First vertical slice

After the foundation, implement exactly this first working slice: configuration → Carapis
provider adapter → raw response → canonical normalization → SQL persistence → search API →
simple React/Vite search screen → tests → POC evaluation report.

## 18. Explicit exclusions

- No unauthorized scraping or bypassing access controls.
- No assumption that Carapis permits SaaS redistribution.
- No hard-coded dependency on a single vehicle source.
- No autonomous customer messaging in the first AI release.
- No microservices unless scale demonstrates a need.
- No unlimited third-party inventory synchronization without filters, quotas and caching.
