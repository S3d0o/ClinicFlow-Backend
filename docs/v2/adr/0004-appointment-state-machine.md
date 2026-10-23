# ADR 0004: One appointment state machine, owned by the entity

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-09 |
| **Deciders** | Backend lead |

## Context

In V1, status rules are copied into each service method: three cancel flows (patient, doctor, admin), complete, notes and review each check statuses their own way (REVIEW §4.3, M14). Two enum values (`Pending`, `NoShow`) are never used, completion has no time check, and the admin cancel flow returns the wrong error for completed appointments.

V2 adds check-in, in-progress, reschedule, no-show, deposits and walk-ins. Without one owner for the rules, every new flow would add another copy.

## Decision

1. The appointment statuses are `Pending`, `Confirmed`, `CheckedIn`, `InProgress`, `Completed`, `Cancelled`, `NoShow`, `Rescheduled`, with the transitions in [05 §4.1](../05_DOMAIN_MODEL.md#41-appointment). (Since 2026-10-22 deposits are optional, so no V2 flow enters `Pending`; the value stays for V1 data and a possible confirmation step.)
2. Each transition is a **method on `Appointment`** (`CheckIn`, `Start`, `Complete`, `Cancel`, `MarkNoShow`, `RevertNoShow`, `RescheduleTo`) taking the actor and `now`, returning `Result`. The method checks the current status and time guards and records the timestamps and actor. *(Amended 2026-10-23, P0 PR 7: `Result` and `Error` moved from `Shared` into `Domain` (`Domain/ResultPattern`), and `AppointmentErrors` into `Domain/Errors`, since `Domain` can't reference `Shared`. A method takes only what its guards need: `now` for time guards, the branch's date for check-in and the no-show correction, and the actor only where it changes a rule (`Cancel` and `RescheduleTo`, typed `CancelledBy`: a patient must act 2 hours before the start). The times are recorded in new columns (`CheckedInAt`, `StartedAt`, `CompletedAt`, `NoShowAt`; migration `AddAppointmentStateTimes`); who acted is the audit trail's job (P0 PR 10). `Start` doesn't check that the doctor has no other visit in progress: that needs a query, so the queue service that calls it (P2) will. Check-in is allowed all day; a narrower window waits for a decision.)*
3. Services handle what the entity can't see: permissions and scope (via `IAccessGuard`), loading related data, slot updates, and saving. They never set `Status` directly; the setter is private.
4. An illegal transition returns `Appointment.InvalidStatusTransition` (409). *(Amended 2026-10-23, P0 PR 7: except where the API already had a code, which stays: `AlreadyCancelled`, `AlreadyCompleted`, `AlreadyNoShow` from those states, and `CannotCompleteCancelled`. The inherited complete route also completes a `Confirmed` appointment once its start has passed, since no route checks in or starts a visit yet; `InProgress → Completed` is the V2 path.)*
5. Each transition raises a domain event (`AppointmentCheckedIn`, …) for the outbox ([06 §5](../06_TECHNICAL_DESIGN.md#5-domain-events-and-the-outbox)). *(Events come with the outbox, P0 PR 9.)*
6. The same pattern applies to `Encounter`, `Invoice`, `Claim`, `Refund`, `LeaveRequest` and `CashSession`.

## Consequences

- Positive: the rules exist once and are unit-testable without a database: every legal and illegal transition gets a test.
- Positive: the three V1 cancel flows collapse into one `Cancel(actor, now, reason)` with role-specific guards.
- Negative: the entities gain behaviour. This moves the Domain project away from anaemic POCOs, which is intended, but EF configuration must map private setters.
- Statuses stay strings in the database, so the new values need no data migration, and V1 rows remain valid.
