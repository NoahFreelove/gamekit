// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Core.Migrations
{
    /// <summary>
    /// Plan 10-01 migration: originally intended to add FK_admin_audit_log_players_ActorId so that
    /// tombstoning the source player during a merge would cascade-null the audit row's actor_id.
    /// This FK was reverted (Plan 10-04 fix): <c>admin_audit_log.actor_id</c> stores BOTH player IDs
    /// (merge service) AND admin user IDs (admin login, ban, etc.). Admin users are not in the
    /// <c>players</c> table, so a strict FK on actor_id → players.id rejects every admin-initiated
    /// audit entry (23503 FK violation). The actor_id column remains a bare nullable UUID — callers
    /// that need actor attribution must ensure correctness at the application layer.
    /// </summary>
    public partial class AddAuditActorIdFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: the FK was not added (see class-level XML doc for rationale).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DR-04: Destructive rollback is not supported. Restore from backup — see docs/runbooks/postgres-backup-restore.md.
            throw new NotSupportedException(
                "Migration rollback via Down() is disabled in GameKit. Restore from a Postgres backup instead. " +
                "See docs/runbooks/postgres-backup-restore.md.");
        }
    }
}
