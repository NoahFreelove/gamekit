// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Rankings.Http.Contracts;

namespace GameKit.Rankings.Services;

/// <summary>
/// Produces a GDPR data-portability export bundle for a player (RANK-13 / D-15 / D-16 / D-17 / D-18).
/// Runs inside a <c>REPEATABLE READ</c> read-only Postgres transaction so all seven table reads
/// share a single point-in-time snapshot without blocking writers.
/// </summary>
public interface IGdprExportService
{
    /// <summary>
    /// Builds a point-in-time GDPR export bundle for the specified player.
    /// </summary>
    /// <param name="playerId">The player whose data should be exported.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The export bundle, or <see langword="null"/> when the player does not exist
    /// (caller maps to HTTP 404).
    /// </returns>
    /// <exception cref="GdprExportPayloadTooLargeException">
    /// Thrown when the serialized response exceeds
    /// <see cref="GameKitRankingsGdprExportOptions.MaxBytes"/> (caller maps to HTTP 413).
    /// </exception>
    Task<GdprExportResponse?> ExportAsync(Guid playerId, CancellationToken ct);

    /// <summary>
    /// Builds a point-in-time GDPR export bundle AND returns the size of the serialized payload
    /// in bytes (WR-07). Callers that need to record <c>byte_size</c> (e.g. the admin audit log)
    /// should use this overload so the bundle is not serialized twice — once for cap enforcement
    /// inside <see cref="ExportAsync"/> and again by the caller.
    /// </summary>
    /// <param name="playerId">The player whose data should be exported.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of the export bundle and the byte-length of its JSON serialization. Returns
    /// <c>(null, 0)</c> when the player does not exist.
    /// </returns>
    /// <exception cref="GdprExportPayloadTooLargeException">
    /// Thrown when the serialized response exceeds
    /// <see cref="GameKitRankingsGdprExportOptions.MaxBytes"/>.
    /// </exception>
    Task<(GdprExportResponse? Response, long ByteSize)> ExportWithSizeAsync(Guid playerId, CancellationToken ct);
}
