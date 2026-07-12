// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Text.RegularExpressions;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Platformer3D.Integration.Tests.Packaging;

/// <summary>
/// Verifies that <c>samples/Platformer3D/docker-compose.yml</c> does NOT publish host port
/// mappings for the <c>postgres</c> or <c>redis</c> services (must-NOT per SPEC D-14 / R3).
/// Only the <c>app</c> service should have a <c>ports:</c> section.
/// </summary>
/// <remarks>
/// This is a pure file-parse test — no Docker daemon, no Testcontainers required.
/// Parses the YAML with a simple line/section reader (no YAML library dependency).
/// </remarks>
[Trait("Category", "Unit")]
[Trait("RequiresDocker", "false")]
public sealed class ComposePortMappingTests
{
    private static readonly string ComposeYamlPath = Path.Combine(
        GitRootLocator.FindRepoRoot(),
        "samples",
        "Platformer3D",
        "docker-compose.yml");

    [Fact(DisplayName = "ComposePort: docker-compose.yml exists at expected path")]
    public void ComposeYaml_Exists()
    {
        Assert.True(File.Exists(ComposeYamlPath),
            $"docker-compose.yml not found at: {ComposeYamlPath}");
    }

    [Fact(DisplayName = "ComposePort: 'postgres' service has NO ports: mapping (must-NOT)")]
    public void Postgres_Service_Has_No_Ports_Mapping()
    {
        var yaml = File.ReadAllText(ComposeYamlPath);

        // Extract the postgres service block (from 'postgres:' to the next top-level service name),
        // then strip YAML comments before checking for 'ports:' so comment lines don't fire false positives.
        var postgresBlock = StripYamlComments(ExtractServiceBlock(yaml, "postgres"));
        Assert.False(string.IsNullOrWhiteSpace(postgresBlock),
            "Could not locate 'postgres:' service block in docker-compose.yml");

        // The postgres block must NOT contain a 'ports:' key (ignoring comment lines).
        Assert.DoesNotContain("ports:", postgresBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "ComposePort: 'redis' service has NO ports: mapping (must-NOT)")]
    public void Redis_Service_Has_No_Ports_Mapping()
    {
        var yaml = File.ReadAllText(ComposeYamlPath);

        // Extract the redis service block, strip comments before checking.
        var redisBlock = StripYamlComments(ExtractServiceBlock(yaml, "redis"));
        Assert.False(string.IsNullOrWhiteSpace(redisBlock),
            "Could not locate 'redis:' service block in docker-compose.yml");

        // The redis block must NOT contain a 'ports:' key (ignoring comment lines).
        Assert.DoesNotContain("ports:", redisBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "ComposePort: 'app' service publishes exactly one host port (port 8080)")]
    public void App_Service_Publishes_Port_8080()
    {
        var yaml = File.ReadAllText(ComposeYamlPath);

        var appBlock = StripYamlComments(ExtractServiceBlock(yaml, "app"));
        Assert.False(string.IsNullOrWhiteSpace(appBlock),
            "Could not locate 'app:' service block in docker-compose.yml");

        // The app block must contain a ports: section (not in a comment).
        Assert.Contains("ports:", appBlock, StringComparison.OrdinalIgnoreCase);

        // The port mapping must reference 8080.
        Assert.Contains("8080", appBlock, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "ComposePort: YAML contains exactly three named services (app, postgres, redis)")]
    public void Yaml_Contains_Exactly_Three_Services()
    {
        var yaml = File.ReadAllText(ComposeYamlPath);

        // Count top-level service names (lines that match exactly two-space-indented + name + colon
        // under the 'services:' block, e.g. '  app:', '  postgres:', '  redis:').
        var matches = Regex.Matches(yaml, @"^\s{2}(app|postgres|redis):\s*$", RegexOptions.Multiline);
        Assert.Equal(3, matches.Count);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes YAML comment lines (lines whose first non-whitespace character is <c>#</c>)
    /// and inline comments (everything from <c>#</c> onwards on a content line, provided it
    /// follows at least one whitespace character).
    /// This prevents comment text such as <c># NO ports: section</c> from triggering
    /// <c>DoesNotContain("ports:")</c> assertions.
    /// </summary>
    private static string StripYamlComments(string block)
    {
        var lines = block.Split('\n');
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            // Skip pure comment lines.
            if (trimmed.StartsWith('#'))
                continue;
            // Remove inline comments (# preceded by whitespace).
            var commentIdx = line.IndexOf(" #", StringComparison.Ordinal);
            var resultLine = commentIdx >= 0 ? line[..commentIdx] : line;
            sb.AppendLine(resultLine);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts the YAML block for a named service. Reads from the line
    /// <c>  {name}:</c> to the next sibling service name (or end of services block).
    /// Returns the raw text of that block.
    /// </summary>
    private static string ExtractServiceBlock(string yaml, string serviceName)
    {
        var lines = yaml.Split('\n');
        int start = -1;
        int end = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd();

            // Two-space-indented service names: "  app:", "  postgres:", "  redis:"
            if (trimmed == $"  {serviceName}:")
            {
                start = i + 1;
                continue;
            }

            if (start >= 0 && end < 0)
            {
                // A new two-space-indented key that is NOT a sub-key of the current service
                // (i.e. it is itself a sibling service name) — or the 'volumes:' top-level key.
                var isTopLevelSibling = trimmed.Length > 2 &&
                                        trimmed[0] == ' ' &&
                                        trimmed[1] == ' ' &&
                                        trimmed[2] != ' ' &&
                                        trimmed.EndsWith(':') &&
                                        trimmed != $"  {serviceName}:";
                if (isTopLevelSibling)
                {
                    end = i;
                    break;
                }
            }
        }

        if (start < 0)
            return string.Empty;

        if (end < 0)
            end = lines.Length;

        return string.Join('\n', lines[start..end]);
    }
}
