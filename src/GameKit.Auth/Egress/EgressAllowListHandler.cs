// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Egress;

/// <summary>
/// Delegating HTTP message handler attached to every named <c>HttpClient</c> in GameKit.Auth.
/// Inspects <c>request.RequestUri.Host</c> and throws <see cref="EgressViolationException"/>
/// if the host is not on <see cref="GameKitAuthOptions.AllowedProviderHosts"/>. Resolves Phase-1
/// D-21's Auth egress carve-out (see CONTEXT D-07 / D-08).
/// </summary>
public sealed class EgressAllowListHandler : DelegatingHandler
{
    private readonly HashSet<string> _allowed;

    /// <summary>Constructs the handler, snapshotting the allow-list from options at registration time.</summary>
    public EgressAllowListHandler(GameKitAuthOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        _allowed = new HashSet<string>(opts.AllowedProviderHosts, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var host = request.RequestUri?.Host;
        if (host is null || !_allowed.Contains(host))
            throw new EgressViolationException(
                host ?? "<null>",
                $"Outbound call to '{host ?? "<null>"}' is not on the GameKit.Auth allow-list. " +
                "Add the host to GameKitAuthOptions.AllowedProviderHosts if intentional.");
        return base.SendAsync(request, cancellationToken);
    }
}
