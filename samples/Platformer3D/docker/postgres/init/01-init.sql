-- SPDX-License-Identifier: Apache-2.0
-- Copyright (c) 2026 GameKit contributors
--
-- 01-init.sql — Postgres bootstrap for the Platformer3D demo.
--
-- This script runs once on the first container start (Postgres executes
-- /docker-entrypoint-initdb.d/*.sql in filename order against the default
-- superuser). It creates:
--   - gamekit_owner: the migration user (has DDL privileges)
--   - gamekit_app:   the runtime app user (DML only)
--   - gamekit:       the application database owned by gamekit_owner
--
-- DEMO ONLY: passwords here are demo-grade and match the docker-compose.yml
-- environment block. Do NOT reuse these credentials in production.
--
-- After this script runs, the app calls EF Core's AutoMigrate on startup
-- (using the gamekit_owner connection string), which creates all GameKit
-- schema tables in the `gamekit` schema. Subsequent restarts skip migration
-- if the DB is already current.
--
-- App-role grants (added to fix 42501 permission denied on gamekit schema):
--   gamekit_app receives USAGE on the gamekit schema, DML on all current
--   tables/sequences, and ALTER DEFAULT PRIVILEGES ensures every future
--   table/sequence that gamekit_owner creates is automatically accessible
--   to gamekit_app — solving the EF Core chicken-and-egg where tables are
--   created after this script runs.

-- Owner / migration user (has CREATE, ALTER, DROP for GameKit schema objects)
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'gamekit_owner') THEN
    CREATE ROLE gamekit_owner WITH LOGIN PASSWORD 'demo_owner_pw';
  END IF;
END
$$;

-- App / runtime user (SELECT, INSERT, UPDATE, DELETE — no DDL)
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'gamekit_app') THEN
    CREATE ROLE gamekit_app WITH LOGIN PASSWORD 'demo_app_pw';
  END IF;
END
$$;

-- Application database — owned by gamekit_owner so it can run migrations
CREATE DATABASE gamekit
  WITH OWNER = gamekit_owner
       ENCODING = 'UTF8'
       LC_COLLATE = 'en_US.utf8'
       LC_CTYPE = 'en_US.utf8'
       TEMPLATE = template0;

-- Grant connection privilege to the app user
GRANT CONNECT ON DATABASE gamekit TO gamekit_app;

\connect gamekit

-- Pre-create the GameKit schema owned by the migration role so the default
-- privileges below apply to every table the EF migrations create in it.
-- EF's EnsureSchema uses CREATE SCHEMA IF NOT EXISTS, so this does not conflict.
CREATE SCHEMA IF NOT EXISTS gamekit AUTHORIZATION gamekit_owner;

-- Runtime app role: enter the schema and read/write its objects.
GRANT USAGE ON SCHEMA gamekit TO gamekit_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA gamekit TO gamekit_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA gamekit TO gamekit_app;

-- Future migration-created objects (owned by gamekit_owner) auto-grant DML to the app role.
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gamekit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
  GRANT USAGE, SELECT ON SEQUENCES TO gamekit_app;
