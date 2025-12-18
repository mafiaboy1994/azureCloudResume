# Azure Resume Counter — CDN + Azure Functions + Cosmos DB (with Throttle)

A lightweight “resume view counter” API that’s callable from a static website via an Azure CDN endpoint, backed by Azure Functions (.NET 8 isolated) and Cosmos DB — with a **throttle** to reduce abuse (count increments at most once per visitor per time window).

---

## ✨ What this project does

- Serves a public API endpoint (through CDN) that returns a JSON counter.
- Updates the counter in Cosmos DB **only when appropriate** (based on throttle rules).
- Keeps secrets **out of the frontend** and **out of the repository** (configuration via environment variables).

---

## 🧭 High-level architecture

```text
Browser / Static Site
        |
        |  GET https://<CDN_ENDPOINT>/api/getResumeCounter
        v
Azure CDN (Classic)
  - Routes /api/* to Function origin
  - Rewrites path to include secret token (origin-only)
        |
        |  GET https://<FUNCTION_ORIGIN>/api/GetResumeCounter/<TOKEN>
        v
Azure Functions (.NET 8 isolated)
  - Validates token
  - Applies per-visitor throttle
  - Increments counter in Cosmos DB only if allowed
        |
        v
Azure Cosmos DB
  - Counter container (stores the count)
  - Throttle container (short-lived “recently counted” markers)
```

---

## 🔌 Public API endpoint

Your website calls **only** the CDN endpoint:

```http
GET https://<CDN_ENDPOINT>/api/getResumeCounter
```

Example response:

```json
{
  "id": "1",
  "partitionKey": "1",
  "count": 420
}
```

✅ The browser **does not** send any secret token.

---

## 🧠 How the CDN forwarding + rewrite works

The CDN has a rule for this one public path:

- **Match:** `/api/getResumeCounter`
- **Origin override:** forwards requests to the Function App origin
- **URL rewrite:** appends the secret token on the origin request

So the *origin* sees a tokenized route like:

```text
/api/GetResumeCounter/<TOKEN>
```

…but the **caller never sees that token**.

---

## 🧩 How the Function works

### 1) Token validation (origin protection)

The Function route is tokenized:

```text
/api/GetResumeCounter/{token}
```

At runtime the function compares `{token}` to the configured secret:

- If invalid → returns **401**
- If valid → continues to throttle/counter logic

> This prevents direct calls to the Function origin unless the caller has the correct token.

---

## 🛡️ Throttle logic (abuse reduction)

Without throttling, a counter endpoint can be inflated by repeated requests.

The throttle changes behavior to:

> **“Increment at most once per visitor per time window.”**

### Visitor identity

For each request the function derives a “visitor key”:

1. **Cookie-based ID** (`visitorId`) if present (best)
2. Otherwise falls back to **client IP** (from forwarded headers)

The function hashes this value using a server-side salt:

```text
hash = SHA256(HASH_SALT + ":" + visitorKey)
```

Only the hash is stored — not the raw IP/cookie value.

### Create-or-conflict gate

Cosmos DB stores a short-lived “marker” document per visitor:

- **Container:** `resumeCounterThrottle`
- **Partition key:** `/pk`
- **TTL:** enabled (marker auto-expires)

Flow per request:

1. Function tries to `CreateItem` `{ id: <hash>, pk: <hash>, ttl: <N seconds> }`
2. If **Create succeeds** → this visitor hasn’t been counted recently → **increment**
3. If **Create returns 409 Conflict** → already counted within the window → **do not increment**

This makes “spam refresh” and simple scripting much less effective.

---

## 🗄️ Cosmos DB containers

### `Counter` container
Stores the counter document (example shape):

```json
{
  "id": "1",
  "partitionKey": "1",
  "count": 420
}
```

### `resumeCounterThrottle` container
Stores short-lived throttle markers:

```json
{
  "id": "<visitor_hash>",
  "pk": "<visitor_hash>",
  "createdUtc": "2025-12-18T12:00:00Z",
  "ttl": 600
}
```

✅ **TTL must be enabled** on this container so markers expire automatically.

---

## ⚙️ Configuration (Environment Variables)

Set these in the Function App **Configuration** (or local settings for development).

| Name | Purpose | Example (safe placeholder) |
|---|---|---|
| `AzureResumeConnectionString` | Cosmos DB connection string used by bindings/SDK | *(do not commit)* |
| `RESUME_COUNTER_SECRET` | Token expected by the Function route | `***` |
| `THROTTLE_WINDOW_SECONDS` | Throttle window length (seconds) | `600` |
| `HASH_SALT` | Salt for hashing visitor keys | `random-long-string` |

> Never commit real secrets to the repository.

---

## ✅ Testing the throttle

### 1) Same client, multiple calls (should not keep increasing)
```bash
curl -s https://<CDN_ENDPOINT>/api/getResumeCounter
curl -s https://<CDN_ENDPOINT>/api/getResumeCounter
```

Expected: the second response returns the **same** count.

### 2) Stable cookie-based visitor key (recommended test)
```bash
curl -s -H "Cookie: visitorId=test-visitor-123" https://<CDN_ENDPOINT>/api/getResumeCounter
curl -s -H "Cookie: visitorId=test-visitor-123" https://<CDN_ENDPOINT>/api/getResumeCounter
```

Expected: increments once, then stays constant within the window.

### 3) Different cookie should increment once
```bash
curl -s -H "Cookie: visitorId=test-visitor-999" https://<CDN_ENDPOINT>/api/getResumeCounter
```

Expected: increments once (new visitor key).

---

## 🔒 Security notes (public endpoint reality)

This endpoint is **public** (no login), so anyone can call the CDN URL.

The project reduces abuse by:

- **Origin protection** (token required at Function origin)
- **Throttle** (repeat calls from same visitor won’t inflate the counter)
- **No-cache headers** (prevents caching stale values)

If you want stronger controls (rate limits / WAF), consider putting the API behind a service that supports rate-limiting rules.

---

## 🧰 Troubleshooting

### Counter doesn’t increment at all
- Check `THROTTLE_WINDOW_SECONDS` isn’t extremely large.
- Ensure `resumeCounterThrottle` has **TTL enabled**.
- Confirm your Function has access to Cosmos DB and can write to `resumeCounterThrottle`.

### Counter increments on every request
- Verify `resumeCounterThrottle` partition key is `/pk`.
- Confirm throttle item creation returns **409 Conflict** on repeat.
- Ensure you are computing a stable visitor key (cookie preferred).

### CDN returns 404 but origin works
- CDN rules may not have fully propagated.
- Confirm rule match includes the correct casing and trailing slash handling.

---

## 📄 License
Personal project — use/adapt as you like.
