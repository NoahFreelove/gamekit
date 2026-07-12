// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace GameKit.Lobby;

/// <summary>
/// Adds WebSocket query-string token extraction for the <c>/hubs/lobby</c> path to the
/// existing <see cref="JwtBearerOptions.Events"/> handler chain. This is the SC#2 mechanism:
/// browsers cannot set an <c>Authorization</c> header on a WebSocket upgrade, so the client
/// passes <c>?access_token=&lt;JWT&gt;</c> instead.
/// </summary>
/// <remarks>
/// <para>
/// Registered via <c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;IPostConfigureOptions&lt;JwtBearerOptions&gt;,
/// LobbyJwtBearerPostConfigure&gt;())</c> in <c>AddLobby()</c> — this approach chains correctly
/// with <c>GameKit.Auth</c>'s existing <see cref="JwtBearerOptions"/> configuration without
/// replacing it (T-11-03-01 mitigation).
/// </para>
/// <para>
/// The handler runs ONLY when <c>context.Token</c> is still empty after calling any
/// previously-registered handler. It scopes itself to the <c>/hubs/lobby</c> path so ordinary
/// HTTP requests are not affected.
/// </para>
/// </remarks>
internal sealed class LobbyJwtBearerPostConfigure : IPostConfigureOptions<JwtBearerOptions>
{
    /// <inheritdoc />
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        // Capture any handler registered by GameKit.Auth or the consumer.
        var existingHandler = options.Events?.OnMessageReceived;
        options.Events ??= new JwtBearerEvents();

        options.Events.OnMessageReceived = async context =>
        {
            // Chain first — allow the existing handler to set context.Token.
            if (existingHandler is not null)
                await existingHandler(context).ConfigureAwait(false);

            // Only read the query-string token when:
            //   (a) no earlier handler already set a token, AND
            //   (b) the request targets the lobby hub path.
            if (string.IsNullOrEmpty(context.Token))
            {
                var accessToken = context.Request.Query["access_token"].ToString();
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs/lobby"))
                {
                    context.Token = accessToken;
                }
            }
        };
    }
}
