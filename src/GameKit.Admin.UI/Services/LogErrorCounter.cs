// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using Microsoft.Extensions.Logging;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// <see cref="ILoggerProvider"/> that feeds every <see cref="LogLevel.Error"/>-and-above event
/// into <see cref="ErrorRateRingBuffer"/>. Registered as a <see cref="ILoggerProvider"/>
/// singleton in <c>AddGameKitAdmin</c> so it participates in the application's standard
/// logging pipeline without any configuration on the consumer side. The health panel's
/// recent-error-rate tile reads directly from the ring buffer — no OpenTelemetry dependency.
/// </summary>
public sealed class LogErrorCounter : ILoggerProvider
{
    private readonly ErrorRateRingBuffer _buf;

    /// <summary>Constructs the provider, binding it to the shared ring buffer.</summary>
    /// <param name="buf">The singleton ring buffer.</param>
    public LogErrorCounter(ErrorRateRingBuffer buf)
    {
        ArgumentNullException.ThrowIfNull(buf);
        _buf = buf;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CountingLogger(_buf);

    /// <inheritdoc />
    public void Dispose() { }

    private sealed class CountingLogger : ILogger
    {
        private readonly ErrorRateRingBuffer _buf;
        public CountingLogger(ErrorRateRingBuffer b) => _buf = b;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;

        public void Log<TState>(
            LogLevel level,
            EventId id,
            TState state,
            Exception? ex,
            Func<TState, Exception?, string> fmt)
        {
            if (level >= LogLevel.Error) _buf.IncrementError();
        }
    }
}
