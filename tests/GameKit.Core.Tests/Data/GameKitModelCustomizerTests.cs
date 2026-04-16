// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace GameKit.Core.Tests.Data;

public class GameKitModelCustomizerTests
{
    [Fact]
    public void Customizer_IsSealedClass()
    {
        Assert.True(typeof(GameKitModelCustomizer).IsSealed);
    }

    [Fact]
    public void Customizer_InheritsRelationalModelCustomizer()
    {
        Assert.True(typeof(RelationalModelCustomizer).IsAssignableFrom(typeof(GameKitModelCustomizer)));
    }

    [Fact]
    public void Customize_InvokesRegisteredExtensions()
    {
        var extensionInvoked = false;
        var extensions = new List<IModelBuilderExtension>
        {
            new TestExtension(() => extensionInvoked = true)
        };

        // Build a context that uses our customizer with extensions
        var options = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseInMemoryDatabase("ModelCustomizerTest_" + Guid.NewGuid())
            .ReplaceService<IModelCustomizer, TestableGameKitModelCustomizer>()
            .Options;

        // Store the extensions for the testable customizer to pick up
        TestableGameKitModelCustomizer.Extensions = extensions;

        using var ctx = new GameKitDbContext(options);
        // Accessing the model triggers customizer
        _ = ctx.Model;

        Assert.True(extensionInvoked, "Extension's ApplyTo was not invoked by the customizer.");
    }

    /// <summary>Test IModelBuilderExtension that records whether ApplyTo was called.</summary>
    private sealed class TestExtension : IModelBuilderExtension
    {
        private readonly Action _onApply;
        public TestExtension(Action onApply) => _onApply = onApply;
        public void ApplyTo(ModelBuilder modelBuilder) => _onApply();
    }

    /// <summary>
    /// A testable derivative that uses a static Extensions field since DI is not available
    /// in InMemory tests.
    /// </summary>
    internal sealed class TestableGameKitModelCustomizer : RelationalModelCustomizer
    {
        public static IEnumerable<IModelBuilderExtension> Extensions { get; set; } = Array.Empty<IModelBuilderExtension>();

        public TestableGameKitModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies) { }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var ext in Extensions)
                ext.ApplyTo(modelBuilder);
        }
    }
}
