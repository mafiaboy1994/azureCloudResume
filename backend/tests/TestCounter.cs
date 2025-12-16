using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

public class TestCounter
{
    [Fact]
    public void Run_IncrementsCounter_AndReturnsOk()
    {
        // Arrange
        var logger =
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Company.Function.GetResumeCounter>.Instance;

        var fn = new Company.Function.GetResumeCounter(logger);

        var req = new DefaultHttpContext().Request;

        var counter = new Company.Function.Counter
        {
            Id = "1",
            PartitionKey = "1",
            Count = 0
        };

        // Act
        var result = fn.Run(req, counter);

        // Assert Cosmos output
        Assert.Equal(1, result.UpdatedCounter.Count);

        // Assert HTTP response
        var ok = Assert.IsType<OkObjectResult>(result.HttpResponse);
        var body = Assert.IsType<Company.Function.Counter>(ok.Value);
        Assert.Equal(1, body.Count);
    }
}
