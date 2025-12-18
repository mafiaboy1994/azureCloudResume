using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class GetResumeCounter
{
    private readonly ILogger<GetResumeCounter> _logger;
    private readonly Container _throttle;

    public GetResumeCounter(ILogger<GetResumeCounter> logger, CosmosClient cosmos)
    {
        _logger = logger;

        var db = cosmos.GetDatabase("AzureResume");
        _throttle = db.GetContainer("resumeCounterThrottle");
    }

    [Function("GetResumeCounter")]
    public async Task<ResumeCounterResponse> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "GetResumeCounter/{token}")]
        HttpRequest req,

        [CosmosDBInput(
            databaseName: "AzureResume",
            containerName: "Counter",
            Connection = "AzureResumeConnectionString",
            Id = "1",
            PartitionKey = "1")]
        Counter counter,

        string token
    )
    {
        _logger.LogInformation("GetResumeCounter processed a request.");

        // ----- Token/GUID check (origin protection) -----
        var expected = Environment.GetEnvironmentVariable("RESUME_COUNTER_SECRET");
        if (string.IsNullOrEmpty(expected) || token != expected)
        {
            return new ResumeCounterResponse
            {
                UpdatedCounter = null,                 // no write-back
                HttpResponse = new UnauthorizedResult()
            };
        }

        // Ensure counter object exists (if Cosmos doc missing)
        counter ??= new Counter { Id = "1", PartitionKey = "1", Count = 0 };

        // ----- Throttle logic -----
        // Choose your window (seconds). 600 = 10 minutes
        var windowSeconds = int.Parse(Environment.GetEnvironmentVariable("THROTTLE_WINDOW_SECONDS") ?? "600");
        var salt = Environment.GetEnvironmentVariable("HASH_SALT") ?? "dev";

        // Prefer a cookie-based visitor id if you add it on the frontend, else fallback to IP
        var visitorId = req.Cookies["visitorId"];
        var ip = visitorId is null ? GetClientIp(req) : null;
        var rawKey = visitorId ?? ip ?? "unknown";

        var keyHash = HashKey(rawKey, salt);

        bool shouldIncrement = false;

        try
        {
            // /pk partition key -> must set pk property and use that value as PartitionKey
            var doc = new ThrottleDoc
            {
                id = keyHash,
                pk = keyHash,
                createdUtc = DateTime.UtcNow,
                ttl = windowSeconds
            };

            await _throttle.CreateItemAsync(doc, new PartitionKey(keyHash));
            shouldIncrement = true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Already counted within the throttle window -> do not increment
        }

        if (shouldIncrement)
        {
            counter.Count += 1;
        }

        // No-cache headers
        req.HttpContext.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        req.HttpContext.Response.Headers["Pragma"] = "no-cache";
        req.HttpContext.Response.Headers["Expires"] = "0";
        req.HttpContext.Response.Headers["Surrogate-Control"] = "no-store";

        return new ResumeCounterResponse
        {
            // IMPORTANT:
            // If shouldIncrement is false, we set UpdatedCounter = null so CosmosDBOutput doesn't write.
            // (This reduces RU usage a lot on read-only requests.)
            UpdatedCounter = shouldIncrement ? counter : null,
            HttpResponse = new OkObjectResult(counter)
        };
    }

    private static string HashKey(string input, string salt)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes($"{salt}:{input}")));
    }

    private static string? GetClientIp(HttpRequest req)
    {
        // Behind CDN/proxies, X-Forwarded-For is typically a comma-separated list
        if (req.Headers.TryGetValue("X-Forwarded-For", out var xff) && !string.IsNullOrWhiteSpace(xff))
            return xff.ToString().Split(',')[0].Trim();

        return null;
    }

    public class ResumeCounterResponse
    {
        [CosmosDBOutput(
            databaseName: "AzureResume",
            containerName: "Counter",
            Connection = "AzureResumeConnectionString")]
        public Counter? UpdatedCounter { get; set; }

        [HttpResult]
        public IActionResult HttpResponse { get; set; } = default!;
    }

    private sealed class ThrottleDoc
    {
        public string id { get; set; } = default!;
        public string pk { get; set; } = default!;
        public DateTime createdUtc { get; set; }
        public int ttl { get; set; } // seconds (requires TTL enabled on the container)
    }
}
