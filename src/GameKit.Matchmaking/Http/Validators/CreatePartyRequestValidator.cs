// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Matchmaking.Http.Contracts;

namespace GameKit.Matchmaking.Http.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CreatePartyRequest"/>. The request payload is
/// empty (player id sourced from the JWT) so this validator is intentionally a no-op stub —
/// registered for symmetry so the FluentValidation DI scan picks up every Matchmaking
/// request type in one pass.
/// </summary>
public sealed class CreatePartyRequestValidator : AbstractValidator<CreatePartyRequest>
{
}
