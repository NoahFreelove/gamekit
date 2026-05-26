-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (c) 2026 GameKit contributors
--
-- docker/postgres/init/02-extensions.sql
--
-- Postgres extensions required by GameKit entities and tests.
--   pgcrypto: gen_random_uuid() used by integration tests to seed Player rows
--             (EF-managed insertions use UuidV7IdGenerator from IIdGenerator instead).
--
-- Must run AFTER 01-roles.sql (which creates the gamekit database).

\c gamekit

CREATE EXTENSION IF NOT EXISTS pgcrypto;
