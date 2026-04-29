# Day 5 Client Security Skeleton

This folder contains the Phase 1 Day 5 client-side baseline:

- `Client.Agent.Wpf`: interactive PC agent (Socket.IO + lock overlay + auto startup + log file)
- `Client.Watchdog.Service`: Windows Service watchdog to monitor and restart agent

## Build

```powershell
cd i:\servermanagerbilling\client
dotnet build ServerManagerClient.slnx
```

## One-machine deployment (recommended flow)

### 1) Publish artifacts

```powershell
cd i:\servermanagerbilling\client\scripts
.\publish-day5.ps1 -Configuration Release -Runtime win-x64
```

Artifacts output:

- `i:\servermanagerbilling\client\dist\day5\Agent`
- `i:\servermanagerbilling\client\dist\day5\Service`

### 2) Configure machine identity and server endpoint

```powershell
cd i:\servermanagerbilling\client\scripts
.\configure-day5.ps1 -ServerUrl "http://192.168.1.50:9000" -AgentId "PC-001"
```

### 3) Deploy + install (run PowerShell as Administrator)

```powershell
cd i:\servermanagerbilling\client\scripts
.\deploy-day5-local.ps1
```

This will:

- Copy files to `C:\Program Files\ServerManagerBilling\Agent` and `...\Service`
- Create/update scheduled task `ServerManagerBillingAgent`
- Create/start Windows service `ServerManagerBillingWatchdog`

### 4) Verify installation

```powershell
cd i:\servermanagerbilling\client\scripts
.\verify-day5.ps1
```

## Single-command quick run (PowerShell Admin)

```powershell
cd i:\servermanagerbilling\client\scripts
.\publish-day5.ps1 -Configuration Release -Runtime win-x64
.\configure-day5.ps1 -ServerUrl "http://192.168.1.50:9000" -AgentId "PC-001"
.\deploy-day5-local.ps1
.\verify-day5.ps1
```

## Manual install (advanced)

Use the packaged install script in `dist\day5\install-day5.ps1` if you already copied binaries manually.

## Uninstall

```powershell
cd i:\servermanagerbilling\client\scripts
.\uninstall-day5.ps1
```

## Logs

- Agent: `%ProgramData%\ServerManagerBilling\logs\client-agent.log`
- Watchdog service: `%ProgramData%\ServerManagerBilling\logs\watchdog-service.log`

## Note about Windows Service and UI

Windows Service runs in Session 0 and cannot directly show UI to desktop users.
This baseline uses a scheduled task to relaunch the interactive WPF agent in user session.

cd i:\servermanagerbilling\backend
npm run start:dev
Admin:
powershell

cd i:\servermanagerbilling\admin
npm run dev
Client ảo (terminal mới):
powershell

cd i:\servermanagerbilling\admin
npm run mock:agent