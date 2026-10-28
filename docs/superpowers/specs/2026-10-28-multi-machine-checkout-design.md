# Thiết kế Kỹ thuật: Tính năng Thanh toán Gộp Nhiều Máy Trạm (Multi-Machine Checkout)

## 1. Mục tiêu & Bối cảnh
- **Bối cảnh:** Khi nhiều khách vãng lai đi theo nhóm (ví dụ: 2-5 người) vào phòng máy, khi ra về thường có 1 người đại diện đứng ra thanh toán toàn bộ tiền giờ chơi và tiền dịch vụ (nước, đồ ăn...) cho cả nhóm.
- **Vấn đề hiện tại:**
  - Thu ngân Ctrl + Click chọn nhiều máy trạm nhưng khi mở menu chuột phải, mục "Thanh toán..." bị mờ (`Disabled`).
  - Sự kiện chuột phải `PreviewMouseRightButtonDown` chưa bảo lưu đúng vùng chọn đa máy nếu dòng click chuột chưa được đánh dấu.
  - Mã nguồn hiện tại mới chỉ có hàm mẫu `ShowMultipleMachinesBillingDetailsAsync` hiển thị bảng kê xem trước với nút "Đóng", chưa có tính năng thực hiện thanh toán, trừ tiền dịch vụ hay khóa máy.
- **Mục tiêu:** Cho phép thu ngân chọn nhiều máy (Ctrl + Click), mở cửa sổ thanh toán gộp `MultiPaymentWindow`, xem bảng kê chi tiết từng máy và bấm 1 nút để thanh toán toàn bộ tiền giờ + dịch vụ và khóa tất cả các máy đã chọn.

---

## 2. Luồng Nghiệp vụ & Trải nghiệm Người dùng (User Flow)

```
[Thu ngân Ctrl + Click chọn nhiều máy]
            │
            ▼
[Chuột phải vào một trong các máy đã chọn]
            │
            ▼
[Menu hiện: "Thanh toán gộp (X máy)..." (Sáng/Enabled)]
            │
            ▼
[Cửa sổ MultiPaymentWindow mở ra]
  ├── Bảng danh sách chi tiết từng máy (Giờ chơi, Tiền giờ, Tiền dịch vụ, Tổng)
  ├── Khối tổng kết (Tổng tiền giờ + Tổng dịch vụ = TỔNG CẦN THU)
  └── Nút "Thanh toán & Khóa X máy"
            │
            ▼
[Thu ngân bấm "Thanh toán & Khóa X máy"]
            │
            ▼
[Hệ thống thực hiện tuần tự cho từng máy]
  ├── Gọi API thanh toán các order dịch vụ chưa trả (nếu có)
  └── Gửi lệnh khóa máy (LOCK) kết thúc phiên
            │
            ▼
[Ghi log giao dịch tổng hợp, làm mới danh sách máy trạm & thông báo thành công]
```

---

## 3. Chi tiết Thiết kế & Triển khai

### 3.1. Cập nhật Menu chuột phải ([MainWindow.Machines.cs](file:///i:/servermanagerbilling/server-admin-app/MainWindow/MainWindow.Machines.cs))
1. **Bảo lưu vùng chọn chuột phải:**
   - Trong `MachinesDataGridRow_PreviewMouseRightButtonDown`:
     ```csharp
     if (!row.IsSelected)
     {
         MachinesDataGrid.SelectedItems.Clear();
         row.IsSelected = true;
     }
     row.Focus();
     ```
   - Điều này đảm bảo khi người dùng đã Ctrl+Click chọn nhiều máy, click chuột phải vào 1 máy trong số đó sẽ **không** bị xóa các máy còn lại.

2. **Cập nhật trạng thái menu chuột phải:**
   - Trong `ApplyMachineContextMenuState`:
     - Lấy danh sách máy đã chọn: `var selectedMachines = GetSelectedMachinesForBilling();`
     - Lọc danh sách máy hợp lệ: `var eligible = selectedMachines.Where(IsGuestBillingEligibleMachine).ToList();`
     - Nếu `eligible.Count > 1`:
       - `ContextViewMachineBillingMenuItem.IsEnabled = true;`
       - `ContextViewMachineBillingMenuItem.Header = $"Thanh toán gộp ({eligible.Count} máy)...";`
     - Nếu `eligible.Count == 1`:
       - `ContextViewMachineBillingMenuItem.IsEnabled = true;`
       - `ContextViewMachineBillingMenuItem.Header = "Thanh toán...";`

3. **Điều hướng sự kiện click menu:**
   - Khi `eligible.Count > 1`, mở cửa sổ `MultiPaymentWindow`.

---

