// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Auth.Egress;

/// <summary>
/// Thrown by <see cref="EgressAllowListHandler"/> when an outbound HTTP request targets a host
/// not on <see cref="GameKitAuthOptions.AllowedProviderHosts"/>. Distinct from the host+port
/// socket-level egress exception in <c>GameKit.TestFixtures</c> (the test fixture inspects sockets;
/// this handler inspects <c>HttpRequestMessage.RequestUri.Host</c>).
/// </summary>
public sealed class EgressViolationException : Exception
{
    /// <summary>The host that was blocked.</summary>
    public string Host { get; }

    /// <summary>Constructs the exception with a blocked host and a diagnostic message.</summary>
    public EgressViolationException(string host, string message) : base(message)
    {
        Host = host;
    }
}
