# PerfPilot

PerfPilot is an internal web tool that lets QA engineers run API performance tests without writing scripts.
You import an OpenAPI spec, pick endpoints, set a load profile and SLA thresholds, and run the test with
[k6](https://k6.io). Results stream in live and are saved for later comparison and export.

- **Environments**: target systems with base URL, optional OpenAPI URL, auth (none, bearer token, API key or login flow), a production flag and a server-side **Test connection** check. Secrets are encrypted at rest and never sent back to the browser.
- **OpenAPI import**: fetch from a URL (optional auth header, timeout, ignore-TLS option) or upload a `.json`/`.yaml` file (OpenAPI 3.x or Swagger 2.0). Every import is stored as a new version, and endpoints are listed by tag with parameters, body schema and a generated sample body.
- **Scenario builder**:
  - **Traffic**: weighted endpoint mix, path/query/header values (static or from a CSV column), and JSON bodies with `{{random.int}}`, `{{random.guid}}`, `{{random.email}}` and `{{csv.column}}` placeholders.
  - **Checks and load**: expected status codes, and Smoke/Load/Stress/Spike/Soak presets with editable stages and think time.
  - **SLAs**: global and per-endpoint p95, p99, error rate and minimum RPS thresholds.
  - **Housekeeping**: save and clone scenarios.
- **k6 generation and execution**: a read-only script preview you can download, background execution with a queue, live metrics over SignalR every 2 s, graceful cancellation, safety caps, production confirmation and run attribution.
- **Results**: pass/fail verdict, per-endpoint table with breaches highlighted, charts over time (latency percentiles, RPS and VUs, error rate), run history filters, a self-contained HTML report, and downloads of the exact script and the raw k6 summary.

> **New to performance testing?** Read the [User Guide](docs/USER-GUIDE.md), also available in the app under **Help** (top bar). It covers the terms, building scenarios,
> preparing test data for multiple endpoints, reading results, and what happens in the backend.

---

## Contents

1. [Prerequisites](#prerequisites)
2. [Setup](#setup)
3. [Running](#running)
4. [Using PerfPilot](#using-perfpilot)
5. [Why target APIs need no CORS changes](#why-target-apis-need-no-cors-changes)
6. [Network, VPN and firewall requirements](#network-vpn-and-firewall-requirements)
7. [Configuration](#configuration)
8. [Architecture](#architecture)
9. [Tests](#tests)
10. [Assumptions](#assumptions)
11. [Extending PerfPilot](#extending-perfpilot)
12. [Troubleshooting](#troubleshooting)

---

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0 | `dotnet --list-sdks` |
| Node.js | 20.19+ (tested with 24) | Only needed to build the UI |
| SQL Server | 2019+ or LocalDB | Default connection string uses `localhost` with Windows auth |
| k6 | v1.0+ (tested with v2.3.0) | Must run on the PerfPilot server |

### Install k6

k6 must be installed on the machine that runs the PerfPilot backend. Browsers and QA machines don't need it.

```powershell
# Windows (any one of these)
winget install k6 --source winget
choco install k6
# or download the MSI/zip from https://github.com/grafana/k6/releases

# macOS
brew install k6

# Debian/Ubuntu: see https://grafana.com/docs/k6/latest/set-up/install-k6/
```

Check it with `k6 version`. If k6 isn't on `PATH`, set `K6:BinaryPath` to the full path of the executable (see [Configuration](#configuration)).
The top bar of the UI shows a warning when the configured k6 binary can't be started.

### SQL Server

Any SQL Server instance works. Examples:

- **Local default instance with Windows auth** (default): `Server=localhost;Database=PerfPilot;Trusted_Connection=True;TrustServerCertificate=True`
- **LocalDB**: `Server=(localdb)\MSSQLLocalDB;Database=PerfPilot;Trusted_Connection=True`
- **SQL auth**: `Server=sql01;Database=PerfPilot;User Id=perfpilot;Password=...;TrustServerCertificate=True`

The database and schema are created on first start (EF Core migrations run automatically; see `Database:ApplyMigrationsOnStartup`).

---

## Setup

```powershell
git clone <repo-url> PerfPilot
cd PerfPilot

# 1. Restore the local dotnet-ef tool (only needed if you create migrations)
dotnet tool restore

# 2. Build the Angular UI into src/PerfPilot.Api/wwwroot
cd src/perfpilot-web
npm ci
npm run build
cd ../..

# 3. Build and test the backend
dotnet build
dotnet test
```

To apply the database schema manually instead of on startup:

```powershell
dotnet ef database update -p src/PerfPilot.Infrastructure -s src/PerfPilot.Api
```

---

## Running

### Single process (normal use)

The API serves the built Angular app from `wwwroot`, so the UI and API share one origin.

```powershell
dotnet run --project src/PerfPilot.Api
# open http://localhost:5084
```

If k6 isn't on `PATH`, point to it first:

```powershell
$env:K6__BinaryPath = "C:\Tools\k6\k6.exe"
dotnet run --project src/PerfPilot.Api
```

To publish for a server:

```powershell
cd src/perfpilot-web; npm ci; npm run build; cd ../..
dotnet publish src/PerfPilot.Api -c Release -o publish
# copy "publish" to the server, set ConnectionStrings__PerfPilot and K6__BinaryPath, run PerfPilot.Api.exe
```

### As a Windows service (recommended on Windows 10/11)

Full step-by-step instructions (any host machine, updates, rollback, moving hosts, IIS, HTTPS): **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)**.

PerfPilot can run as a Windows service on its built-in web server (Kestrel), with no IIS needed.

> **Don't host PerfPilot in IIS on a Windows client edition.** IIS on Windows 10/11 limits concurrent requests to **3 (Home)** or **10 (Pro)** and queues the rest.
> Every browser tab on a Runs page keeps a live-update connection open, so a few users fill the limit and the whole UI hangs until a connection closes.
> IIS on Windows Server has no such limit.

In an admin PowerShell:

```powershell
cd src\perfpilot-web; npm ci; npm run build; cd ..\..
dotnet publish src\PerfPilot.Api -c Release -o C:\PerfPilot

# Settings go in C:\PerfPilot\appsettings.Production.json (connection string, K6:BinaryPath, DataProtection:Store)
icacls C:\PerfPilot /grant "NT AUTHORITY\LocalService:(OI)(CI)M"
icacls C:\Tools\k6 /grant "NT AUTHORITY\LocalService:(OI)(CI)RX"

sc.exe create PerfPilot binPath= "C:\PerfPilot\PerfPilot.Api.exe --urls http://0.0.0.0:8080" start= delayed-auto obj= "NT AUTHORITY\LocalService" DisplayName= "PerfPilot"
sc.exe failure PerfPilot reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start PerfPilot
```

To update: `sc.exe stop PerfPilot`, publish again to `C:\PerfPilot`, then `sc.exe start PerfPilot`. Logs are in `C:\PerfPilot\logs`.

### UI development (hot reload)

```powershell
# terminal 1 – API on http://localhost:5084
dotnet run --project src/PerfPilot.Api

# terminal 2 – Angular dev server on http://localhost:4200
cd src/perfpilot-web
npm start
```

`ng serve` forwards `/api` and `/hubs` (including the SignalR WebSocket) to the API through `proxy.conf.json`.
The browser only ever talks to `localhost:4200`, so this setup needs no CORS either.

---

## Using PerfPilot

1. **Set your name.** The first visit asks for a display name. It's recorded on every run you start.
2. **Create an environment** (Environments → New). Enter the base URL, the optional OpenAPI URL and auth settings, then click **Test connection**. The server reports DNS, TLS, timeout and 401/403 problems with a clear message.
3. **Import the spec** (Environments → Endpoints). Fetch it from the URL or upload a file, then browse the endpoints by tag.
4. **Build a scenario** (Scenarios → New, or "New scenario from this spec"):
   - **Endpoints**: pick endpoints, set the weights (they must total 100%; "Distribute evenly" helps), parameter values, request body and expected status codes, and optional per-endpoint SLAs.
   - **Load profile & SLA**: choose a preset or edit stages, set think time and global SLAs.
   - **Test data**: paste CSV (or cells copied from a spreadsheet). Use a column as a parameter source or as `{{csv.column}}`.
   - **k6 script**: preview, copy or download the generated script.
5. **Run.** You land on the run page with live VUs, RPS, p95 and error rate, plus charts. **Cancel run** stops k6 gracefully and keeps partial results. Production environments ask you to type the environment name first.
6. **Review.** The page shows the verdict banner, SLA table, per-endpoint table (breached cells highlighted) and charts. **HTML report** downloads a self-contained file you can attach to a ticket. **Runs** lists the history, filterable by scenario, environment and status.

### Running a downloaded script yourself

Downloaded scripts never contain secrets. They read them from environment variables, and the script header lists the ones it needs:

| Variable | Used when |
|----------|-----------|
| `PERFPILOT_BEARER_TOKEN` | Auth = Bearer token |
| `PERFPILOT_API_KEY` | Auth = API key |
| `PERFPILOT_LOGIN_BODY` | Auth = Login flow (the body for the token endpoint) |
| `PERFPILOT_LOGIN_HEADERS` | Auth = Login flow, optional: JSON object of login request headers, e.g. `{"Content-Type":"application/json","X-Tenant-Id":"acme"}` (default: JSON content type only) |
| `PERFPILOT_BASE_URL` | Optional: point the script at a different base URL |

```powershell
k6 run -e PERFPILOT_BEARER_TOKEN=<token> pet-store-load.k6.js
```

---

## Why target APIs need no CORS changes

CORS is a browser rule: it only limits JavaScript in a web page that calls a different origin.
PerfPilot is built so that **the browser never calls a target API or downloads a spec**:

| Action | Who makes the outbound call |
|--------|-----------------------------|
| Test connection | PerfPilot backend (`HttpClient`) |
| Fetch OpenAPI spec from URL | PerfPilot backend (`HttpClient`) |
| Login flow during a test | k6 process started by the backend |
| Load test traffic | k6 process started by the backend |

The Angular UI talks only to the PerfPilot backend on its own origin (`/api` and `/hubs`).
Server-to-server HTTP isn't subject to CORS, so **target APIs need no CORS configuration, proxy or header change**,
and PerfPilot itself doesn't enable CORS.

---

## Network, VPN and firewall requirements

Since every outbound call comes from the PerfPilot server, **the server** (not the QA engineer's laptop) needs network access:

- **Reach the target hosts.** The server must resolve and connect to every environment's base URL, OpenAPI URL and login endpoint, usually on HTTPS/443 or the API's port.
  - If targets are only reachable over VPN, the server needs that VPN or route, or must run inside the same network.
  - Open the firewall or security group from the server to the targets, and allow the server's egress IP on target-side allowlists, WAFs and API gateways.
- **Expect rate limiting.** Load tests come from one IP. WAFs, rate limiters and DDoS protection may throttle or block it, so coordinate an allowlist entry with the target team before larger tests.
- **Proxies.** If outbound traffic goes through a corporate proxy, set `HTTPS_PROXY`/`HTTP_PROXY`/`NO_PROXY` for the PerfPilot process. Both .NET's `HttpClient` and k6 honor them.
- **TLS.** Internal staging certificates signed by a private CA should be trusted by the server's OS certificate store. For a quick test, "Ignore TLS errors" can be enabled per environment (connection test and k6) or per spec import.
- **Users only need to reach the PerfPilot server** (HTTP on the configured port, plus WebSockets for live updates).
- **Local ports.** k6 binds its REST API to a random `127.0.0.1` port for graceful cancellation; nothing listens externally.
- **Load generator sizing.** The server generates the load, so size its CPU and network for your peak VUs. The `Safety:MaxVus` and `Safety:MaxDurationMinutes` caps protect both the server and the targets.

---

## Configuration

All settings are in `src/PerfPilot.Api/appsettings.json` and can be overridden with environment variables (`Section__Key`).

| Setting | Default | Description |
|---------|---------|-------------|
| `ConnectionStrings:PerfPilot` | localhost, Windows auth | SQL Server connection string |
| `K6:BinaryPath` | `k6` | k6 executable (`k6` = from PATH; relative paths resolve against the app folder) |
| `K6:WorkDirectory` | `work` | Scripts, raw metrics, summaries and k6 logs, one folder per run (`work/runs/<id>`) |
| `K6:MaxConcurrentRuns` | `1` | Runs executed in parallel; others wait in the queue |
| `K6:LiveUpdateIntervalSeconds` | `2` | Live metric push interval |
| `K6:GracefulStopTimeoutSeconds` | `20` | Wait after a graceful stop before killing k6 |
| `K6:DeleteRawOutputAfterRun` | `false` | Delete `metrics.json` (can be large) after results are saved |
| `K6:InstanceName` | machine name + app folder | Name recorded on the runs this instance executes. Only needs setting if two instances share a machine *and* a folder |
| `Safety:MaxVus` | `500` | Highest stage target allowed |
| `Safety:MaxDurationMinutes` | `120` | Longest total test duration allowed |
| `SpecImport:DefaultTimeoutSeconds` | `30` | Default spec download timeout |
| `SpecImport:MaxTimeoutSeconds` | `300` | Upper bound for the per-import timeout |
| `SpecImport:MaxSpecSizeMb` | `25` | Maximum spec size (download or upload) |
| `ConnectionTest:TimeoutSeconds` | `15` | Timeout per check in "Test connection" |
| `Identity:Mode` | `Header` | `Header`: the UI asks for a display name. `Windows`: Negotiate (Kerberos/NTLM) authentication |
| `DataProtection:Store` | `FileSystem` | Where the key ring that encrypts stored secrets lives. `FileSystem`: the `KeysDirectory` folder. `Database`: the `DataProtectionKeys` table, shared by every instance using the database |
| `DataProtection:KeysDirectory` | `keys` | Key ring folder when `Store` is `FileSystem` |
| `Database:ApplyMigrationsOnStartup` | `true` | Apply EF Core migrations at startup |
| `Serilog` | Console plus rolling file `logs/` | Structured logging configuration |

> **Key ring and secrets.** Stored secrets (tokens, API keys, login bodies, login headers) can only be decrypted with the key ring that encrypted them.
> - With `Store = FileSystem`, back up the `keys` folder. On Windows its keys are also protected with DPAPI for the account running PerfPilot, so only that account can use them.
> - With `Store = Database`, the keys are kept unencrypted in the `DataProtectionKeys` table. Anyone who can read the database can decrypt the stored secrets, so restrict database access.
> - If the key ring is lost or changed, re-enter the environment secrets.

### Several instances sharing one database

You can run more than one PerfPilot instance against the same database, for example the IIS site and a developer's `dotnet run`:

1. Point both at the same `ConnectionStrings:PerfPilot` and set `DataProtection:Store` to `Database` on both, so either one can decrypt secrets saved by the other.
2. Each run is executed by the instance it was started on. Instances claim runs atomically and record their name on the run (`RunnerInstance`), so a run never executes twice.
3. **Cancel** works from either instance: the request goes through the database, and the executing instance stops k6 within about 3 seconds.
4. On startup, an instance only marks *its own* interrupted runs as failed.
5. Live charts stream from the executing instance. Pages opened on the other instance refresh every 5–10 seconds instead.

Each instance has its own `K6:MaxConcurrentRuns` limit and generates load from its own machine.

---

## Architecture

```
src/
  PerfPilot.Api/             ASP.NET Core host: controllers, SignalR hub (/hubs/runs), ProblemDetails, Serilog, static UI
  PerfPilot.Core/            Domain model, services, validation, k6 script generator, k6 summary parser, HTML report
  PerfPilot.Infrastructure/  EF Core (SQL Server) + migrations, Data Protection, HttpClient (connection test, spec fetch,
                             login), OpenAPI parser (Microsoft.OpenApi.Readers), k6 runner, queue and background worker
  perfpilot-web/             Angular 21 standalone components, PrimeNG 21 (MIT), Chart.js, @microsoft/signalr
tests/
  PerfPilot.Tests/           xUnit: OpenAPI mapping, script generator snapshots, summary parser, aggregator, validator, …
```

**Run pipeline**
1. `POST /api/runs` validates the scenario (including caps and production confirmation), generates the script, stores a snapshot and queues the run.
2. `RunWorker` (a hosted service) takes runs off the in-process channel, up to `K6:MaxConcurrentRuns` at a time.
3. `RunExecutor` writes `script.js`, starts `k6 run --out json=metrics.json --summary-export=summary.json --address 127.0.0.1:<port>`, and passes secrets as environment variables.
4. The JSON output is tailed and aggregated, and every 2 s the backend pushes VUs, RPS, p95 and error rate to the run's SignalR group.
5. When k6 exits, the summary export is parsed into overall and per-endpoint metrics and threshold results. The time series, verdict and raw summary are saved.
6. Cancelling calls k6's REST API (`PATCH /v1/status stopped=true`) so k6 still writes its summary. If k6 doesn't stop within the timeout, the process tree is killed.

**How per-endpoint SLAs work in k6**
Every request is tagged `endpoint:epN`, `name:"METHOD /path"` and `kind:test`, while login requests are tagged `kind:auth`.
Thresholds are declared on submetrics (`http_req_duration{endpoint:ep1}`, `http_req_failed{kind:test}`, …).
Submetrics without an SLA get an always-true threshold so k6 still reports them in the summary.
Each request uses `responseCallback: http.expectedStatuses(...)`, so an unexpected status counts as failed in `http_req_failed`.

---

## Tests

```powershell
dotnet test
```

- `OpenApiSpecParserTests`: OpenAPI 3.0 JSON, Swagger 2.0 YAML and 3.1 fallback; tags, merged path/operation parameters, enums/defaults, JSON body selection, sample generation from examples, recursion safety and invalid documents.
- `K6ScriptGeneratorTests`: snapshot tests (`tests/PerfPilot.Tests/Snapshots/*.verified.js`) plus assertions for each auth type, the threshold plan, always-declared submetrics and deterministic output.
- `K6SummaryParserTests`: overall and per-endpoint metrics, check counts, threshold flags (`true` = failed), fallbacks and the result mapper verdict.
- Utility tests: duration and CSV parsing, JSONPath, scenario validation and caps, live metrics aggregation, the output tailer and the HTML report.

**Updating snapshots.** When generator output changes on purpose, the test writes `*.received.js` next to the snapshot. Review it, then rename it to `*.verified.js`, or run with `UPDATE_SNAPSHOTS=1`.

---

## Assumptions

1. **Identity.** No full user management exists. In `Header` mode (default) the UI asks for a display name, which is sent as `X-PerfPilot-User` and recorded as "started by". Set `Identity:Mode=Windows` to use Windows authentication (Negotiate) for verified identities.
2. **Weights** are percentages and must total 100% (±0.5). The builder distributes them evenly when endpoints are added or removed.
3. **Failures.** A request is "failed" when its status isn't one of the endpoint's expected codes. The error-rate SLA and the error-rate charts use this definition.
4. **Login traffic is excluded from metrics.** Global SLAs and overall metrics cover only scenario traffic (`kind:test`), not login requests.
5. **Login flow timing.** k6 doesn't allow HTTP calls in its init context. "Once per VU" therefore logs in on each VU's first iteration and reuses the token. "Shared" logs in once in `setup()` and all VUs share the token. The token goes in a configurable header with a configurable prefix (default `Authorization: Bearer <token>`).
6. **JSONPath** supports the common subset `$.a.b`, `$.items[0].token` and `$['key']`. Filters and wildcards aren't supported.
7. **Secrets** (bearer token, API key, login body) are encrypted with ASP.NET Core Data Protection. The API only reports whether a secret is set, with a fixed mask. Secrets reach k6 as process environment variables and never appear in generated or downloaded scripts.
8. **TLS.** The environment's "Ignore TLS errors" applies to Test connection and k6 (`insecureSkipTLSVerify`). Spec import has its own toggle. Both are off by default.
9. **OpenAPI.** `Microsoft.OpenApi.Readers` 1.x reads OpenAPI 3.0 and Swagger 2.0. OpenAPI 3.1 documents are read with 3.0 rules and a warning; most work, but 3.1-only constructs are ignored. `TRACE` operations are skipped.
10. **Spec versions.** Each import creates a new version, and the endpoint browser defaults to the latest. Scenarios keep their own copy of endpoint settings and aren't changed when a newer spec is imported.
11. **CSV data.** Each scenario has one dataset with a header row (comma or tab separated), up to 100,000 rows. It's embedded in the script as a k6 `SharedArray`, and every iteration picks one random row, so all CSV values in a request come from the same row.
12. **Placeholders.**
    - `{{random.int}}` is a number from 1 to 1,000,000.
    - `{{random.guid}}` (alias `{{random.uuid}}`) is a v4 UUID.
    - `{{random.email}}` is `perfpilot.<random>@example.com`.
    - In JSON bodies, a placeholder may be used unquoted for numbers.
13. **Load presets.** Each preset's stages stay editable:
    - Smoke: 1 VU for 1 m.
    - Load: ramp to 20 VUs over 2 m, hold 5 m, ramp down 1 m.
    - Stress: 50 VUs, then 100 VUs.
    - Spike: from 5 to 100 VUs and back.
    - Soak: 30 VUs for 1 h.
14. **Safety caps** are enforced when a scenario is saved and again when a run starts, in case the caps were lowered in between.
15. **Production confirmation** requires typing the environment name exactly (case-sensitive). The server enforces it, not just the UI.
16. **Queue.** The queue is in-process. On restart, runs still `Queued` are queued again and runs that were `Running` are marked `Failed` ("interrupted").
17. **Verdict.** A completed run passes when every SLA threshold passes, or when none are defined. k6 exit code 99 (thresholds failed) counts as a completed run with a failed verdict. Other non-zero codes mark the run `Failed` with k6's error output.
18. **Metrics sources.** Final metrics (count, avg, p50, p90, p95, p99, max, error rate, RPS) come from k6's summary export. Live metrics and the stored time series are computed by PerfPilot from the k6 JSON stream. Time buckets are `max(2 s, planned duration / 300)`, so long soak tests stay compact, and long runs use reservoir sampling to keep percentile estimates accurate with bounded memory.
19. **Charts** use Chart.js through PrimeNG's `p-chart`. The HTML report is fully self-contained (inline CSS and SVG charts, no scripts), so it opens offline.
20. **UI library.** The UI uses **Angular 21.2** with **PrimeNG 21.1.10** (MIT, pinned), not Angular 22. PrimeNG 22 is commercially licensed (PrimeUI) and needs a license key; PrimeNG 21's community releases are free, and they require Angular 21.
21. **Schema setup.** The database schema is applied automatically at startup (EF Core migrations). Turn this off with `Database:ApplyMigrationsOnStartup=false` where DBAs manage schema changes.
22. **Run files.** Raw k6 output, scripts and logs stay in `work/runs/<run id>` for troubleshooting. Use `K6:DeleteRawOutputAfterRun` to drop the large `metrics.json` after results are saved.

---

## Extending PerfPilot

The design leaves room for the features that are out of scope for now:

- **Baseline comparison and regression detection.** Per-endpoint metrics and threshold results are stored relationally (`RunEndpointResults`, `RunThresholdResults`), keyed by run, scenario and environment, so a comparison service can query two runs directly. Each run also keeps a scenario snapshot.
- **CI/CD integration.** Everything the UI does goes through the REST API. A pipeline can call `POST /api/runs`, poll `GET /api/runs/{id}` and fail on `thresholdsPassed == false`, or download the generated script and run k6 itself.
- **Application Insights correlation.** All k6 traffic carries `User-Agent: PerfPilot/1.0 (k6)`. A per-run correlation header can be added in `K6ScriptGenerator` together with the run id, which `RunExecutor` can pass as an environment variable.
- **Distributed load generation.** Queueing (`IRunQueue`), cancellation (`IRunCanceller`), notifications (`IRunNotifier`) and execution (`RunExecutor`) are separate abstractions. A remote-agent implementation can replace the in-process worker without changing the API or UI.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Banner "k6 is not available on the server" | Install k6 or set `K6:BinaryPath` / `K6__BinaryPath` to the full path |
| Test connection shows **Dns** | The PerfPilot server can't resolve the host: check VPN/DNS on the server |
| Test connection shows **Tls** | The server doesn't trust the certificate: install the CA, or enable "Ignore TLS errors" for staging |
| Test connection shows **Unauthorized/Forbidden** | Check the environment's auth settings; for a login flow, check the token JSONPath |
| Run fails with "the script threw an exception" | Open the run's `work/runs/<id>/k6-output.log`; the error is also shown on the run page |
| "A stored secret could not be decrypted" | The Data Protection key ring changed (new machine or account): re-enter the environment secrets |
| UI shows a blank page | Build the UI (`cd src/perfpilot-web && npm run build`) so `src/PerfPilot.Api/wwwroot` exists |
