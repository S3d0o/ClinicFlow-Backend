---
name: smoke
description: Run the API locally against the demo data and exercise the main flows as admin, doctor and patient over real HTTP (with real JWT auth), comparing each response with docs/API_CONTRACT.md.
argument-hint: "[flow to focus on, e.g. booking, reviews, admin; default: all]"
disable-model-invocation: true
---

# Smoke test $ARGUMENTS

Integration tests use `TestAuthHandler`. This command exercises the **real** JWT login, refresh and role claims against the demo data.

## Setup

1. **Use or start the API.** If `http://localhost:5143` already answers, use it. Otherwise start `dotnet run --project ClinicFlow/ClinicFlow.csproj --launch-profile http` with Bash `run_in_background`, and poll `/openapi/v1.json` until it returns 200.
2. **Check the demo data.** Demo data must exist, which is the Development default. Logins are in the doc comment at the top of `Persistance/AppData/DataSeeder.cs`. Use:
   - `admin@clinicflow.com / Admin@1234`
   - `ahmed.elsayed@clinicflow.com / Doctor@1234`
   - `mohamed.ali@gmail.com / Patient@1234`
3. **Get request shapes from the contract.** Take request bodies, routes and expected statuses from §5 of `docs/API_CONTRACT.md`. Don't guess them.

Use `curl -s -w "\n%{http_code}"` and keep the tokens in shell variables. Never print full tokens; print the first 12 characters at most.

## Flows (run all, or only the one named in $ARGUMENTS)

1. **Auth:**
   - login as each role, then `refresh-token`
   - reusing the old refresh token must fail with `Auth.InvalidRefreshToken`
   - a wrong password returns 401 `Auth.InvalidCredentials`
   - finish with `logout`
2. **Discovery (anonymous):**
   - list specialties and doctors
   - get one doctor and their slots for the next working day (Sun–Thu)
3. **Booking (patient):**
   - book an available slot (expect 201)
   - book the same slot again (409)
   - list `GET /api/appointments/my`
   - get the appointment by id
   - get the same id as a different patient (`sara.ibrahim@gmail.com`), which must be 403
4. **Doctor:**
   - `GET /api/appointments/doctor/my`
   - add notes on the new appointment
   - read own schedule
5. **Cancel (patient):** cancel the appointment made in step 3 (expect 204); cancel it again (409).
6. **Reviews:** read reviews for a doctor. Submitting one needs a completed appointment; use a seeded completed one if the contract allows it.
7. **Notifications:** list for the patient, then mark one as read.
8. **Admin:**
   - stats overview, pending doctors
   - the same calls with the patient token must return 403

Leave the data as you found it where possible. For example, cancel what you booked.

## Report

A table: `step | request | expected (contract) | actual | ✅/❌`. Under it, list every mismatch with the response body. For each one, say whether it's already tracked in `docs/REVIEW.md` or §9 of the contract. §11 says an untracked difference is a backend bug.

Stop the API if you started it.
