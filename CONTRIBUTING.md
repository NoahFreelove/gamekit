# Contributing to GameKit

## License

GameKit is GPL-3.0-or-later. By contributing, you agree your contributions are licensed under the same terms.

## Per-File SPDX Header (mandatory)

Every `*.cs` file in `src/` and `tests/` must begin with this exact two-line header:

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

- No blank line between the two lines.
- One blank line after the header, before `using` directives or namespace declarations.
- CI enforces this via the `reuse lint` job; PRs with missing headers fail to merge.

## Versioning

Versions are derived from Git tags by MinVer. Tag with `vMAJOR.MINOR.PATCH` (e.g., `v0.1.0`) or prerelease (`v0.1.0-alpha.1`). All six GameKit packages stamp the same version from a single tag.

## Build

Requires .NET SDK 10.0.106+ (pinned via `global.json`). Run `dotnet restore && dotnet build` at the repo root.

## Central Package Management

All NuGet package versions are pinned in `Directory.Packages.props` at the repo root. Individual `.csproj` files must **not** contain inline `<PackageReference Version="..." />` attributes — the build will fail if they do.

## Code Style

- See `.editorconfig` for formatting rules.
- All public APIs require XML doc comments (CS1591 is an error).
- Nullable reference types are enabled repo-wide.
- Warnings are treated as errors.

## Testing

- Unit tests: xUnit + Moq
- Integration tests: xUnit + Testcontainers (Postgres + Redis)
- Run all tests: `dotnet test` at the repo root.
