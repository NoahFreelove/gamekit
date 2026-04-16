// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Reflection;
using GameKit.Core.Data;
using Xunit;

namespace GameKit.Core.Tests.Data;

public class IModelBuilderExtensionTests
{
    [Fact]
    public void IModelBuilderExtension_Is_Public_Interface()
    {
        var type = typeof(IModelBuilderExtension);
        Assert.True(type.IsInterface);
        Assert.True(type.IsPublic);
    }

    [Fact]
    public void IModelBuilderExtension_Has_ApplyTo_Method()
    {
        var method = typeof(IModelBuilderExtension).GetMethod("ApplyTo");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal("modelBuilder", parameters[0].Name);
        Assert.Equal("ModelBuilder", parameters[0].ParameterType.Name);
    }
}
