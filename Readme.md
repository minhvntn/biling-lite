# ServerManagerBilling - Hu?ng d?n ch?y local d? test

## 1. Y�u c?u m�i tru?ng
- Windows 10/11
- Node.js 18+
- .NET SDK 10
- PostgreSQL 16 (ho?c b?n tuong th�ch)

## 2. T?o database
D�ng `pgAdmin` ho?c `psql` v� t?o DB:

```sql
CREATE DATABASE servermanagerbilling;
```

C?u h�nh m?c d?nh backend dang d�ng:
- user: `postgres`
- password: `postgres`
- host: `localhost`
- port: `5432`
- database: `servermanagerbilling`

N?u t�i kho?n/m?t kh?u kh�c, s?a trong `backend/.env`.

## 3. C�i dependencies

### Backend
```powershell
cd I:\servermanagerbilling\backend
npm install
```

### Admin Web
```powershell
cd I:\servermanagerbilling\admin
npm install
```

### Server Admin App (WPF)
```powershell
cd I:\servermanagerbilling\server-admin-app
dotnet restore
```

## 4. Ch?y migration database
```powershell
cd I:\servermanagerbilling\backend
npx prisma migrate deploy
npx prisma generate
```

## 5. Ch?y c�c service
M? 4 terminal ri�ng.

### Terminal A - Backend API (port 9000)
```powershell
cd I:\servermanagerbilling\backend
npm run start:dev
```

### Terminal B - Admin Web (port 5173)
```powershell
cd I:\servermanagerbilling\admin
npm run dev
```

### Terminal C - Mock Agent (gi? l?p m�y tr?m)
```powershell
cd I:\servermanagerbilling\admin
npm run mock:agent
```

### Terminal D - Server Admin Desktop App
```powershell
cd I:\servermanagerbilling\server-admin-app
dotnet run
```

## 6. Checklist test nhanh
Trong Server Admin Desktop app, kiểm tra:

Máy trạm: thấy danh sách máy, mở/khóa máy
Tài khoản: bấm Thêm hội viên → popup/modal → nhập tên đăng nhập/mật khẩu → tạo hội viên
Nhật ký hệ thống: danh sách log tải được
Nhật ký giao dịch: danh sách phiên/doanh thu tải được
Nhóm máy: có tổng hợp nhóm + danh sách máy
Dịch vụ: có các nút thao tác và vùng log hoạt động
Cài đặt: kéo thanh cỡ chữ → UI đổi ngay → bấm Lưu cài đặt
7. Ghi chú
Nếu service PostgreSQL chưa chạy, backend sẽ báo lỗi kết nối Prisma.
Nếu PowerShell chặn script (ExecutionPolicy), chạy lệnh thủ công thay vì file .ps1.
API base URL app desktop đang dùng: http://localhost:9000/api/v1