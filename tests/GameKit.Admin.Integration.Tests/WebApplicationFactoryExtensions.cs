// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Helpers for admin-cookie acquisition + antiforgery-token harvesting in integration tests.
/// Later plans (03-04, 03-07, 03-13) call these verbatim — do not change signatures without
/// updating the consumer plans.
/// </summary>
public static class WebApplicationFactoryExtensions
{
    /// <summary>
    /// POSTs <c>/admin/api/login</c> with the provided credentials and returns the same
    /// <see cref="HttpClient"/> whose <c>CookieContainer</c> now carries the resulting
    /// <c>gk_admin_session</c> cookie. The caller must construct the client via
    /// <c>WebApplicationFactory{TEntryPoint}.CreateClient</c> with a cookie-capable handler.
    /// </summary>
    /// <param name="client">Cookie-capable client from the test web-application factory.</param>
    /// <param name="username">Admin username to submit.</param>
    /// <param name="password">Admin password to submit.</param>
    /// <returns>The same <paramref name="client"/> for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the login endpoint does not return <c>200 OK</c>.
    /// </exception>
    public static async Task<HttpClient> LoginAsAdminAsync(
        this HttpClient client,
        string username,
        string password)
    {
        var resp = await client.PostAsJsonAsync("/admin/api/login",
            new { username, password }).ConfigureAwait(false);
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"LoginAsAdminAsync failed: status={resp.StatusCode} body={body}");
        }
        return client;
    }

    /// <summary>
    /// GETs <c>/admin/login</c>, harvests the antiforgery token from the rendered Blazor form,
    /// and returns the token value. Callers attach it to mutation requests via the configured
    /// CSRF header name (<c>AdminAuthenticationSchemeConstants.CsrfHeaderName</c> =
    /// <c>X-GameKit-Admin-CSRF</c>). The matching cookie is captured by the caller's
    /// <c>CookieContainer</c> automatically.
    /// </summary>
    /// <param name="client">Cookie-capable client from the test web-application factory.</param>
    /// <returns>The antiforgery token value to place in the CSRF header.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the rendered HTML does not contain a <c>__RequestVerificationToken</c> input.
    /// </exception>
    public static async Task<string> HarvestAntiforgeryTokenAsync(this HttpClient client)
    {
        var page = await client.GetStringAsync("/admin/login").ConfigureAwait(false);
        // Blazor renders <input name="__RequestVerificationToken" value="..." />
        var m = Regex.Match(page, @"name=""__RequestVerificationToken""\s+value=""([^""]+)""");
        if (!m.Success)
        {
            throw new InvalidOperationException(
                "HarvestAntiforgeryTokenAsync: no __RequestVerificationToken found in /admin/login HTML");
        }
        return m.Groups[1].Value;
    }
}
