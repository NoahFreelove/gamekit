// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Cryptography;
using System.Text;

namespace GameKit.Auth.Services;

/// <summary>Singleton default implementation of <see cref="IExternalIdHasher"/>.</summary>
public sealed class ExternalIdHasher : IExternalIdHasher
{
    /// <inheritdoc />
    public string Hash(string provider, string externalId)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(externalId);
        var input = Encoding.UTF8.GetBytes($"{provider}:{externalId}");
        var digest = SHA256.HashData(input);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
