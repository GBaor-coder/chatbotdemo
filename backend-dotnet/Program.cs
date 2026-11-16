using BackendDotnet.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

// HttpClient cho Python retrieval
builder.Services.AddHttpClient<IRetrievalClient, RetrievalClient>();

// Db connection factory
builder.Services.AddSingleton<IDbConnectionFactory>(sp => new NpgsqlConnectionFactory(
    builder.Configuration.GetConnectionString("Default") ?? "Host=db;Port=5432;Database=chatbot;Username=postgres;Password=postgres"));

// Tenant context (dev: header-based)
builder.Services.AddScoped<ITenantContext, TenantContextProvider>();

// Business services
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<ILlmClient, LlmClient>();
builder.Services.AddScoped<IGuardrail, Guardrail>();
builder.Services.AddScoped<IPromptBuilder, PromptBuilder>();
builder.Services.AddScoped<IStructuredQueryService, AdmissionStructuredQueryService>();

// CORS cho frontend React
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowFrontend");
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "backend-dotnet" }));
app.MapControllers();
app.Run();

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _cs;
    public NpgsqlConnectionFactory(string cs) => _cs = cs;
    public NpgsqlConnection CreateConnection() => new(_cs);
}

public interface IDbConnectionFactory { NpgsqlConnection CreateConnection(); }
