// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

// Plan 03-11: grants the CLI test project access to internal command types
// (AdminCreateCommand + any future Spectre commands that prefer internal visibility).
// AssemblyName of the test project is GameKit.Cli.Tests — standard .NET test-project
// naming convention.
[assembly: InternalsVisibleTo("GameKit.Cli.Tests")]
