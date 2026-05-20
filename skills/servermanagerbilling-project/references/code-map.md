# Code Map

## Backend (NestJS)
- Main reports stats logic: `backend/src/reports/reports.service.ts`
- Members business logic: `backend/src/members/members.service.ts`
- Members DTOs: `backend/src/members/dto/*`
- Pricing/groups logic: `backend/src/pricing/pricing.service.ts`
- Prisma schema: `backend/prisma/schema.prisma`

## Server Admin App (WPF)
- Main UI shell: `server-admin-app/MainWindow.xaml`
- Members module: `server-admin-app/MainWindow/MainWindow.Members.cs`
- Stats dashboard module: `server-admin-app/MainWindow/MainWindow.Stats.cs`
- Groups module: `server-admin-app/MainWindow/MainWindow.Groups.cs`
- Services module: `server-admin-app/MainWindow/MainWindow.Services.cs`

## Client Agent (WPF)
- Main logic + dialogs: `client/src/Client.Agent.Wpf/App.xaml.cs`
- Main window actions: `client/src/Client.Agent.Wpf/MainWindow.xaml.cs`

## Release runtime mirrors
- Release backend entry area: `release/backend/dist/src/*`
- Members release runtime: `release/backend/dist/src/members/*`
- Reports release runtime: `release/backend/dist/src/reports/*`

## Quick diagnosis pointers
- "Top members wrong": check `reports.service` member ranking query source.
- "UI shows empty list": verify DTO fields in `MainWindow.Stats.cs` align with API response.
- "Change not applied": confirm running backend path (`backend` vs `release/backend`).
