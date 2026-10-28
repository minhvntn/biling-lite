# Multi-Machine Checkout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cho phép thu ngân Ctrl + Click chọn nhiều máy trạm vãng lai, mở cửa sổ thanh toán gộp `MultiPaymentWindow` và thanh toán toàn bộ tiền giờ + dịch vụ kèm khóa máy cùng lúc chỉ với 1 cú click.

**Architecture:** Nâng cấp menu ngữ cảnh trên `MachinesDataGrid` để bảo lưu đa vùng chọn khi chuột phải và kích hoạt tùy chọn "Thanh toán gộp (X máy)..."; xây dựng cửa sổ chuyên dụng `MultiPaymentWindow.xaml` với bảng kê chi tiết và khối tổng kết; thực hiện thanh toán order dịch vụ qua API `/services/pcs/{id}/orders/pay` và khóa máy qua API `/pcs/{id}/lock`.

**Tech Stack:** C# .NET 10, WPF, XAML, SharpVectors (SVG), HttpClient REST API.

---

### Task 1: Preserve Multi-Selection on Right Click & Enable Multi-Machine Checkout in Context Menu

**Files:**
- Modify: `server-admin-app/MainWindow/MainWindow.Machines.cs:1068-1190`

- [ ] **Step 1: Update `MachinesDataGridRow_PreviewMouseRightButtonDown` to preserve multi-selection**
  In `MainWindow.Machines.cs`, check `if (!row.IsSelected)` before clearing or altering selection so right clicking on any already-selected row preserves all selected rows.

- [ ] **Step 2: Update `ApplyMachineContextMenuState` to evaluate all selected machines**
  Query `GetSelectedMachinesForBilling()`, filter by `IsGuestBillingEligibleMachine`, and when `eligible.Count > 1`:
  - Set `ContextViewMachineBillingMenuItem.IsEnabled = true;`
  - Set `ContextViewMachineBillingMenuItem.Header = $"Thanh toán gộp ({eligible.Count} máy)...";`
  When `eligible.Count == 1`:
  - Set `ContextViewMachineBillingMenuItem.Header = "Thanh toán...";`

---

### Task 2: Create `MultiPaymentWindow.xaml` and `MultiPaymentWindow.xaml.cs`

**Files:**
- Create: `server-admin-app/Windows/MultiPaymentWindow.xaml`
- Create: `server-admin-app/Windows/MultiPaymentWindow.xaml.cs`

- [ ] **Step 1: Create `MultiPaymentWindow.xaml`**
  Design sleek window with:
  - Header: Icon + Title "Tổng hợp thanh toán nhóm" + Badge số lượng máy
  - DataGrid displaying: Tên máy, Khách/Phiên, Thời gian chơi, Đơn giá/giờ, Tiền giờ chơi, Tiền dịch vụ, Tổng cộng từng máy
  - Summary Card: Tổng tiền giờ, Tổng tiền dịch vụ, Tổng cộng cần thu (lớn, nổi bật `#1E40AF` trên nền `#EEF2FF`)
  - Footer Buttons: Nút "Hủy / Đóng" và Nút "Thanh toán & Khóa X máy" (`#10B981`, White text, icon)

- [ ] **Step 2: Create `MultiPaymentWindow.xaml.cs`**
  Implement:
  - ViewModel / row mapping for each machine (calculating precise play amount and fetching unpaid service amounts via `_httpClient`)
  - `CheckoutButton_Click` event handler:
    1. Confirm popup showing total amount and machine list
    2. Loop over each machine:
       - If machine has unpaid services, call `POST /services/pcs/{id}/orders/pay`
       - Send lock command: `POST /pcs/{id}/lock`
    3. Log transaction summary to service logs
    4. Call callbacks to refresh machines & logs
    5. Close window and show success message

---

### Task 3: Connect `ContextViewMachineBillingMenuItem_Click` to `MultiPaymentWindow`

**Files:**
- Modify: `server-admin-app/MainWindow/MainWindow.Machines.cs:2622-2665`

- [ ] **Step 1: Replace old code-behind popup call with `MultiPaymentWindow`**
  In `ContextViewMachineBillingMenuItem_Click`, instantiate and show `MultiPaymentWindow` when `eligibleMachines.Count > 1`.

---

### Task 4: Build, Verify and Restart Application

**Files:**
- Run build and restart commands

- [ ] **Step 1: Compile `Server.Admin.App.csproj`**
  Run `dotnet build server-admin-app/Server.Admin.App.csproj` and verify 0 warnings and 0 errors.

- [ ] **Step 2: Restart `Server.Admin.App.exe`**
  Launch process and verify smooth execution.
