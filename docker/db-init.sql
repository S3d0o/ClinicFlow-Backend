-- Run by the db-init service in docker-compose.yml, as sa, before the backend starts (REVIEW.md NM6).
-- Creates the database and a dedicated login for the app, so the app no longer connects as sa.
-- Idempotent: safe on every `docker compose up`, and on a volume whose database the app already created as sa.
-- sqlcmd reads APP_LOGIN and APP_PASSWORD from the environment. The password must not contain a single quote.
--
-- The login gets db_owner on the ClinicFlow database only (no server-level rights): the app applies EF
-- migrations at startup, which needs DDL. Once migrations move to a deploy step (REVIEW.md §4.9), this
-- can drop to db_datareader + db_datawriter.

IF DB_ID(N'ClinicFlow') IS NULL
    CREATE DATABASE [ClinicFlow];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(APP_LOGIN)')
    CREATE LOGIN [$(APP_LOGIN)] WITH PASSWORD = N'$(APP_PASSWORD)', DEFAULT_DATABASE = [ClinicFlow], CHECK_POLICY = ON;
ELSE
    -- Keeps the login in step with APP_DB_PASSWORD when it is rotated in .env
    ALTER LOGIN [$(APP_LOGIN)] WITH PASSWORD = N'$(APP_PASSWORD)';
GO

USE [ClinicFlow];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_LOGIN)')
    CREATE USER [$(APP_LOGIN)] FOR LOGIN [$(APP_LOGIN)];
GO

IF IS_ROLEMEMBER(N'db_owner', N'$(APP_LOGIN)') = 0
    ALTER ROLE db_owner ADD MEMBER [$(APP_LOGIN)];
GO
