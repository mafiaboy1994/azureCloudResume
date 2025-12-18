using System;
using System.Threading.Tasks;
using Company.Function;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class TestCounter
{
    // We only need a CosmosClient instance to satisfy the constructor.
    // For the Unauthorized test, Cosmos is never used (auth fails first),
    // so this can be a dummy endpoint.
    private static CosmosClient CreateDummyCosmosClient()
    {
        // Valid *format* (won’t parse-error). It will never be used in the invalid-token test.
        const string endpoint = "https://localhost:8081";
        const string key =
            "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4X7C1D+"
        + "0Jk1rYVYtG7rj7G4mQ==";

        return new CosmosClient(endpoint, key);
    }

    [Fact(Skip = "GetResumeCounter now requires CosmosClient + hits Cosmos on valid-token path. Re-enable after refactoring throttling to be mockable or after adding Cosmos integration test setup.")]
    public async Task Run_WithValidToken_IncrementsAndReturnsOk()
    {
        // Arrange
        Environment.SetEnvironmentVariable("RESUME_COUNTER_SECRET", "test-token");

        var logger = NullLogger<GetResumeCounter>.Instance;
        var cosmos = CreateDummyCosmosClient();
        var fn = new GetResumeCounter(logger, cosmos);

        var req = new DefaultHttpContext().Request;
        var counter = new Counter { Id = "1", PartitionKey = "1", Count = 0 };

        // Act
        var result = await fn.Run(req, counter, "test-token");

        // Assert
        Assert.NotNull(result.UpdatedCounter);
        Assert.Equal(1, result.UpdatedCounter!.Count);

        var ok = Assert.IsType<OkObjectResult>(result.HttpResponse);
        var body = Assert.IsType<Counter>(ok.Value);
        Assert.Equal(1, body.Count);
    }

    [Fact]
    public async Task Run_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        Environment.SetEnvironmentVariable("RESUME_COUNTER_SECRET", "test-token");

        var logger = NullLogger<GetResumeCounter>.Instance;
        var cosmos = CreateDummyCosmosClient();
        var fn = new GetResumeCounter(logger, cosmos);

        var req = new DefaultHttpContext().Request;
        var counter = new Counter { Id = "1", PartitionKey = "1", Count = 0 };

        // Act
        var result = await fn.Run(req, counter, "wrong-token");

        // Assert
        Assert.IsType<UnauthorizedResult>(result.HttpResponse);
    }
}
