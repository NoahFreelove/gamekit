; Unshipped analyzer releases for GameKit.Build.
; See https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
GK0001 | GameKit.Security | Error | PII span attribute key detected — key contains a token from the PII denylist.
GK0002 | GameKit.Security | Warning | Non-literal span attribute key — analyzer cannot evaluate the key statically.
