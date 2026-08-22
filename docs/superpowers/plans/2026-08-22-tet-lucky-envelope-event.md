# Sự kiện Lì Xì Tết (Tet Lucky Red Envelope Event) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai hoàn chỉnh tính năng Sự kiện Lì Xì Tết trên toàn bộ hệ thống gồm Backend (NestJS), Server Admin App (WPF), và Client Agent (WPF), cho phép hội viên dùng Điểm tích lũy để mở phong bao lì xì nhận ngẫu nhiên phút chơi khuyến mãi trong dịp Tết.

**Architecture:** Sử dụng kiến trúc AppSetting key-value kết hợp Loyalty Transaction sẵn có trong Backend để đảm bảo tính an toàn ACID. Server Admin App cung cấp giao diện quản lý cấu hình (Bật/Tắt, Ngày bắt đầu/kết thúc, Điểm đổi, Min/Max phút, Giới hạn lượt). Client Agent tích hợp Tab Lì Xì Tết trong cửa sổ Điểm tích lũy với giao diện Tết rực rỡ và hiệu ứng mở phong bao nhận thưởng.

**Tech Stack:** NestJS, Prisma ORM, TypeScript, C# .NET WPF, XAML.

---

### Task 1: Backend DTOs & Service Logic cho Sự kiện Lì Xì Tết

**Files:**
- Create: `backend/src/members/dto/lucky-envelope-settings.dto.ts`
- Create: `backend/src/members/dto/open-lucky-envelope.dto.ts`
- Modify: `backend/src/members/members.service.ts`
- Modify: `backend/src/members/members.controller.ts`

- [ ] **Step 1: Tạo DTOs cho cấu hình và thao tác mở Lì Xì**
Tạo file `backend/src/members/dto/lucky-envelope-settings.dto.ts`:
```typescript
import { Type } from 'class-transformer';
import { IsBoolean, IsDateString, IsInt, IsOptional, Max, Min } from 'class-validator';

export class UpdateLuckyEnvelopeSettingsDto {
  @IsBoolean()
  enabled!: boolean;

  @IsOptional()
  @IsDateString()
  startDate?: string;

  @IsOptional()
  @IsDateString()
  endDate?: string;

  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(10000)
  pointCost!: number;

  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(100000)
  minMinutes!: number;

  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(100000)
  maxMinutes!: number;

  @Type(() => Number)
  @IsInt()
  @Min(0)
  @Max(1000)
  maxClaims!: number;
}
```

Tạo file `backend/src/members/dto/open-lucky-envelope.dto.ts`:
```typescript
import { Transform } from 'class-transformer';
import { IsOptional, IsString, MaxLength } from 'class-validator';

export class OpenLuckyEnvelopeDto {
  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  createdBy?: string;
}
```

- [ ] **Step 2: Cài đặt logic Service trong `members.service.ts`**
Thêm các constants và phương thức:
- `getLuckyEnvelopeSettings(memberId?: string)`
- `updateLuckyEnvelopeSettings(dto: UpdateLuckyEnvelopeSettingsDto)`
- `openLuckyEnvelope(memberId: string, payload: OpenLuckyEnvelopeDto)`
Bao gồm thuật toán phân bổ trọng số may mắn 3 tầng (`Tier 1: 60%`, `Tier 2: 30%`, `Tier 3: 10%`) và danh sách 5 câu chúc Tết ngẫu nhiên, thực thi trong `prisma.$transaction`.

- [ ] **Step 3: Khai báo Controller Endpoints trong `members.controller.ts`**
Thêm:
- `GET /members/loyalty/lucky-envelope/settings`
- `PUT /members/loyalty/lucky-envelope/settings`
- `POST /members/:id/loyalty/lucky-envelope/open`

- [ ] **Step 4: Kiểm tra build Backend**
Run: `npm run build` trong `backend/`
Expected: Build thành công không có lỗi TypeScript.

- [ ] **Step 5: Viết và chạy kịch bản kiểm thử tích hợp (Scratch test)**
Tạo `backend/scratch/test_lucky_envelope.ts` kiểm tra:
1. Đọc settings ban đầu.
2. Cập nhật settings.
3. Mở lì xì (trừ điểm, cộng phút, ghi audit log, sinh câu chúc Tết).
4. Kiểm tra validate khi không đủ điểm.
Expected: PASS toàn bộ các case.

- [ ] **Step 6: Commit Backend changes**
```bash
git add backend/src/members/
git commit -m "feat(backend): add tet lucky envelope settings and redeem endpoints"
```

---

### Task 2: Server Admin App - Quản lý Cấu hình Sự kiện Lì Xì Tết

**Files:**
- Modify: `server-admin-app/Models/AdminShellModels.cs`
- Modify: `server-admin-app/MainWindow.xaml`
- Modify: `server-admin-app/MainWindow/MainWindow.MiniGame.cs`

- [ ] **Step 1: Định nghĩa DTOs trong `AdminShellModels.cs`**
Thêm `LuckyEnvelopeSettingsResponse` và `UpdateLuckyEnvelopeSettingsRequest`.

