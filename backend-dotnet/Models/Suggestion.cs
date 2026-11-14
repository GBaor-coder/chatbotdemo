using System.Text.Json.Serialization;

namespace BackendDotnet.Models;

public sealed class Suggestion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;
}
