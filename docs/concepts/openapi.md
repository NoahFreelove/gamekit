# GameKit.OpenApi — Concepts

## What It Does

`GameKit.OpenApi` exposes an OpenAPI 3.x document for the player-facing GameKit HTTP surface
at `/openapi/v1.json` (configurable). It is a thin configuration-only package — it registers
two document transformers (`GameKitInfoTransformer` for title/version metadata and
`GameKitBearerSchemeTransformer` for the `BearerAuth` security scheme) and wires the admin
path exclusion filter. There are **no public interfaces** — the only extension point is the
**endpoint-inclusion predicate** built into `AddGameKitOpenApi`.

## No Public Interfaces

`GameKit.OpenApi` has zero public interfaces to implement. It is configuration-only. The
consumer seam is the `configure` callback passed to `AddGameKitOpenApi(...)`:

```csharp
// IServiceCollection extension — orthogonal to the IGameKitBuilder chain
builder.Services.AddGameKitOpenApi(opts =>
{
    opts.DocumentName = "v1";           // default; must not collide with other AddOpenApi() calls
    opts.Title        = "My Game API";  // default: "GameKit API"
    opts.MountPath    = "/openapi";     // default; produces /openapi/v1.json
});

// In the pipeline:
app.MapGameKitOpenApi();   // GET /openapi/v1.json — anonymous; admin paths excluded
```

## Admin Path Exclusion (D-19)

The `ShouldInclude` predicate filters out any endpoint whose route contains `"admin"`. This
is wired as an `OpenApiOptions.ShouldInclude` lambda — not as a transformer — because
operation transformers cannot remove paths from the document, only decorate them. The result
is that `/openapi/v1.json` contains only the public, player-facing surface.

This is the consumer seam for path-level control: if you need to include additional paths
(e.g. a health endpoint) or exclude additional paths, pass a custom `ShouldInclude`
predicate:

```csharp
builder.Services.AddGameKitOpenApi(opts =>
{
    var defaultPredicate = opts.ShouldInclude;    // admin exclusion
    opts.ShouldInclude = desc =>
        defaultPredicate(desc) && !desc.RelativePath!.StartsWith("/internal");
});
```

## Document Name Collision

`AddGameKitOpenApi` registers a document named `v1` by default. If the consumer also calls
`builder.Services.AddOpenApi("v1", …)`, the documents collide. Resolve by overriding
`DocumentName`:

```csharp
builder.Services.AddGameKitOpenApi(opts => opts.DocumentName = "gamekit");
// Result: /openapi/gamekit.json
```

## `AddGameKitOpenApi` Is Not on `IGameKitBuilder`

Unlike other packages, `AddGameKitOpenApi` is an `IServiceCollection` extension method (not
an `IGameKitBuilder` extension). This allows consumers to opt in to OpenAPI generation
independently of the core GameKit package chain — useful for scenarios where the consumer
wants to include GameKit endpoints in an existing OpenAPI document setup without re-ordering
their builder calls.

## Wire-Up

```csharp
// Registration (any order relative to AddGameKit):
builder.Services.AddGameKitOpenApi();

// Endpoint mapping (after app = builder.Build()):
app.MapGameKitOpenApi();   // GET /openapi/v1.json — anonymous GET, admin paths excluded
```

## Library-vs-Consumer Responsibility Line

| GameKit.OpenApi owns | Consumer owns |
|----------------------|---------------|
| Admin path exclusion filter | Additional path exclusions (custom `ShouldInclude` predicate) |
| Bearer scheme declaration in the document | OAuth2 flow docs (not in scope) |
| Document title and version metadata | Custom title/description (`opts.Title`, `opts.MountPath`) |
| Anonymous GET on `/openapi/v1.json` | Authorization policy for custom document endpoints |

## See Also

- [API reference](../../api/GameKit.OpenApi.Builder.yml) — full member-level docs.
- [docs/security-checklist.md](../security-checklist.md) — API surface hardening.
