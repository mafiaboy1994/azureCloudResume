using System.Text.Json.Serialization;

namespace Company.Function;

public class Counter
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "1";

    // Keep only if your container PK path is /partitionKey
    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; set; } = "1";

    [JsonPropertyName("count")]
    public int Count { get; set; }
}