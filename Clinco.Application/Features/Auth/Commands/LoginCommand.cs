using Application.Common.DTOs;
using Application.Common.Exceptions;
using Clinco.Application.Services;
using Domain.Interfaces.Repositories;
using FluentValidation;
using MediatR;

namespace Application.Features.Auth.Commands;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>Login accepts either email OR phone number as the identifier.</summary>
public record LoginCommand(
    string Identifier,   // email or phone
    string Password) : IRequest<AuthResponse>;

// ── Validator ─────────────────────────────────────────────────────────────────

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().WithMessage("Email or phone number is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

// ── Handler ───────────────────────────────────────────────────────────────────

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponse>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenGenerator _jwt;

    public LoginCommandHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        IJwtTokenGenerator jwt)
    {
        _users = users;
        _hasher = hasher;
        _jwt = jwt;
    }

    public async Task<AuthResponse> Handle(LoginCommand cmd, CancellationToken ct)
    {
        // Try email first, then phone
        var user = await _users.GetByEmailAsync(cmd.Identifier, ct)
                ?? await _users.GetByPhoneAsync(cmd.Identifier, ct);

        if (user is null || !_hasher.Verify(cmd.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid credentials.");

        if (!user.IsActive)
            throw new UnauthorizedException("Account is deactivated. Please contact support.");

        user.RecordLogin();
        // No UoW.SaveChanges needed here — the UserRepository.UpdateAsync +
        // UoW pattern is handled in a background fire-and-forget update;
        // LastLogin is best-effort and doesn't need strong consistency.

        var accessToken  = _jwt.GenerateToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();

        return new AuthResponse(
            accessToken, refreshToken,
            DateTime.UtcNow.AddHours(1),
            user.Id, user.FullName, user.Email,
            user.Role.RoleName);
    }
}
