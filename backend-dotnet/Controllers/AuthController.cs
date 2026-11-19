using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BackendDotnet.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public AuthController(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    [HttpPost("dev-token")]
    [AllowAnonymous]
    public IActionResult GetDevToken()
    {
        var env = _env.EnvironmentName;
        var appEnv = _config["APP_ENV"];
        if (!string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(appEnv, "development", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var key = _config["Jwt:Key"] ?? "dev-chatbot-signing-key-change-me-32chars";
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("tenant_id", _config["DEFAULT_TENANT_ID"] ?? "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.NameIdentifier, "dev-user"),
            new Claim(ClaimTypes.Name, "dev-user"),
        };

        var token = new JwtSecurityToken(
            issuer: "chatbot-dev",
            audience: "chatbot-dev",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return Ok(new { token = tokenString });
    }
}
