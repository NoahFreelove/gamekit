// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Http.Contracts;

/// <summary>
/// Request body for <c>POST /auth/refresh</c>. The client device fingerprint is read from the
/// <c>X-GameKit-Device</c> header (CONTEXT D-05); it is not part of the JSON body.
/// </summary>
/// <param name="RefreshToken">The raw refresh token the client currently holds.</param>
public sealed record RefreshRequest(string RefreshToken);
