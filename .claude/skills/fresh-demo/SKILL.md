---
name: fresh-demo
description: Drop the local development database and restart the API so the demo dataset is re-seeded with appointment dates relative to today.
disable-model-invocation: true
---

# Fresh demo database

Demo appointments keep the dates from the day they were first seeded. Only slots are topped up on each start. This command drops the local database so the next start re-seeds everything relative to today.

1. **Confirm the target.** Run:
   ```
   dotnet ef dbcontext info --project Persistance/Persistence.csproj --startup-project ClinicFlow
   ```
   Show me the **data source** and **database name**. Continue only if the data source is local: `localhost`, `127.0.0.1`, `.`, `(localdb)` or a local Docker container. If it looks remote or shared, stop and ask.
2. **Stop anything running.** If the API is running on port 5143 or 7129, stop it first.
3. **Drop the database.**
   ```
   dotnet ef database drop --force --project Persistance/Persistence.csproj --startup-project ClinicFlow
   ```
4. **Start the API.** Run `dotnet run --project ClinicFlow/ClinicFlow.csproj --launch-profile http` with Bash `run_in_background`. Development has `Seeding:DemoData=true` by default. Poll `http://localhost:5143/openapi/v1.json` until it returns 200; startup migrates and seeds, which can take a minute.
5. **Check the seed.** `POST http://localhost:5143/api/auth/login` with `{"email":"admin@clinicflow.com","password":"Admin@1234"}` must return 200.
6. **Report.** Say it's ready, and print the demo logins from the doc comment at the top of `Persistance/AppData/DataSeeder.cs`. Ask whether to leave the API running or stop it.
