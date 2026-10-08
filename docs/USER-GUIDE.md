# PerfPilot User Guide

A practical guide for QA engineers who are new to performance testing. It covers:

- the performance-testing terms you'll see in PerfPilot
- how to set up and run a test, step by step
- how to prepare **test data** when a scenario has several endpoints with different parameters
- what happens in the backend when you click **Run**
- how to **read the results** and decide whether an API performs well

> PerfPilot address: **http://PCLPT208:8080** on the office network, or http://localhost:8080 on the host machine.

---

## Contents

1. [Performance testing in 5 minutes](#1-performance-testing-in-5-minutes)
2. [Glossary of terms](#2-glossary-of-terms)
3. [The workflow at a glance](#3-the-workflow-at-a-glance)
4. [Step 1: Create an environment](#4-step-1-create-an-environment)
5. [Step 2: Import the API spec](#5-step-2-import-the-api-spec)
6. [Step 3: Build a scenario](#6-step-3-build-a-scenario)
7. [Test data for multiple endpoints](#7-test-data-for-multiple-endpoints)
8. [Choosing load and SLA thresholds](#8-choosing-load-and-sla-thresholds)
9. [Step 4: Run the test](#9-step-4-run-the-test)
10. [Analysing results: does the API perform well?](#10-analysing-results-does-the-api-perform-well)
11. [Common patterns in the charts](#11-common-patterns-in-the-charts)
12. [How it works in the backend](#12-how-it-works-in-the-backend)
13. [Pitfalls and good practice](#13-pitfalls-and-good-practice)
14. [FAQ](#14-faq)

---

## 1. Performance testing in 5 minutes

Functional testing asks *"does the API return the right answer?"*.
Performance testing asks *"does it still return the right answer, fast enough, when many people use it at the same time?"*

PerfPilot simulates many users (**virtual users**) calling your API endpoints in a realistic mix, for a set amount of time. It measures:

- **How fast** responses are (latency / response time)
- **How many** requests per second the API handles (throughput)
- **How many** requests fail (error rate)

Then it compares the measurements with your targets (**SLA thresholds**) and gives a **PASSED** / **FAILED** verdict.

A typical progression for one API:

| Test type | Question it answers | When |
|-----------|---------------------|------|
| **Smoke** | Does the scenario work at all? (1 user) | Always first, after every change to the scenario |
| **Load** | Is it fast enough at our *normal/peak expected* traffic? | Before each release |
| **Stress** | Where does it start to break? | When you need to know capacity limits |
| **Spike** | Does it survive a sudden burst and recover? | For APIs with bursty traffic (sales, campaigns, batch jobs) |
| **Soak** | Does it stay healthy for a long time (memory leaks, connection exhaustion)? | Before major releases, or after infrastructure changes |

---

## 2. Glossary of terms

| Term | What it means | Where you see it in PerfPilot |
|------|---------------|-------------------------------|
| **Virtual user (VU)** | A simulated user. Each VU runs in a loop: pick an endpoint → send one request → wait (think time) → repeat. 10 VUs = 10 such loops in parallel. | Load profile stages, "Virtual users" live KPI, VUs line on the throughput chart |
| **Iteration** | One pass through that loop (in PerfPilot: one request + the think-time pause). | (internal) |
| **Request** | One HTTP call to the API. | "Requests" KPI and column |
| **Latency / response time** | Time from sending the request until the full response is received, in milliseconds (ms). 1000 ms = 1 second. | Avg, p50, p90, p95, p99, Max |
| **Average (Avg)** | Sum of all response times ÷ number of requests. **Easily misleading**: a few very slow requests hide behind many fast ones. Prefer percentiles. | "Avg latency" |
| **Percentile (pXX)** | "XX% of requests were *at or faster than* this value." See the example below. | p50, p90, p95, p99 |
| **p50 (median)** | Half of the requests were faster than this; the "typical" experience. | Endpoint table, latency chart |
| **p95** | 95% of requests were faster; only 1 in 20 was slower. **The most common SLA metric.** | KPI, endpoint table, SLA |
| **p99** | 99% were faster; 1 in 100 was slower. Shows the "tail": what your unluckiest users experience. | KPI, endpoint table, SLA |
| **Max** | The single slowest request. Often an outlier; don't build SLAs on it. | Endpoint table |
| **Tail latency** | The slow end of the distribution (p95/p99/max). A big gap between p50 and p99 means inconsistent performance. | Compare p50 vs p99 |
| **Throughput / RPS** | Requests per second the API completed. | "Throughput", "Requests/s", RPS column and chart |
| **Error rate** | % of requests that failed. In PerfPilot a request fails when its HTTP status is **not one of the endpoint's expected status codes**, or when there's no response at all (timeout, connection refused). | "Error rate" KPI, Errors column, error chart |
| **Checks** | % of responses whose status matched the expected codes. Normally ≈ 100% − error rate. | "Status checks" KPI, Checks column |
| **Think time** | The pause each VU takes between requests, like a real user reading the page. Without it, each VU fires requests as fast as possible. | Load profile → Think time min/max |
| **Stage** | One segment of the load profile: "over this *duration*, move to this many *target VUs*". VUs change **gradually** within a stage. | Load profile → stages table |
| **Ramp-up / ramp-down** | Stages where VUs increase / decrease. Ramping up gradually avoids an unrealistic "everyone arrives at the same millisecond" spike. | First/last stages |
| **Load profile** | The full shape of the test over time (all stages). | Smoke/Load/Stress/Spike/Soak presets |
| **SLA / threshold** | Your target, e.g. "p95 ≤ 500 ms" or "error rate ≤ 1%". Each one is PASS or FAIL after the run. | Global SLA, Endpoint SLA, thresholds table |
| **Verdict** | PASSED if *every* threshold passed (or none were defined); FAILED otherwise. | Banner at the top of the results page |
| **Weight** | Share of traffic an endpoint gets, in %. All weights add up to 100%. | Endpoint card (weight box) |
| **Saturation** | The point where the API (or database, CPU, connection pool…) is fully busy. More users then only add waiting time. | Throughput stops rising while latency climbs |
| **Bottleneck** | The component that saturates first (DB, CPU, thread pool, a downstream service…). | Found by correlating results with server monitoring |
| **Baseline** | A known-good run you compare later runs against. | Run history (compare runs of the same scenario) |
| **Warm-up** | The first seconds/minutes when caches are empty and connections are being opened. Often slower. | Left edge of the latency chart |
| **Timeout** | No response within 60 s (k6's default). Counts as an error with ~60,000 ms latency. | Errors + huge Max |
| **HTTP 4xx / 5xx / 429** | 4xx = client problem (400 bad data, 401/403 auth, 404 not found). 5xx = server error. **429 = rate limited** (the API or a gateway is refusing excess traffic). | Errors (if not expected) |

### Percentiles explained with an example

10 requests took (sorted, ms): `120, 130, 135, 140, 150, 155, 160, 170, 900, 2500`

- **Average** = 456 ms → suggests every user waits ~0.5 s. Nobody actually did.
- **p50** ≈ 152 ms → the typical user waited ~150 ms.
- **p90** ≈ 1060 ms → 1 in 10 users waited a second or more.
- **Max** = 2500 ms → one user waited 2.5 s.

That's why SLAs are written on **p95/p99**, not averages: they describe what real, unlucky users experience.

### How VUs, think time and RPS relate

Each VU does roughly one request every *(response time + think time)* seconds:

```
RPS ≈ VUs ÷ (average response time in seconds + average think time in seconds)
```

Example: 20 VUs, responses take 0.2 s, think time 1–3 s (average 2 s):
`RPS ≈ 20 ÷ (0.2 + 2) ≈ 9 requests/second`.

Use this the other way round to choose VUs for a target load:
`VUs ≈ target RPS × (response time + think time)`.
To reach 50 RPS with 0.2 s responses and 1 s think time: `50 × 1.2 = 60 VUs`.

---

## 3. The workflow at a glance

```
 Environment            Spec                Scenario                       Run                 Results
 ───────────            ────                ────────                       ───                 ───────
 Base URL + auth  ──►   Import OpenAPI ──►  Pick endpoints, weights,  ──►  k6 generates   ──►  Verdict, SLA table,
 Test connection        (URL or file)       params, test data,             load, live          endpoint table,
                                            load profile, SLAs             metrics             charts, HTML report
```

---

## 4. Step 1: Create an environment

**Environments → New environment**

| Field | What to enter |
|-------|---------------|
| Name | Something clear, e.g. `Orders API – QA` |
| Base URL | The API root, e.g. `https://orders-qa.company.local/api`. Endpoint paths are appended to it. |
| OpenAPI URL | Optional. The swagger/openapi JSON or YAML URL. |
| Production environment | Turn on for production. Every run will then ask you to type the environment name to confirm. **Avoid load testing production** unless it's agreed and scheduled. |
| Ignore TLS certificate errors | Only for internal QA/staging servers with self-signed certificates. |
| Authentication | **None**, **Bearer token**, **API key** (header name + value) or **Login flow** (PerfPilot calls a token endpoint with a JSON body and extracts the token using a JSONPath like `$.access_token`). |

Click **Test connection**. The PerfPilot *server* calls the API and tells you what it sees:

- ✅ **Ok**: reachable.
- ⚠️ **Warning** on Base URL with 404: normal if the API has no page at its root.
- ❌ **Dns**: the server can't find the host (VPN / DNS issue on the PerfPilot machine).
- ❌ **Tls**: certificate problem; for QA servers, tick "Ignore TLS certificate errors".
- ❌ **Unauthorized/Forbidden**: wrong or expired token/key, or wrong login settings.
- ❌ **Timeout**: the server didn't answer in time (firewall, server down).

Secrets (tokens, keys, login bodies) are encrypted and never shown again. To change one, type the new value and save. Leaving the field blank keeps the stored value.

---

## 5. Step 2: Import the API spec

**Environments → Endpoints** on your environment.

- **Fetch from URL**: uses the environment's OpenAPI URL (or any URL you type). Optional extra header for protected swagger pages, timeout, and TLS option.
- **Upload file**: a `.json` or `.yaml` OpenAPI 3.x / Swagger 2.0 file.

Each import is saved as a new **version** (v1, v2…). You can browse endpoints grouped by tag, see parameters, the request body schema, and a generated sample body.

---

## 6. Step 3: Build a scenario

**Scenarios → New scenario** (or "New scenario from this spec" on the spec page). Give it a name and choose the environment.

### 6.1 Endpoints tab

1. **Add endpoints** → tick the endpoints to include → **Add N endpoint(s)**. Endpoints that aren't in the spec can be added under "Not in the spec?".
2. Set each endpoint's **weight**: its share of the traffic. The total must be **100%** ("Traffic total" turns green); **Distribute evenly** splits it equally.
   *Example:* a real app might do 70% reads (`GET /orders/{id}`), 20% searches, 10% creates. Copy the production mix if you know it.
3. Click an endpoint to expand it and set:
   - **Parameters**: one row per path/query/header parameter. See [section 7](#7-test-data-for-multiple-endpoints).
   - **Request body** (POST/PUT/PATCH): pre-filled with a sample from the spec. Edit it in the JSON editor. **Format** tidies it; **Reset to sample** restores the spec sample.
   - **Expected status code(s)**: comma-separated, e.g. `200` or `200, 201` or `200, 404`. Anything else counts as an **error**.
   - **Endpoint SLA**: optional per-endpoint targets (see [section 8](#8-choosing-load-and-sla-thresholds)).

### 6.2 Load profile & SLA tab

Pick a preset (Smoke, Load, Stress, Spike, Soak) or edit the stages directly. The chart on the right shows the VU curve; **Total duration** and **Peak VUs** are calculated for you. Set think time and the **Global SLA thresholds**.

### 6.3 Test data tab

Paste CSV test data. Covered in detail in the next section.

### 6.4 k6 script tab

Shows the exact script that will run. You don't need to read it, but it's useful for a developer or for running the test outside PerfPilot (**Download .js**). It never contains passwords or tokens.

**Save**, or **Save & run**.

---

## 7. Test data for multiple endpoints

### 7.1 Why test data matters

If every request asks for the same thing, for example `GET /orders/1` 10,000 times, the API answers from its **cache** after the first call. The results then look great but don't reflect reality. Varying the values (different order IDs, customers, search terms) forces the API and database to do real work.

PerfPilot has three ways to give a parameter a value:

| Value source | Example | Use when |
|--------------|---------|----------|
| **Static / placeholder**, fixed text | `available` | The value doesn't matter or must be constant (e.g. `api-version: 2`) |
| **Static / placeholder** with random placeholders | `{{random.int}}`, `{{random.guid}}`, `{{random.email}}` | Any value is fine, or you need *new unique-ish* values (e.g. creating records) |
| **CSV column** | column `orderId` | The value must be **valid / existing** data (real IDs, real customers, valid search terms) |

Placeholders also work **inside text and inside JSON bodies**, and can be mixed: `ORD-{{csv.orderId}}`, `"email": "{{random.email}}"`, `"quantity": {{random.int}}`.

| Placeholder | Produces |
|-------------|----------|
| `{{random.int}}` | A number from 1 to 1,000,000 |
| `{{random.guid}}` (or `{{random.uuid}}`) | e.g. `3f2c9e1a-7b4d-4c1e-9a8f-2d6b5e4c3a21` |
| `{{random.email}}` | e.g. `perfpilot.3f2c9e1a-7b4@example.com` |
| `{{csv.columnName}}` | The value of that column in the current CSV row |

### 7.2 The key rule: one CSV per scenario, one row per request

- A scenario has **one** CSV table (Test data tab), with a header row of column names.
- **For every request, PerfPilot picks one random row** from the whole table.
- The endpoint chosen for that request uses **only the columns it needs** from that row and ignores the rest.

So when your endpoints need different parameters, **put all the columns in the same CSV**, side by side.

### 7.3 Worked example: four endpoints with different parameters

Scenario endpoints:

| Endpoint | Needs | Weight |
|----------|-------|--------|
| `GET /customers/{customerId}` | path `customerId` | 30% |
| `GET /customers/{customerId}/orders/{orderId}` | path `customerId` + `orderId` (the order must belong to that customer) | 30% |
| `GET /products/search?term=...&page=...` | query `term`, `page` | 30% |
| `POST /orders` | JSON body with `customerId`, `sku`, a unique reference | 10% |

**One CSV for the whole scenario** (Test data tab):

```csv
customerId,orderId,term,sku
C1001,O-5001,laptop,SKU-100
C1001,O-5002,mouse,SKU-200
C1002,O-5101,monitor,SKU-300
C1003,O-5201,keyboard,SKU-100
C1004,O-5301,laptop bag,SKU-400
```

**Parameter settings:**

| Endpoint | Parameter | In | Value source | Value / column |
|----------|-----------|----|--------------|----------------|
| `GET /customers/{customerId}` | customerId | Path | CSV column | `customerId` |
| `GET /customers/{customerId}/orders/{orderId}` | customerId | Path | CSV column | `customerId` |
| | orderId | Path | CSV column | `orderId` |
| `GET /products/search` | term | Query | CSV column | `term` |
| | page | Query | Static / placeholder | `1` |
| `POST /orders` | (body) | | | see below |

Body of `POST /orders`:

```json
{
  "customerId": "{{csv.customerId}}",
  "sku": "{{csv.sku}}",
  "quantity": 1,
  "clientReference": "{{random.guid}}"
}
```

**What happens at runtime**, for example on three consecutive requests from one VU:

| Request | Random row picked | Endpoint picked (by weight) | Actual request sent |
|---------|------------------|-----------------------------|---------------------|
| 1 | row 3 (`C1002, O-5101, monitor, SKU-300`) | Search | `GET /products/search?term=monitor&page=1` |
| 2 | row 1 (`C1001, O-5001, laptop, SKU-100`) | Customer order | `GET /customers/C1001/orders/O-5001` |
| 3 | row 4 (`C1003, O-5201, keyboard, SKU-100`) | Create order | `POST /orders` with `"customerId":"C1003","sku":"SKU-100",…,"clientReference":"<new guid>"` |

Notice in request 2 that `customerId` and `orderId` come **from the same row**, so they always match each other. That's why **related values must be on the same row**.

### 7.4 Rules and tips for building the CSV

1. **Related values go in the same row.** If an order belongs to a customer, a token to a user, or a product to a store, put them together on one row.
2. **Unrelated lists can be different lengths.** Repeat the shorter list to fill the rows. Every row should have a usable value in every column, because any row can be picked for any endpoint.
   *Example:* 200 order IDs but only 5 search terms → repeat the 5 terms down 200 rows.
   An **empty cell** sends an **empty value**:
   - an empty **query** parameter is left out of the URL
   - an empty **path** parameter breaks the URL (`/customers//orders`) and causes 404 errors.
3. **Use real, existing data** from the test environment for IDs you read (export a few hundred rows from the QA database or an API listing). Invalid IDs give 404s, which count as errors unless `404` is an expected code.
4. **More rows = more realistic.** 100–1,000 rows is a good range. The maximum is 100,000 rows.
5. **Copy-paste from Excel works**: tab-separated data is detected automatically. Quote values that contain commas: `"Smith, John"`.
6. **Column names are case-sensitive** and must match exactly what you select or write in `{{csv.name}}`. PerfPilot refuses to save if a parameter or placeholder refers to a column that doesn't exist.
7. **Rows are picked at random *with repetition*.** The same row can be used many times, by many VUs at once. That's fine for reads. For **creates that require uniqueness** (unique email, username, reference), use `{{random.guid}}` or `{{random.email}}` instead of CSV values.
   `{{random.int}}` can repeat across thousands of requests, so don't use it where uniqueness is enforced.
8. **Random IDs that may not exist:** if you use `{{random.int}}` for an ID, many requests will get `404`. Either add `404` to the expected status codes (you're then mainly testing the "not found" path) or use a CSV of real IDs (better).
9. **Creates change data.** `POST`/`PUT`/`DELETE` endpoints really create, update or delete records in the target environment. Use a QA environment and agree on clean-up.
10. **Check the preview.** The Test data tab shows the detected columns, row count and the first rows. The k6 script tab shows the embedded data.

### 7.5 Endpoints that don't need test data

Endpoints without parameters (e.g. `GET /health`, `GET /categories`) ignore the CSV. A scenario doesn't need a CSV at all if every value is static or random.

---

## 8. Choosing load and SLA thresholds

### 8.1 How much load?

Ask the product owner / developers, or check production monitoring (Application Insights, gateway logs) for:

- **Peak requests per second** on these endpoints (busiest hour of the busiest day)
- **Concurrent users** at peak
- **Mix**: which endpoints are called most (use this for the weights)

Then:

- **Load test** = expected peak (use `VUs ≈ RPS × (response time + think time)` from [section 2](#how-vus-think-time-and-rps-relate)).
- **Stress test** = 2–3× the peak, in steps, to see where it breaks.
- **Spike** = jump from normal to 5–10× and back.
- **Soak** = normal load for 1–4 hours (or longer within the configured cap).

If you have no numbers at all, start with the **Load** preset (20 VUs), look at the results, and adjust.

**Think time:** the default 1–3 s simulates people clicking. For APIs called by other systems (no humans), use a small think time (0–0.5 s).

### 8.2 Which SLA values?

Agree on these with the product owner. Typical **starting points** for internal web APIs:

| Kind of endpoint | p95 | p99 | Error rate |
|------------------|-----|-----|------------|
| Simple reads (get by ID, lists) | ≤ 300–500 ms | ≤ 1,000 ms | ≤ 1% |
| Searches / reports | ≤ 1,000–2,000 ms | ≤ 3,000 ms | ≤ 1% |
| Writes (create/update) | ≤ 500–1,000 ms | ≤ 2,000 ms | ≤ 1% |

- **Global SLA** applies to all scenario traffic combined. **Endpoint SLA** applies to one endpoint. Use endpoint SLAs when endpoints differ a lot; a slow report shouldn't fail the SLA meant for a fast lookup.
- **Throughput ≥ (req/s)** checks the **average over the whole test, including ramp-up and ramp-down**, so it's lower than the peak you see in the chart. It also depends on how many VUs you configure: a low VU count gives low RPS even if the API is fast. Use it only when you know the RPS your VUs should produce.
- Leave a field empty to skip that threshold.

---

## 9. Step 4: Run the test

1. **Always run a Smoke test first** (1 VU, ~1 minute). If the smoke test has errors, fix the scenario before running bigger tests: wrong IDs, missing auth, wrong expected codes.
2. Click **Run** / **Save & run**. For production environments, type the environment name to confirm.
3. The run page shows **live** values every 2 seconds: virtual users, requests/s, p95, error rate, and growing charts.
4. **Cancel run** stops the test gracefully. Results collected so far are kept and the run is marked *Cancelled*.
5. Only one run executes at a time (by default); others wait as *Queued*.

> Tell the API team before stress/spike/soak tests on shared environments, and coordinate with anyone else using the environment. Your load affects everyone.

---

## 10. Analysing results: does the API perform well?

Work through these questions in order. The first two decide whether the test itself is trustworthy.

### Question 1: Did the test run correctly?

| Look at | Healthy | If not |
|---------|---------|--------|
| Status | **Completed** | **Error** → read the message (wrong k6 config, auth/login failure, script error). **Cancelled** → partial results only. |
| Error rate during the **first minute** | ~0% | Errors from the very start usually mean a **setup problem**, not a performance problem: wrong IDs (404), expired token (401/403), invalid body (400), wrong expected codes. Fix the scenario and re-run. |
| Requests count | Roughly what you expected | Very few requests → long timeouts or very slow responses. |

### Question 2: Did we generate the load we intended?

Look at the **Throughput & virtual users** chart:

- The VU line should follow your stages (ramp up, hold, ramp down).
- RPS should rise with VUs. If RPS stays flat while VUs rise, the API is **saturated**; see question 5.
- Compare the achieved RPS with your target. If it's much lower even though latency is fine, your think time is too long or you need more VUs.

### Question 3: Are errors acceptable?

- **Error rate KPI** and the **Errors** column per endpoint. Normally **< 1%** under expected load. Under stress tests, errors are expected at some point; *when* they start is the finding.
- **Where** do errors happen? If one endpoint has all the errors, investigate that endpoint.
- **When** do they happen? See the **Error rate** chart:
  - Errors from the start → data/config problem (question 1).
  - Errors start when VUs reach a certain level → **capacity limit**. Note the VU/RPS level: that's your breaking point.
  - Errors appear after a long time in a soak test → resource leak, or token expiry (see [section 13](#13-pitfalls-and-good-practice)).
- **Why?** PerfPilot shows the error *rate*, not the error *type*. To find the cause:
  1. Re-run a **Smoke** test, then check the target API's logs at that time.
  2. Ask the API team to check their logs or monitoring (look for 5xx, timeouts, DB errors).
  3. Advanced: the raw k6 output for every request, including its status code, is on the PerfPilot server at `C:\inetpub\PerfPilot\work\runs\<run-id-without-dashes>\metrics.json`. For example, to count HTTP 500s:
     `(Select-String -Path metrics.json -Pattern '"metric":"http_reqs".*"status":"500"').Count`
     (each request writes several metric lines; matching only `http_reqs` counts each request once).

### Question 4: Is it fast enough?

- Look at the **SLA thresholds** table: every row shows Limit, Actual and PASS/FAIL.
- In the **Endpoints** table, **red cells** are breached endpoint SLAs.
- Judge on **p95/p99**, not Avg (see the [percentile example](#percentiles-explained-with-an-example)).
- Compare **p50 vs p99** for each endpoint:
  - p99 within ~3× of p50 → consistent performance. 👍
  - p99 is 10× p50 or more → **long tail**: most users are fine, but some wait much longer. Common causes are slow DB queries for some data, lock contention, garbage-collection pauses, cold caches and occasional slow downstream calls.
- **Max** far above p99 is usually a one-off (a single timeout or warm-up request). Note it, but don't panic.
- Check the **Latency percentiles** chart to see whether latency was stable or got worse as load increased (question 5).

### Question 5: How does it behave as load grows?

On the **Latency** and **Throughput & virtual users** charts, compare the stages:

- **Healthy scaling**: as VUs go up, RPS goes up proportionally and latency stays roughly flat.
- **Saturation (the "knee")**: past a certain VU level, RPS stops increasing and latency climbs. That VU/RPS level is the **maximum capacity** of the system as configured. Report it.
- **Breaking point**: errors appear, or latency explodes (timeouts).

### Question 6: Is it stable over time? (soak tests)

At constant VUs, latency and error rate should be **flat**. A slow, steady increase over an hour is a red flag: memory leak, growing queue, connection pool exhaustion, database tables growing, log files filling disks.

### Question 7: Is it better or worse than before?

Open **Runs**, filter by scenario, and compare the new run with an earlier good run (your **baseline**) on p95, p99, error rate and RPS. Changes > 10–20% are worth raising with the developers. Always compare runs of the **same scenario** on the **same environment** with the **same load**.

### Final checklist: "the API performs well" if…

- [ ] Smoke test passes with 0% errors
- [ ] Load test at expected peak: **verdict PASSED**, all SLAs green
- [ ] Error rate < 1% (or your agreed value), with no error bursts in the chart
- [ ] p95 and p99 within SLA for every endpoint; p99 not wildly above p50
- [ ] RPS reached the expected level and rose with VUs
- [ ] Latency stable throughout the hold stage (no upward drift)
- [ ] (If run) Stress test: you know the breaking point, and it's comfortably above expected peak (e.g. ≥ 2×)
- [ ] (If run) Soak test: flat latency and errors for the whole duration
- [ ] (If run) Spike test: latency and errors return to normal after the spike

### Sharing results

- **HTML report** button: a single file with the verdict, SLA table, endpoint table and charts. Attach it to the Jira ticket / email; it opens offline.
- **Script** and **k6 summary** downloads are for developers who want the raw details.

When reporting a problem, include the endpoint, the load level (VUs/RPS) where it started, p95/p99, the error rate, the time of the run (so the API team can match it to their logs), and the HTML report.

---

## 11. Common patterns in the charts

| What you see | What it usually means | What to do |
|--------------|-----------------------|------------|
| Latency high in the first 10–30 s, then drops | **Warm-up**: caches empty, connections opening, JIT compilation | Normal. Judge the hold stage, not the start. Add a ramp-up stage if needed. |
| Latency rises together with VUs, RPS flattens | **Saturation**: the system reached its capacity | Note the VU/RPS at the knee; report as capacity limit |
| Error rate jumps at a certain VU level | **Breaking point** (pool exhausted, rate limiting, crashes) | Note the level; ask the team to check logs at that time |
| Steady latency climb at constant VUs (soak) | **Leak / resource exhaustion** | Report with the run time range; dev team checks memory/connections |
| Periodic spikes every few minutes | Scheduled jobs, garbage collection, cache expiry, auto-scaling | Ask the team what runs on that schedule |
| Errors from the very beginning | **Test setup problem** (bad IDs, auth, body, expected codes) | Fix the scenario; run Smoke again |
| Many errors, very low latency | API rejects quickly (401/403/400/404/429) | Check auth, data and expected codes; 429 = rate limited, ask for an allow-list |
| Errors with ~60 s latency | **Timeouts** | The API is overloaded or hanging; report it |
| Low RPS but low latency and no errors | Not enough load: too few VUs or too much think time | Increase VUs or reduce think time |
| Spike test: latency doesn't recover after the spike | System didn't recover (queues backed up, crashed instances) | Report; this is a resilience issue |

---

## 12. How it works in the backend

You don't need this to use PerfPilot, but it helps when interpreting results or discussing them with developers.

### 12.1 Big picture

```
 Your browser ──(HTTP + SignalR)──► PerfPilot server (IIS / ASP.NET Core) ──► SQL Server (scenarios, runs, results)
                                         │
                                         ├─ spec downloads & "Test connection" ─────────► Target API
                                         │
                                         └─ starts k6 process ──(load traffic)─────────► Target API
```

- Your **browser never calls the target API**. All traffic comes from the PerfPilot **server**. That's why target APIs need no CORS changes, and why the PerfPilot server, not your laptop, needs network/VPN access to them.
- **k6** is an open-source load-testing engine. PerfPilot writes a k6 script for your scenario and runs it as a separate process on the server.

### 12.2 What happens when you click Run

1. **Validation.** PerfPilot checks the scenario again: weights = 100%, every path parameter has a value, CSV columns exist, JSON bodies are valid, and VUs and duration are within the safety caps (`MaxVus`, `MaxDurationMinutes`). Production environments need the typed confirmation.
2. **Snapshot.** A copy of the scenario is stored with the run, so editing the scenario later doesn't change past results.
3. **Script generation.** The scenario is turned into a k6 JavaScript file (visible in the *k6 script* tab):
   - **Stages** become k6 `stages` (gradual VU ramps).
   - **SLAs** become k6 `thresholds`, e.g. `'http_req_duration{endpoint:ep1}': ['p(95)<=500']`.
   - **Endpoints** become a list with weights, parameters, body templates and expected codes. Each gets a key (`ep1`, `ep2`, …) used to tag its requests.
   - **The CSV** is parsed on the server and embedded in the script as a k6 `SharedArray`: one read-only copy in memory shared by all VUs, however many there are.
   - **Secrets are not written into the script.** The script reads them from environment variables (`PERFPILOT_BEARER_TOKEN`, `PERFPILOT_API_KEY`, `PERFPILOT_LOGIN_BODY`), which PerfPilot passes only to the k6 process.
4. **Queue.** The run is queued (status *Queued*). A background worker on the server runs one test at a time (configurable).
5. **Execution.** The worker creates a folder `work\runs\<run-id>`, writes `script.js`, and starts:
   `k6 run --out json=metrics.json --summary-export=summary.json script.js`
6. **Live metrics.** k6 writes every request's measurements to `metrics.json` as it goes. PerfPilot reads the file continuously, aggregates the last 2 seconds (VUs, RPS, p95, error rate), and pushes them to your browser over **SignalR** (a live web connection). That's the live KPIs and charts.
7. **Finish.** When k6 ends, PerfPilot reads k6's **summary** (`summary.json`), the authoritative final numbers: count, avg, p50, p90, p95, p99, max, error rate and RPS, overall and per endpoint, plus PASS/FAIL for every threshold. These, the time series for the charts and the raw summary are saved in SQL Server, and the verdict is computed.
8. **Cancel.** "Cancel run" asks k6 (through its local control API) to stop gracefully, so it still writes the summary of what ran. If k6 doesn't stop within 20 s, the process is killed.

### 12.3 What each VU does, step by step

Every virtual user repeats this loop until the load profile says it's no longer needed:

```
 ┌───────────────────────────────────────────────────────────────────────────┐
 │ 1. Pick a random CSV row (if test data exists)                            │
 │ 2. Pick an endpoint at random, weighted (70% weight ≈ 70% of picks)       │
 │ 3. Build the request:                                                     │
 │      path params  → replaced in the URL (URL-encoded)                     │
 │      query params → appended (?a=1&b=2; empty values are skipped)         │
 │      headers      → added                                                 │
 │      body         → placeholders filled ({{csv.x}}, {{random.int}}, …)    │
 │      auth         → bearer token / API key / login token header added     │
 │ 4. Send the request, tagged with the endpoint key                         │
 │ 5. Check: is the status one of the expected codes? (no → counts as error) │
 │ 6. Sleep a random think time between min and max                          │
 └───────────────────────────────────────────────────────────────────────────┘
```

Consequences worth knowing:

- **One request per iteration.** Each loop sends exactly one request to one endpoint. It's a *traffic mix*, not a user journey (login → search → order in sequence).
- **Weights are probabilities.** 70/30 gives *about* 70/30; short tests can deviate slightly.
- **Login flow:**
  - "Once per virtual user": each VU logs in on its first loop and reuses its token afterwards.
  - "Once in setup()": one login before the test, and all VUs share that token.
  - Either way, login requests are excluded from the results and SLAs.
- **Expected status codes** decide what's an error, for both the **error rate** and the **checks**.

### 12.4 Where the numbers come from

| Number on screen | Source |
|------------------|--------|
| Live KPIs and live charts (while running) | PerfPilot's own calculation from k6's per-request output, every 2 s |
| Final KPIs, endpoint table, SLA results | k6's end-of-test summary (authoritative) |
| Charts after the run | PerfPilot's stored time series (bucket size grows with test length so long soak tests stay readable) |
| "Throughput" / RPS in tables | Total requests ÷ total test duration (including ramp-up/down), i.e. an **average** |

Live values and final values can differ slightly. The final values are the ones to report.

---

## 13. Pitfalls and good practice

- **Smoke first, always.** Most "performance problems" in a first run are actually test data or auth problems.
- **Change one thing at a time.** Compare runs that differ only in load, or only in the build, so you know what caused a change.
- **Run each important test 2–3 times.** Shared environments are noisy; one run can be unlucky.
- **The load generator is one machine.** PerfPilot runs on one server. If latency is high *and* the PerfPilot machine's CPU is near 100%, the bottleneck may be PerfPilot itself. Lower the VUs, or ask for a bigger machine.
- **Network matters.** If the PerfPilot server reaches the API over VPN or from another region, network time is included in every response time.
- **Rate limits / WAF.** Gateways may throttle a single IP (`429 Too Many Requests`) or block it. Ask for the PerfPilot server's IP to be allow-listed before big tests.
- **Data side effects.** Create/update/delete endpoints change real data. Agree on test accounts and clean-up.
- **Token expiry in long tests.** Static tokens and logged-in tokens are not refreshed during a run. If a token expires in the middle of a soak test, you'll suddenly see 401 errors. Use a token that lives longer than the test.
- **Production.** Only with explicit approval, at agreed times, starting small.
- **Environment parity.** QA servers are usually smaller than production. Results show *relative* behaviour (regressions, bottlenecks) more reliably than absolute production capacity.
- **Write down the context** of important runs: build/version tested, environment state, anything unusual. Run history keeps the scenario snapshot, the time and who started it.

---

## 14. FAQ

**The verdict is PASSED but some endpoints look slow?**
The verdict only checks the thresholds you defined. Add endpoint SLAs for the endpoints you care about.

**The error rate is 100%.**
Almost always configuration: wrong base URL, expired token, or expected status code set to `200` for an endpoint that returns `201`/`204`. Run a Smoke test and check the endpoint settings.

**Why is my RPS lower than the number of VUs?**
Each VU waits for the response *and* the think time before its next request. With 1–3 s think time, each VU makes well under 1 request per second (see the [RPS formula](#how-vus-think-time-and-rps-relate)).

**Can one CSV row be used by different endpoints at the same time?**
Yes. Rows are shared and read-only; many VUs can use the same row simultaneously.

**I need a different CSV per endpoint.**
Put all the columns in the one scenario CSV ([section 7](#7-test-data-for-multiple-endpoints)). Alternatively, create separate scenarios per endpoint group.

**What does "Cancelled" mean for the results?**
The numbers cover only the part that ran. Don't compare them with full runs.

**The run failed with "A stored secret could not be decrypted".**
The environment's token/key was saved on a different PerfPilot installation. Edit the environment, re-enter the secret and save.

**Can I run the script myself?**
Yes: **k6 script → Download .js**, then `k6 run -e PERFPILOT_BEARER_TOKEN=<token> script.js` (the script header lists the variables it needs).
