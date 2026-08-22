# Tài liệu Thiết kế Kỹ thuật: Sự kiện Lì Xì Tết (Tet Lucky Red Envelope Event)

## 1. Tổng quan
Tính năng **Sự kiện Lì Xì Tết** cho phép phòng máy tổ chức sự kiện khai xuân may mắn trong dịp Tết:
- Hội viên sử dụng Điểm tích lũy (Loyalty Points) để mở phong bao lì xì may mắn trên máy trạm (Client Agent).
- Số phút chơi nhận được sẽ được tạo ngẫu nhiên theo trọng số xác suất may mắn trong khoảng cấu hình `[Min - Max]` phút và cộng trực tiếp vào thời gian chơi khuyến mãi (`playSeconds`) của hội viên.
- Server Admin App có thể cấu hình linh hoạt: Bật/Tắt sự kiện, khoảng thời gian (Từ ngày - Đến ngày), số điểm cần để đổi, khoảng phút thưởng (Min - Max), và giới hạn số lượt mở/hội viên.

---

## 2. Kiến trúc & Thiết kế Dữ liệu

### 2.1 Cấu hình Sự kiện (`app_settings`)
Các thông số sự kiện được lưu trữ trong bảng `app_settings` dưới dạng key-value:
- `LUCKY_ENVELOPE_ENABLED`: `"true"` | `"false"` (Mặc định: `"false"`)
- `LUCKY_ENVELOPE_START_DATE`: Chuỗi ISO Date (ví dụ: `"2026-02-10T00:00:00.000Z"`)
- `LUCKY_ENVELOPE_END_DATE`: Chuỗi ISO Date (ví dụ: `"2026-02-25T23:59:59.999Z"`)
- `LUCKY_ENVELOPE_POINT_COST`: Số nguyên (Mặc định: `10` điểm)
- `LUCKY_ENVELOPE_MIN_MINUTES`: Số nguyên (Mặc định: `60` phút)
- `LUCKY_ENVELOPE_MAX_MINUTES`: Số nguyên (Mặc định: `360` phút)
- `LUCKY_ENVELOPE_MAX_CLAIMS`: Số nguyên (Mặc định: `0` = Không giới hạn)

### 2.2 Ghi nhận Lịch sử Giao dịch
Khi hội viên mở lì xì thành công:
- Trừ điểm trong hệ thống Loyalty snapshot.
- Tạo bản ghi `MemberTransaction`:
  - `type`: `ADJUSTMENT`
  - `amountDelta`: `0`
  - `playSecondsDelta`: `grantedMinutes * 60`
  - `note`: `LOYALTY_REDEEM_LUCKY_ENVELOPE: Đổi {pointCost} điểm nhận lì xì {grantedMinutes} phút ({blessing})`
  - `createdBy`: `"client.loyalty.lucky_envelope"`

---

## 3. Backend API Endpoints (NestJS)

### 3.1 `GET /members/loyalty/lucky-envelope/settings`
- **Query Params**: `memberId?: string`
- **Response**:
```json
{
  "enabled": true,
  "isActive": true,
  "startDate": "2026-02-10T00:00:00.000Z",
  "endDate": "2026-02-25T23:59:59.999Z",
  "pointCost": 10,
  "minMinutes": 60,
  "maxMinutes": 360,
  "maxClaims": 0,
  "claimedCount": 1,
  "canClaim": true,
  "reason": null,
  "updatedAt": "2026-08-22T14:00:00.000Z"
}
```

### 3.2 `PUT /members/loyalty/lucky-envelope/settings`
- **Body**:
```json
{
  "enabled": true,
  "startDate": "2026-02-10T00:00:00.000Z",
  "endDate": "2026-02-25T23:59:59.999Z",
  "pointCost": 10,
  "minMinutes": 60,
  "maxMinutes": 360,
  "maxClaims": 0
}
```
- **Validation**:
  - `pointCost >= 1`
  - `minMinutes >= 1` và `maxMinutes >= minMinutes`
  - `startDate <= endDate`
  - `maxClaims >= 0`

