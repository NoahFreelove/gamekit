// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Text.Json;

namespace GameKit.Core.Entities;

/// <summary>
/// Immutable audit record for an admin action (ban, unban, manual rank adjustment, GDPR delete, etc.).
/// Written by every GameKit package that exposes a privileged mutation.
/// </summary>
public sealed class AdminAuditLog
{
    /// <summary>Audit row id — UUIDv7 (chronological ordering inherent).</summary>
    public Guid Id { get; set; }

    /// <summary>Admin player id performing the action. Null for system-originated actions (e.g. scheduled jobs).</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Action verb — stable string identifier (e.g. "player.ban", "player.unban", "rank.adjust", "gdpr.delete").</summary>
    public required string Action { get; set; }

    /// <summary>Target entity type — stable string identifier (e.g. "player", "session", "ladder").</summary>
    public required string TargetType { get; set; }

    /// <summary>Target entity id, if applicable. Null for actions that operate on a non-id target (e.g. global config flip).</summary>
    public Guid? TargetId { get; set; }

    /// <summary>JSONB snapshot of the target entity state before the action. Null when not applicable.</summary>
    public JsonDocument? Before { get; set; }

    /// <summary>JSONB snapshot of the target entity state after the action.</summary>
    public JsonDocument? After { get; set; }

    /// <summary>Free-text reason — required by business logic for actions like ban (enforced at the service layer, not the schema).</summary>
    public string? Reason { get; set; }

    /// <summary>UTC timestamp at which the action was recorded.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
