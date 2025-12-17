using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http; // HttpResult
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class GetResumeCounter
{
    private readonly ILogger<GetResumeCounter> _logger;

    public GetResumeCounter(ILogger<GetResumeCounter> logger)
    {
        _logger = logger;
    }

    [Function("GetResumeCounter")]
    public ResumeCounterResponse Run(
    [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req,

    [CosmosDBInput(
        databaseName: "AzureResume",
        containerName: "Counter",
        Connection = "AzureResumeConnectionString",
        Id = "1",
        PartitionKey = "1")]
    Counter counter
    )
    {
        _logger.LogInformation("GetResumeCounter processed a request.");

    counter ??= new Counter { Id = "1", PartitionKey = "1", Count = 0 };
    counter.Count += 1;

    // 🔒 Tell CDNs/browsers: do NOT cache this response
    req.HttpContext.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
    req.HttpContext.Response.Headers["Pragma"] = "no-cache";
    req.HttpContext.Response.Headers["Expires"] = "0";
    // Optional extra for some proxies/CDNs:
    req.HttpContext.Response.Headers["Surrogate-Control"] = "no-store";

    return new ResumeCounterResponse
        {
            UpdatedCounter = counter,
            HttpResponse = new OkObjectResult(counter)
        };
    }

public class ResumeCounterResponse
{
    [CosmosDBOutput(
        databaseName: "AzureResume",
        containerName: "Counter",
        Connection = "AzureResumeConnectionString")]
    public Counter UpdatedCounter { get; set; } = default!;

    [HttpResult]
    public IActionResult HttpResponse { get; set; } = default!;
}
}

// Make sure these match your document fields / partition key path in Cosmos.
// public class Counter
// {
//     public string Id { get; set; } = "1";
//     public string PartitionKey { get; set; } = "1";
//     public int Count { get; set; }
// }
