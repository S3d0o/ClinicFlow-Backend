---
name: regen-openapi
description: Regenerate docs/openapi.json from the running API and summarize what changed. Use after any endpoint or DTO change.
disable-model-invocation: true
---

# Regenerate docs/openapi.json

Follows "Regenerating openapi.json" in `docs/SETUP_AND_DEPLOYMENT.md`.

1. **Check the configuration.** The API needs `ConnectionStrings:DefaultConnection` and `JwtSettings:Secret` (user secrets). It also migrates and seeds on startup, so SQL Server must be reachable.
2. **Check port 5143.** If something already listens there, check whether it's this API: `curl -s -o /dev/null -w "%{http_code}" http://localhost:5143/openapi/v1.json`. If it is, ask whether it's running the current code before reusing it.
3. **Start the API.** Run `dotnet run --project ClinicFlow/ClinicFlow.csproj --launch-profile http` with Bash `run_in_background`. The http profile sets `ASPNETCORE_ENVIRONMENT=Development`, which is required: the OpenAPI document is only mapped in Development.
4. **Wait for it.** Poll `http://localhost:5143/openapi/v1.json` until it returns 200 (allow about 90 s, because startup retries the DB for about a minute). If it never comes up, show the last lines of the background output and stop.
5. **Save the document.** `curl -s http://localhost:5143/openapi/v1.json -o docs/openapi.json`
6. **Fix the server URL.** Make sure `servers[0].url` is `http://localhost:5143/`. Fix it with Edit if it isn't.
7. **Stop the API.** Stop the background task, and make sure no `ClinicFlow` process is left running.
8. **Review the diff.** Run `git diff --stat docs/openapi.json` and `git diff docs/openapi.json`, then summarize:
   - paths added or removed
   - operations whose parameters, request body, responses or status codes changed
   - schemas added, removed or with changed properties

If something changed that `docs/API_CONTRACT.md` doesn't reflect, list it. Contract and OpenAPI must ship together; the `/contract-change` command covers the full process.
