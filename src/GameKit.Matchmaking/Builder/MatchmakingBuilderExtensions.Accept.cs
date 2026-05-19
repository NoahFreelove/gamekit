// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Matchmaking.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Partial-class registrations for the Plan 05-06 accept-step proposal flow services —
/// <see cref="IProposalService"/> (<see cref="ProposalService"/>),
/// <see cref="IDeclineCooldownService"/> (<see cref="DeclineCooldownService"/>),
/// <see cref="IDeclineHistoryReader"/> (<see cref="EfDeclineHistoryReader"/>), and
/// <see cref="TeamAssignmentService"/>. The Lua scripts that drive the accept-and-complete
/// atomic check + decline-and-reap re-queue are folded into <c>ProposalService</c> directly
/// (see <see cref="GameKit.Matchmaking.Redis.ProposalScripts"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Channel binding:</b> <see cref="ProposalService"/> resolves
/// <see cref="System.Threading.Channels.ChannelWriter{T}"/> of
/// <see cref="GameKit.Matchmaking.Entities.TicketEvent"/> from the Plan 05-04 placeholder
/// (replaced by Plan 05-07's <c>AddBackgroundServices</c> with the options-driven bounded
/// instance). No channel registration lives here.
/// </para>
/// <para>
/// <b>Lifetimes:</b>
/// <list type="bullet">
///   <item><see cref="ProposalService"/> is <em>scoped</em> — opens its own scoped
///         <see cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"/> for
///         the <c>GameSession</c> create write, but the entry point itself is HTTP-request-scoped.</item>
///   <item><see cref="DeclineCooldownService"/> is <em>scoped</em> — wraps a scoped
///         <see cref="IDeclineHistoryReader"/> (which itself wraps a scoped
///         <see cref="GameKit.Core.Data.GameKitDbContext"/>).</item>
///   <item><see cref="TeamAssignmentService"/> is a <em>stateless singleton</em>.</item>
/// </list>
/// </para>
/// </remarks>
public static partial class MatchmakingBuilderExtensions
{
    /// <summary>
    /// Registers the accept-step proposal services (Plan 05-06): <see cref="IProposalService"/>
    /// + <see cref="IDeclineCooldownService"/> + <see cref="IDeclineHistoryReader"/> +
    /// <see cref="TeamAssignmentService"/>. Idempotent via <c>TryAddScoped</c> /
    /// <c>TryAddSingleton</c>.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection for chaining.</returns>
    internal static IServiceCollection AddProposalServices(this IServiceCollection services)
    {
        services.TryAddSingleton<TeamAssignmentService>();
        services.TryAddScoped<IDeclineHistoryReader, EfDeclineHistoryReader>();
        services.TryAddScoped<IDeclineCooldownService, DeclineCooldownService>();
        services.TryAddScoped<IProposalService, ProposalService>();
        return services;
    }
}
