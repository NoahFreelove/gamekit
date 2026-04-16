// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Core.Integration.Tests;

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }

[CollectionDefinition("PostgresAndRedis")]
public sealed class PostgresAndRedisCollection
    : ICollectionFixture<PostgresFixture>, ICollectionFixture<RedisFixture> { }
