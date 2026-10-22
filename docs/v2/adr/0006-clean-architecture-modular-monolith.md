# ADR 0006: Keep Clean Architecture as a modular monolith; enforce SOLID with architecture tests

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-22 |
| **Deciders** | Developer (product owner) |

## Context

V1 is a Clean Architecture solution with one project per layer: `Domain`, `Shared`, `Persistence`, `Services.Abstraction`, `Services`, `Presentation` and the `ClinicFlow` host. It uses the Result pattern, `IUnitOfWork` with lazy repositories, ASP.NET Identity and EF Core on SQL Server. It is one deployable app.

V2 adds about fifteen modules (recruitment, accounts, scheduling, front desk, billing, EMR, insurance, reporting, chatbot) and several external providers (SMS, Paymob, file storage, PDF, an LLM). It is built by one developer working with Claude. The question was whether the V1 architecture still fits, and how to keep the code SOLID and clean as it grows.

REVIEW.md §4.6 lists the places where V1 bends its own layering:
- `Services` references `Persistence`.
- `Services` holds MailKit and JwtBearer.
- `Domain` references Identity's EF Core package.
- An interface sits in the wrong namespace.

Nothing stops new shortcuts: the project boundaries are documented, not enforced.

## Decision

1. **Keep the architecture.** V2 stays one deployable app with one project per layer, as a **modular monolith**: features are organised as module folders inside each layer (`Services/Implementations/Billing/…`, `Presentation/Controllers/Billing/…`, `Shared/DTOs/Billing/…`).
2. **Dependencies point inward.**
   - `Presentation` → `Services.Abstraction`
   - `Services` → `Domain` + `Services.Abstraction`
   - `Persistence` → `Domain`, plus `Services.Abstraction` and `Shared` where it implements query interfaces (decision 8)
   - `Infrastructure` (new) → `Services.Abstraction`
   - The host references everything and wires it together.
3. **Fix the leaks in P0.**
   - Drop `Services → Persistence` behind an `ITransaction` abstraction in `Domain`.
   - Move MailKit into `Infrastructure` and JWT setup into the host.
   - Put every external provider behind a small interface in `Services.Abstraction`, implemented in `Infrastructure`.
4. **Accepted trade-off.** `Domain` keeps its reference to Identity's EF package, because `ApplicationUser : IdentityUser` is pragmatic and costly to undo.
5. **Enforce it.** Architecture tests (e.g. NetArchTest, in `ClinicFlow.Tests`) fail the build on a forbidden reference.
6. **SOLID and clean-code rules.** They are written down per principle in [06 §2.1](../06_TECHNICAL_DESIGN.md#21-design-principles) and checked in review:
   - small use-case services
   - status rules inside entities
   - new channels and gateways added as new implementations
   - one contract test per provider interface
   - small interfaces
   - the CLAUDE.md conventions
7. **Mapping.** Prefer explicit mapping to AutoMapper. This is decided in P0, together with AutoMapper's licence question.
8. **CQRS-lite, without a framework.** Inside a module, writes and reads are separate classes:
   - **Commands:** command services (e.g. `AppointmentBookingService`) load tracked entities through `IUnitOfWork`, call the entity's methods (the state machine), save, and raise events.
   - **Queries:** query services (e.g. `IAppointmentQueries`) never change data. They use `AsNoTracking()` and project straight to response DTOs with `Select`, including paging and the dashboard series. The interfaces live in `Services.Abstraction` and are implemented in `Persistence`, so the dependency still points inward.
   - **One database.** There is no separate read store and no event sourcing.
   - **No MediatR.** It adds a dispatch layer, and like AutoMapper it moved to a commercial licence in 2025. Plain interfaces and DI do the same job.
   - **Where it pays off.** Modules whose reads and writes differ a lot: appointments, the queue, billing, recruitment, the dashboard and reports. A small CRUD module (rooms, specialties) may keep one service until it grows.
9. **Patterns are adopted on merit, for new code and for enhancements.** CQRS-lite, strategy, specification, domain events and the like are used when they make the code clearer and the cost is small. That includes reworking existing V1 code while enhancing it. Each adoption that changes how a module is structured is recorded in the decisions log ([10 §6](../10_ROADMAP.md#6-decisions-log)) or a new ADR. A pattern that only adds ceremony is not adopted.
10. **Not adopted:** microservices, separate read databases, event sourcing. They add infrastructure one developer would have to run, and nothing in V2 needs them (one organization, one database, a few hundred concurrent users). This is revisited if the scale changes (V3).

## Consequences

- Positive: no rewrite. V1 code, tests and conventions carry over, and each module is added the same way (CLAUDE.md "new feature checklist").
- Positive: the compiler and the architecture tests guard the layers, so shortcuts can't creep back in. Providers can be swapped (a second SMS provider, another gateway) without touching services.
- Positive: module folders keep a large codebase navigable, and they leave a path to split a module into its own project later if one grows too big.
- Positive: with CQRS-lite, read code is plain projections that are easy to optimise, and write code is small and rule-focused. Neither drags the other's concerns along.
- Negative: more, smaller classes and interfaces than a "fat service" style, plus some one-time P0 work to remove the existing leaks.
- Negative: explicit mapping means writing mapping code by hand. In exchange, it is searchable and compile-checked.
- CLAUDE.md's solution-layout table and new-feature checklist are updated when the P0 changes land.
