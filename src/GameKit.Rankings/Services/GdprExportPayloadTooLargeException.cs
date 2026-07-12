// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Rankings.Services;

/// <summary>
/// Thrown by <see cref="IGdprExportService.ExportAsync"/> when the serialized export payload
/// exceeds the configured <see cref="GameKitRankingsGdprExportOptions.MaxBytes"/> cap (D-18).
/// The endpoint handler maps this exception to HTTP 413 Payload Too Large.
/// </summary>
public sealed class GdprExportPayloadTooLargeException : Exception
{
    /// <summary>Actual byte size of the serialized payload.</summary>
    public long ActualBytes { get; }

    /// <summary>Configured cap in bytes.</summary>
    public long MaxBytes { get; }

    /// <summary>Constructs the exception with the actual and max byte sizes.</summary>
    /// <param name="actualBytes">Actual serialized size that triggered the cap.</param>
    /// <param name="maxBytes">Configured maximum allowed size.</param>
    public GdprExportPayloadTooLargeException(long actualBytes, long maxBytes)
        : base(
            $"GDPR export payload exceeds the configured cap. " +
            $"Actual: {actualBytes:N0} bytes, Max: {maxBytes:N0} bytes. " +
            $"Consider raising GameKitRankingsOptions.GdprExport.MaxBytes or requesting a streaming export (v2).")
    {
        ActualBytes = actualBytes;
        MaxBytes = maxBytes;
    }
}
