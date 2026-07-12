// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Core.Services;

/// <summary>Abstraction over Id generation. Default impl produces time-ordered UUIDv7 values for index-friendly inserts.</summary>
public interface IIdGenerator
{
    /// <summary>Returns a new Id. Default impl returns a UUIDv7 via <see cref="Guid.CreateVersion7()"/>.</summary>
    Guid NewId();
}
