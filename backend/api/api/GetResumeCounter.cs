using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class ThrottleDoc
{
    public string id { get; set; } = default!;
    public string pk { get; set; } = default!;
    public DateTime createdUtc { get; set; }
    public int ttl { get; set; } // seconds
}



public class GetResumeCounter
{
    private readonly ILogger<GetResumeCounter> _logger;
    private readonly Container _counterContainer;
    private readonly Container _throttleContainer;

    private const string DbName = "AzureResume";
    private const string CounterContainerName = "Counter";
    private const string ThrottleContainerName = "resumeCounterThrottle";

    // Your counter item id (and because PK is /id, PK value is also this string)
    private const string CounterId = "1";

    public GetResumeCounter(ILogger<GetResumeCounter> logger, CosmosClient cosmos)
    {
        _logger = logger;

        var db = cosmos.GetDatabase(DbName);
        _counterContainer = db.GetContainer(CounterContainerName, CounterContainerName);
        _throttleContainer = db.GetContainer(DbName, ThrottleContainerName);
        // ^ if your container is in DbName; if not, adjust GetContainer(dbName, containerName)
        // safer explicit form:
        _counterContainer = cosmos.GetContainer(DbName, CounterContainerName);
        _throttleContainer = cosmos.GetContainer(DbName, ThrottleContainerName);
    }

    [Function("GetResumeCounter")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "getResumeCounter")] HttpRequestData req)
    {
        _logger.LogInformation("GetResumeCounter hit.");

        // --- Token check (your CDN appends this via rewrite) ---
        // If you're no longer passing it as a route segment, remove this section.
        // For example, token could be a query param ?token=... or a header.
        var expected = Environment.GetEnvironmentVariable("RESUME_COUNTER_SECRET");
        var token = GetQuery(req, "token"); // or read from header if you prefer

        if (string.IsNullOrEmpty(expected) || token != expected)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            AddNoCacheHeaders(unauthorized);
            return unauthorized;
        }

        // --- Throttle key: cookie preferred, IP fallback ---
        var visitorId = GetCookie(req, "visitorId");
        var ip = visitorId is null ? GetClientIp(req) : null;
        var rawKey = visitorId ?? ip ?? "unknown";

        var salt = Environment.GetEnvironmentVariable("HASH_SALT") ?? "dev";
        var keyHash = HashKey(rawKey, salt);

        var windowSeconds = int.Parse(Environment.GetEnvironmentVariable("THROTTLE_WINDOW_SECONDS") ?? "600");

        // --- Try to create throttle record (pk = /pk) ---
        var throttleDoc = new ThrottleDoc
        {
            id = keyHash,
            pk = keyHash,
            createdUtc = DateTime.UtcNow,
            ttl = windowSeconds
        };

        bool shouldIncrement = false;
        try
        {
            await _throttleContainer.CreateItemAsync(throttleDoc, new PartitionKey(keyHash));
            shouldIncrement = true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // already counted in this window
        }

        int count;

        if (shouldIncrement)
        {
            // Patch increment is the cleanest way to avoid lost updates under concurrency. :contentReference[oaicite:2]{index=2}
            var ops = new[] { PatchOperation.Increment("/count", 1) };
            var patched = await _counterContainer.PatchItemAsync<CounterDoc>(
                id: CounterId,
                partitionKey: new PartitionKey(CounterId), // because pk path is /id
                patchOperations: ops);

            count = patched.Resource.count;
        }
        else
        {
            var current = await _counterContainer.ReadItemAsync<CounterDoc>(
                id: CounterId,
                partitionKey: new PartitionKey(CounterId));

            count = current.Resource.count;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        AddNoCacheHeaders(ok);
        await ok.WriteAsJsonAsync(new { count });
        return ok;
    }

    private static void AddNoCacheHeaders(HttpResponseData res)
    {
        res.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
        res.Headers.Add("Pragma", "no-cache");
        res.Headers.Add("Expires", "0");
        res.Headers.Add("Surrogate-Control", "no-store");
    }

    private static string HashKey(string input, string salt)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes($"{salt}:{input}")));
    }

    private static string? GetClientIp(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("X-Forwarded-For", out var xff))
            return xff.FirstOrDefault()?.Split(',')[0].Trim();
        return null;
    }

    private static string? GetCookie(HttpRequestData req, string name)
    {
        if (!req.Headers.TryGetValues("Cookie", out var cookies)) return null;
        var all = string.Join("; ", cookies);

        foreach (var part in all.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim() == name) return kv[1].Trim();
        }
        return null;
    }

    private static string? GetQuery(HttpRequestData req, string key)
    {
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        return qs[key];
    }

    private sealed class CounterDoc
    {
        public string id { get; set; } = default!;
        public int count { get; set; }
    }

    private sealed class ThrottleDoc
    {
        public string id { get; set; } = default!;
        public string pk { get; set; } = default!;
        public DateTime createdUtc { get; set; }
        public int ttl { get; set; } // seconds (when TTL enabled) :contentReference[oaicite:3]{index=3}
    }
}