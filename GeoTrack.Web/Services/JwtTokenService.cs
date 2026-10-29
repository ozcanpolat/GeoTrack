using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace GeoTrack.Web.Services;

public sealed class JwtTokenService(IConfiguration configuration, UserManager<ApplicationUser> users)
{
    public async Task<(string Token, DateTime ExpiresAt)> CreateAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        var expires = DateTime.UtcNow.AddHours(2);
        var key = configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt signing key missing.");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
            new("security_stamp", user.SecurityStamp ?? string.Empty)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "GeoTrack",
            audience: configuration["Jwt:Audience"] ?? "GeoTrackClient",
            claims: claims, notBefore: DateTime.UtcNow, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
