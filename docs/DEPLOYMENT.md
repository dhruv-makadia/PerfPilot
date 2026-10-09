# PerfPilot Deployment Guide

How to build PerfPilot, install it on a host machine, keep it updated, and move it to another machine.

PerfPilot can be hosted on **any Windows machine** that can reach your SQL Server and the APIs you want to test.
All data, including the keys that encrypt stored secrets, lives in the shared database, so the host machine holds no state that matters.
You can move PerfPilot to another machine at any time without losing scenarios, run history or saved tokens.

---

## Contents

1. [How a deployment fits together](#1-how-a-deployment-fits-together)
2. [Choose a host machine](#2-choose-a-host-machine)
3. [Hosting options](#3-hosting-options)
4. [Build a release package](#4-build-a-release-package)
5. [Prepare the host machine](#5-prepare-the-host-machine)
6. [Configure](#6-configure)
7. [Install as a Windows service](#7-install-as-a-windows-service)
8. [Verify the deployment](#8-verify-the-deployment)
9. [Update to a new version](#9-update-to-a-new-version)
10. [Move PerfPilot to another machine](#10-move-perfpilot-to-another-machine)
11. [Several instances and local development](#11-several-instances-and-local-development)
12. [Alternative: IIS on Windows Server](#12-alternative-iis-on-windows-server)
13. [Optional: HTTPS and Windows sign-in](#13-optional-https-and-windows-sign-in)
14. [Uninstall](#14-uninstall)
15. [Troubleshooting](#15-troubleshooting)

---

## 1. How a deployment fits together

```
  QA users' browsers                Host machine (any Windows PC/server)              Shared SQL Server
 ────────────────────   HTTP    ───────────────────────────────────────────   SQL   ──────────────────────────
  http://<host>:8080   ───────►  PerfPilot (Windows service, port 8080)     ───────►  PerfPilot database
                                   └─ starts k6 ──── load traffic ───────────┐          scenarios, runs, results,
                                                                             │          encryption keys
                                                                             ▼
                                                                    APIs under test
```

- **Browsers** only talk to PerfPilot on the host. They never call the APIs under test, so those APIs need no CORS changes.
- **The host machine** runs PerfPilot and k6. All test traffic and Test-connection calls come from this machine.
- **The database** (currently `172.16.10.6\SQL2019`, database `PerfPilot`) stores everything: environments, encrypted secrets and their key ring, scenarios and runs.
- The only files on the host are the application, its settings file, logs and raw k6 output. None of them are needed to keep your data.

---

## 2. Choose a host machine

| Requirement | Why |
|-------------|-----|
| Windows 10/11 (Home or Pro) or Windows Server 2019+, 64-bit | The steps in this guide are for Windows |
| Always on, not sleeping, ideally wired network | If the host sleeps or shuts down, the site is unavailable and running tests are cancelled |
| A fixed name or IP address | Users bookmark it. Use the computer name (`http://PCNAME:8080`) or ask IT for a DHCP reservation or DNS name |
| Network access to the SQL Server | For `172.16.10.6\SQL2019`: TCP to the instance's port, plus UDP 1434 (SQL Browser) to find the named instance |
| Network access to every API you test | Same VPN, routes and firewall openings a tester would need, because the load comes from this machine |
| Reachable by users on the chosen port (default 8080) | Inbound firewall rule (step 5.4) |
| CPU and network sized for your largest test | k6 runs on this machine. A few hundred VUs need several cores; watch CPU during big tests |
| Admin rights for the installation | Installing the runtime, k6 and the service |

**Software needed on the host:** ASP.NET Core Runtime 10 (not needed if you build a self-contained package, see 4.2) and k6. No SDK, Node.js or source code is needed on the host.

> **Rate limits and allow-lists.** The APIs under test see all load coming from the host's IP address. If a gateway or WAF limits per IP, ask for the host's IP to be allow-listed.

---

## 3. Hosting options

| Option | Use when | Notes |
|--------|----------|-------|
| **Windows service** (recommended) | Any Windows 10/11 or Windows Server machine | PerfPilot runs on its built-in web server (Kestrel). No request limit, starts with Windows, restarts itself on failure. **This guide's main path.** |
| IIS | Windows **Server** only, if your organization standardizes on IIS | See [section 12](#12-alternative-iis-on-windows-server) |
| `dotnet run` | Development only | See [section 11](#11-several-instances-and-local-development) |

> **Don't use IIS on Windows 10/11.** IIS on client editions handles only **3 (Home)** or **10 (Pro)** requests at a time.
> Every browser tab on a Runs page keeps a live-update connection open, so a few users fill the limit and the whole UI hangs until a connection closes.
> The Windows service has no such limit.

---

## 4. Build a release package

Do this on a **build machine** that has the source code, the .NET 10 SDK and Node.js 20.19+ (e.g. your development PC).

### 4.1 Framework-dependent package (smaller; host needs ASP.NET Core Runtime 10)

```powershell
cd D:\Repos\PerfPilot

# 1. Build the web UI into the API's wwwroot folder
cd src\perfpilot-web
npm ci
npm run build
cd ..\..

# 2. Optional: run the tests
dotnet test

# 3. Publish
dotnet publish src\PerfPilot.Api -c Release -o out\PerfPilot

# 4. Zip it for copying to the host
Compress-Archive -Path out\PerfPilot\* -DestinationPath out\PerfPilot-$(Get-Date -Format yyyyMMdd-HHmm).zip -Force
```

### 4.2 Self-contained package (larger; host needs no .NET runtime)

Use this if you don't want to install anything .NET on the host:

```powershell
dotnet publish src\PerfPilot.Api -c Release -r win-x64 --self-contained true -o out\PerfPilot
```

Then zip it as in step 4 above. Build the UI first (step 1) in both cases.

**What's in the package:** `PerfPilot.Api.exe` with its DLLs, the web UI (`wwwroot`), `appsettings.json` (defaults) and `web.config` (used only by IIS).
**What's not in it:** `appsettings.Production.json`, which holds your connection string and machine-specific settings. You create that once on each host (section 6), and updates never overwrite it.

---

## 5. Prepare the host machine

Run everything in an **administrator** PowerShell on the host.

### 5.1 ASP.NET Core Runtime 10

Skip this for a self-contained package (4.2).

Download **"ASP.NET Core Runtime 10.0.x – Windows x64 Installer"** (or the "Hosting Bundle", which includes it) from
https://dotnet.microsoft.com/download/dotnet/10.0 and install it. Check:

```powershell
dotnet --list-runtimes    # must list Microsoft.AspNetCore.App 10.0.x
```

### 5.2 k6

```powershell
# Downloads the official k6 installer from k6's own server and installs it for all users
$ProgressPreference = 'SilentlyContinue'   # much faster downloads in Windows PowerShell
$msi = "$env:TEMP\k6-latest-amd64.msi"
Invoke-WebRequest https://dl.k6.io/msi/k6-latest-amd64.msi -OutFile $msi
Start-Process msiexec.exe -ArgumentList '/i', $msi, '/qn' -Wait
& "C:\Program Files\k6\k6.exe" version
```

If downloads are blocked on the host, copy `k6.exe` from another machine into a folder such as `C:\Tools\k6` and use that path in section 6.

### 5.3 Check the database connection

```powershell
$cs = 'Server=172.16.10.6\SQL2019;Database=PerfPilot;User Id=<sql user>;Password=<sql password>;TrustServerCertificate=True;Encrypt=True'
$c = New-Object System.Data.SqlClient.SqlConnection $cs
$c.Open(); "Connected: SQL Server $($c.ServerVersion)"; $c.Close()
```

If this fails, ask IT to open the route or firewall from the host to the SQL Server (the instance's TCP port and UDP 1434). Or use the instance's fixed port in the connection string: `Server=172.16.10.6,<port>`.

The SQL login needs **db_owner** on the `PerfPilot` database. PerfPilot applies database updates (migrations) automatically when a new version starts.

### 5.4 Firewall

Allow users on your network to reach port 8080:

```powershell
New-NetFirewallRule -DisplayName "PerfPilot (8080)" -Direction Inbound -Protocol TCP -LocalPort 8080 `
  -Action Allow -Profile Domain,Private,Public -RemoteAddress LocalSubnet
```

`LocalSubnet` only allows machines on the host's own subnet. If users are on other subnets or VPN, replace it with their address ranges, e.g. `-RemoteAddress 192.168.0.0/16`, or ask IT.

---

## 6. Configure

### 6.1 Copy the package

```powershell
New-Item -ItemType Directory -Force C:\PerfPilot | Out-Null
Expand-Archive -Path <path>\PerfPilot-<version>.zip -DestinationPath C:\PerfPilot -Force
```

### 6.2 Create `C:\PerfPilot\appsettings.Production.json`

```powershell
@'
{
  "ConnectionStrings": {
    "PerfPilot": "Server=172.16.10.6\\SQL2019;Database=PerfPilot;User Id=<sql user>;Password=<sql password>;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True"
  },
  "DataProtection": { "Store": "Database" },
  "K6": { "BinaryPath": "C:\\Program Files\\k6\\k6.exe" }
}
'@ | Set-Content -Encoding utf8 C:\PerfPilot\appsettings.Production.json
```

In JSON, every backslash is written twice (`SQL2019` is preceded by `\\`).

**Keep `DataProtection:Store` set to `Database`.** That's what lets any host, and local development, decrypt the tokens, API keys and login details saved in environments. Keys stored there are unencrypted, so anyone who can read the database can decrypt those secrets. Only give the SQL login to people who should have that access.

### 6.3 Other settings you may want

Add them to the same file. Defaults are in `appsettings.json`.

| Setting | Default | When to change |
|---------|---------|----------------|
| `Safety:MaxVus` | `500` | Highest number of virtual users a test may use. Lower it to protect a small host or fragile targets |
| `Safety:MaxDurationMinutes` | `120` | Longest test allowed |
| `K6:MaxConcurrentRuns` | `1` | Tests this host runs at the same time; others wait in a queue |
| `K6:DeleteRawOutputAfterRun` | `false` | Set `true` if long tests fill the disk with `work\runs\…\metrics.json` |
| `Identity:Mode` | `Header` | `Windows` to identify users by Windows sign-in (section 13) |
| `Serilog` | logs to `logs\` | Log levels and retention |

Example:

```json
{
  "ConnectionStrings": { "PerfPilot": "…" },
  "DataProtection": { "Store": "Database" },
  "K6": { "BinaryPath": "C:\\Program Files\\k6\\k6.exe", "MaxConcurrentRuns": 1 },
  "Safety": { "MaxVus": 200, "MaxDurationMinutes": 90 }
}
```

### 6.4 Permissions for the service account

The service runs as the built-in low-privilege **Local Service** account. It needs to write `logs` and `work` under the app folder, and to run k6:

```powershell
icacls C:\PerfPilot /grant "NT AUTHORITY\LocalService:(OI)(CI)M"
# Only if k6 is in a custom folder (C:\Program Files is already readable):
icacls C:\Tools\k6 /grant "NT AUTHORITY\LocalService:(OI)(CI)RX"
```

---

## 7. Install as a Windows service

```powershell
sc.exe create PerfPilot binPath= "C:\PerfPilot\PerfPilot.Api.exe --urls http://0.0.0.0:8080" start= delayed-auto obj= "NT AUTHORITY\LocalService" DisplayName= "PerfPilot"
sc.exe description PerfPilot "PerfPilot API performance testing (web UI on port 8080)"
sc.exe failure PerfPilot reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start PerfPilot
```

- `sc.exe` needs the space after each `=`, as written.
- `--urls http://0.0.0.0:8080` listens on all network interfaces on port 8080. To use another port, change it here and in the firewall rule.
- `start= delayed-auto` starts PerfPilot automatically shortly after Windows boots.
- The failure actions restart the service a minute after a crash.
- On first start PerfPilot applies any pending database updates. That can take a few seconds.

Manage it later with `sc.exe stop|start|query PerfPilot`, or in **Services** (`services.msc`).

---

## 8. Verify the deployment

On the host:

```powershell
sc.exe query PerfPilot                                   # STATE : 4 RUNNING
Invoke-RestMethod http://localhost:8080/api/config | Select-Object -ExpandProperty k6
#   available : True   ← k6 found
Get-Content C:\PerfPilot\logs\perfpilot-*.log -Tail 20   # "Database migrations applied", "Now listening on: http://0.0.0.0:8080"
```

From another computer:

1. Open `http://<host name or IP>:8080`. The page loads, and there's no yellow "k6 is not available" banner.
2. **Environments** → open an environment → **Test connection**. Every check should be green.
3. **Scenarios** → run a **Smoke** scenario. Live numbers update every few seconds and the run finishes with a verdict.
4. **Help** opens the user guide.

---

## 9. Update to a new version

1. Build a new package on the build machine (section 4).
2. On the host (admin PowerShell):

```powershell
sc.exe stop PerfPilot
Start-Sleep -Seconds 5

# Keep the current version for rollback (skip run data and logs)
robocopy C:\PerfPilot C:\PerfPilot.previous /MIR /XD work logs /NFL /NDL /NJH /NJS | Out-Null

Expand-Archive -Path <path>\PerfPilot-<new version>.zip -DestinationPath C:\PerfPilot -Force
sc.exe start PerfPilot
```

3. Run the checks in section 8. Users should press **Ctrl+F5** once to load the new UI.

- **What updates keep:** `appsettings.Production.json`, the `work` folder (raw k6 output) and `logs`. The package doesn't contain them.
- **Database updates** run automatically on start. So far they only *add* tables and columns, so data is never removed. Consider a database backup before big upgrades; ask your DBA if you can't run `BACKUP DATABASE` yourself.
- **Running tests:** stopping the service cancels any test in progress, and its partial results are saved. Update when no important test is running.

### Roll back

```powershell
sc.exe stop PerfPilot
robocopy C:\PerfPilot.previous C:\PerfPilot /MIR /XD work logs /XF appsettings.Production.json /NFL /NDL /NJH /NJS | Out-Null
sc.exe start PerfPilot
```

Older versions keep working with a database that a newer version has already updated, as long as those updates only added tables and columns, which is true so far.

---

## 10. Move PerfPilot to another machine

Because all data is in the shared database, moving is just "install on the new machine, stop on the old one".

1. **Prepare the new host:** sections 5–7, using the **same** `appsettings.Production.json` (same database, `DataProtection:Store = Database`).
2. **Check it:** section 8 on the new host. Saved tokens and API keys work immediately. Nothing needs re-entering.
3. **Wait** until no test is running on the old host (Runs page).
4. **Stop the old host:**
   ```powershell
   sc.exe stop PerfPilot
   sc.exe config PerfPilot start= disabled     # or uninstall it (section 14)
   ```
5. **Tell users** the new address, `http://<new host>:8080`.

The old host's `work` folder only contains raw k6 output files kept for troubleshooting. Run results, charts and reports are all in the database.

> **Running two hosts at the same time is safe.** Several PerfPilot instances can share one database (section 11). So there's no downtime if you start the new host before stopping the old one.

---

## 11. Several instances and local development

You can run more than one PerfPilot against the same database: two hosts, or a host plus a developer's `dotnet run`.

- **Shared secrets:** every instance must use `DataProtection:Store = Database`, so secrets saved on one can be decrypted on the others.
- **No double runs:** each test runs on the instance where it was started. Instances claim runs atomically and record their name on the run, so a test never runs twice.
- **Cancel from anywhere:** **Cancel** works from any instance. The executing instance stops k6 within about 3 seconds.
- **Safe restarts:** a restarting instance only marks *its own* interrupted runs as failed.
- **Live updates across instances:** live charts stream from the instance running the test. Pages opened on another instance refresh every 5–10 seconds instead.
- **Load source:** each instance generates load from its own machine. A test started from a developer's laptop sends traffic from that laptop.

### Local development against the shared database

Store the connection string in .NET user secrets, which live in your Windows profile, never in the repository:

```powershell
cd D:\Repos\PerfPilot
dotnet user-secrets set "ConnectionStrings:PerfPilot" "Server=172.16.10.6\SQL2019;Database=PerfPilot;User Id=<sql user>;Password=<sql password>;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True" --project src\PerfPilot.Api
dotnet user-secrets set "DataProtection:Store" "Database" --project src\PerfPilot.Api
dotnet run --project src\PerfPilot.Api      # http://localhost:5084
```

User secrets use a single backslash, because the value is not JSON. k6 must be on `PATH` (or set `K6:BinaryPath` in user secrets).

> **Never put the connection string with the password in `appsettings.json`.** That file is committed to git.

---

## 12. Alternative: IIS on Windows Server

Only on **Windows Server** (see the warning in section 3). Summary of the steps:

1. **Enable IIS** with the **WebSocket Protocol** and **Application Initialization** features.
2. **Install the ASP.NET Core 10 Hosting Bundle** after IIS, then run `net stop was /y; net start w3svc`.
3. **Copy the package** to the site folder, e.g. `C:\inetpub\PerfPilot`, and create `appsettings.Production.json` there (section 6.2).
4. **Create the app pool:**
   - **.NET CLR version:** No Managed Code
   - **Start mode:** AlwaysRunning
   - **Idle time-out:** 0
   - **Regular recycling:** off
   - **Disable overlapped recycle:** True
   - **Load user profile:** True
5. **Create the site:** binding `http/*:8080`, preload enabled.
6. **Grant permissions:** Modify on the site folder for `IIS AppPool\PerfPilot`, and read/execute on the k6 folder.
7. **Updates:** stop the app pool, copy the new package, start the app pool.

The included `web.config` already sets these, so don't remove it:
- a 60-second shutdown limit, so running tests can stop gracefully
- a 50 MB request size limit for spec uploads

---

## 13. Optional: HTTPS and Windows sign-in

### HTTPS

Plain HTTP is common on internal networks. Tokens and API keys typed into PerfPilot then travel unencrypted on the network. To use HTTPS:

1. Get a certificate for the host name from IT and import it into **Local Computer → Personal**.
2. Give `NT AUTHORITY\LocalService` read access to its private key: in `certlm.msc`, right-click the certificate → *All Tasks → Manage Private Keys*.
3. Add to `appsettings.Production.json`:
   ```json
   "Kestrel": {
     "Endpoints": {
       "Http": { "Url": "http://0.0.0.0:8080" },
       "Https": {
         "Url": "https://0.0.0.0:8443",
         "Certificate": { "Subject": "perfpilot.company.local", "Store": "My", "Location": "LocalMachine", "AllowInvalid": false }
       }
     }
   }
   ```
   When `Kestrel:Endpoints` is configured, the `--urls` value in the service command is ignored. List every port you want here; drop the `Http` entry to serve HTTPS only.
4. Open port 8443 in the firewall and restart the service (`sc.exe stop PerfPilot`, then `sc.exe start PerfPilot`).

### Windows sign-in

By default PerfPilot asks each user for a display name, which is easy to fake. To record users' real Windows accounts instead, set `"Identity": { "Mode": "Windows" }` and restart.

Browsers then sign users in automatically on the domain. NTLM works without extra setup; for Kerberos, IT may need to set up an SPN or run the service under a domain service account.

---

## 14. Uninstall

```powershell
sc.exe stop PerfPilot
sc.exe delete PerfPilot
Remove-NetFirewallRule -DisplayName "PerfPilot (8080)"
Remove-Item -Recurse -Force C:\PerfPilot, C:\PerfPilot.previous
```

This doesn't touch the database. Scenarios, runs and secrets stay available to other hosts.

---

## 15. Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Service starts then stops; log says *"address already in use"* | Another program uses port 8080, often an old IIS site. Find it with `Get-NetTCPConnection -LocalPort 8080 -State Listen`. Stop that program, or use another port |
| Service won't start, no log written | Check *Event Viewer → Windows Logs → Application* for `.NET Runtime` errors. Common causes: ASP.NET Core Runtime 10 missing (5.1), or `appsettings.Production.json` has a JSON error |
| Log: *"Login failed for user"* / *"network-related or instance-specific error"* | Wrong SQL login or password, or the host can't reach the SQL Server. Run the check in 5.3 on the host |
| Yellow banner *"k6 is not available"* | Wrong `K6:BinaryPath`, or the service account can't read that folder (6.4) |
| *"A stored secret could not be decrypted"* | This instance uses a different key ring. Make sure `DataProtection:Store` is `Database` on every instance. Secrets saved before that switch must be re-entered once |
| Users can't open the site, but `http://localhost:8080` works on the host | Firewall (5.4), or users are on another subnet than `LocalSubnet` allows. Test from a user's PC with `Test-NetConnection <host> -Port 8080` |
| UI hangs for long periods under IIS | IIS on Windows 10/11 request limit (section 3). Switch to the Windows service |
| Tests fail with DNS/TLS/timeout errors that don't happen on your PC | The host lacks the VPN, routes or certificates your PC has. Test connection on the host, and fix its network access |
| Disk filling up | Raw k6 output in `C:\PerfPilot\work\runs`. Set `K6:DeleteRawOutputAfterRun` to `true`, or delete old run folders (results stay in the database) |
| Need more detail | `C:\PerfPilot\logs\perfpilot-<date>.log`, and for a failed run `C:\PerfPilot\work\runs\<run id without dashes>\k6-output.log` |
