using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Server.Admin.App.Models;

namespace Server.Admin.App.Windows;

public partial class MultiPaymentWindow : Window
{
    private readonly IReadOnlyList<MachineBillingSummaryRow> _summaryRows;
    private readonly Func<MachineRow, IReadOnlyList<string>?, string?, Task<PayPcServiceOrdersResponse>> _payServiceOrdersAsync;
    private readonly Func<string, MachineRow, bool, Task> _sendCommandAsync;
    private readonly Action<string> _appendServiceLog;
    private readonly Func<Task> _onPaymentCompletedAsync;
    private bool _isProcessing;

    public MultiPaymentWindow(
        IReadOnlyList<MachineBillingSummaryRow> summaryRows,
        Func<MachineRow, IReadOnlyList<string>?, string?, Task<PayPcServiceOrdersResponse>> payServiceOrdersAsync,
        Func<string, MachineRow, bool, Task> sendCommandAsync,
        Action<string> appendServiceLog,
        Func<Task> onPaymentCompletedAsync)
    {
        InitializeComponent();

        _summaryRows = summaryRows ?? throw new ArgumentNullException(nameof(summaryRows));
        _payServiceOrdersAsync = payServiceOrdersAsync ?? throw new ArgumentNullException(nameof(payServiceOrdersAsync));
        _sendCommandAsync = sendCommandAsync ?? throw new ArgumentNullException(nameof(sendCommandAsync));
        _appendServiceLog = appendServiceLog ?? throw new ArgumentNullException(nameof(appendServiceLog));
        _onPaymentCompletedAsync = onPaymentCompletedAsync ?? throw new ArgumentNullException(nameof(onPaymentCompletedAsync));

        InitializeData();
    }

    private void InitializeData()
    {
        MachinesBillingDataGrid.ItemsSource = _summaryRows;

        var count = _summaryRows.Count;
        MachineCountBadgeText.Text = $"{count} máy";
        CheckoutButtonText.Text = $"Thanh toán & Hoàn tất {count} máy";

        var totalPlay = _summaryRows.Sum(x => x.PlayAmount);
        var totalService = _summaryRows.Sum(x => x.ServiceAmount);
        var grandTotal = _summaryRows.Sum(x => x.TotalAmount);

        TotalPlayAmountTextBlock.Text = $"{totalPlay:N0} VND";
        TotalServiceAmountTextBlock.Text = $"{totalService:N0} VND";
        GrandTotalAmountTextBlock.Text = $"{grandTotal:N0} VND";
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
        {
            return;
        }
        Close();
    }

    private async void CheckoutGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
        {
            return;
        }

        var totalCount = _summaryRows.Count;
        var grandTotal = _summaryRows.Sum(x => x.TotalAmount);
        var totalPlay = _summaryRows.Sum(x => x.PlayAmount);
        var totalService = _summaryRows.Sum(x => x.ServiceAmount);

        var confirm = MessageBox.Show(
            $"Xác nhận thu tổng cộng {grandTotal:N0} VND và hoàn tất thanh toán cho {totalCount} máy đã chọn?\n\n" +
            $"• Tổng tiền giờ chơi: {totalPlay:N0} VND\n" +
            $"• Tổng tiền dịch vụ: {totalService:N0} VND\n\n" +
            $"Hệ thống sẽ tự động gạch nợ tất cả dịch vụ và chuyển các máy về trạng thái Sẵn sàng (Available).",
            "Xác nhận thanh toán gộp",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _isProcessing = true;
        CheckoutGroupButton.IsEnabled = false;
        Cursor = Cursors.Wait;

        var successCount = 0;
        var errorCount = 0;
        var errorMessages = new List<string>();

        try
        {
            foreach (var row in _summaryRows)
            {
                var machine = row.Machine;
                if (machine == null)
                {
                    continue;
                }

                try
                {
                    // 1. Pay pending services if any
                    if (row.ServiceAmount > 0)
                    {
                        try
                        {
                            await _payServiceOrdersAsync(
                                machine,
                                null,
                                $"Thanh toán gộp ({totalCount} máy)");
                        }
                        catch (Exception ex)
                        {
                            _appendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi thanh toán dịch vụ cho máy {machine.Name}: {ex.Message}");
                            errorMessages.Add($"{machine.Name} (Dịch vụ): {ex.Message}");
                        }
                    }

                    // 2. Transition machine to Available (Sẵn sàng)
                    if (machine.IsVirtual)
                    {
                        await _sendCommandAsync("available", machine, true);
                    }
                    else
                    {
                        await _sendCommandAsync("lock", machine, true);
                    }
                    successCount++;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _appendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi kết thúc máy {machine.Name}: {ex.Message}");
                    errorMessages.Add($"{machine.Name}: {ex.Message}");
                }
            }

            _appendServiceLog(
                $"[{DateTime.Now:HH:mm:ss}] Đã thanh toán gộp và chuyển về Sẵn sàng cho {totalCount} máy: " +
                $"Tổng cộng {grandTotal:N0} VND (Giờ: {totalPlay:N0} VND, Dịch vụ: {totalService:N0} VND)");

            try
            {
                await _onPaymentCompletedAsync();
            }
            catch (Exception ex)
            {
                _appendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi làm mới dữ liệu sau thanh toán: {ex.Message}");
            }

            if (errorCount > 0 || errorMessages.Count > 0)
            {
                MessageBox.Show(
                    $"Đã xử lý cho {successCount}/{totalCount} máy.\n" +
                    $"Có một số cảnh báo / lỗi phát sinh:\n" + string.Join("\n", errorMessages.Take(5)),
                    "Kết quả thanh toán gộp",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    $"Thanh toán gộp thành công cho {totalCount} máy!\nTổng số tiền đã thu: {grandTotal:N0} VND",
                    "Thanh toán hoàn tất",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Close();
        }
        finally
        {
            _isProcessing = false;
            CheckoutGroupButton.IsEnabled = true;
            Cursor = Cursors.Arrow;
        }
    }
}
