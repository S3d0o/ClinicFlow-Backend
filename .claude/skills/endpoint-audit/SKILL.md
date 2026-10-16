---
name: endpoint-audit
description: Read-only audit of every controller action against the ClinicFlow endpoint rules (explicit authorization, summary, ProducesResponseType, CancellationToken, HandleResult) and against docs/API_CONTRACT.md and docs/openapi.json. Use before a PR that touches controllers or DTOs.
argument-hint: "[controller name to limit the audit, optional]"
---

# Endpoint audit $ARGUMENTS

Read-only: change nothing. Audit every action in `Presentation/Controllers/*Controller.cs`, or only the named controller if one is given.

## Per action, check

1. **Authorization.** There is no fallback policy, so an action is anonymous unless it, or its controller, has `[Authorize]` / `[Authorize(Roles = "...")]`. List every anonymous action, and cross-check each one against the "(anonymous)" endpoints in §5 of `docs/API_CONTRACT.md`. An anonymous action that the contract doesn't list as public is **critical**. NM5 in REVIEW.md tracks this.
2. **Role versus contract.** Do the roles match the contract's §4 / §5 entry?
3. **Ownership.** For actions on a specific record (`{id}`), confirm the service checks ownership, not just the role. Follow the call into the service.
4. **Summary.** There must be either `[EndpointSummary]`/`[EndpointDescription]` or an XML `/// <summary>` (+ `<remarks>`). CLAUDE.md asks for the attributes, but most controllers use XML comments; report which one each action uses, and flag actions with neither.
5. **`[ProducesResponseType]`.** There must be one per status the action can return:
   - `ProblemDetails` for errors, `ValidationProblemDetails` for 400
   - every `ErrorType` the service method can return, mapped by `ApiController.GetStatusCode`
   - the success code that `HandleResult` actually produces: 204 for `Result`, 200 for `Result<T>`, 201 with a location or status code
6. **Signature.** `CancellationToken ct` must be the last parameter and be passed to the service.
7. **Body.** It must be `return HandleResult(await service.XAsync(..., ct));` or an equivalent, with no logic in the controller.

## Against the docs

- Every route in the controllers appears in §4 of `docs/API_CONTRACT.md`, and the reverse.
- Every route appears in `docs/openapi.json` (`paths`). If the attributes differ from what openapi.json shows, `openapi.json` is stale.
- Error codes returned by services (`Shared/Errors/*Errors.cs`, the ones that are reachable) are in the §3.3 catalog with the right status.

## Output

- A table: `Controller.Action | route | auth | issues`, with only the actions that have issues.
- Below it, the issues grouped by severity: **Critical** (unintended anonymous access, missing ownership check), **Contract drift**, **Convention**.
- Give each issue `file:line`, and say whether it's already tracked in `docs/REVIEW.md`, with its ID.

Finish with counts. Offer to fix the convention issues; contract drift goes through `/contract-change`.
