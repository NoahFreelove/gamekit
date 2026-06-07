// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using Microsoft.Extensions.Logging;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// <see cref="ILoggerProvider"/> that feeds every <see cref="LogLevel.Error"/>-and-above event
/// into <see cref="ErrorRateRingBuffer"/> and, when available, also into
/// <see cref="IRedisErrorRateCounter"/> for cross-replica aggregation (ADMIN-14). Registered as
/// a <see cref="ILoggerProvider"/> singleton in <c>AddGameKitAdmin</c> so it participates in the
/// application's standard logging pipeline without any configuration on the consumer side. The
/// health panel's recent-error-rate tile reads directly from the ring buffer (or from the Redis
/// counter when registered) — no OpenTelemetry dependency.
/// </summary>
public sealed class LogErrorCounter : ILoggerProvider
{
    private readonly ErrorRateRingBuffer _buf;
    private readonly IRedisErrorRateCounter? _redis;

    /// <summary>Constructs the provider, binding it to the shared ring buffer.</summary>
    /// <param name="buf">The singleton ring buffer.</param>
    /// <param name="redis">
    /// Optional Redis counter for cross-replica aggregation. When <see langword="null"/>
    /// (single-instance install with no <c>IConnectionMultiplexer</c>), only the in-memory
    /// ring buffer is incremented.
    /// </param>
    public LogErrorCounter(ErrorRateRingBuffer buf, IRedisErrorRateCounter? redis = null)
    {
        ArgumentNullException.ThrowIfNull(buf);
        _buf = buf;
        _redis = redis;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CountingLogger(_buf, _redis);

    /// <inheritdoc />
    public void Dispose() { }

    private sealed class CountingLogger : ILogger
    {
        private readonly ErrorRateRingBuffer _buf;
        private readonly IRedisErrorRateCounter? _redis;

        public CountingLogger(ErrorRateRingBuffer b, IRedisErrorRateCounter? redis)
        {
            _buf = b;
            _redis = redis;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;

        public void Log<TState>(
            LogLevel level,
            EventId id,
            TState state,
            Exception? ex,
            Func<TState, Exception?, string> fmt)
        {
            if (level < LogLevel.Error) return;
            _buf.IncrementError();
            _redis?.IncrementError();  // fire-and-forget per IRedisErrorRateCounter contract
        }
    }
}
