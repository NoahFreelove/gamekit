-- SPDX-License-Identifier: Apache-2.0
-- Copyright (c) 2026 GameKit contributors
--
-- docker/postgres/init/01-roles.sql
--
-- Three-role Postgres ops model per RESEARCH.md Pattern 3.
-- Executed by the postgres official image via /docker-entrypoint-initdb.d on first run.
-- Idempotent: DO blocks guard role creation; IF NOT EXISTS guards schema + db.

-- ====================================================================
-- Roles (idempotent via DO blocks — CREATE ROLE has no IF NOT EXISTS)
-- ====================================================================
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'gamekit_owner') THEN
        CREATE ROLE gamekit_owner LOGIN PASSWORD 'gamekit_owner_dev';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'gamekit_app') THEN
        CREATE ROLE gamekit_app LOGIN PASSWORD 'gamekit_app_dev';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'gamekit_reader') THEN
        CREATE ROLE gamekit_reader LOGIN PASSWORD 'gamekit_reader_dev';
    END IF;
END
$$;

-- ====================================================================
-- Database (owned by gamekit_owner)
-- ====================================================================
SELECT 'CREATE DATABASE gamekit OWNER gamekit_owner'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'gamekit')
\gexec

-- Connect to gamekit for subsequent grants + schema + default privileges
\c gamekit

-- ====================================================================
-- Schema (owned by gamekit_owner)
-- ====================================================================
CREATE SCHEMA IF NOT EXISTS gamekit AUTHORIZATION gamekit_owner;

-- ====================================================================
-- Schema usage grants
-- ====================================================================
GRANT USAGE ON SCHEMA gamekit TO gamekit_app, gamekit_reader;

-- ====================================================================
-- Default privileges — applied to FUTURE objects created BY gamekit_owner
-- in schema gamekit. Guarantees migrations authored by Core/Auth/etc. land
-- with correct grants without per-migration GRANT statements.
-- ====================================================================
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gamekit_app;

ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT USAGE, SELECT ON SEQUENCES TO gamekit_app;

ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT ON TABLES TO gamekit_reader;

ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT EXECUTE ON FUNCTIONS TO gamekit_app;

-- ====================================================================
-- Public schema hardening — revoke the default PUBLIC CREATE on public
-- (prevents the app role from smuggling objects into the public schema)
-- ====================================================================
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO gamekit_app, gamekit_reader;
