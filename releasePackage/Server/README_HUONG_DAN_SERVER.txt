======================================================================
         HƯỚNG DẪN CÀI ĐẶT VÀ SỬ DỤNG MÁY CHỦ (SERVER)
======================================================================

1. YÊU CẦU HỆ THỐNG:
- Hệ điều hành: Windows 10 / 11 / Windows Server (64-bit).
- Đã cài đặt Node.js (khuyến nghị bản LTS): https://nodejs.org/
- Kết nối mạng LAN nội bộ ổn định giữa Máy chủ và các Máy trạm.

2. CÁC BƯỚC THỰC HIỆN:
Bước 1: Chạy file "1-Cai-Dat-Server.bat"
        -> Chương trình sẽ tự động cài đặt các thư viện cần thiết và khởi tạo cơ sở dữ liệu.
        -> Chỉ cần chạy file này trong lần đầu tiên cài đặt máy chủ.

Bước 2: Chạy file "2-Khoi-Dong-Server.bat"
        -> Chương trình sẽ khởi động dịch vụ Backend (cổng 9000) và tự động mở giao diện phần mềm quản lý Server Admin App.

3. LƯU Ý KHI SỬ DỤNG:
- Để biết địa chỉ IP của máy chủ cho các máy trạm kết nối tới:
  Mở CMD trên máy chủ, gõ: ipconfig (tìm dòng IPv4 Address, ví dụ: 192.168.1.100).
- Hãy cấu hình tường lửa (Firewall) cho phép mở cổng 9000 để các máy trạm kết nối thông suốt.
- Tài khoản quản trị mặc định hoặc tạo mới trong ứng dụng quản lý.
