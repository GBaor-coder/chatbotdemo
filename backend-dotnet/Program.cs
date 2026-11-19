using BackendDotnet.Hubs;
using BackendDotnet.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ---------- Authentication (JWT) ----------
var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-chatbot-signing-key-change-me-32chars";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var path = context.HttpContext.Request.Path;
                // WebSocket của trình duyệt không gán header Authorization -> đọc access_token từ query string,
                // chỉ cho path /hubs/chat.
                if (path.StartsWithSegments("/hubs/chat", StringComparison.OrdinalIgnoreCase))
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        context.Token = accessToken;
                    }
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddControllers();

// ---------- CORS (chỉ origin cụ thể, AllowCredentials) ----------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowCredentials()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Dev fallback: chỉ cho origin của frontend dev server.
            policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                  .AllowCredentials()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

// ---------- SignalR ----------
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
});

// ---------- HttpClient cho Python retrieval ----------
builder.Services.AddHttpClient<IRetrievalClient, RetrievalClient>();

// ---------- Db connection factory ----------
builder.Services.AddSingleton<IDbConnectionFactory>(sp => new NpgsqlConnectionFactory(
    builder.Configuration.GetConnectionString("Default") ?? "Host=db;Port=5432;Database=chatbot;Username=postgres;Password=postgres"));

// ---------- Tenant context (từ JWT claims) ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContextProvider>();

// ---------- Business services ----------
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<IChatStore>(sp => sp.GetRequiredService<ChatService>());
builder.Services.AddScoped<IChatOrchestrator, ChatOrchestrator>();
builder.Services.AddScoped<ILlmClient, LlmClient>();
builder.Services.AddScoped<IGuardrail, Guardrail>();
builder.Services.AddScoped<IPromptBuilder, PromptBuilder>();
builder.Services.AddScoped<IStructuredQueryService, AdmissionStructuredQueryService>();

var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "backend-dotnet" }));
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _cs;
    public NpgsqlConnectionFactory(string cs) => _cs = cs;
    public NpgsqlConnection CreateConnection() => new(_cs);
}

public interface IDbConnectionFactory { NpgsqlConnection CreateConnection(); }
