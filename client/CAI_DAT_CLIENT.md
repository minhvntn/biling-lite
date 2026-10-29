
  
Cập nhật Client siêu nhanh: Sau này app có tính năng mới hay sửa giao diện, bạn chỉ cần thay file .exe trên ổ G: ở máy chủ là toàn bộ dàn máy con được cập nhật ngay lập tức!

**HOÀN TOÀN ĐƯỢC! Đây là cách rất nhiều phòng máy Bootrom hay dùng để không cần phải mở băng (mở Super) Image.**

Khi để ở ổ **G:** trên Server, Bootrom sẽ ánh xạ ổ G: này xuống toàn bộ máy con. Bạn chỉ cần làm theo 3 bước cực kỳ đơn giản sau ngay trên máy chủ:

---

### Bước 1: Đặt bộ Client vào ổ G: trên Server
1. Chép thư mục `Client-Agent` vào ổ G:, ví dụ đường dẫn là:
   ```text
   G:\Client-Agent\
   ```
2. Mở file `G:\Client-Agent\appsettings.json` chỉnh IP máy chủ:
   - Điền đúng IP máy chủ quản lý tính tiền (ví dụ `http://192.168.1.100:39090`).
   - Mục `"AgentId": ""` bạn cứ **để trống**, máy con tự động lấy tên máy (như `MAY-01`, `MAY-02`...) khi chạy.

---

### Bước 2: Tạo 1 file script tắt GCafe và bật Client mới
Tạo một file đặt tên là `StartClient.bat` nằm ngay trong `G:\Client-Agent\` với nội dung sau:

```cmd
@echo off
:: 1. Tắt tiến trình GCafe Client cũ đang chạy ngầm trên máy con
taskkill /f /im GCafeClient.exe >nul 2>&1
taskkill /f /im ClientApp.exe >nul 2>&1
net stop "GCafeClient" >nul 2>&1

:: 2. Đợi 1 giây
timeout /t 1 /nobreak >nul

:: 3. Khởi chạy Client mới từ ổ G:
start "" "G:\Client-Agent\Client.Agent.Wpf.exe"
```

---

### Bước 3: Cho Bootrom tự động gọi file này khi máy con khởi động
Tùy vào phần mềm Bootrom phòng máy đang dùng (Icafe8, GCafe Mavin, CCBoot...):

1. **Nếu dùng Icafe8 / GCafe Mavin / CCBoot**:
   - Mở bảng quản trị Bootrom Server lên.
   - Tìm đến mục **Client Management (Quản lý máy trạm)** hoặc **Run Batch / Startup Task (Tác vụ khởi động)**.
   - Thêm đường dẫn file chạy:
     ```text
     G:\Client-Agent\StartClient.bat
     ```
   *(hoặc điền thẳng lệnh: `start "" "G:\Client-Agent\Client.Agent.Wpf.exe"`)*

2. **Cách dự phòng qua Menu Game (nếu Bootrom không có Run Batch)**:
   - Trong Menu Game trên máy chủ, bạn thêm 1 mục Startup tự chạy ngầm cùng Windows trỏ đến `G:\Client-Agent\StartClient.bat`.

---

### Ưu điểm của cách này:
- **Không lo lỗi Image**: Tuyệt đối không cần mở băng Super Client.
- **Cập nhật Client siêu nhanh**: Sau này app có tính năng mới hay sửa giao diện, bạn chỉ cần thay file `.exe` trên ổ G: ở máy chủ là **toàn bộ dàn máy con được cập nhật ngay lập tức**!