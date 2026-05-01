// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using Bunit;
using Xunit;

namespace GameKit.Admin.Tests.Components;

public sealed class AccessibilityTests
{
    [Fact(Skip = "TODO Wave 4 — flips live when phase-gate verification ships in plan 03.1-09 (SC#6 — :focus-visible accent contrast ≥3:1)")]
    [Trait("Category", "Component")]
    public void FocusVisibleRing_ContrastRatio_MeetsWcagAA() { /* placeholder */ }
}
