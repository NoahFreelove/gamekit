// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Rankings.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameKit.Rankings.Authentication;

/// <summary>
/// Custom <see cref="AuthenticationHandler{TOptions}"/> for the <c>GameKitServiceToken</c> scheme.
/// Reads <c>Authorization: Bearer &lt;token&gt;</c>, delegates SHA-256 hash lookup to
/// <see cref="IServiceTokenService.FindByRawAsync"/>, and returns a
/// <see cref="AuthenticateResult.Success"/> result with role <c>service-account</c> on a valid,
/// non-revoked, non-expired token (D-05 / D-06 / T-04-04-RV).
/// </summary>
/// <remarks>
/// <para>
/// The handler is <see langword="public sealed"/> because ASP.NET Core's
/// <c>AddScheme&lt;TOptions, THandler&gt;</c> reflects on the concrete type.
/// </para>
/// <para>
/// <b>Pitfall 10 (DB hot-read):</b> v1 accepts one database round-trip per authenticated request.
/// The <c>RequiresServiceToken</c> rate-limit (300 req/min/token per D-10) keeps the aggregate
/// load tractable. TODO(v2): Add an <c>IMemoryCache</c> TTL layer to eliminate the DB call on
/// cache-hit paths.
/// </para>
/// </remarks>
public sealed class ServiceTokenAuthenticationHandler
    : AuthenticationHandler<ServiceTokenAuthenticationOptions>
{
    // WR-04: debounce LastUsedAt writes so the DB sees at most one UPDATE per minute per token.
    private static readonly TimeSpan LastUsedDebounce = TimeSpan.FromMinutes(1);

    private readonly IServiceTokenService _tokenService;
    private readonly IClock _clock;
    private readonly IMemoryCache _lastUsedDebounceCache;

    /// <summary>
    /// Constructs the handler. <paramref name="tokenService"/>, <paramref name="clock"/>,
    /// and <paramref name="lastUsedDebounceCache"/> are injected from the per-request scope.
    /// </summary>
    public ServiceTokenAuthenticationHandler(
        IOptionsMonitor<ServiceTokenAuthenticationOptions> opts,
        ILoggerFactory log,
        UrlEncoder enc,
        IServiceTokenService tokenService,
        IClock clock,
        IMemoryCache lastUsedDebounceCache)
        : base(opts, log, enc)
    {
        ArgumentNullException.ThrowIfNull(tokenService);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(lastUsedDebounceCache);
        _tokenService = tokenService;
        _clock = clock;
        _lastUsedDebounceCache = lastUsedDebounceCache;
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var raw = authHeader.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var token = raw.AsSpan("Bearer ".Length).TrimStart().ToString();
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        var row = await _tokenService
            .FindByRawAsync(token, Context.RequestAborted)
            .ConfigureAwait(false);

        if (row is null
            || row.RevokedAt is not null
            || (row.ExpiresAt is { } exp && exp < _clock.UtcNow))
        {
            return AuthenticateResult.Fail("invalid_service_token");
        }

        // WR-04: touch LastUsedAt, debounced via IMemoryCache so we issue at most one UPDATE
        // per token per minute. The cache key is the token id; absorbed-set semantics give us
        // a natural "first request in window wins" guarantee under per-token concurrency.
        await TouchLastUsedIfDueAsync(row.Id, Context.RequestAborted).ConfigureAwait(false);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, row.Id.ToString()),
            new Claim(ClaimTypes.Name, row.Name),
            new Claim(ClaimTypes.Role, "service-account"),
        }, Scheme.Name);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private async Task TouchLastUsedIfDueAsync(Guid tokenId, System.Threading.CancellationToken ct)
    {
        var cacheKey = "gamekit.svctoken.lastused:" + tokenId;
        if (_lastUsedDebounceCache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        _lastUsedDebounceCache.Set(cacheKey, true, LastUsedDebounce);

        try
        {
            await _tokenService.TouchLastUsedAsync(tokenId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort — never fail auth because the LastUsedAt write failed.
            Logger.LogWarning(ex,
                "ServiceTokenAuthenticationHandler: failed to update LastUsedAt for token {Id}; continuing.",
                tokenId);
        }
    }
}
