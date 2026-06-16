using Domain.Entities;

namespace Clinco.Application.Services;

public interface IJwtTokenGenerator
{
    string GenerateToken(User user);
    string GenerateRefreshToken();
}
