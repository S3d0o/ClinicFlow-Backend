# ADR 0003: Each branch has a time zone; slots store local time plus UTC

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-09 |
| **Deciders** | Backend lead |

## Context

V1 stores slot dates and times as local wall-clock values (`DateOnly` + `TimeOnly`) but compares them with `DateTime.UtcNow`. For a clinic in Egypt (UTC+2, or UTC+3 under daylight saving, which Egypt observes again since 2023), that means past slots stay bookable for 2–3 hours, the 2-hour cancellation window is wrong, and reminders are shifted (REVIEW M2). "Now" is also read directly in about 28 places (REVIEW §4.1).

V2 adds branches, and in V3 organizations may be in other countries. Staff and patients must see times as the branch's wall clock, while business rules need an unambiguous instant.

## Decision

1. `Branch.TimeZoneId` holds an IANA zone id (default `Africa/Cairo`).
2. Slots keep `Date` + `StartTime` + `EndTime` in **branch-local** time (what people see and what schedules generate), and also store `StartsAtUtc` and `EndsAtUtc`, computed when the slot is generated.
3. Every rule that compares with "now" (booking in the past, cancellation and reschedule windows, check-in window, no-show grace, reminders, deposit holds) uses the UTC columns and `IClock.UtcNow`.
4. "Today" and other calendar questions use `IClock.Today(branchId)`.
5. `IClock` extends V1's `IDateTimeProvider` and is the **only** source of time. A test fails the build if `DateTime.UtcNow` or `DateTime.Now` appears outside the clock implementation.
6. Daylight saving: local times that don't exist (the skipped hour) are not generated; ambiguous times (the repeated hour) map to the first occurrence. If zone rules change, a job recomputes the UTC columns of future slots.
7. API: slot responses return the local `date`/`startTime` **and** `startsAtUtc`; all timestamps are UTC with `Z`.

## Consequences

- Positive: fixes M2 and makes time testable everywhere (the clock is stubbed in tests, as V1 already does for two rules).
- Positive: branches in different zones (V3) work without changes.
- Negative: two representations of the same instant on slots; generation is the only writer, which keeps them consistent.
- Negative: the zone database must be present in the container image (`tzdata` on Linux images, which the .NET images include).
