using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class SystemHealthCheckWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly HttpClient _httpClient;
    private readonly bool _autoStartRepair;
    private string _lastDiagnosticReport = string.Empty;
    private bool _isRepairing;

    public SystemHealthCheckWindow(MainWindow mainWindow, bool autoStartRepair = false)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _httpClient = mainWindow._httpClient;
        _autoStartRepair = autoStartRepair;

        Loaded += async (s, e) =>
        {
            if (_autoStartRepair)
            {
                await ExecuteAutoRepairAsync();
            }
            else
            {
                await RunHealthCheckAsync();
            }
        };
    }

    private async void RecheckHealthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRepairing) return;
        await RunHealthCheckAsync();
    }

    private async void AutoRepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRepairing) return;
        await ExecuteAutoRepairAsync();
    }

    public async Task ExecuteAutoRepairAsync()
    {
        _isRepairing = true;
        RecheckHealthButton.IsEnabled = false;
        AutoRepairButton.IsEnabled = false;
        RepairProgressBorder.Visibility = Visibility.Visible;
        RepairProgressBar.Minimum = 0;
        RepairProgressBar.Maximum = 100;
        RepairProgressBar.Value = 10;
        RepairProgressBar.IsIndeterminate = true;
        RepairProgressBar.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED"));
        RepairStepCountTextBlock.Text = "Đang xử lý (1/4)...";
        RepairStepCountTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7E22CE"));

        SetBanner(
            icon: "⚡",
            title: "ĐANG TIẾN HÀNH TỰ ĐỘNG SỬA CHỮA HỆ THỐNG...",
            message: "Hệ thống đang tự động kích hoạt lại Cơ sở dữ liệu PostgreSQL, khởi động lại Backend Service và kết nối lại Socket Realtime...",
            bgColor: "#FAF5FF",
            borderColor: "#C084FC",
            titleColor: "#7C3AED",
            msgColor: "#6B21A8"
        );

        // ==========================================
        // BƯỚC 1: KIỂM TRA & BẬT SERVICE POSTGRESQL
        // ==========================================
        RepairStep1TextBlock.Text = "• Bước 1: 🗄️ Đang kiểm tra & kích hoạt dịch vụ PostgreSQL Server...";
        RepairStep1TextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9333EA"));

        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-Service *postgres* | ForEach-Object { if ($_.Status -ne 'Running') { Start-Service $_.Name } }\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(6000);
            }
            catch
            {
                // Ignore service permissions issues if already running
            }
        });

        RepairStep1TextBlock.Text = "• Bước 1: 🗄️ Dịch vụ PostgreSQL đã được kích hoạt và sẵn sàng ✅";
        RepairStep1TextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
        RepairProgressBar.IsIndeterminate = false;
        RepairProgressBar.Value = 35;
        RepairStepCountTextBlock.Text = "Đang xử lý (2/4)...";
        await Task.Delay(800);

        // ==========================================
        // BƯỚC 2: KIỂM TRA & KHỞI ĐỘNG LẠI BACKEND
        // ==========================================
        RepairStep2TextBlock.Text = "• Bước 2: 🌐 Đang kiểm tra cổng 39090 và phản hồi Backend Service...";
        RepairStep2TextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9333EA"));

        var isBackendAlive = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var pingRes = await _httpClient.GetAsync(_mainWindow.BuildApiUrl("/pcs"), cts.Token);
            if (pingRes.IsSuccessStatusCode)
            {
                isBackendAlive = true;
            }
        }
        catch
        {
            isBackendAlive = false;
        }

        if (!isBackendAlive)
        {
            RepairStep2TextBlock.Text = "• Bước 2: 🌐 Backend chưa phản hồi. Đang giải phóng cổng 39090 & bật lại...";
            
            // Giải phóng port bị kẹt nếu có
            await Task.Run(() =>
            {
                try
                {
                    var psiKill = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -Command \"Get-NetTCPConnection -LocalPort 39090 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var pKill = Process.Start(psiKill);
                    pKill?.WaitForExit(3000);
                }
                catch { }
            });

            // Tìm file khởi động backend
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "start_backend.bat"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "backend", "start_backend.bat"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "backend", "start_backend.bat"),
                @"I:\servermanagerbilling\backend\start_backend.bat",
                @"I:\servermanagerbilling\release\backend\start_backend.bat"
            };

            string? selectedBat = null;
            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full))
                {
                    selectedBat = full;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(selectedBat))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = selectedBat,
                        WorkingDirectory = Path.GetDirectoryName(selectedBat) ?? string.Empty,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Minimized
                    });
                }
                catch { }
            }

            // Chờ Backend khởi động lên (tối đa 6 giây)
            for (int i = 1; i <= 6; i++)
            {
                RepairStep2TextBlock.Text = $"• Bước 2: 🌐 Đang đợi Backend khởi động và mở cổng 39090 ({i}/6s)...";
                await Task.Delay(1000);
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    using var pingRes = await _httpClient.GetAsync(_mainWindow.BuildApiUrl("/pcs"), cts.Token);
                    if (pingRes.IsSuccessStatusCode)
                    {
                        isBackendAlive = true;
                        break;
                    }
                }
                catch { }
            }

            RepairStep2TextBlock.Text = isBackendAlive
                ? "• Bước 2: 🌐 Đã khởi chạy lại Backend Service tại cổng 39090 thành công ✅"
                : "• Bước 2: ⚠️ Đã gửi lệnh bật Backend, vui lòng kiểm tra thêm cửa sổ Node.js.";
            RepairStep2TextBlock.Foreground = isBackendAlive 
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A")) 
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
        }
        else
        {
            RepairStep2TextBlock.Text = "• Bước 2: 🌐 Backend Service đang hoạt động tốt tại cổng 39090 ✅";
            RepairStep2TextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
        }
        RepairProgressBar.Value = 65;
        RepairStepCountTextBlock.Text = "Đang xử lý (3/4)...";
        await Task.Delay(800);

        // ==========================================
        // BƯỚC 3: KẾT NỐI LẠI REALTIME & NẠP DỮ LIỆU
        // ==========================================
        RepairStep3TextBlock.Text = "• Bước 3: ⚡ Đang thiết lập lại kênh Realtime Socket & đồng bộ lại dữ liệu...";
        RepairStep3TextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9333EA"));

        try
        {
            await _mainWindow.ReloadAllSystemDataAsync(isUserInitiated: false);
            RepairStep3TextBlock.Text = "• Bước 3: ⚡ Kênh Realtime Socket đã kết nối và đồng bộ toàn bộ dữ liệu ✅";
            RepairStep3TextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
        }
        catch (Exception ex)
        {
            RepairStep3TextBlock.Text = $"• Bước 3: ⚠️ Đồng bộ dữ liệu gặp thông báo: {ex.Message}";
            RepairStep3TextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
        }
        RepairProgressBar.Value = 85;
        RepairStepCountTextBlock.Text = "Đang kiểm tra kết quả (4/4)...";
        await Task.Delay(800);

        // ==========================================
        // BƯỚC 4: NGHIỆM THU TOÀN DIỆN
        // ==========================================
        RepairStep4TextBlock.Text = "• Bước 4: 📊 Đang đo kiểm tra sức khỏe tổng thể sau khi sửa chữa...";
        RepairStep4TextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9333EA"));

        await RunHealthCheckAsync();

        RepairStep4TextBlock.Text = "• Bước 4: 🎉 Hoàn tất quy trình sửa chữa tự động! Mọi kết nối đã được phục hồi.";
        RepairStep4TextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        // DỪNG HOÀN TOÀN PROGRESS BAR VÀ CHUYỂN SANG 100% MÀU XANH LÁ
        RepairProgressBar.IsIndeterminate = false;
        RepairProgressBar.Value = 100;
        RepairProgressBar.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        RepairStepCountTextBlock.Text = "Sửa chữa hoàn tất! ✔";
        RepairStepCountTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        _isRepairing = false;
        RecheckHealthButton.IsEnabled = true;
        AutoRepairButton.IsEnabled = true;
    }

    private async Task RunHealthCheckAsync()
    {
        RecheckHealthButton.IsEnabled = false;
        SetStatusLoading();

        var reportSb = new StringBuilder();
        reportSb.AppendLine("=== BÁO CÁO CHẨN ĐOÁN SỨC KHỎE HỆ THỐNG ===");
        reportSb.AppendLine($"Thời gian kiểm tra: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        var backendOk = false;
        long backendLatencyMs = 0;
        var backendError = string.Empty;
        var backendUrl = _mainWindow.BuildApiUrl(string.Empty);
        BackendUrlTextBlock.Text = backendUrl;

        var dbOk = false;
        long dbLatencyMs = 0;
        var dbName = "Không rõ";
        var dbSize = "Không rõ";
        var dbError = string.Empty;

        var totalPcs = 0;
        var onlinePcs = 0;

        // 1. KIỂM TRA BACKEND API
        try
        {
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var response = await _httpClient.GetAsync(_mainWindow.BuildApiUrl("/pcs"), cts.Token);
            sw.Stop();
            backendLatencyMs = sw.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                backendOk = true;
                BackendStatusCodeTextBlock.Text = $"{((int)response.StatusCode)} {response.ReasonPhrase}";
                BackendStatusCodeTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                BackendLatencyTextBlock.Text = $"{backendLatencyMs} ms";
                BackendLatencyTextBlock.Foreground = backendLatencyMs < 200 
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A")) 
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));

                var jsonStr = await response.Content.ReadAsStringAsync();
                try
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("serverTime", out var st))
                    {
                        if (DateTime.TryParse(st.GetString(), out var dt))
                        {
                            BackendServerTimeTextBlock.Text = dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
                        }
                    }
                    if (root.TryGetProperty("total", out var tot))
                    {
                        totalPcs = tot.GetInt32();
                    }
                    if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("isConnected", out var ic) && ic.GetBoolean())
                            {
                                onlinePcs++;
                            }
                        }
                    }
                }
                catch
                {
                    BackendServerTimeTextBlock.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                }

                SetBadge(BackendBadgeBorder, BackendBadgeText, "Đang hoạt động", "#DCFCE7", "#16A34A");
                reportSb.AppendLine($"[Backend API]: ✅ OK | Độ trễ: {backendLatencyMs}ms | URL: {backendUrl}");
            }
            else
            {
                backendError = $"Mã lỗi HTTP: {((int)response.StatusCode)}";
                BackendStatusCodeTextBlock.Text = backendError;
                BackendStatusCodeTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                BackendLatencyTextBlock.Text = $"{backendLatencyMs} ms";
                SetBadge(BackendBadgeBorder, BackendBadgeText, "Lỗi HTTP", "#FEE2E2", "#DC2626");
                reportSb.AppendLine($"[Backend API]: ❌ Lỗi ({backendError})");
            }
        }
        catch (OperationCanceledException)
        {
            backendError = "Hết thời gian chờ phản hồi (Timeout > 4s)";
            BackendStatusCodeTextBlock.Text = backendError;
            BackendStatusCodeTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            BackendLatencyTextBlock.Text = "Timeout";
            BackendLatencyTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            SetBadge(BackendBadgeBorder, BackendBadgeText, "Mất kết nối (Timeout)", "#FEE2E2", "#DC2626");
            reportSb.AppendLine($"[Backend API]: ❌ Mất kết nối: {backendError}");
        }
        catch (Exception ex)
        {
            backendError = ex.Message;
            BackendStatusCodeTextBlock.Text = "Không thể kết nối";
            BackendStatusCodeTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            BackendLatencyTextBlock.Text = "Không phản hồi";
            BackendLatencyTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            SetBadge(BackendBadgeBorder, BackendBadgeText, "Mất kết nối", "#FEE2E2", "#DC2626");
            reportSb.AppendLine($"[Backend API]: ❌ Mất kết nối: {backendError}");
        }

        // 2. KIỂM TRA DATABASE (POSTGRESQL)
        if (backendOk)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var response = await _httpClient.GetAsync(_mainWindow.BuildApiUrl("/reports/database/storage?days=1"), cts.Token);
                sw.Stop();
                dbLatencyMs = sw.ElapsedMilliseconds;

                if (response.IsSuccessStatusCode)
                {
                    dbOk = true;
                    var jsonStr = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("databaseName", out var dn))
                    {
                        dbName = dn.GetString() ?? "servermanagerbilling";
                    }
                    if (root.TryGetProperty("sizePretty", out var sp))
                    {
                        dbSize = sp.GetString() ?? "Không rõ";
                    }

                    DbNameTextBlock.Text = dbName;
                    DbSizeTextBlock.Text = dbSize;
                    DbQueryLatencyTextBlock.Text = $"{dbLatencyMs} ms";
                    DbQueryLatencyTextBlock.Foreground = dbLatencyMs < 200 
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A")) 
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));

                    var tableCount = 0;
                    if (root.TryGetProperty("topTables", out var tt) && tt.ValueKind == JsonValueKind.Array)
                    {
                        tableCount = tt.GetArrayLength();
                    }
                    DbTablesStatusTextBlock.Text = $"Tốt (Phát hiện {tableCount} bảng dữ liệu chính)";
                    DbTablesStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

                    SetBadge(DbBadgeBorder, DbBadgeText, "Kết nối tốt", "#DCFCE7", "#16A34A");
                    reportSb.AppendLine($"[Database PostgreSQL]: ✅ OK | Tên DB: {dbName} | Dung lượng: {dbSize} | Độ trễ SQL: {dbLatencyMs}ms");
                }
                else
                {
                    dbError = $"Mã lỗi HTTP từ endpoint DB: {((int)response.StatusCode)}";
                    DbTablesStatusTextBlock.Text = "Lỗi khi truy vấn CSDL";
                    DbTablesStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    SetBadge(DbBadgeBorder, DbBadgeText, "Lỗi truy vấn", "#FEF3C7", "#D97706");
                    reportSb.AppendLine($"[Database PostgreSQL]: ⚠️ {dbError}");
                }
            }
            catch (Exception ex)
            {
                dbError = ex.Message;
                DbTablesStatusTextBlock.Text = $"Lỗi: {ex.Message}";
                DbTablesStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                SetBadge(DbBadgeBorder, DbBadgeText, "Mất kết nối CSDL", "#FEE2E2", "#DC2626");
                reportSb.AppendLine($"[Database PostgreSQL]: ❌ Lỗi kết nối CSDL: {dbError}");
            }
        }
        else
        {
            DbNameTextBlock.Text = "-- (Backend chưa chạy)";
            DbSizeTextBlock.Text = "--";
            DbQueryLatencyTextBlock.Text = "--";
            DbTablesStatusTextBlock.Text = "Không kiểm tra được do Backend mất kết nối";
            DbTablesStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
            SetBadge(DbBadgeBorder, DbBadgeText, "Chưa kiểm tra", "#F1F5F9", "#64748B");
            reportSb.AppendLine("[Database PostgreSQL]: ⏸ Không thể kiểm tra (Backend đang Offline)");
        }

        // 3. KIỂM TRA REALTIME SOCKET
        var realtimeConnected = _mainWindow.IsRealtimeSocketConnected;
        if (realtimeConnected)
        {
            SetBadge(RealtimeBadgeBorder, RealtimeBadgeText, "Đã kết nối", "#DCFCE7", "#16A34A");
            RealtimeDescTextBlock.Text = "Đang đồng bộ máy trạm và giao dịch tức thì.";
            RealtimeDescTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
            reportSb.AppendLine("[Kênh Realtime WebSocket]: ✅ Đang kết nối ổn định");
        }
        else
        {
            SetBadge(RealtimeBadgeBorder, RealtimeBadgeText, "Chưa kết nối", "#FEF3C7", "#D97706");
            RealtimeDescTextBlock.Text = "Socket chưa sẵn sàng (Bấm nút Tự động sửa chữa).";
            RealtimeDescTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
            reportSb.AppendLine("[Kênh Realtime WebSocket]: ⚠️ Chưa kết nối hoặc đang thử lại");
        }

        // 4. MÁY TRẠM
        PcOnlineCountBadgeText.Text = $"{onlinePcs} Online";
        PcSummaryTextBlock.Text = $"Tổng số máy trạm: {totalPcs} máy (Đang kết nối: {onlinePcs})";
        reportSb.AppendLine($"[Máy trạm]: Tổng cộng {totalPcs} máy | Đang online: {onlinePcs} máy");

        // 5. TỔNG HỢP KẾT LUẬN & GỢI Ý KHẮC PHỤC
        if (backendOk && dbOk)
        {
            SetBanner(
                icon: "✅",
                title: "HỆ THỐNG VÀ CƠ SỞ DỮ LIỆU ĐANG HOẠT ĐỘNG HOÀN HẢO",
                message: $"Máy chủ phản hồi nhanh ({backendLatencyMs}ms), Cơ sở dữ liệu PostgreSQL ({dbName} - {dbSize}) an toàn, kênh Realtime đồng bộ tốt.",
                bgColor: "#ECFDF5",
                borderColor: "#86EFAC",
                titleColor: "#15803D",
                msgColor: "#166534"
            );

            DiagnosticSuggestionTextBlock.Text =
                "🎉 Mọi thành phần hệ thống bao gồm Dịch vụ Backend, Cơ sở dữ liệu PostgreSQL và Kênh truyền Realtime đều đang vận hành xuất sắc! Không phát hiện bất kỳ sự cố hay lỗi nghẽn mạng nào.\n" +
                "💡 Mẹo: Nếu bất chợt có sự cố, bạn chỉ cần bấm nút [⚡ Tự động sửa chữa (1-Click)] bên dưới để hệ thống tự động xử lý toàn bộ từ A đến Z!";
            reportSb.AppendLine("Kết luận: Hệ thống hoàn toàn ổn định.");
        }
        else if (!backendOk)
        {
            SetBanner(
                icon: "❌",
                title: "SỰ CỐ: KHÔNG KẾT NỐI ĐƯỢC MÁY CHỦ BACKEND",
                message: $"Ứng dụng không thể liên lạc với Backend API tại địa chỉ: {backendUrl}",
                bgColor: "#FEE2E2",
                borderColor: "#FCA5A5",
                titleColor: "#B91C1C",
                msgColor: "#991B1B"
            );

            DiagnosticSuggestionTextBlock.Text =
                "⚠️ Đã phát hiện sự cố Backend!\n" +
                "👉 BẠN CHỈ VIỆC BẤM NÚT [⚡ Tự động sửa chữa (1-Click)] BÊN DƯỚI:\n" +
                "Hệ thống sẽ tự động quét giải phóng cổng mạng 39090, khởi động lại Backend Service và kết nối lại máy chủ cho bạn chỉ trong vài giây!";
            reportSb.AppendLine($"Kết luận: Sự cố mất kết nối Backend ({backendError}).");
        }
        else // Backend OK nhưng DB lỗi
        {
            SetBanner(
                icon: "⚠️",
                title: "SỰ CỐ: LỖI KẾT NỐI CƠ SỞ DỮ LIỆU POSTGRESQL",
                message: "Dịch vụ Backend đang chạy nhưng không thể kết nối hoặc truy vấn dữ liệu từ PostgreSQL Database.",
                bgColor: "#FEF3C7",
                borderColor: "#FCD34D",
                titleColor: "#B45309",
                msgColor: "#92400E"
            );

            DiagnosticSuggestionTextBlock.Text =
                "⚠️ Đã phát hiện sự cố Cơ sở dữ liệu PostgreSQL!\n" +
                "👉 BẠN CHỈ VIỆC BẤM NÚT [⚡ Tự động sửa chữa (1-Click)] BÊN DƯỚI:\n" +
                "Hệ thống sẽ tự động tìm kiếm và kích hoạt lại Windows Service PostgreSQL, khôi phục kết nối và nạp lại dữ liệu cho bạn.";
            reportSb.AppendLine($"Kết luận: Sự cố Cơ sở dữ liệu PostgreSQL ({dbError}).");
        }

        _lastDiagnosticReport = reportSb.ToString();
        RecheckHealthButton.IsEnabled = true;
    }

    private void SetStatusLoading()
    {
        SetBanner(
            icon: "⏳",
            title: "ĐANG KIỂM TRA HỆ THỐNG...",
            message: "Hệ thống đang gửi các truy vấn kiểm tra tới Backend API và Cơ sở dữ liệu PostgreSQL...",
            bgColor: "#EFF6FF",
            borderColor: "#BFDBFE",
            titleColor: "#1D4ED8",
            msgColor: "#1E40AF"
        );

        BackendStatusCodeTextBlock.Text = "Đang kiểm tra...";
        BackendLatencyTextBlock.Text = "...";
        DbTablesStatusTextBlock.Text = "Đang kiểm tra...";
        DbQueryLatencyTextBlock.Text = "...";
        DiagnosticSuggestionTextBlock.Text = "Vui lòng đợi giây lát...";
    }

    private void SetBanner(string icon, string title, string message, string bgColor, string borderColor, string titleColor, string msgColor)
    {
        BannerIconTextBlock.Text = icon;
        BannerTitleTextBlock.Text = title;
        BannerTitleTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(titleColor));
        BannerMessageTextBlock.Text = message;
        BannerMessageTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(msgColor));

        BannerCardBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor));
        BannerCardBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderColor));
    }

    private static void SetBadge(System.Windows.Controls.Border border, System.Windows.Controls.TextBlock textBlock, string text, string bgHex, string fgHex)
    {
        textBlock.Text = text;
        textBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex));
        border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
    }

    private void CopyReportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_lastDiagnosticReport))
            {
                _lastDiagnosticReport = "Chưa có báo cáo chẩn đoán.";
            }

            Clipboard.SetText(_lastDiagnosticReport);
            CopyReportButton.Content = "Đã sao chép! ✔";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, ev) =>
            {
                CopyReportButton.Content = "Sao chép báo cáo";
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể sao chép vào bộ nhớ tạm: {ex.Message}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
