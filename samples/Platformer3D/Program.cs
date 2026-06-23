// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

// Platformer3D — Wave 1 stub: project shell builds successfully.
// Real application startup code is written in Phase 21, Plan 21-04.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Platformer3D - Phase 21 demo (stub)");

await app.RunAsync();
