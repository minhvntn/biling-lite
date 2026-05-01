
# ServerManagerBilling - Hướng dẫn chạy local để test

## 1. Yêu cầu môi trường

Trước khi chạy project, cần cài sẵn:

- Windows 10/11
- Node.js 18+
- .NET SDK 10
- PostgreSQL 16 hoặc phiên bản tương thích

---

## 2. Tạo database

Dùng `pgAdmin` hoặc `psql` để tạo database:

```sql
CREATE DATABASE servermanagerbilling;

Cấu hình mặc định backend đang sử dụng:

Thông tin	Giá trị
User	postgres
Password	postgres
Host	localhost
Port	5432
Database	servermanagerbilling

Nếu tài khoản hoặc mật khẩu PostgreSQL khác, hãy chỉnh lại trong file:

backend/.env
3. Cài dependencies
Backend
cd I:\servermanagerbilling\backend
npm install
Admin Web
cd I:\servermanagerbilling\admin
npm install
Server Admin App WPF
cd I:\servermanagerbilling\server-admin-app
dotnet restore
4. Chạy migration database
cd I:\servermanagerbilling\backend
npx prisma migrate deploy
npx prisma generate
5. Chạy các service

Mở 4 terminal riêng biệt và chạy lần lượt các service sau.

Terminal A - Backend API

Backend API chạy tại port 9000.

cd I:\servermanagerbilling\backend
npm run start:dev
Terminal B - Admin Web

Admin Web chạy tại port 5173.

cd I:\servermanagerbilling\admin
npm run dev
Terminal C - Mock Agent

Mock Agent dùng để giả lập máy trạm.

cd I:\servermanagerbilling\admin
npm run mock:agent
Terminal D - Server Admin Desktop App
cd I:\servermanagerbilling\server-admin-app
dotnet run
6. Checklist test nhanh

Sau khi chạy đủ các service, mở Server Admin Desktop App và kiểm tra các chức năng sau:

Máy trạm
Hiển thị được danh sách máy.
Có thể mở máy.
Có thể khóa máy.
Tài khoản
Bấm Thêm hội viên.
Popup/modal hiển thị đúng.
Nhập tên đăng nhập và mật khẩu.
Tạo hội viên thành công.
Nhật ký hệ thống
Danh sách log hệ thống tải được.
Dữ liệu hiển thị đúng.
Nhật ký giao dịch
Danh sách phiên sử dụng tải được.
Doanh thu hiển thị đúng.
Nhóm máy
Có tổng hợp nhóm máy.
Có danh sách máy theo nhóm.
Dịch vụ
Có các nút thao tác.
Có vùng log hoạt động.
Cài đặt
Kéo thanh cỡ chữ.
UI thay đổi ngay.
Bấm Lưu cài đặt thành công.
7. Ghi chú
Nếu service PostgreSQL chưa chạy, backend sẽ báo lỗi kết nối Prisma.
Nếu PowerShell chặn script do ExecutionPolicy, hãy chạy lệnh thủ công thay vì chạy file .ps1.
API base URL mà desktop app đang sử dụng:
http://localhost:9000/api/v1

Lưu ý nhỏ: file `README.md` nên lưu bằng encoding **UTF-8** để tiếng Việt không bị lỗi font. Trong VS Code, có thể bấm góc dưới bên phải chỗ encoding rồi chọn **Save with Encoding → UTF-8**.
## 8. Dynamic pricing by group (new)

Backend now supports dynamic hourly pricing at server level and per machine group.

### API endpoints

- `GET /api/v1/pricing`
  - Get default hourly rate and all groups.
- `PUT /api/v1/pricing/default-rate`
  - Update default hourly rate.
  - Body: `{ "hourlyRate": 5000 }`
- `POST /api/v1/pricing/groups`
  - Create group rate.
  - Body: `{ "name": "VIP", "hourlyRate": 7000 }`
- `PATCH /api/v1/pricing/groups/:groupId`
  - Update group name/rate.
  - Body example: `{ "hourlyRate": 6000 }`
- `POST /api/v1/pricing/pcs/:pcId/group`
  - Assign machine to a pricing group.
  - Body: `{ "groupId": "<uuid>" }`

### PowerShell examples

```powershell
# 1) Set default rate = 5000 VND/hour
Invoke-RestMethod -Method Put \
  -Uri "http://localhost:9000/api/v1/pricing/default-rate" \
  -ContentType "application/json" \
  -Body '{"hourlyRate":5000}'

# 2) Create group rate = 7000 VND/hour
$group = Invoke-RestMethod -Method Post \
  -Uri "http://localhost:9000/api/v1/pricing/groups" \
  -ContentType "application/json" \
  -Body '{"name":"Phong VIP","hourlyRate":7000}'

# 3) Assign a PC to this group
Invoke-RestMethod -Method Post \
  -Uri "http://localhost:9000/api/v1/pricing/pcs/<pcId>/group" \
  -ContentType "application/json" \
  -Body ("{\"groupId\":\"" + $group.id + "\"}")
```

### Effect

- New sessions use the hourly rate from the machine group.
- If machine has no group, it uses the default group rate.
- Client receives `hourlyRate` in realtime `command.execute` for OPEN/RESUME so child machine UI follows server pricing.
