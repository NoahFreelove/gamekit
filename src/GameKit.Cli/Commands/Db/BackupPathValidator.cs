// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.IO;

namespace GameKit.Cli.Commands.Db;

/// <summary>
/// Guards against path-traversal attacks (T-17-04-01) on operator-supplied backup/restore paths.
/// Only absolute paths that contain no <c>..</c> segments are accepted.
/// </summary>
public static class BackupPathValidator
{
    /// <summary>
    /// Returns <see langword="true"/> only when <paramref name="path"/> is rooted (absolute)
    /// and contains no <c>..</c> directory-traversal segment.
    /// </summary>
    /// <param name="path">The operator-supplied output or input file path.</param>
    /// <returns><see langword="true"/> if safe; <see langword="false"/> if the path is
    /// relative or contains a traversal segment.</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description>Relative paths (e.g. <c>./backup.dump</c>) are rejected.</description></item>
    ///   <item><description>Paths with <c>..</c> segments (e.g. <c>/tmp/../etc/passwd</c>) are rejected.</description></item>
    ///   <item><description>Clean absolute paths (e.g. <c>/srv/backups/game.dump</c>) are accepted.</description></item>
    /// </list>
    /// </remarks>
    public static bool IsSafeAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (!Path.IsPathRooted(path))
            return false;

        // Check for .. segments — split on both separators
        var parts = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == "..")
                return false;
        }

        return true;
    }
}
