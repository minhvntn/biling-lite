---
name: servermanagerbilling-project
description: Use when working in the ServerManagerBilling monorepo to quickly navigate architecture, pick the correct runtime target (backend vs release/backend), and apply safe implementation + verification workflows for backend (NestJS/Prisma), server-admin-app (WPF), and client agent (WPF).
---

# ServerManagerBilling Project Skill

## Use this skill when
- The task touches this repository (`backend`, `server-admin-app`, `client`, `release`).
- You need to decide whether to patch source code (`backend/src`) or active release runtime (`release/backend/dist`).
- You need fast, reliable edit + build verification steps for this project.

## Project layout
- `backend`: NestJS + Prisma source.
- `server-admin-app`: WPF server desktop admin app.
- `client/src/Client.Agent.Wpf`: WPF client agent.
- `release/backend/dist`: compiled backend JS sometimes used directly in local release runs.

Read [references/code-map.md](references/code-map.md) for high-value file paths.

## Runtime targeting rule (very important)
1. If user runs dev backend (`backend`), edit `backend/src/*` and build with `npm run build` in `backend`.
2. If user runs release backend (`release/backend`), patch corresponding `release/backend/dist/src/*` too when needed.
3. When uncertain, check where user starts backend (`backend` vs `release/backend`) before finishing.

## Standard verification commands
- Backend source build:
```powershell
cd i:\servermanagerbilling\backend
npm run build
```
- Server admin app build:
```powershell
cd i:\servermanagerbilling
dotnet build server-admin-app/Server.Admin.App.csproj -p:OutputPath=bin/TempBuild/
```
- Client agent build:
```powershell
cd i:\servermanagerbilling
dotnet build client/src/Client.Agent.Wpf/Client.Agent.Wpf.csproj -p:OutputPath=bin/TempBuild/
```

## Common pitfalls to avoid
- Updating only `backend/src` while user actually runs `release/backend/dist`.
- Ranking/usage features mixing up "play time consumed" vs "remaining play seconds".
- Large WPF UI edits without rebuilding affected app.

## Implementation style for this repo
- Prefer minimal, targeted edits over broad refactors.
- Keep UX labels Vietnamese where existing UI is Vietnamese.
- For member/account money fields, validate non-negative values before API calls.
- After backend API shape changes, update matching desktop DTO/view-model fields.

## Done checklist before replying
- Build passes for changed components.
- Runtime target consistency checked (`backend` and/or `release/backend/dist`).
- Mention restart/reload steps if runtime process must be restarted.
