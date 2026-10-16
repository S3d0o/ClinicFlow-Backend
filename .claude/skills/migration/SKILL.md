---
name: migration
description: Add an EF Core migration with the right project flags, then review the generated migration and SQL for data loss, locking and missing defaults before it is applied.
argument-hint: <PascalCaseMigrationName>
disable-model-invocation: true
---

# Add migration: $ARGUMENTS

## 1. Pre-checks

- **Name.** The name must be PascalCase with no spaces. If `$ARGUMENTS` isn't, propose a corrected name and use it.
- **Tool.** `dotnet ef --version` must work. If it doesn't, tell me to run `dotnet tool install --global dotnet-ef` and stop.
- **Build.** `dotnet build ClinicFlow.slnx` must pass. `dotnet ef` builds and starts the host, so the configuration in CLAUDE.md (connection string, JWT secret) must be present.
- **Last migration.** Find the newest migration in `Persistance/AppData/Migrations/` and note its id. You need it for the SQL script in step 3.

## 2. Add it

```
dotnet ef migrations add $ARGUMENTS --project Persistance/Persistence.csproj --startup-project ClinicFlow --output-dir AppData/Migrations
```

If it reports "no changes", say so, remove the empty migration with `dotnet ef migrations remove --project Persistance/Persistence.csproj --startup-project ClinicFlow`, and stop.

## 3. Review it

Read the generated `*_$ARGUMENTS.cs` and the `ClinicDbContextModelSnapshot.cs` diff. Generate the SQL:

```
dotnet ef migrations script <previous-migration-id> $ARGUMENTS --project Persistance/Persistence.csproj --startup-project ClinicFlow
```

Flag:
- **Data loss:**
  - `DropColumn` / `DropTable`
  - a column type narrowed (e.g. `nvarchar(max)` → `nvarchar(100)`)
  - a rename that EF generated as drop + add; suggest `RenameColumn`
- **Existing rows:**
  - a new non-nullable column without `defaultValue`
  - a new unique index on data that may already have duplicates
  - a new FK on data that may have orphans
- **Behaviour:**
  - cascade delete changes; compare with the `NoAction` choices in REVIEW.md H4
  - enums stored as strings versus ints; several are strings, check the `*Config`
- **Seeding:** whether `Persistance/AppData/DataSeeder.cs` needs a matching update, so demo data and the bootstrap admin still seed.

## 4. Report

Summarize what the migration does in plain words, then the flags above. Remind me that `dotnet run` applies it automatically on startup (`DataSeeder.SeedAsync` → `MigrateAsync`), so it should be reviewed before the next run. Don't apply it with `database update` unless I ask.
