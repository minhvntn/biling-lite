# Server Admin Desktop App (Lightweight Shell)

Desktop app for managing the server UI in parallel with the web app.

## Why this stack

This app uses **WPF + WebView2**:
- Reuses the existing React admin UI (`/pcs`, `/history`).
- Does **not** bundle a full Chromium runtime like Electron.
- No Rust toolchain requirement on your current machine.

## Run

1. Start backend (`:9000`):

```powershell
cd i:\servermanagerbilling\backend
npm run start:dev
```

2. Start admin web (`:5173`):

```powershell
cd i:\servermanagerbilling\admin
npm run dev
```

3. Run desktop shell:

```powershell
cd i:\servermanagerbilling\server-admin-app
dotnet run
```

## Config

Edit `appsettings.json`:

- `AdminBaseUrl`: default `http://localhost:5173`
- `BackendHealthUrl`: default `http://localhost:9000/api/v1/pcs`
- `StartPath`: default `/pcs`

## Build exe

```powershell
cd i:\servermanagerbilling\server-admin-app
dotnet publish -c Release -r win-x64 --self-contained false
```
