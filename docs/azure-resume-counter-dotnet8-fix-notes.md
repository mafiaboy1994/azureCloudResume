# Azure Resume Counter – .NET 8 Isolated Fix Notes (Code-Focused)

> **Goal:** migrate an Azure Functions “resume counter” API to **.NET 8 isolated worker**, fix build/test failures, and restore Cosmos DB read/update behavior.

---

## What we were building

**Frontend**
- Static site calls a single HTTP endpoint: `GET /api/GetResumeCounter`
- The response JSON contains the current visit count.

**Backend**
- Azure Function increments a counter stored in Cosmos DB (single document with `id = "1"`)

---

## Symptoms we hit

### Build / SDK mismatch
- Mixed **in-proc** and **isolated** packages caused build failures:
  - `Microsoft.NET.Sdk.Functions` (in-proc) + `Microsoft.Azure.Functions.Worker.*` (isolated) = ❌

### Old WebJobs code in a new isolated worker app
- References to:
  - `Microsoft.Azure.WebJobs.*`
  - `[FunctionName]`, `[CosmosDB]`, `ILogger` patterns from in-proc templates

### Unit tests failing
- Tests referenced in-proc namespaces (`Microsoft.Azure.WebJobs`) and old ASP.NET Core internals (`Microsoft.AspNetCore.Http.Internal`).

### Function class “not found” by tests
- `Company.Function.GetResumeCounter` missing in the built assembly.
- Root cause: source file was **outside the `.csproj` folder** (or had a `.cs.old` extension).

### Cosmos DB output binding failed locally (400 BadRequest)
- Cosmos write failed due to **document shape mismatches** (especially `id`/partition key fields).
- Fix was to ensure consistent JSON property naming in the model.

---

## Solution summary (high level)

### ✅ 1) Standardize on .NET 8 **Isolated Worker**
We committed to isolated worker and removed in-proc bits.

**`api.csproj` must not reference in-proc packages**
- **Remove**: `Microsoft.NET.Sdk.Functions`
- **Use**: `Microsoft.Azure.Functions.Worker` + `Microsoft.Azure.Functions.Worker.Sdk`

Example (core pieces):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.*" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="1.*" OutputItemType="Analyzer" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.*" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore" Version="*" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.CosmosDB" Version="4.*" />
  </ItemGroup>
</Project>
```

---

## ✅ 2) Update `Program.cs` for isolated worker + DI

We used ASP.NET Core integration:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        // Optional App Insights wiring (requires packages):
        // - Microsoft.ApplicationInsights.WorkerService
        // - Microsoft.Azure.Functions.Worker.ApplicationInsights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
    .Build();

host.Run();
```

---

## ✅ 3) Convert Function code from WebJobs (in-proc) → isolated bindings

### Old in-proc style (what we removed)
- `[FunctionName]`
- `Microsoft.Azure.WebJobs.*`
- `out Counter updatedCounter`
- `[CosmosDB(...)]`

### New isolated worker style
- `[Function("Name")]`
- `[CosmosDBInput]` + `[CosmosDBOutput]`
- **Multi-output return object** containing:
  - Cosmos DB output document
  - HTTP response (marked with `[HttpResult]`)

Example:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req,

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

        return new ResumeCounterResponse
        {
            UpdatedCounter = counter,
            HttpResponse = new OkObjectResult(counter)
        };
    }
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
```

---

## ✅ 4) Fix the Counter model (single source of truth)

### Problem we found
We accidentally had **two `Counter` classes** in the same namespace:
- one in `Counter.cs`
- one inside `GetResumeCounter.cs`

That causes type confusion, missing properties, and test failures.

### Fix
- Keep **only one** `Counter` model (recommended: `Counter.cs`).
- Ensure Cosmos-required fields are correct.

We also made JSON names explicit using **System.Text.Json** to avoid serialization mismatches between HTTP response + Cosmos binding:

```csharp
using System.Text.Json.Serialization;

namespace Company.Function;

public class Counter
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "1";

    // Keep if your container partition key path is /partitionKey
    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; set; } = "1";

    [JsonPropertyName("count")]
    public int Count { get; set; }
}
```

✅ This resolved Cosmos “BadRequest (400) – One of the specified inputs is invalid” errors during writes.

---

## ✅ 5) Ensure the function source file is compiled into the project

### Symptom
Unit tests couldn’t see `Company.Function.GetResumeCounter` even though the project built.

### Root cause
The `GetResumeCounter.cs` file was:
- in a **parent folder** (outside `backend/api/api/`), or
- renamed as `GetResumeCounter.cs.old`

### Fix
- Move the file under the `.csproj` folder tree (`backend/api/api/`), **or**
- explicitly include it in the `.csproj`:

```xml
<ItemGroup>
  <Compile Include="..\GetResumeCounter.cs" Link="GetResumeCounter.cs" />
</ItemGroup>
```

We validated inclusion using:

```bash
dotnet build backend/api/api/api.csproj -c Release -v diag | grep GetResumeCounter.cs
```

---

## ✅ 6) Update unit tests for .NET 8 / isolated worker

### What we stopped doing
- No `Microsoft.Azure.WebJobs.*`
- No `Microsoft.AspNetCore.Http.Internal`

### What we did instead
- Instantiate the function class directly
- Create an `HttpRequest` with `DefaultHttpContext()`
- Assert the returned `OkObjectResult` + updated count

Example test:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

public class TestCounter
{
    [Fact]
    public void Run_IncrementsCounter_AndReturnsOk()
    {
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

        var result = fn.Run(req, counter);

        Assert.Equal(1, result.UpdatedCounter.Count);

        var ok = Assert.IsType<OkObjectResult>(result.HttpResponse);
        var body = Assert.IsType<Company.Function.Counter>(ok.Value);
        Assert.Equal(1, body.Count);
    }
}
```

---

## Local verification (quick commands)

From repo root:

```bash
dotnet clean backend/api/api/api.csproj
dotnet build backend/api/api/api.csproj -c Release
dotnet test backend/tests/tests.csproj -c Release
```

Run Functions locally (requires Core Tools + .NET 8 SDK):

```bash
cd backend/api/api
func start
```

Then hit:

- `http://localhost:7071/api/GetResumeCounter`

Success looks like:

```
Executed 'Functions.GetResumeCounter' (Succeeded, ...)
```

---

## Deployment/Frontend note (code-adjacent)

Because the frontend calls the function from the browser, we switched to:

```csharp
AuthorizationLevel.Anonymous
```

…and updated the frontend fetch URL to the new app hostname:

```js
const functionApiUrl =
  "https://getazureresumecounterew-isolated.azurewebsites.net/api/GetResumeCounter";
```

If the page still calls the old endpoint, it’s usually **cached JS/CDN** — a purge or cache-buster fixes it.

---

## Cheatsheet

✅ **Isolated worker**: `Microsoft.Azure.Functions.Worker.*`  
❌ **In-proc**: `Microsoft.NET.Sdk.Functions`, `Microsoft.Azure.WebJobs.*`

✅ `[Function]`, `[CosmosDBInput]`, `[CosmosDBOutput]`  
❌ `[FunctionName]`, `[CosmosDB]`

✅ one `Counter` model, explicit JSON names  
✅ source `.cs` files must be inside the `.csproj` folder tree

---

## Final state (what “good” looks like)
- `dotnet build` succeeds
- `dotnet test` succeeds (2 tests passing)
- Local `func start` shows the function endpoint
- Browser refresh increments Cosmos count for `id = "1"`
- Deployed frontend shows the live counter and no CORS errors ✅
