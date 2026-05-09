# ServerManagerBilling - Huong dan chay local de test

He thong quan ly phong may gom:

- Backend API: NestJS + Prisma + PostgreSQL
- Admin Web: React + Vite
- Server Admin Desktop App: WPF
- Client Agent: WPF agent tren may tram
- Watchdog Service: Windows Service giu agent hoat dong

## 1. Yeu cau moi truong

- Windows 10/11
- Node.js 18+
- .NET SDK 10
- PostgreSQL 16 hoac phien ban tuong thich

## 2. Tao database

Dung `pgAdmin` hoac `psql` de tao database:

```sql
CREATE DATABASE servermanagerbilling;
```

Cau hinh mac dinh backend:

| Thong tin | Gia tri |
| --- | --- |
| User | postgres |
| Password | postgres |
| Host | localhost |
| Port | 5432 |
| Database | servermanagerbilling |

Neu tai khoan hoac mat khau PostgreSQL khac, tao file `backend/.env` tu `backend/.env.example` va chinh `DATABASE_URL`.

## 3. Cai dependencies

Backend:

```powershell
cd I:\servermanagerbilling\backend
npm install
```

Admin Web:

```powershell
cd I:\servermanagerbilling\admin
npm install
```

Server Admin Desktop App:

```powershell
cd I:\servermanagerbilling\server-admin-app
dotnet restore
```

Client Agent:

```powershell
cd I:\servermanagerbilling\client
dotnet restore ServerManagerClient.slnx
```

## 4. Chay migration database

```powershell
cd I:\servermanagerbilling\backend
npx prisma migrate deploy
npx prisma generate
```

## 5. Chay cac service local

Mo cac terminal rieng biet.

Backend API, port `9000`:

```powershell
cd I:\servermanagerbilling\backend
npm run start:dev
```

Admin Web, port `5173`:

```powershell
cd I:\servermanagerbilling\admin
npm run dev
```

Mock Agent de gia lap may tram:

```powershell
cd I:\servermanagerbilling\admin
npm run mock:agent
```

Server Admin Desktop App:

```powershell
cd I:\servermanagerbilling\server-admin-app
dotnet run
```

Client Agent WPF:

```powershell
cd I:\servermanagerbilling\client\src\Client.Agent.Wpf
dotnet run
```

## 6. URL mac dinh

- Backend API: `http://localhost:9000/api/v1`
- Socket.IO namespace: `http://localhost:9000/billing`
- Admin Web: `http://localhost:5173`
- Server Admin Desktop config: `server-admin-app/appsettings.json`
- Client Agent config: `client/src/Client.Agent.Wpf/appsettings.json`

## 7. Checklist test nhanh

Sau khi chay du service, mo Server Admin Desktop App va kiem tra:

- May tram hien thi danh sach may, trang thai online/offline/in use/locked.
- Co the mo may, khoa may, tam dung/tiep tuc neu agent dang ket noi.
- Tao hoi vien moi, nhap username, mat khau, so dien thoai/CCCD neu can.
- Dang nhap hoi vien tu lock screen client.
- Nap tien, mua gio, dieu chinh so du, xem lich su giao dich.
- Xem nhat ky he thong, nhat ky giao dich, website logs neu bat tinh nang.
- Tao nhom may, dat gia theo nhom, gan may vao nhom.
- Tao mat hang dich vu va goi dich vu cho may.
- Luu cai dat client runtime, web filter, guest login, loyalty.

## 8. Dynamic pricing by group

Backend ho tro gia theo nhom may va gia mac dinh.

### API endpoints

- `GET /api/v1/pricing`: lay gia mac dinh va danh sach nhom.
- `PUT /api/v1/pricing/default-rate`: cap nhat gia mac dinh.
- `POST /api/v1/pricing/groups`: tao nhom gia.
- `PATCH /api/v1/pricing/groups/:groupId`: cap nhat ten/gia nhom.
- `POST /api/v1/pricing/pcs/:pcId/group`: gan may vao nhom.

### PowerShell examples

```powershell
# Dat gia mac dinh = 5000 VND/gio
Invoke-RestMethod -Method Put `
  -Uri "http://localhost:9000/api/v1/pricing/default-rate" `
  -ContentType "application/json" `
  -Body '{"hourlyRate":5000}'

# Tao nhom gia = 7000 VND/gio
$group = Invoke-RestMethod -Method Post `
  -Uri "http://localhost:9000/api/v1/pricing/groups" `
  -ContentType "application/json" `
  -Body '{"name":"Phong VIP","hourlyRate":7000}'

# Gan PC vao nhom vua tao
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:9000/api/v1/pricing/pcs/<pcId>/group" `
  -ContentType "application/json" `
  -Body ("{""groupId"":""" + $group.id + """}")
```

## 9. Deploy client agent mot may

Chay PowerShell voi quyen Administrator:

```powershell
cd I:\servermanagerbilling\client\scripts
.\publish-day5.ps1 -Configuration Release -Runtime win-x64
.\configure-day5.ps1 -ServerUrl "http://<server-ip>:9000" -AgentId "PC-001"
.\deploy-day5-local.ps1
.\verify-day5.ps1
```

Log mac dinh:

- Agent: `%ProgramData%\ServerManagerBilling\logs\client-agent.log`
- Watchdog: `%ProgramData%\ServerManagerBilling\logs\watchdog-service.log`

## 10. Luu y bao mat

- Khong commit mat khau, API key, token, URL private hoac thong tin tai khoan ca nhan vao README.
- Doi mat khau agent-admin trong tab Cai dat cua Server Admin Desktop truoc khi dung ngoai moi truong test.
- Neu can chia se cau hinh, tao file `.env.example` hoac `appsettings.example.json` khong chua secret that.

## 11. Loi thuong gap

- Backend bao loi Prisma: kiem tra PostgreSQL dang chay va `DATABASE_URL`.
- PowerShell chan script: mo PowerShell Administrator va dieu chinh ExecutionPolicy cho phien hien tai neu can.
- Desktop app khong ket noi backend: kiem tra `BackendApiBaseUrl` trong `server-admin-app/appsettings.json`.
- Agent khong hien tren admin: kiem tra `Agent.ServerUrl`, firewall port `9000`, va log agent.
