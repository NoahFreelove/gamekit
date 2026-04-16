// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

// Allow test assemblies to access internal types (sparingly — prefer public contracts).
[assembly: InternalsVisibleTo("GameKit.Core.Tests")]
[assembly: InternalsVisibleTo("GameKit.Core.Integration.Tests")]
[assembly: InternalsVisibleTo("GameKit.Integration.Tests")]
