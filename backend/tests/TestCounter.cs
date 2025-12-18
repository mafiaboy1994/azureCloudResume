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
        return new CosmosClient("https://example.invalid");
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
