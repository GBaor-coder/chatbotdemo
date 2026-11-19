using BackendDotnet.Models;
using BackendDotnet.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BackendDotnet.Tests;

public class GuardrailTests
{
    private static IConfiguration Config(IDictionary<string, string> data)
    {
        var builder = new ConfigurationBuilder();
        foreach (var kv in data) builder.AddInMemoryCollection(new[] { new KeyValuePair<string, string?>(kv.Key, kv.Value) });
        return builder.Build();
    }

    [Fact]
    public void ShouldRefuse_True_WhenNoChunkAndNoStructured()
    {
        var g = new Guardrail(Config(new Dictionary<string, string>
        {
            ["GUARDRAIL_REFUSAL_MESSAGE"] = "Lời từ chối.",
            ["GUARDRAIL_MIN_SCORE"] = "0.45",
        }));
        Assert.True(g.ShouldRefuse(new List<ChunkResult>(), new StructuredResult { HasResult = false }));
    }

    [Fact]
    public void ShouldRefuse_False_WhenRelevantChunk()
    {
        var g = new Guardrail(Config(new Dictionary<string, string>
        {
            ["GUARDRAIL_REFUSAL_MESSAGE"] = "Lời từ chối.",
            ["GUARDRAIL_MIN_SCORE"] = "0.45",
        }));
        Assert.False(g.ShouldRefuse(new List<ChunkResult> { new ChunkResult { Score = 0.9 } }, new StructuredResult { HasResult = false }));
    }

    [Fact]
    public void ShouldRefuse_False_WhenStructuredResult()
    {
        var g = new Guardrail(Config(new Dictionary<string, string>
        {
            ["GUARDRAIL_REFUSAL_MESSAGE"] = "Lời từ chối.",
            ["GUARDRAIL_MIN_SCORE"] = "0.45",
        }));
        Assert.False(g.ShouldRefuse(new List<ChunkResult>(), new StructuredResult { HasResult = true }));
    }

    [Fact]
    public void Boundary_ScoreEqualMinScore_NotRefuse()
    {
        var g = new Guardrail(Config(new Dictionary<string, string>
        {
            ["GUARDRAIL_REFUSAL_MESSAGE"] = "Lời từ chối.",
            ["GUARDRAIL_MIN_SCORE"] = "0.45",
        }));
        Assert.False(g.ShouldRefuse(new List<ChunkResult> { new ChunkResult { Score = 0.45 } }, new StructuredResult { HasResult = false }));
    }

    [Fact]
    public void Boundary_JustBelowMinScore_Refuse()
    {
        var g = new Guardrail(Config(new Dictionary<string, string>
        {
            ["GUARDRAIL_REFUSAL_MESSAGE"] = "Lời từ chối.",
            ["GUARDRAIL_MIN_SCORE"] = "0.45",
        }));
        Assert.True(g.ShouldRefuse(new List<ChunkResult> { new ChunkResult { Score = 0.44 } }, new StructuredResult { HasResult = false }));
    }

    [Fact]
    public void GetRefusalMessage_ReturnsConfiguredValue()
    {
        var g = new Guardrail(Config(new Dictionary<string, string> { ["GUARDRAIL_REFUSAL_MESSAGE"] = "Từ chối theo cấu hình." }));
        Assert.Equal("Từ chối theo cấu hình.", g.GetRefusalMessage());
    }

    [Fact]
    public void Constructor_Throws_WhenRefusalMessageMissing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new Guardrail(Config(new Dictionary<string, string>())));
        Assert.Contains("GUARDRAIL_REFUSAL_MESSAGE", ex.Message);
    }
}
