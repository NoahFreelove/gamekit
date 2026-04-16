// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;

namespace GameKit.TestFixtures;

/// <summary>
/// Walks up from <see cref="AppContext.BaseDirectory"/> to locate the repo root
/// (directory containing <c>.git</c>). Used by test fixtures to resolve paths
/// relative to the repository root (e.g. <c>docker/postgres/init</c>).
/// </summary>
public static class GitRootLocator
{
    /// <summary>
    /// Returns the absolute path to the repository root directory.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no <c>.git</c> directory is found.</exception>
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repo root (.git) from test output directory.");
    }
}
