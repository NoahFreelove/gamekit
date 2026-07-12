// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace GameKit.TestFixtures;

/// <summary>
/// xUnit collection fixture: spins up a Postgres 17.9 container with the shipped
/// <c>docker/postgres/init</c> scripts mounted, exposing connection strings for
/// the three GameKit roles (<c>gamekit_owner</c>, <c>gamekit_app</c>, <c>gamekit_reader</c>).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Superuser connection string (bootstrap role).</summary>
    public string AdminConnectionString { get; private set; } = string.Empty;

    /// <summary>DDL/migration role connection string.</summary>
    public string OwnerConnectionString { get; private set; } = string.Empty;

    /// <summary>Runtime DML role connection string.</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    /// <summary>Read-only role connection string.</summary>
    public string ReaderConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var initDir = Path.Combine(GitRootLocator.FindRepoRoot(), "docker", "postgres", "init");

        _container = new PostgreSqlBuilder("postgres:17.9")
            .WithUsername("postgres")
            .WithPassword("postgres_test")
            .WithDatabase("postgres")
            .WithBindMount(initDir, "/docker-entrypoint-initdb.d")
            .Build();

        await _container.StartAsync();

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(5432);

        AdminConnectionString  = $"Host={host};Port={port};Database=gamekit;Username=postgres;Password=postgres_test";
        OwnerConnectionString  = $"Host={host};Port={port};Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev";
        AppConnectionString    = $"Host={host};Port={port};Database=gamekit;Username=gamekit_app;Password=gamekit_app_dev";
        ReaderConnectionString = $"Host={host};Port={port};Database=gamekit;Username=gamekit_reader;Password=gamekit_reader_dev";
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
