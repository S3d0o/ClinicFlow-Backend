-- Run by the db-init service in docker-compose.yml, as sa, before the migrate and backend services start
-- (REVIEW.md NM6). Creates the V2 database and two logins, so nothing but db and db-init connects as sa:
--   * the deploy login (APP_DEPLOY_LOGIN), db_owner of this database only, used by the migrate service to apply
--     the migration bundle;
--   * the app login (APP_LOGIN), used by the backend: a member of the clinicflow_app role, which reads and writes
--     data, changes no schema, and can't update or delete audit entries (FR-AUD-04). The role's permissions come
--     from the AddAppDatabaseRole migration, so they exist wherever the schema does.
-- Idempotent: safe on every `docker compose up`, and on a volume from before P0 PR 11, where the app login was
-- db_owner and ran the migrations itself (it is moved out of db_owner here).
-- sqlcmd reads APP_DATABASE, APP_LOGIN, APP_PASSWORD, APP_DEPLOY_LOGIN and APP_DEPLOY_PASSWORD from the
-- environment. The passwords must not contain a single quote.
--
-- V2 has its own database (docs/v2/adr/0007-v2-separate-product-line.md); APP_DATABASE is ClinicFlowV2 and the
-- backend refuses any other name (Database:ExpectedName).

IF DB_ID(N'$(APP_DATABASE)') IS NULL
    CREATE DATABASE [$(APP_DATABASE)];
GO

-- Logins are created, or kept in step with the passwords in .env when they are rotated
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(APP_LOGIN)')
    CREATE LOGIN [$(APP_LOGIN)] WITH PASSWORD = N'$(APP_PASSWORD)', DEFAULT_DATABASE = [$(APP_DATABASE)], CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [$(APP_LOGIN)] WITH PASSWORD = N'$(APP_PASSWORD)';
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(APP_DEPLOY_LOGIN)')
    CREATE LOGIN [$(APP_DEPLOY_LOGIN)] WITH PASSWORD = N'$(APP_DEPLOY_PASSWORD)', DEFAULT_DATABASE = [$(APP_DATABASE)], CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [$(APP_DEPLOY_LOGIN)] WITH PASSWORD = N'$(APP_DEPLOY_PASSWORD)';
GO

USE [$(APP_DATABASE)];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_DEPLOY_LOGIN)')
    CREATE USER [$(APP_DEPLOY_LOGIN)] FOR LOGIN [$(APP_DEPLOY_LOGIN)];
GO

IF IS_ROLEMEMBER(N'db_owner', N'$(APP_DEPLOY_LOGIN)') = 0
    ALTER ROLE db_owner ADD MEMBER [$(APP_DEPLOY_LOGIN)];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_LOGIN)')
    CREATE USER [$(APP_LOGIN)] FOR LOGIN [$(APP_LOGIN)];
GO

-- Created here too, so a new database can take the member before the first migration gives the role its rights
IF DATABASE_PRINCIPAL_ID(N'clinicflow_app') IS NULL
    CREATE ROLE [clinicflow_app];
GO

IF IS_ROLEMEMBER(N'clinicflow_app', N'$(APP_LOGIN)') = 0
    ALTER ROLE [clinicflow_app] ADD MEMBER [$(APP_LOGIN)];
GO

-- db_owner would bypass the role's DENY on audit entries
IF IS_ROLEMEMBER(N'db_owner', N'$(APP_LOGIN)') = 1
    ALTER ROLE db_owner DROP MEMBER [$(APP_LOGIN)];
GO
