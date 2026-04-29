# Phase 1 MVP Blueprint (Billing Core)

## 1) Mục tiêu Phase 1
- Quản lý trạng thái máy trạm online/offline theo thời gian thực.
- Admin mở máy / khóa máy từ web.
- Tạo phiên sử dụng, tính tiền theo phút ở server.
- Lưu lịch sử phiên để đối soát.
- Chạy ổn trong LAN nội bộ.

## 2) Scope chốt cho Phase 1
### In-scope
- NestJS + PostgreSQL + Socket.IO server.
- C# WPF client agent kết nối server realtime.
- React admin dashboard tối giản (desktop trước, responsive cơ bản).
- Session billing theo giá tiền/phút global.

### Out-of-scope
- Hội viên, nạp tiền.
- Bán hàng, tồn kho.
- Chống tắt app nâng cao (watchdog/service mức sâu).
- Cloud sync.

## 3) Task breakdown theo 7 ngày
## Day 1 - Foundation backend
- Khởi tạo NestJS project + module: `pcs`, `sessions`, `pricing`, `commands`, `auth_admin` (auth có thể hardcode token nội bộ cho MVP).
- Setup PostgreSQL + migration tool (Prisma hoặc TypeORM, chọn 1 và cố định).
- Tạo schema DB bản đầu.
- Seed dữ liệu `pricing_config` mặc định.

Deliverable:
- Server chạy local + kết nối DB.
- Migration chạy được 1 lệnh.

## Day 2 - Kết nối máy trạm realtime
- Thiết kế định danh máy: `agentId` duy nhất (UUID hoặc mã máy cố định).
- Client C# kết nối Socket.IO, gửi `agent.hello` + heartbeat mỗi 10 giây.
- Server cập nhật trạng thái `online/offline`, `last_seen_at`, `ip_address`, `hostname`.
- Job timeout đánh dấu offline nếu quá 25-30 giây không heartbeat.

Deliverable:
- Bật/tắt client thấy trạng thái đổi realtime ở server.

## Day 3 - Admin danh sách máy
- React page `/pcs`: bảng máy, trạng thái, phiên hiện tại, thời gian chạy hiện tại.
- API lấy danh sách máy + trạng thái.
- Socket push để admin không cần refresh.

Deliverable:
- Admin thấy máy online/offline realtime.

## Day 4 - Mở máy / khóa máy
- Admin action: `OPEN` / `LOCK`.
- Server tạo `command` và phát socket tới đúng agent.
- Client thực thi lệnh rồi phản hồi `command.ack` (`SUCCESS`/`FAILED`).
- Log đầy đủ command lifecycle.

Deliverable:
- Bấm mở/khóa từ admin điều khiển được client thật.

## Day 5 - Session + Billing theo phút
- Khi OPEN thành công: tạo `session` với `start_at`.
- Khi LOCK thành công: đóng `session` với `end_at`.
- Tính tiền ở server:
  - `duration_seconds = end_at - start_at`
  - `billable_minutes = ceil(duration_seconds / 60)`
  - `amount = billable_minutes * price_per_minute`
- API trả session active + số tiền tạm tính realtime.

Deliverable:
- Tiền tăng đúng theo phút và chốt đúng khi khóa máy.

## Day 6 - Lịch sử phiên + doanh thu ngày cơ bản
- API lịch sử phiên (filter theo máy, ngày).
- API tổng doanh thu ngày (sum session đã đóng).
- Trang admin `Session History` + `Today Revenue`.

Deliverable:
- Có thể đối soát phiên và doanh thu trong ngày.

## Day 7 - Hardening & UAT LAN
- Test case mất mạng/reconnect/khởi động lại server.
- Idempotency cho command (tránh double-open, double-close).
- Thêm audit log tối thiểu.
- Đóng gói bản demo 1 server + 2-3 client.

Deliverable:
- Bản chạy thử ổn định trong LAN.