- [ ] **Step 2: Cập nhật giao diện Tab Mini Game trong `MainWindow.xaml`**
Thêm nhóm giao diện `🧧 Sự kiện Lì Xì Tết (Lucky Red Envelope)` gồm:
- `CheckBox x:Name="LuckyEnvelopeEnabledCheckBox"`
- `DatePicker x:Name="LuckyEnvelopeStartDatePicker"` & `DatePicker x:Name="LuckyEnvelopeEndDatePicker"`
- `TextBox x:Name="LuckyEnvelopePointCostTextBox"`
- `TextBox x:Name="LuckyEnvelopeMinMinutesTextBox"`
- `TextBox x:Name="LuckyEnvelopeMaxMinutesTextBox"`
- `TextBox x:Name="LuckyEnvelopeMaxClaimsTextBox"`
- `Button x:Name="SaveLuckyEnvelopeSettingsButton"` & `Button x:Name="RefreshLuckyEnvelopeSettingsButton"`
- `TextBlock x:Name="LuckyEnvelopeStatusTextBlock"`

- [ ] **Step 3: Cài đặt logic code-behind trong `MainWindow.MiniGame.cs`**
Thêm:
- `RefreshLuckyEnvelopeSettingsAsync()`
- `SaveLuckyEnvelopeSettingsAsync()`
- Tự động gọi tải cấu hình khi mở Tab Mini Game.

- [ ] **Step 4: Kiểm tra build Server Admin App**
Run: `dotnet build server-admin-app/Server.Admin.App.csproj -p:OutputPath=bin/TempBuild/`
Expected: Build thành công không có lỗi biên dịch.

- [ ] **Step 5: Commit Server Admin App changes**
```bash
git add server-admin-app/
git commit -m "feat(server-admin): add tet lucky envelope configuration UI"
```

---

### Task 3: Client Agent - Giao diện & Hiệu ứng Mở Lì Xì Tết

**Files:**
- Modify: `client/src/Client.Agent.Wpf/App.ClientModels.cs`
- Modify: `client/src/Client.Agent.Wpf/App.xaml.cs`
- Modify: `client/src/Client.Agent.Wpf/LoyaltyWindow.xaml`
- Modify: `client/src/Client.Agent.Wpf/LoyaltyWindow.xaml.cs`
- Create: `client/src/Client.Agent.Wpf/LuckyEnvelopeResultWindow.xaml`
- Create: `client/src/Client.Agent.Wpf/LuckyEnvelopeResultWindow.xaml.cs`

- [ ] **Step 1: Định nghĩa DTOs trong `App.ClientModels.cs`**
Thêm `LuckyEnvelopeSettingsResponse`, `OpenLuckyEnvelopeResponse`.

- [ ] **Step 2: Tạo Popup kết quả mở Lì Xì `LuckyEnvelopeResultWindow.xaml`**
Thiết kế cửa sổ popup phong cách Tết với viền vàng kim, nền đỏ ánh kim, hiển thị số phút thưởng to rõ và lời chúc mừng năm mới may mắn.

- [ ] **Step 3: Thêm Tab "🧧 Lì Xì Tết" trong `LoyaltyWindow.xaml`**
Tab có tiêu đề Tết, thẻ thông tin số điểm hiện có, chi phí đổi, nút mở lì xì phong cách Tết.

- [ ] **Step 4: Xử lý logic và tương tác mở Lì Xì trong `LoyaltyWindow.xaml.cs` & `App.xaml.cs`**
- Gọi API lấy cấu hình Lì xì khi mở LoyaltyWindow.
- Nếu `isActive == false` -> ẩn Tab Lì Xì Tết (hoặc hiển thị thông báo sự kiện chưa bắt đầu/kết thúc).
- Khi bấm "Mở Bao Lì Xì": Gọi API `POST /members/:id/loyalty/lucky-envelope/open`, cập nhật `Member` & `Loyalty`, hiển thị `LuckyEnvelopeResultWindow`.

- [ ] **Step 5: Kiểm tra build Client Agent**
Run: `dotnet build client/src/Client.Agent.Wpf/Client.Agent.Wpf.csproj -p:OutputPath=bin/TempBuild/`
Expected: Build thành công không có lỗi biên dịch.

- [ ] **Step 6: Commit Client Agent changes**
```bash
git add client/src/Client.Agent.Wpf/
git commit -m "feat(client-agent): add tet lucky envelope tab and reward animation dialog"
```

---

### Task 4: End-to-End Verification & Synchronize Release Runtimes

**Files:**
- Sync: `release/backend/dist` (nếu cần theo runtime rule)

- [ ] **Step 1: Kiểm tra build toàn bộ giải pháp**
- `backend`: `npm run build`
- `server-admin-app`: `dotnet build`
- `client`: `dotnet build`

- [ ] **Step 2: Chạy kiểm thử luồng thực tế (End-to-end integration flow)**
Kích hoạt sự kiện từ Server Admin App -> Đăng nhập Client -> Mở Lì xì -> Kiểm tra số phút được cộng và điểm bị trừ chính xác.

- [ ] **Step 3: Commit hoàn tất**
```bash
git add .
git commit -m "feat: complete tet lucky red envelope event for server and client"
```
