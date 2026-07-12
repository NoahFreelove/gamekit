// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.TestFixtures;

/// <summary>
/// Thrown by <see cref="EgressGuardFixture"/> when an outbound HTTP connection is attempted
/// from a GameKit code path that should never make outbound calls.
/// </summary>
public sealed class EgressViolationException : Exception
{
    /// <summary>The host the connection attempted to reach.</summary>
    public string Host { get; }

    /// <summary>The port the connection attempted to reach.</summary>
    public int Port { get; }

    /// <summary>Constructs the exception with the attempted destination.</summary>
    public EgressViolationException(string host, int port)
        : base($"Egress violation: outbound HTTP connect attempted to {host}:{port}")
    {
        Host = host;
        Port = port;
    }
}
