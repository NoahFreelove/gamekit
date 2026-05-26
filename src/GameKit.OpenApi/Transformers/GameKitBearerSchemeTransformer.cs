// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GameKit.OpenApi.Transformers;

/// <summary>
/// <see cref="IOpenApiDocumentTransformer"/> that injects a
/// <c>bearerAuth</c> security scheme into <c>components.securitySchemes</c>
/// when the host has registered the standard JwtBearer authentication
/// scheme (name <c>"Bearer"</c>), and applies it globally to every
/// operation (D-08).
/// </summary>
/// <remarks>
/// <para>
/// Probes <see cref="IAuthenticationSchemeProvider"/> for the JwtBearer
/// scheme. If the scheme is not registered (e.g. a Core-only consumer
/// without <c>GameKit.Auth</c>), the transformer is a no-op so the
/// document stays clean and consumer-driven.
/// </para>
/// <para>
/// <b>JwtBearer string-literal rationale:</b> the scheme name is matched
/// against the literal <c>"Bearer"</c> (the value of
/// <c>JwtBearerDefaults.AuthenticationScheme</c>). The
/// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> package is NOT
/// referenced by <c>GameKit.OpenApi</c> on purpose — pulling it in would
/// force every OpenAPI consumer to ship the JwtBearer runtime even when
/// they have not installed <c>GameKit.Auth</c>. Hardcoding the OAuth 2.0
/// well-known scheme name keeps OpenAPI optional and dependency-free.
/// </para>
/// <para>
/// <b>Pitfall 7 acknowledged:</b> applying the requirement globally is
/// misleading for the handful of anonymous endpoints (e.g. <c>/auth/login/*</c>).
/// A future iteration may add an operation transformer that REMOVES the
/// requirement when <c>[AllowAnonymous]</c> metadata is present. Shipping
/// the global pattern is the v1 contract — the 95% of endpoints that DO
/// require JWT are described accurately.
/// </para>
/// </remarks>
internal sealed class GameKitBearerSchemeTransformer : IOpenApiDocumentTransformer
{
    /// <summary>Scheme key written to <c>components.securitySchemes</c>.</summary>
    public const string SchemeName = "bearerAuth";

    /// <summary>
    /// JwtBearer authentication scheme name — matches
    /// <c>JwtBearerDefaults.AuthenticationScheme</c> verbatim
    /// (literal kept here so we don't take a PackageReference on
    /// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c>).
    /// </summary>
    internal const string JwtBearerSchemeName = "Bearer";

    private readonly IAuthenticationSchemeProvider _schemeProvider;

    /// <summary>Creates the transformer with the DI-resolved scheme provider.</summary>
    /// <param name="schemeProvider">ASP.NET Core authentication scheme registry.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="schemeProvider"/> is null.</exception>
    public GameKitBearerSchemeTransformer(IAuthenticationSchemeProvider schemeProvider)
    {
        ArgumentNullException.ThrowIfNull(schemeProvider);
        _schemeProvider = schemeProvider;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs once per document regeneration. If JwtBearer is not registered, the
    /// document is returned unmodified.
    /// </remarks>
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var schemes = await _schemeProvider.GetAllSchemesAsync().ConfigureAwait(false);
        var jwtRegistered = false;
        foreach (var s in schemes)
        {
            if (string.Equals(s.Name, JwtBearerSchemeName, StringComparison.Ordinal))
            {
                jwtRegistered = true;
                break;
            }
        }
        if (!jwtRegistered)
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type         = SecuritySchemeType.Http,
            Scheme       = "bearer",
            BearerFormat = "JWT",
            In           = ParameterLocation.Header,
            Description  = "Player JWT issued by /auth/login/*",
        };

        // Apply the bearerAuth requirement globally to every operation. Pitfall 7 ack:
        // anonymous endpoints inherit a misleading requirement; v2 may add an operation
        // transformer that strips it for [AllowAnonymous] handlers.
        //
        // Microsoft.OpenApi v2 dictionary shape: OpenApiSecurityRequirement extends
        // Dictionary<OpenApiSecuritySchemeReference, List<string>>. Use the named-reference
        // constructor (referenceId + hostDocument + externalResource) — passing the document
        // as hostDocument lets the serializer resolve the $ref against components.securitySchemes.
        //
        // KNOWN-BUG WORKAROUND: Microsoft.OpenApi 2.0.0's OpenApiSecurityRequirement.SerializeAsV3 +
        // SerializeAsV31 both produce empty `{ }` instead of `{ "bearerAuth": [] }` — the base
        // Dictionary entries are not iterated. We subclass and override the serialize methods to
        // walk the dictionary manually. Remove WorkingSecurityRequirement once Microsoft.OpenApi
        // ships a fix.
        var schemeRef = new OpenApiSecuritySchemeReference(SchemeName, document, externalResource: null);
        var requirement = new WorkingSecurityRequirement
        {
            [schemeRef] = new List<string>(),
        };

        if (document.Paths is null)
        {
            return;
        }

        foreach (var pathItem in document.Paths.Values)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }
            foreach (var op in pathItem.Operations.Values)
            {
                op.Security ??= new List<OpenApiSecurityRequirement>();
                op.Security.Add(requirement);
            }
        }
    }

    /// <summary>
    /// Workaround for the Microsoft.OpenApi 2.0.0 serialization bug where
    /// <see cref="OpenApiSecurityRequirement.SerializeAsV3"/> /
    /// <see cref="OpenApiSecurityRequirement.SerializeAsV31"/> produce an empty
    /// object instead of writing the per-reference key + scopes-array. Walks the
    /// underlying <see cref="System.Collections.Generic.IDictionary{TKey,TValue}"/>
    /// directly and emits the canonical OpenAPI 3.x security-requirement shape.
    /// </summary>
    /// <remarks>
    /// Remove this subclass once Microsoft.OpenApi fixes the upstream defect (the
    /// base <c>SerializeInternal</c> action-callback path does not enumerate entries).
    /// </remarks>
    private sealed class WorkingSecurityRequirement : OpenApiSecurityRequirement
    {
        /// <inheritdoc />
        public override void SerializeAsV3(IOpenApiWriter writer)
            => Write(writer);

        /// <inheritdoc />
        public override void SerializeAsV31(IOpenApiWriter writer)
            => Write(writer);

        private void Write(IOpenApiWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStartObject();
            IDictionary<OpenApiSecuritySchemeReference, List<string>> dict = this;
            foreach (var kv in dict)
            {
                var id = kv.Key.Reference?.Id ?? string.Empty;
                writer.WritePropertyName(id);
                writer.WriteStartArray();
                if (kv.Value is not null)
                {
                    foreach (var scope in kv.Value)
                    {
                        writer.WriteValue(scope);
                    }
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
    }
}
