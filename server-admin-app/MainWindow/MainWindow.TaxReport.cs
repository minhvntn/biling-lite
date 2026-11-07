using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TaxReportDailyRow> _taxReportDailyRows = new();
    private TaxReportResponse? _currentTaxReportResponse;
    private bool _isTaxReportInitialized;
    private bool _isUpdatingTaxFilters;

    private static readonly string[] VietnameseDigits = { "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín" };
    private static readonly string[] VietnameseUnits = { "", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ" };

    private void InitializeTaxReportUi()
    {
        if (_isTaxReportInitialized)
        {
            return;
        }

        TaxDailyReportDataGrid.ItemsSource = _taxReportDailyRows;

        // Load Business info into textboxes
        if (TaxBusinessOwnerTextBox != null)
        {
            TaxBusinessOwnerTextBox.Text = string.IsNullOrWhiteSpace(_settings.TaxBusinessOwner) ? "Nguyễn Văn A" : _settings.TaxBusinessOwner;
        }
        if (TaxIdNumberTextBox != null)
        {
            TaxIdNumberTextBox.Text = string.IsNullOrWhiteSpace(_settings.TaxIdNumber) ? "0312345678" : _settings.TaxIdNumber;
        }
        if (TaxAddressTextBox != null)
        {
            TaxAddressTextBox.Text = string.IsNullOrWhiteSpace(_settings.TaxAddress) ? "12 Đường Mẫu, Quận 1, TP. Hồ Chí Minh" : _settings.TaxAddress;
        }
        if (TaxStoreLocationTextBox != null)
        {
            TaxStoreLocationTextBox.Text = string.IsNullOrWhiteSpace(_settings.TaxStoreLocation) ? "Phòng máy CyberS" : _settings.TaxStoreLocation;
        }

        var currentYear = DateTime.Today.Year;
        var currentMonth = DateTime.Today.Month;

        _isUpdatingTaxFilters = true;
        try
        {
            // Populate Years (2024 .. currentYear + 1)
            TaxYearComboBox.Items.Clear();
            for (var y = currentYear - 2; y <= currentYear + 1; y++)
            {
                var item = new ComboBoxItem { Content = $"Năm {y}", Tag = y };
                TaxYearComboBox.Items.Add(item);
                if (y == currentYear)
                {
                    TaxYearComboBox.SelectedItem = item;
                }
            }

            // Populate Months into MonthQuarter ComboBox
            UpdateMonthQuarterOptions("month");

            TaxFromDatePicker.SelectedDate = new DateTime(currentYear, currentMonth, 1);
            TaxToDatePicker.SelectedDate = DateTime.Today;

            TaxRatioSlider.Value = 100;
            TaxRatioValueTextBlock.Text = "100%";

            TaxPeriodTypeComboBox.SelectedIndex = 0; // Theo tháng
            TaxVatRateComboBox.SelectedIndex = 1; // 5%
            TaxPitRateComboBox.SelectedIndex = 1; // 2%
        }
        finally
        {
            _isUpdatingTaxFilters = false;
        }

        _isTaxReportInitialized = true;
    }

    private void SaveTaxBusinessInfo()
    {
        if (TaxBusinessOwnerTextBox != null)
        {
            _settings.TaxBusinessOwner = TaxBusinessOwnerTextBox.Text.Trim();
        }
        if (TaxIdNumberTextBox != null)
        {
            _settings.TaxIdNumber = TaxIdNumberTextBox.Text.Trim();
        }
        if (TaxAddressTextBox != null)
        {
            _settings.TaxAddress = TaxAddressTextBox.Text.Trim();
        }
        if (TaxStoreLocationTextBox != null)
        {
            _settings.TaxStoreLocation = TaxStoreLocationTextBox.Text.Trim();
        }
        SaveSettings();
    }

    private void TaxBusinessInfoTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveTaxBusinessInfo();
    }

    private void UpdateMonthQuarterOptions(string periodType)
    {
        TaxMonthQuarterComboBox.Items.Clear();
        var currentMonth = DateTime.Today.Month;
        var currentQuarter = (currentMonth - 1) / 3 + 1;

        if (periodType == "quarter")
        {
            TaxMonthQuarterLabelTextBlock.Text = "Chọn quý:";
            for (var q = 1; q <= 4; q++)
            {
                var item = new ComboBoxItem { Content = $"Quý {q} (Tháng {(q - 1) * 3 + 1} - {q * 3})", Tag = q };
                TaxMonthQuarterComboBox.Items.Add(item);
                if (q == currentQuarter)
                {
                    TaxMonthQuarterComboBox.SelectedItem = item;
                }
            }
        }
        else
        {
            TaxMonthQuarterLabelTextBlock.Text = "Chọn tháng:";
            for (var m = 1; m <= 12; m++)
            {
                var item = new ComboBoxItem { Content = $"Tháng {m:D2}", Tag = m };
                TaxMonthQuarterComboBox.Items.Add(item);
                if (m == currentMonth)
                {
                    TaxMonthQuarterComboBox.SelectedItem = item;
                }
            }
        }

        if (TaxMonthQuarterComboBox.SelectedItem == null && TaxMonthQuarterComboBox.Items.Count > 0)
        {
            TaxMonthQuarterComboBox.SelectedIndex = 0;
        }
    }

    private void TaxPeriodTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isUpdatingTaxFilters || TaxPeriodTypeComboBox.SelectedItem is not ComboBoxItem selected)
        {
            return;
        }

        var tag = selected.Tag?.ToString() ?? "month";

        _isUpdatingTaxFilters = true;
        try
        {
            if (tag == "custom")
            {
                TaxYearPanel.Visibility = Visibility.Collapsed;
                TaxMonthQuarterPanel.Visibility = Visibility.Collapsed;
                TaxCustomDatePanel.Visibility = Visibility.Visible;
            }
            else if (tag == "year")
            {
                TaxYearPanel.Visibility = Visibility.Visible;
                TaxMonthQuarterPanel.Visibility = Visibility.Collapsed;
                TaxCustomDatePanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                TaxYearPanel.Visibility = Visibility.Visible;
                TaxMonthQuarterPanel.Visibility = Visibility.Visible;
                TaxCustomDatePanel.Visibility = Visibility.Collapsed;
                UpdateMonthQuarterOptions(tag);
            }
        }
        finally
        {
            _isUpdatingTaxFilters = false;
        }

        _ = RefreshTaxReportAsync();
    }

    private void TaxFilterControl_Changed(object sender, EventArgs e)
    {
        if (!IsLoaded || _isUpdatingTaxFilters || !_isTaxReportInitialized)
        {
            return;
        }

        _ = RefreshTaxReportAsync();
    }

    private void TaxRatioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TaxRatioValueTextBlock != null)
        {
            TaxRatioValueTextBlock.Text = $"{e.NewValue:0}%";
        }

        if (!IsLoaded || _isUpdatingTaxFilters || !_isTaxReportInitialized)
        {
            return;
        }

        _ = RefreshTaxReportAsync();
    }

    public async Task RefreshTaxReportAsync()
    {
        if (!IsLoaded)
        {
            return;
        }

        try
        {
            TaxReportLoadingProgressBar.Visibility = Visibility.Visible;

            var periodType = (TaxPeriodTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "month";
            var year = (TaxYearComboBox.SelectedItem as ComboBoxItem)?.Tag is int y ? y : DateTime.Today.Year;
            var monthOrQuarter = (TaxMonthQuarterComboBox.SelectedItem as ComboBoxItem)?.Tag is int mq ? mq : (periodType == "quarter" ? 4 : DateTime.Today.Month);
            var ratio = Math.Round(TaxRatioSlider.Value);
            var vatRate = (TaxVatRateComboBox.SelectedItem as ComboBoxItem)?.Tag is string vr && double.TryParse(vr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVr) ? parsedVr : 5.0;
            var pitRate = (TaxPitRateComboBox.SelectedItem as ComboBoxItem)?.Tag is string pr && double.TryParse(pr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedPr) ? parsedPr : 2.0;

            var urlBuilder = new StringBuilder($"/reports/tax?type={periodType}&ratio={ratio}&vatRate={vatRate}&pitRate={pitRate}");

            if (periodType == "quarter")
            {
                urlBuilder.Append($"&year={year}&quarter={monthOrQuarter}");
            }
            else if (periodType == "year")
            {
                urlBuilder.Append($"&year={year}");
            }
            else if (periodType == "custom")
            {
                var fromStr = TaxFromDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
                var toStr = TaxToDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
                urlBuilder.Append($"&startDate={fromStr}&endDate={toStr}");
            }
            else
            {
                urlBuilder.Append($"&year={year}&month={monthOrQuarter}");
            }

            var response = await _httpClient.GetFromJsonAsync<TaxReportResponse>(
                BuildApiUrl(urlBuilder.ToString()),
                JsonOptions());

            if (response != null)
            {
                _currentTaxReportResponse = response;
                RenderTaxReport(response);
            }
        }
        catch (Exception ex)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi tải báo cáo thuế: {ex.Message}");
        }
        finally
        {
            TaxReportLoadingProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderTaxReport(TaxReportResponse response)
    {
        // 1. KPI Cards & Headers
        TaxActualTotalTextBlock.Text = $"{response.TotalActualRevenue:N0} đ";
        TaxActualDetailTextBlock.Text = $"Giờ chơi: {response.TotalActualPlaytime:N0} đ  |  Dịch vụ: {response.TotalActualService:N0} đ";

        TaxDeclaredTotalTextBlock.Text = $"{response.TotalTaxableRevenue:N0} đ";
        TaxDeclaredRatioTextBlock.Text = $"Áp dụng tỷ lệ khai thuế: {response.TaxRatio:0.#}%";

        TaxVatAmountTextBlock.Text = $"{response.TotalVatAmount:N0} đ";
        TaxVatRateLabelTextBlock.Text = $"Thuế suất GTGT: {response.VatRate:0.#}%";

        TaxTotalAmountTextBlock.Text = $"{response.TotalTaxAmount:N0} đ";
        TaxPitAmountDetailTextBlock.Text = $"Thuế TNCN/TNDN ({response.PitRate:0.#}%): {response.TotalPitAmount:N0} đ";

        TaxReportPeriodBadgeTextBlock.Text = $"Kỳ kê khai: {response.PeriodLabel}";
        if (TaxHeaderPeriodTextBlock != null)
        {
            TaxHeaderPeriodTextBlock.Text = response.PeriodLabel;
        }

        // 2. DataGrid rows
        _taxReportDailyRows.Clear();
        var idx = 1;
        foreach (var item in response.DailyBreakdown)
        {
            var dt = DateTime.TryParse(item.Date, out var parsed) ? parsed : DateTime.MinValue;
            _taxReportDailyRows.Add(new TaxReportDailyRow
            {
                Index = idx++,
                DateText = dt != DateTime.MinValue ? dt.ToString("dd-MM-yyyy") : item.Date,
                DayOfWeekText = item.DayOfWeek,
                SessionCount = item.SessionCount,
                ActualPlaytimeRevenue = item.ActualPlaytime,
                ActualServiceRevenue = item.ActualService,
                TaxablePlaytimeRevenue = item.TaxablePlaytime,
                TaxableServiceRevenue = item.TaxableService,
                VatAmount = item.VatAmount,
                PitAmount = item.PitAmount,
                Date = dt,
            });
        }

        // 3. Footer summary
        TaxReportSummaryFooterTextBlock.Text =
            $"Kỳ: {response.PeriodLabel}  |  Tổng số ngày: {response.DailyBreakdown.Count}  |  " +
            $"Thực thu: {response.TotalActualRevenue:N0} đ  ➔  Khai thuế: {response.TotalTaxableRevenue:N0} đ  |  " +
            $"Thuế GTGT: {response.TotalVatAmount:N0} đ  +  Thuế TNCN: {response.TotalPitAmount:N0} đ  =  Tổng thuế: {response.TotalTaxAmount:N0} đ";
    }

    private void RefreshTaxReportButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshTaxReportAsync();
    }

    private void ExportTaxReportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTaxReportResponse == null || _taxReportDailyRows.Count == 0)
        {
            MessageBox.Show("Không có dữ liệu báo cáo để xuất. Vui lòng bấm 'Xem báo cáo' trước.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveTaxBusinessInfo();

        var resp = _currentTaxReportResponse;
        var safePeriod = resp.PeriodLabel.Replace("/", "_").Replace(" ", "_").Replace(":", "_");
        var dialog = new SaveFileDialog
        {
            Title = "Xuất Báo Cáo Thuế (Excel CSV)",
            Filter = "File Excel CSV (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName = $"Bao_Cao_Thue_{safePeriod}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();

            // UTF-8 BOM
            sb.AppendLine("BÁO CÁO KÊ KHAI DOANH THU & NGHĨA VỤ THUẾ PHÒNG MÁY (MẪU S1a-HKD)");
            sb.AppendLine($"Hộ/Cá nhân kinh doanh:;{_settings.TaxBusinessOwner}");
            sb.AppendLine($"Mã số thuế:;{_settings.TaxIdNumber}");
            sb.AppendLine($"Địa chỉ:;{_settings.TaxAddress}");
            sb.AppendLine($"Địa điểm kinh doanh:;{_settings.TaxStoreLocation}");
            sb.AppendLine($"Kỳ báo cáo:;{resp.PeriodLabel}");
            sb.AppendLine($"Thời gian từ:;{resp.StartDate} đến {resp.EndDate}");
            sb.AppendLine($"Tỷ lệ khai thuế áp dụng:;{resp.TaxRatio}%");
            sb.AppendLine($"Thuế suất GTGT:;{resp.VatRate}%");
            sb.AppendLine($"Thuế suất TNCN/TNDN:;{resp.PitRate}%");
            sb.AppendLine($"Tổng doanh thu thực tế (Thực thu):;{resp.TotalActualRevenue:N0} VND");
            sb.AppendLine($"Tổng doanh thu kê khai tính thuế:;{resp.TotalTaxableRevenue:N0} VND");
            sb.AppendLine($"Thuế GTGT tạm tính:;{resp.TotalVatAmount:N0} VND");
            sb.AppendLine($"Thuế TNCN/TNDN tạm tính:;{resp.TotalPitAmount:N0} VND");
            sb.AppendLine($"TỔNG NGHĨA VỤ THUẾ PHẢI NỘP:;{resp.TotalTaxAmount:N0} VND");
            sb.AppendLine($"Thời điểm xuất báo cáo:;{DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine();

            // Table Header
            sb.AppendLine("STT;Ngày;Thứ;Số phiên;DT Giờ chơi thực tế (VND);DT Dịch vụ thực tế (VND);Tổng DT thực tế (VND);DT Kê khai tính thuế (VND);Thuế GTGT tạm tính (VND);Thuế TNCN tạm tính (VND);Tổng thuế phải nộp (VND)");

            foreach (var row in _taxReportDailyRows)
            {
                sb.AppendLine($"{row.Index};{row.DateText};{row.DayOfWeekText};{row.SessionCount};{row.ActualPlaytimeRevenue:0};{row.ActualServiceRevenue:0};{row.ActualTotalRevenue:0};{row.TaxableTotalRevenue:0};{row.VatAmount:0};{row.PitAmount:0};{row.TotalTaxAmount:0}");
            }

            // Summary row
            sb.AppendLine($";TỔNG CỘNG;;{_taxReportDailyRows.Sum(x => x.SessionCount)};{resp.TotalActualPlaytime:0};{resp.TotalActualService:0};{resp.TotalActualRevenue:0};{resp.TotalTaxableRevenue:0};{resp.TotalVatAmount:0};{resp.TotalPitAmount:0};{resp.TotalTaxAmount:0}");

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã xuất báo cáo thuế ra file: {dialog.FileName}");
            MessageBox.Show($"Đã xuất file báo cáo thuế thành công!\n\nĐường dẫn: {dialog.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể xuất file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportTaxReportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTaxReportResponse == null || _taxReportDailyRows.Count == 0)
        {
            MessageBox.Show("Không có dữ liệu báo cáo để xuất PDF. Vui lòng bấm 'Xem báo cáo' trước.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveTaxBusinessInfo();

        var resp = _currentTaxReportResponse;
        var safePeriod = resp.PeriodLabel.Replace("/", "_").Replace(" ", "_").Replace(":", "_");
        var dialog = new SaveFileDialog
        {
            Title = "Xuất Sổ Chi Tiết Doanh Thu Bán Hàng Hóa, Dịch Vụ PDF (Mẫu S1a-HKD)",
            Filter = "File PDF (*.pdf)|*.pdf|File HTML (*.html)|*.html|All Files (*.*)|*.*",
            FileName = $"So_Chi_Tiet_Doanh_Thu_Mau_S1a-HKD_{safePeriod}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var htmlContent = GenerateTaxReportHtml(resp, _taxReportDailyRows);
            var targetFile = dialog.FileName;
            var browserExe = GetEdgeOrBrowserExecutablePath();

            if (targetFile.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(browserExe))
            {
                var htmlOutPath = Path.ChangeExtension(targetFile, ".html");
                File.WriteAllText(htmlOutPath, htmlContent, Encoding.UTF8);
                Process.Start(new ProcessStartInfo(htmlOutPath) { UseShellExecute = true });
                MessageBox.Show($"Đã xuất file biểu mẫu HTML chuẩn thành công!\n\nĐường dẫn: {htmlOutPath}\n\nBạn có thể mở và nhấn Ctrl + P để In hoặc Lưu dưới dạng PDF.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Convert HTML to PDF via headless browser (Edge / Chrome)
            var tempHtmlPath = Path.Combine(Path.GetTempPath(), $"tax_s1a_report_{Guid.NewGuid():N}.html");
            File.WriteAllText(tempHtmlPath, htmlContent, Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName = browserExe,
                Arguments = $"--headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf=\"{targetFile}\" \"{tempHtmlPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using (var process = Process.Start(psi))
            {
                if (process != null)
                {
                    process.WaitForExit(15000);
                }
            }

            try
            {
                File.Delete(tempHtmlPath);
            }
            catch
            {
                // Ignore temp cleanup failure
            }

            if (File.Exists(targetFile) && new FileInfo(targetFile).Length > 0)
            {
                AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã xuất Sổ chi tiết doanh thu PDF (Mẫu S1a-HKD) ra: {targetFile}");

                try
                {
                    Process.Start(new ProcessStartInfo(targetFile) { UseShellExecute = true });
                }
                catch
                {
                    // Ignore launch error
                }

                MessageBox.Show($"Đã xuất Sổ chi tiết doanh thu bán hàng hóa, dịch vụ (Mẫu S1a-HKD) ra file PDF thành công!\n\nĐường dẫn file: {targetFile}", "Xuất PDF Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Fallback to HTML
                var fallbackHtml = Path.ChangeExtension(targetFile, ".html");
                File.WriteAllText(fallbackHtml, htmlContent, Encoding.UTF8);
                Process.Start(new ProcessStartInfo(fallbackHtml) { UseShellExecute = true });
                MessageBox.Show($"Đã xuất biểu mẫu HTML chuẩn (Mẫu S1a-HKD):\n\n{fallbackHtml}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể xuất PDF: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string GenerateTaxReportHtml(TaxReportResponse resp, IEnumerable<TaxReportDailyRow> rows)
    {
        var ownerName = string.IsNullOrWhiteSpace(_settings.TaxBusinessOwner) ? "Nguyễn Văn A" : _settings.TaxBusinessOwner;
        var taxCode = string.IsNullOrWhiteSpace(_settings.TaxIdNumber) ? "0312345678" : _settings.TaxIdNumber;
        var address = string.IsNullOrWhiteSpace(_settings.TaxAddress) ? "12 Đường Mẫu, Quận 1, TP. Hồ Chí Minh" : _settings.TaxAddress;
        var storeLocation = string.IsNullOrWhiteSpace(_settings.TaxStoreLocation) ? "Phòng máy CyberS" : _settings.TaxStoreLocation;
        var circular = string.IsNullOrWhiteSpace(_settings.TaxCircularReference)
            ? "Kèm theo Thông tư số 152/2025/TT-BTC ngày 31 tháng 12 năm 2025 của Bộ trưởng Bộ Tài chính"
            : _settings.TaxCircularReference;

        var amountInWords = NumberToVietnameseWords((long)resp.TotalTaxAmount);
        var now = DateTime.Now;

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"vi\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<title>SỔ CHI TIẾT DOANH THU BÁN HÀNG HÓA, DỊCH VỤ - MẪU S1a-HKD</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  @page { size: A4 portrait; margin: 12mm 14mm 14mm 14mm; }");
        sb.AppendLine("  * { box-sizing: border-box; }");
        sb.AppendLine("  body { font-family: 'Times New Roman', Times, serif; font-size: 11pt; line-height: 1.35; color: #000; margin: 0; padding: 0; background: #fff; }");
        sb.AppendLine("  .header-table { width: 100%; border-collapse: collapse; margin-bottom: 8px; }");
        sb.AppendLine("  .header-table td { vertical-align: top; border: none; padding: 0; }");
        sb.AppendLine("  .header-left { width: 58%; font-size: 10.5pt; line-height: 1.4; }");
        sb.AppendLine("  .header-left strong { font-weight: bold; }");
        sb.AppendLine("  .header-right { width: 42%; text-align: center; font-size: 10pt; line-height: 1.35; }");
        sb.AppendLine("  .doc-title { font-weight: bold; font-size: 11pt; }");
        sb.AppendLine("  .doc-sub { font-style: italic; font-size: 9.5pt; margin-top: 2px; }");
        sb.AppendLine("  .title-section { text-align: center; margin: 12px 0 10px 0; }");
        sb.AppendLine("  .title-section h1 { font-size: 14pt; font-weight: bold; text-transform: uppercase; margin: 0 0 4px 0; letter-spacing: 0.5px; }");
        sb.AppendLine("  .title-section .location { font-size: 11pt; margin: 2px 0; }");
        sb.AppendLine("  .title-section .period { font-size: 11.5pt; font-weight: bold; font-style: italic; margin-top: 2px; }");
        sb.AppendLine("  .summary-card { margin: 8px 0 12px 0; padding: 8px 12px; border: 1px solid #333; background: #fcfcfc; font-size: 10pt; }");
        sb.AppendLine("  .summary-grid { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 4px 12px; }");
        sb.AppendLine("  .summary-item { min-width: 45%; }");
        sb.AppendLine("  .data-table { width: 100%; border-collapse: collapse; margin-top: 8px; font-size: 9.5pt; }");
        sb.AppendLine("  .data-table th, .data-table td { border: 1px solid #000; padding: 4px 5px; }");
        sb.AppendLine("  .data-table th { background: #f2f2f2; text-align: center; font-weight: bold; font-size: 9.5pt; }");
        sb.AppendLine("  .text-center { text-align: center; }");
        sb.AppendLine("  .text-right { text-align: right; }");
        sb.AppendLine("  .text-left { text-align: left; }");
        sb.AppendLine("  .bold { font-weight: bold; }");
        sb.AppendLine("  .total-row { background: #f5f5f5; font-weight: bold; }");
        sb.AppendLine("  .words-row { margin-top: 10px; font-size: 10.5pt; font-style: italic; }");
        sb.AppendLine("  .signatures-table { width: 100%; border-collapse: collapse; margin-top: 20px; page-break-inside: avoid; }");
        sb.AppendLine("  .signatures-table td { width: 50%; text-align: center; vertical-align: top; border: none; padding: 0; }");
        sb.AppendLine("  .sign-title { font-weight: bold; text-transform: uppercase; font-size: 11pt; }");
        sb.AppendLine("  .sign-note { font-style: italic; font-size: 9.5pt; margin-top: 2px; }");
        sb.AppendLine("  .sign-space { height: 60px; }");
        sb.AppendLine("  .sign-name { font-weight: bold; font-size: 11pt; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // 1. Header (Left: Business info, Right: S1a-HKD)
        sb.AppendLine("<table class=\"header-table\">");
        sb.AppendLine("  <tr>");
        sb.AppendLine("    <td class=\"header-left\">");
        sb.AppendLine($"      <strong>HỘ, CÁ NHÂN KINH DOANH:</strong> {ownerName}<br>");
        sb.AppendLine($"      <strong>Mã số thuế:</strong> {taxCode}<br>");
        sb.AppendLine($"      <strong>Địa chỉ:</strong> {address}");
        sb.AppendLine("    </td>");
        sb.AppendLine("    <td class=\"header-right\">");
        sb.AppendLine("      <div class=\"doc-title\">\"Mẫu số S1a-HKD</div>");
        sb.AppendLine($"      <div class=\"doc-sub\">({circular})\"</div>");
        sb.AppendLine("    </td>");
        sb.AppendLine("  </tr>");
        sb.AppendLine("</table>");

        // 2. Title Section
        sb.AppendLine("<div class=\"title-section\">");
        sb.AppendLine("  <h1>SỔ CHI TIẾT DOANH THU BÁN HÀNG HÓA, DỊCH VỤ</h1>");
        sb.AppendLine($"  <div class=\"location\"><strong>Địa điểm kinh doanh:</strong> {storeLocation}</div>");
        sb.AppendLine($"  <div class=\"period\">Kỳ kê khai: {resp.PeriodLabel}</div>");
        sb.AppendLine("</div>");

        // 3. Tax Parameters & Overview Box
        sb.AppendLine("<div class=\"summary-card\">");
        sb.AppendLine("  <div class=\"summary-grid\">");
        sb.AppendLine($"    <div class=\"summary-item\">• <strong>Tỷ lệ khai thuế áp dụng:</strong> {resp.TaxRatio:0.#}%</div>");
        sb.AppendLine($"    <div class=\"summary-item\">• <strong>Thuế suất áp dụng:</strong> GTGT {resp.VatRate:0.#}% | TNCN/TNDN {resp.PitRate:0.#}%</div>");
        sb.AppendLine($"    <div class=\"summary-item\">• <strong>Tổng DT thực tế (Thực thu):</strong> {resp.TotalActualRevenue:N0} VND (Giờ chơi: {resp.TotalActualPlaytime:N0} đ | Dịch vụ: {resp.TotalActualService:N0} đ)</div>");
        sb.AppendLine($"    <div class=\"summary-item\">• <strong>Doanh thu kê khai tính thuế:</strong> {resp.TotalTaxableRevenue:N0} VND</div>");
        sb.AppendLine($"    <div class=\"summary-item\">• <strong>Thuế GTGT tạm tính:</strong> {resp.TotalVatAmount:N0} VND</div>");
        sb.AppendLine($"    <div class=\"summary-item\">• <strong>Thuế TNCN/TNDN tạm tính:</strong> {resp.TotalPitAmount:N0} VND</div>");
        sb.AppendLine($"    <div class=\"summary-item\" style=\"min-width: 100%; margin-top: 3px;\">• <strong>TỔNG NGHĨA VỤ THUẾ PHẢI NỘP:</strong> <span style=\"font-size: 11pt; color: #b91c1c;\"><strong>{resp.TotalTaxAmount:N0} VND</strong></span></div>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");

        // 4. Data Table
        sb.AppendLine("<table class=\"data-table\">");
        sb.AppendLine("  <thead>");
        sb.AppendLine("    <tr>");
        sb.AppendLine("      <th rowspan=\"2\" style=\"width: 32px;\">STT</th>");
        sb.AppendLine("      <th rowspan=\"2\" style=\"width: 80px;\">Ngày</th>");
        sb.AppendLine("      <th rowspan=\"2\" style=\"width: 60px;\">Thứ</th>");
        sb.AppendLine("      <th rowspan=\"2\" style=\"width: 45px;\">Số phiên</th>");
        sb.AppendLine("      <th colspan=\"3\">Doanh thu thực tế (VND)</th>");
        sb.AppendLine("      <th rowspan=\"2\" style=\"width: 95px;\">DT Kê khai tính thuế</th>");
        sb.AppendLine("      <th colspan=\"2\">Nghĩa vụ thuế tạm tính</th>");
        sb.AppendLine("      <th rowspan=\"2\" style=\"width: 95px;\">Tổng thuế phải nộp</th>");
        sb.AppendLine("    </tr>");
        sb.AppendLine("    <tr>");
        sb.AppendLine("      <th style=\"width: 80px;\">Giờ chơi</th>");
        sb.AppendLine("      <th style=\"width: 75px;\">Dịch vụ</th>");
        sb.AppendLine("      <th style=\"width: 85px;\">Tổng thực tế</th>");
        sb.AppendLine($"      <th style=\"width: 75px;\">GTGT ({resp.VatRate:0.#}%)</th>");
        sb.AppendLine($"      <th style=\"width: 75px;\">TNCN ({resp.PitRate:0.#}%)</th>");
        sb.AppendLine("    </tr>");
        sb.AppendLine("  </thead>");
        sb.AppendLine("  <tbody>");

        var totalSessions = 0;
        foreach (var row in rows)
        {
            totalSessions += row.SessionCount;
            sb.AppendLine("    <tr>");
            sb.AppendLine($"      <td class=\"text-center\">{row.Index}</td>");
            sb.AppendLine($"      <td class=\"text-center\">{row.DateText}</td>");
            sb.AppendLine($"      <td class=\"text-center\">{row.DayOfWeekText}</td>");
            sb.AppendLine($"      <td class=\"text-center\">{row.SessionCount}</td>");
            sb.AppendLine($"      <td class=\"text-right\">{row.ActualPlaytimeRevenue:N0}</td>");
            sb.AppendLine($"      <td class=\"text-right\">{row.ActualServiceRevenue:N0}</td>");
            sb.AppendLine($"      <td class=\"text-right bold\">{row.ActualTotalRevenue:N0}</td>");
            sb.AppendLine($"      <td class=\"text-right bold\">{row.TaxableTotalRevenue:N0}</td>");
            sb.AppendLine($"      <td class=\"text-right\">{row.VatAmount:N0}</td>");
            sb.AppendLine($"      <td class=\"text-right\">{row.PitAmount:N0}</td>");
            sb.AppendLine($"      <td class=\"text-right bold\">{row.TotalTaxAmount:N0}</td>");
            sb.AppendLine("    </tr>");
        }

        // Summary Total Row
        sb.AppendLine("    <tr class=\"total-row\">");
        sb.AppendLine("      <td colspan=\"3\" class=\"text-center\">TỔNG CỘNG</td>");
        sb.AppendLine($"      <td class=\"text-center\">{totalSessions}</td>");
        sb.AppendLine($"      <td class=\"text-right\">{resp.TotalActualPlaytime:N0}</td>");
        sb.AppendLine($"      <td class=\"text-right\">{resp.TotalActualService:N0}</td>");
        sb.AppendLine($"      <td class=\"text-right\">{resp.TotalActualRevenue:N0}</td>");
        sb.AppendLine($"      <td class=\"text-right\">{resp.TotalTaxableRevenue:N0}</td>");
        sb.AppendLine($"      <td class=\"text-right\">{resp.TotalVatAmount:N0}</td>");
        sb.AppendLine($"      <td class=\"text-right\">{resp.TotalPitAmount:N0}</td>");
        sb.AppendLine($"      <td class=\"text-right\">{resp.TotalTaxAmount:N0}</td>");
        sb.AppendLine("    </tr>");
        sb.AppendLine("  </tbody>");
        sb.AppendLine("</table>");

        // Amount in words
        sb.AppendLine("<div class=\"words-row\">");
        sb.AppendLine($"  <strong>Số tiền thuế bằng chữ:</strong> {amountInWords}");
        sb.AppendLine("</div>");

        // 5. Signatures
        sb.AppendLine("<table class=\"signatures-table\">");
        sb.AppendLine("  <tr>");
        sb.AppendLine("    <td></td>");
        sb.AppendLine($"    <td style=\"font-style: italic; padding-bottom: 6px;\">Ngày {now.Day:D2} tháng {now.Month:D2} năm {now.Year}</td>");
        sb.AppendLine("  </tr>");
        sb.AppendLine("  <tr>");
        sb.AppendLine("    <td>");
        sb.AppendLine("      <div class=\"sign-title\">NGƯỜI LẬP BIỂU</div>");
        sb.AppendLine("      <div class=\"sign-note\">(Ký, ghi rõ họ tên)</div>");
        sb.AppendLine("      <div class=\"sign-space\"></div>");
        sb.AppendLine($"      <div class=\"sign-name\">{ownerName}</div>");
        sb.AppendLine("    </td>");
        sb.AppendLine("    <td>");
        sb.AppendLine("      <div class=\"sign-title\">CHỦ HỘ KINH DOANH / ĐẠI DIỆN HỢP PHÁP</div>");
        sb.AppendLine("      <div class=\"sign-note\">(Ký, ghi rõ họ tên, đóng dấu nếu có)</div>");
        sb.AppendLine("      <div class=\"sign-space\"></div>");
        sb.AppendLine($"      <div class=\"sign-name\">{ownerName}</div>");
        sb.AppendLine("    </td>");
        sb.AppendLine("  </tr>");
        sb.AppendLine("</table>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string? GetEdgeOrBrowserExecutablePath()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe"),
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    public static string NumberToVietnameseWords(long number)
    {
        if (number == 0) return "Không đồng chẵn.";
        if (number < 0) return "Âm " + NumberToVietnameseWords(-number);

        var sNumber = number.ToString();
        var groups = (sNumber.Length + 2) / 3;
        var padded = sNumber.PadLeft(groups * 3, '0');
        var sb = new StringBuilder();

        for (var i = 0; i < groups; i++)
        {
            var h = padded[i * 3] - '0';
            var t = padded[i * 3 + 1] - '0';
            var u = padded[i * 3 + 2] - '0';
            if (h == 0 && t == 0 && u == 0) continue;

            if (sb.Length > 0 || groups > 1)
            {
                sb.Append(ReadThreeDigits(h, t, u, i > 0));
            }
            else
            {
                sb.Append(ReadThreeDigits(h, t, u, false));
            }

            var unitIdx = groups - 1 - i;
            if (unitIdx > 0 && unitIdx < VietnameseUnits.Length)
            {
                sb.Append(" ").Append(VietnameseUnits[unitIdx]);
            }
            sb.Append(" ");
        }

        var result = sb.ToString().Trim();
        while (result.Contains("  "))
        {
            result = result.Replace("  ", " ");
        }
        if (result.Length > 0)
        {
            result = char.ToUpper(result[0]) + result.Substring(1) + " đồng chẵn.";
        }
        return result;
    }

    private static string ReadThreeDigits(int h, int t, int u, bool hasLeadingGroup)
    {
        var sb = new StringBuilder();
        if (h > 0 || hasLeadingGroup)
        {
            sb.Append(VietnameseDigits[h]).Append(" trăm");
        }
        if (t == 0 && u > 0)
        {
            if (h > 0 || hasLeadingGroup) sb.Append(" linh ");
            sb.Append(VietnameseDigits[u]);
        }
        else if (t == 1)
        {
            if (sb.Length > 0) sb.Append(" ");
            sb.Append("mười");
            if (u == 1) sb.Append(" một");
            else if (u == 5) sb.Append(" lăm");
            else if (u > 0) sb.Append(" ").Append(VietnameseDigits[u]);
        }
        else if (t > 1)
        {
            if (sb.Length > 0) sb.Append(" ");
            sb.Append(VietnameseDigits[t]).Append(" mươi");
            if (u == 1) sb.Append(" mốt");
            else if (u == 4) sb.Append(" tư");
            else if (u == 5) sb.Append(" lăm");
            else if (u > 0) sb.Append(" ").Append(VietnameseDigits[u]);
        }
        return sb.ToString();
    }

    private void TaxReportScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv && e.Delta != 0)
        {
            double scrollDelta = (e.Delta / 120.0) * 85.0;
            double newOffset = sv.VerticalOffset - scrollDelta;
            if (newOffset < 0) newOffset = 0;
            if (newOffset > sv.ScrollableHeight) newOffset = sv.ScrollableHeight;
            sv.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }
    }

    private void TaxDailyReportDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TaxReportScrollViewer != null && e.Delta != 0)
        {
            double scrollDelta = (e.Delta / 120.0) * 85.0;
            double newOffset = TaxReportScrollViewer.VerticalOffset - scrollDelta;
            if (newOffset < 0) newOffset = 0;
            if (newOffset > TaxReportScrollViewer.ScrollableHeight) newOffset = TaxReportScrollViewer.ScrollableHeight;
            TaxReportScrollViewer.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }
    }
}
