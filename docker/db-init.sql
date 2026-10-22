-- Run by the db-init service in docker-compose.yml, as sa, before the backend starts (REVIEW.md NM6).
-- Creates the V2 database and a dedicated login for the app, so the app doesn't connect as sa.
-- Idempotent: safe on every `docker compose up`, and on a volume whose database the app already created as sa.
-- sqlcmd reads APP_DATABASE, APP_LOGIN and APP_PASSWORD from the environment. The password must not contain a
-- single quote.
--
-- V2 has its own database (docs/v2/adr/0007-v2-separate-product-line.md); APP_DATABASE is ClinicFlowV2 and the
-- backend refuses any other name (Database:ExpectedName).
--
-- The login gets db_owner on that database only (no server-level rights): the app applies EF migrations at
-- startup, which needs DDL. Once migrations move to a deploy step (docs/v2/plans/P0.md, PR 11), this drops to
-- db_datareader + db_datawriter.

IF DB_ID(N'$(APP_DATABASE)') IS NULL
    CREATE DATABASE [$(APP_DATABASE)];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(APP_LOGIN)')
    CREATE LOGIN [$(APP_LOGIN)] WITH PASSWORD = N'$(APP_PASSWORD)', DEFAULT_DATABASE = [$(APP_DATABASE)], CHECK_POLICY = ON;
ELSE
    -- Keeps the login in step with APP_DB_PASSWORD when it is rotated in .env
    ALTER LOGIN [$(APP_LOGIN)] WITH PASSWORD = N'$(APP_PASSWORD)';
GO

USE [$(APP_DATABASE)];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_LOGIN)')
    CREATE USER [$(APP_LOGIN)] FOR LOGIN [$(APP_LOGIN)];
GO

IF IS_ROLEMEMBER(N'db_owner', N'$(APP_LOGIN)') = 0
    ALTER ROLE db_owner ADD MEMBER [$(APP_LOGIN)];
GO