## 4) Data model (PostgreSQL skeleton)
```sql
-- pcs: trạng thái máy trạm
CREATE TABLE pcs (
  id UUID PRIMARY KEY,
  agent_id VARCHAR(100) UNIQUE NOT NULL,
  name VARCHAR(100) NOT NULL,
  hostname VARCHAR(120),
  ip_address VARCHAR(45),
  status VARCHAR(20) NOT NULL DEFAULT 'OFFLINE', -- OFFLINE|ONLINE|IN_USE|LOCKED
  last_seen_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- pricing config (MVP: 1 dòng active)
CREATE TABLE pricing_config (
  id UUID PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  price_per_minute NUMERIC(12,2) NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT true,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- sessions: phiên sử dụng máy
CREATE TABLE sessions (
  id UUID PRIMARY KEY,
  pc_id UUID NOT NULL REFERENCES pcs(id),
  started_at TIMESTAMPTZ NOT NULL,
  ended_at TIMESTAMPTZ,
  duration_seconds INT,
  billable_minutes INT,
  price_per_minute NUMERIC(12,2),
  amount NUMERIC(12,2),
  status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE', -- ACTIVE|CLOSED
  closed_reason VARCHAR(30), -- ADMIN_LOCK|AUTO_OFFLINE|SYSTEM
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- commands: hàng đợi lệnh tới client
CREATE TABLE commands (
  id UUID PRIMARY KEY,
  pc_id UUID NOT NULL REFERENCES pcs(id),
  type VARCHAR(20) NOT NULL, -- OPEN|LOCK
  status VARCHAR(20) NOT NULL DEFAULT 'PENDING', -- PENDING|SENT|ACK_SUCCESS|ACK_FAILED|TIMEOUT
  requested_by VARCHAR(100) NOT NULL,
  requested_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  sent_at TIMESTAMPTZ,
  ack_at TIMESTAMPTZ,
  error_message TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- events log: audit đơn giản
CREATE TABLE events_log (
  id UUID PRIMARY KEY,
  source VARCHAR(30) NOT NULL, -- ADMIN|SERVER|CLIENT
  event_type VARCHAR(50) NOT NULL,
  pc_id UUID,
  payload JSONB,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_pcs_status ON pcs(status);
CREATE INDEX idx_sessions_pc_id ON sessions(pc_id);
CREATE INDEX idx_sessions_started_at ON sessions(started_at);
CREATE INDEX idx_commands_pc_id ON commands(pc_id);
CREATE INDEX idx_commands_status ON commands(status);
```

## 5) REST API skeleton
Base: `/api/v1`

### PC
- `GET /pcs`
  - Trả danh sách máy + trạng thái + activeSession (nếu có).
- `GET /pcs/:id`
  - Chi tiết máy.

### Commands
- `POST /pcs/:id/open`
  - Tạo lệnh OPEN.
- `POST /pcs/:id/lock`
  - Tạo lệnh LOCK.
- `GET /commands/:id`
  - Tra trạng thái command.

### Sessions
- `GET /sessions?pcId=&from=&to=&status=`
  - Lịch sử phiên.
- `GET /sessions/active`
  - Tất cả phiên đang chạy.

### Reports (mức cơ bản cho Phase 2-ready)
- `GET /reports/revenue/daily?date=YYYY-MM-DD`
  - Tổng doanh thu ngày.

## 6) Socket event contract (quan trọng)
Namespace: `/billing`

### Client -> Server
- `agent.hello`
```json
{ "agentId": "PC-001", "hostname": "MAY01", "ip": "192.168.1.10", "version": "0.1.0" }
```
- `agent.heartbeat`
```json
{ "agentId": "PC-001", "at": "2026-04-28T10:00:00Z" }
```
- `command.ack`
```json
{ "commandId": "uuid", "agentId": "PC-001", "result": "SUCCESS", "message": "locked" }
```

### Server -> Client
- `command.execute`
```json
{ "commandId": "uuid", "type": "OPEN", "issuedAt": "2026-04-28T10:00:00Z" }
```

### Server -> Admin
- `pc.status.changed`
```json
{ "pcId": "uuid", "agentId": "PC-001", "status": "ONLINE", "at": "..." }
```
- `session.updated`
```json
{ "sessionId": "uuid", "pcId": "uuid", "status": "ACTIVE", "elapsedSeconds": 360, "estimatedAmount": 18000 }
```
- `command.updated`
```json
{ "commandId": "uuid", "status": "ACK_SUCCESS", "pcId": "uuid" }
```

## 7) Business rules bắt buộc cho MVP
- Chỉ 1 `ACTIVE session` trên 1 máy tại 1 thời điểm.
- Không `OPEN` nếu máy đang `IN_USE`.
- Không `LOCK` nếu không có session active (vẫn cho phép nhưng trả state phù hợp).
- Billing chỉ tính ở server, không tin số từ client.
- Dùng `ceil` theo phút để tránh thất thoát.
- Nếu client offline đột ngột:
  - Giữ session active tối đa X phút (config),
  - Quá timeout thì auto-close với `closed_reason=AUTO_OFFLINE`.

## 8) Test checklist (UAT)
- Client online -> admin thấy ONLINE trong < 3s.
- Open máy -> command ack success -> session active tạo đúng.
- Chạy 3 phút 10 giây -> billable = 4 phút.
- Lock máy -> session closed + amount finalize.
- Mất mạng client 1 phút rồi có lại -> reconnect không tạo session duplicate.
- Bấm open 2 lần nhanh -> không tạo 2 session.

## 9) Definition of Done Phase 1
- Quản lý được trạng thái online/offline realtime.
- Mở/khóa máy từ admin hoạt động ổn định.
- Session billing theo phút chính xác.
- Có lịch sử phiên để đối soát.
- Demo LAN với ít nhất 2-3 máy trạm chạy ổn.

## 10) Backlog ngay sau Phase 1
- Responsive admin hoàn chỉnh cho mobile (Phase 2).
- Hội viên + ví tiền (Phase 3).
- POS mini + tồn kho (Phase 4).
- Service/watchdog/chống tắt app (Phase 5).
