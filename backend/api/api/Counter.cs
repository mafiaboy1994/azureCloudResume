using Newtonsoft.Json;

namespace Company.Function
{
    public class Counter
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = "1";

        // Only keep this if your Cosmos container partition key path is /partitionKey
        [JsonProperty(PropertyName = "partitionKey")]
        public string PartitionKey { get; set; } = "1";

        [JsonProperty(PropertyName = "count")]
        public int Count { get; set; }
    }
}