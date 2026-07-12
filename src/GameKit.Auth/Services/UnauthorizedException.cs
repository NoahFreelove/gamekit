// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Auth.Services;

/// <summary>Signalled by Auth services when a caller is not authorized; middleware maps to 401.</summary>
public sealed class UnauthorizedException : Exception
{
    /// <summary>Stable error code (e.g. <c>unknown_refresh</c>, <c>refresh_revoked</c>, <c>refresh_expired</c>).</summary>
    public string Code { get; }

    /// <summary>Constructs with a stable error code.</summary>
    /// <param name="code">A stable machine-readable error code that middleware maps to a response body.</param>
    public UnauthorizedException(string code) : base(code)
    {
        Code = code;
    }
}
