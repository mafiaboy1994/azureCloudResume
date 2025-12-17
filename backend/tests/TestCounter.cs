using System;
using Company.Function;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class TestCounter
{
    [Fact]
    public void Run_WithValidToken_IncrementsAndReturnsOk()
    {
        // Arrange
        Environment.SetEnvironmentVariable("RESUME_COUNTER_SECRET", "test-token");

        var logger = NullLogger<GetResumeCounter>.Instance;
        var fn = new GetResumeCounter(logger);

        var req = new DefaultHttpContext().Request;

        var counter = new Counter { Id = "1", PartitionKey = "1", Count = 0 };

        // Act
        var result = fn.Run(req, counter, "test-token");

        // Assert
        Assert.Equal(1, result.UpdatedCounter.Count);

        var ok = Assert.IsType<OkObjectResult>(result.HttpResponse);
        var body = Assert.IsType<Counter>(ok.Value);
        Assert.Equal(1, body.Count);
    }

    [Fact]
    public void Run_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        Environment.SetEnvironmentVariable("RESUME_COUNTER_SECRET", "test-token");

        var logger = NullLogger<GetResumeCounter>.Instance;
        var fn = new GetResumeCounter(logger);

        var req = new DefaultHttpContext().Request;

        var counter = new Counter { Id = "1", PartitionKey = "1", Count = 0 };

        // Act
        var result = fn.Run(req, counter, "wrong-token");

        // Assert
        Assert.IsType<UnauthorizedResult>(result.HttpResponse);
    }
}