### 3.2. Cửa sổ chuyên dụng `MultiPaymentWindow.xaml`
- **Vị trí file:**
  - `server-admin-app/Windows/MultiPaymentWindow.xaml`
  - `server-admin-app/Windows/MultiPaymentWindow.xaml.cs`
- **Giao diện & Cấu trúc:**
  - Header: Tiêu đề "Tổng hợp thanh toán nhóm - X máy" kèm badge số lượng.
  - Bảng DataGrid:
    - `Máy trạm`: Tên máy (ví dụ Máy 03, Máy 04).
    - `Khách`: Tên khách vãng lai hoặc tên phiên.
    - `Thời gian chơi`: hh:mm (ví dụ 01:45).
    - `Đơn giá`: Đơn giá phiên tính tiền (VND/giờ).
    - `Tiền giờ`: Số tiền giờ chơi tạm tính.
    - `Tiền dịch vụ`: Tổng tiền các món đồ ăn, nước uống chưa trả.
    - `Tổng cộng`: Tiền giờ + Dịch vụ của máy đó.
  - Khối Summary:
    - Tổng tiền giờ chơi: `... VND`
    - Tổng tiền dịch vụ: `... VND`
    - **TỔNG CỘNG CẦN THU: `... VND`** (Font to, màu xanh `#1E40AF` trên nền xanh dịu `#EEF2FF`).
  - Footer Action:
    - Nút "Hủy / Đóng" (Background White, Border xám).
    - Nút "Thanh toán & Khóa X máy" (Background `#10B981`, Foreground White, Icon ổ khóa + tiền).

---

### 3.3. Logic Xử lý Thanh toán & Gọi API Backend
Khi thu ngân bấm "Thanh toán & Khóa X máy":
1. **Xác nhận số tiền:**
   - Hiển thị thông báo xác nhận:
     `Xác nhận thu tổng cộng {totalAmount:N0} VND và kết thúc phiên cho {machines.Count} máy?`
2. **Xử lý từng máy:**
   - Bước 1: Kiểm tra nếu máy có tiền dịch vụ > 0, gọi API:
     `POST /services/pcs/{machine.Id}/orders/pay` với payload `{ requestedBy = "admin.desktop", note = "Thanh toán gộp" }`.
   - Bước 2: Gửi lệnh khóa máy:
     `POST /pcs/{machine.Id}/lock` với `{ requestedBy = "admin.desktop" }`.
3. **Ghi nhật ký & Đồng bộ:**
   - Ghi vào Service Log: `[{HH:mm:ss}] Đã thanh toán gộp {Count} máy: {totalAmount:N0} VND (Giờ chơi: {play:N0}, Dịch vụ: {service:N0})`.
   - Gọi `await RefreshMachinesAsync();` và `await RefreshTransactionLogsAsync();`.
   - Đóng cửa sổ `MultiPaymentWindow`.
   - Hiển thị thông báo hoàn tất thanh toán thành công.

---

## 4. Xử lý các trường hợp biên (Edge Cases)
- **Chọn lẫn cả máy hội viên:**
  - Hệ thống tự động lọc bỏ các máy hội viên trả trước (vì hội viên tự trừ số dư tài khoản, không phải thanh toán kiểu khách vãng lai) và chỉ đưa các máy khách vãng lai / phiên nợ vào bảng kê thanh toán gộp.
- **Máy không có dịch vụ:**
  - Hiển thị tiền dịch vụ = 0 VND, chỉ tính tiền giờ chơi.
- **Máy có dịch vụ nhưng không có tiền giờ (hoặc ngược lại):**
  - Vẫn tính đúng tổng tiền và đóng order dịch vụ.
- **Lỗi mạng khi gọi API:**
  - Sử dụng try/catch có thông báo rõ ràng cho từng máy nếu xảy ra lỗi cục bộ, không làm crash ứng dụng.

---

## 5. Kế hoạch Kiểm thử & Xác minh
1. Kiểm tra build `dotnet build server-admin-app/Server.Admin.App.csproj` đạt 0 Error, 0 Warning liên quan.
2. Khởi chạy `Server.Admin.App.exe`:
   - Chọn 1 máy vãng lai -> Menu hiển thị "Thanh toán...".
   - Ctrl + Click chọn 2 máy hoặc 3 máy vãng lai -> Menu hiển thị "Thanh toán gộp (3 máy)...".
   - Chuột phải vào máy đã chọn -> Đảm bảo không bị mất các máy đã chọn khác.
   - Click "Thanh toán gộp" -> Cửa sổ `MultiPaymentWindow` mở ra hiển thị đầy đủ chi tiết từng máy và tổng cộng.
   - Bấm "Thanh toán & Khóa máy" -> Các máy được khóa thành công, dịch vụ được thanh toán, doanh thu được ghi nhận.