### 3.3 `POST /members/:id/loyalty/lucky-envelope/open`
- **Body**: `{ "createdBy": "client.loyalty.lucky_envelope" }`
- **Transaction Flow**:
  1. Kiểm tra sự kiện active theo thời gian Server.
  2. Kiểm tra `availablePoints >= pointCost`.
  3. Kiểm tra số lần đã nhận nếu `maxClaims > 0`.
  4. Tính toán số phút thưởng ngẫu nhiên theo thuật toán xác suất `[minMinutes, maxMinutes]` (làm tròn bước nhảy 5 phút).
  5. Tăng `playSeconds` cho `Member` thêm `grantedMinutes * 60`.
  6. Tạo bản ghi `MemberTransaction`.
  7. Trả về kết quả mở lì xì kèm câu chúc Tết.

---

## 4. Thuật toán Random Xác suất & Lời chúc Tết

### 4.1 Phân bổ Tỷ lệ May mắn
Khoảng `range = maxMinutes - minMinutes`:
- **Tier 1 (May Mắn - 60% tỉ lệ)**: `[minMinutes, minMinutes + 0.3 * range]`
- **Tier 2 (Đại Cát - 30% tỉ lệ)**: `[minMinutes + 0.3 * range, minMinutes + 0.7 * range]`
- **Tier 3 (Thần Tài Nổ Hũ - 10% tỉ lệ)**: `[minMinutes + 0.7 * range, maxMinutes]`

Số phút trong mỗi tier được làm tròn theo bước nhảy 5 phút (`Math.round(val / 5) * 5`) và đảm bảo trong khoảng `[minMinutes, maxMinutes]`.

### 4.2 Danh sách Lời chúc Tết
- 🌸 *“Chúc mừng năm mới! Vạn sự như ý - Tỉ sự như mơ!”*
- 🧧 *“Khai xuân đắc lộc! Mã đáo thành công!”*
- 💰 *“Tấn tài tấn lộc - Công danh rạng rỡ!”*
- 🎆 *“Xuân an khang - Đắc lộc toàn niên!”*
- ⭐ *“Năm mới phát tài - Chơi game bất bại!”*

---

## 5. Thiết kế Giao diện Người dùng

### 5.1 Server Admin App (WPF)
- Thêm nhóm cấu hình **"🧧 Sự kiện Lì Xì Tết"** trong tab **Mini Game**:
  - CheckBox Bật/Tắt sự kiện.
  - DatePicker `Từ ngày` & `Đến ngày`.
  - TextBox `Điểm / lượt mở`, `Phút tối thiểu`, `Phút tối đa`, `Giới hạn lượt/hội viên`.
  - Nút **Lưu cấu hình** & **Làm mới**.

### 5.2 Client Agent (WPF)
- Trong `LoyaltyWindow.xaml`:
  - Thêm tab **"🧧 Lì Xì Tết"** ở danh mục bên trái (hiển thị khi sự kiện đang active).
  - Giao diện phong cách Tết với tông màu đỏ - vàng kim (`#DC2626`, `#F59E0B`).
  - Thẻ thông tin hiển thị số điểm của hội viên, chi phí mở và nút **"🧧 Mở Phong Bao Lì Xì"**.
- Dialog kết quả mở lì xì:
  - Hiển thị hoạt họa mở bao lì xì, số phút thưởng `+X Phút Chơi` và câu chúc Tết may mắn.
  - Đồng bộ tức thì điểm và thời gian chơi khuyến mãi lên thanh trạng thái máy trạm.

---

## 6. Kế hoạch Kiểm thử & Xác thực
1. **Backend Unit / Integration Tests**:
   - Kiểm tra validation cấu hình settings (min/max phút, ngày bắt đầu/kết thúc).
   - Kiểm tra transaction mở lì xì: trừ điểm đúng, cộng phút đúng, ghi transaction log đúng.
   - Kiểm tra xử lý lỗi khi không đủ điểm hoặc hết thời gian sự kiện hoặc vượt giới hạn lượt.
2. **Desktop Build Verification**:
   - Build `backend` (`npm run build`).
   - Build `server-admin-app` (`dotnet build`).
   - Build `client/src/Client.Agent.Wpf` (`dotnet build`).
