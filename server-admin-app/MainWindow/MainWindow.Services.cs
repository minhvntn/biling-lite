using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private async Task RefreshServiceItemsAsync()
    {
        try
        {
            var selectedId = _selectedServiceItemId ?? (ServiceItemsDataGrid.SelectedItem as ServiceItemRow)?.Id;
            var response = await _httpClient.GetFromJsonAsync<ServiceItemsResponse>(
                BuildApiUrl("/services/items?includeInactive=true"),
                JsonOptions());

            if (response is null)
            {
                return;
            }

            var mapped = response.Items
                .Select(ToServiceItemRow)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Category)
                .ThenBy(x => x.Name)
                .ToList();

            _serviceItemRows.Clear();
            foreach (var row in mapped)
            {
                _serviceItemRows.Add(row);
            }

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                var selected = mapped.FirstOrDefault(x => x.Id == selectedId);
                if (selected is not null)
                {
                    ServiceItemsDataGrid.SelectedItem = selected;
                    ServiceItemsDataGrid.ScrollIntoView(selected);
                    _selectedServiceItemId = selected.Id;
                }
            }

            var activeCount = mapped.Count(x => x.IsActive);
            ServiceInfoTextBlock.Text =
                $"Tổng dịch vụ: {mapped.Count} - Đang bán: {activeCount} - Cập nhật: {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            // Keep UI responsive when backend is temporarily unavailable.
        }
    }

    private static ServiceItemRow ToServiceItemRow(ServiceItemDto item)
    {
        return new ServiceItemRow
        {
            Id = item.Id,
            Name = item.Name,
            Category = string.IsNullOrWhiteSpace(item.Category) ? "-" : item.Category,
            UnitPrice = item.UnitPrice,
            UnitPriceText = item.UnitPrice.ToString("N0", CultureInfo.InvariantCulture),
            IsActive = item.IsActive,
            UpdatedAtText = FormatDateTime(item.UpdatedAt),
        };
    }

    private async Task CreateServiceItemAsync()
    {
        var input = PromptCreateServiceItem();
        if (input is null)
        {
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl("/services/items"),
            new
            {
                name = input.Name,
                category = input.Category,
                unitPrice = Convert.ToDouble(input.UnitPrice),
                isActive = true,
            });

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            MessageBox.Show(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Tạo dịch vụ thất bại ({(int)response.StatusCode})"
                    : errorBody,
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var qtyLog = input.Quantity == "Không giới hạn" ? "Không giới hạn" : $"{input.Quantity} chiếc";
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã thêm dịch vụ: {input.Name} ({input.UnitPrice:N0} VND, Số lượng: {qtyLog})");
        await RefreshServiceItemsAsync();
    }

    private CreateServiceItemInput? PromptCreateServiceItem()
    {
        var dialog = new Window
        {
            Title = "Thêm dịch vụ mới",
            Width = 450,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
            Owner = this,
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var content = new StackPanel();
        
        content.Children.Add(new TextBlock
        {
            Text = "Thêm dịch vụ mới vào hệ thống",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16),
        });

        // 1. Tên dịch vụ
        content.Children.Add(new TextBlock { Text = "Tên dịch vụ:", Margin = new Thickness(0, 0, 0, 4) });
        var nameTextBox = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        content.Children.Add(nameTextBox);

        // 2. Danh mục & Giá (VND)
        var gridFields = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        gridFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        gridFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var colLeft = new StackPanel();
        colLeft.Children.Add(new TextBlock { Text = "Danh mục:", Margin = new Thickness(0, 0, 0, 4) });
        var categoryTextBox = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        colLeft.Children.Add(categoryTextBox);
        Grid.SetColumn(colLeft, 0);
        gridFields.Children.Add(colLeft);

        var colRight = new StackPanel();
        colRight.Children.Add(new TextBlock { Text = "Giá bán (VND):", Margin = new Thickness(0, 0, 0, 4) });
        var priceTextBox = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Text = "15000" };
        colRight.Children.Add(priceTextBox);
        Grid.SetColumn(colRight, 2);
        gridFields.Children.Add(colRight);

        content.Children.Add(gridFields);

        // 3. Số lượng
        content.Children.Add(new TextBlock { Text = "Số lượng trong kho:", Margin = new Thickness(0, 0, 0, 4) });
        var quantityPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        
        var unlimitedCheckBox = new CheckBox
        {
            Content = "Không giới hạn (mặc định)",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        };
        quantityPanel.Children.Add(unlimitedCheckBox);

        var quantityTextBox = new TextBox
        {
            Width = 80,
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Text = "∞",
            IsEnabled = false,
        };
        quantityPanel.Children.Add(quantityTextBox);
        content.Children.Add(quantityPanel);

        var errorTextBlock = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        content.Children.Add(errorTextBlock);

        Grid.SetRow(content, 0);
        root.Children.Add(content);

        // Button panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var createButton = new Button
        {
            Content = "Thêm",
            Width = 100,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 80,
            Height = 30,
            IsCancel = true,
        };

        buttonPanel.Children.Add(createButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 1);
        root.Children.Add(buttonPanel);

        // Interaction logic
        unlimitedCheckBox.Checked += (s, e) =>
        {
            quantityTextBox.IsEnabled = false;
            quantityTextBox.Text = "∞";
        };
        unlimitedCheckBox.Unchecked += (s, e) =>
        {
            quantityTextBox.IsEnabled = true;
            quantityTextBox.Text = "1";
        };

        CreateServiceItemInput? result = null;

        createButton.Click += (s, e) =>
        {
            errorTextBlock.Text = string.Empty;

            var name = nameTextBox.Text.Trim();
            var category = categoryTextBox.Text.Trim();
            var priceRaw = priceTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                errorTextBlock.Text = "Vui lòng nhập tên dịch vụ.";
                return;
            }

            if (!TryParsePositiveMoney(priceRaw, out var unitPrice))
            {
                errorTextBlock.Text = "Giá dịch vụ không hợp lệ.";
                return;
            }

            string quantityText = "Không giới hạn";
            if (unlimitedCheckBox.IsChecked == false)
            {
                var qRaw = quantityTextBox.Text.Trim();
                if (!int.TryParse(qRaw, out var qty) || qty <= 0)
                {
                    errorTextBlock.Text = "Số lượng không hợp lệ (phải là số nguyên dương).";
                    return;
                }
                quantityText = qty.ToString();
            }

            result = new CreateServiceItemInput
            {
                Name = name,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                UnitPrice = unitPrice,
                Quantity = quantityText,
            };

            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.Content = root;
        dialog.Loaded += (s, e) => nameTextBox.Focus();
        _ = dialog.ShowDialog();

        return result;
    }

    private sealed class CreateServiceItemInput
    {
        public string Name { get; init; } = string.Empty;
        public string? Category { get; init; }
        public decimal UnitPrice { get; init; }
        public string Quantity { get; init; } = "Không giới hạn";
    }

    private async Task ToggleSelectedServiceItemAsync()
    {
        if (ServiceItemsDataGrid.SelectedItem is not ServiceItemRow selected)
        {
            MessageBox.Show("Vui lòng chọn dịch vụ trước.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var response = await _httpClient.PatchAsJsonAsync(
            BuildApiUrl($"/services/items/{selected.Id}"),
            new
            {
                isActive = !selected.IsActive,
            });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show($"Cập nhật dịch vụ thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var statusText = selected.IsActive ? "tạm ngưng" : "bật bán";
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã {statusText} dịch vụ: {selected.Name}");
        await RefreshServiceItemsAsync();
    }

    private async Task UpdateSelectedServicePriceAsync()
    {
        if (ServiceItemsDataGrid.SelectedItem is not ServiceItemRow selected)
        {
            MessageBox.Show("Vui lòng chọn dịch vụ trước.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var rawPrice = PromptText(
            "Đổi giá dịch vụ",
            $"Nhập giá mới cho \"{selected.Name}\" (VND):",
            selected.UnitPrice.ToString("0"));

        if (string.IsNullOrWhiteSpace(rawPrice))
        {
            return;
        }

        if (!TryParsePositiveMoney(rawPrice, out var newPrice))
        {
            MessageBox.Show("Giá dịch vụ không hợp lệ.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var response = await _httpClient.PatchAsJsonAsync(
            BuildApiUrl($"/services/items/{selected.Id}"),
            new
            {
                unitPrice = Convert.ToDouble(newPrice),
            });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show($"Đổi giá dịch vụ thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã đổi giá dịch vụ {selected.Name} -> {newPrice:N0} VND");
        await RefreshServiceItemsAsync();
    }

    private async Task OpenServiceOrderDialogForSelectedMachineAsync()
    {
        if (MachinesDataGrid.SelectedItem is not MachineRow selectedMachine)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_serviceItemRows.Count == 0)
        {
            await RefreshServiceItemsAsync();
        }

        var activeItems = _serviceItemRows.Where(x => x.IsActive).ToList();
        if (activeItems.Count == 0)
        {
            MessageBox.Show("Chưa có dịch vụ đang bán. Hãy thêm trong tab Dịch vụ.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Dictionary<string, ExistingServiceOrderSummary> existingOrdersByServiceId;
        try
        {
            existingOrdersByServiceId = await GetUnpaidServiceOrderSummaryForMachineAsync(selectedMachine);
        }
        catch
        {
            existingOrdersByServiceId = new Dictionary<string, ExistingServiceOrderSummary>(StringComparer.OrdinalIgnoreCase);
        }

        var orderInput = PromptServiceOrder(selectedMachine, activeItems, existingOrdersByServiceId);
        if (orderInput is null)
        {
            return;
        }

        var adjustmentLines = orderInput.Lines
            .Where(x => x.Quantity != 0)
            .ToList();
        if (adjustmentLines.Count == 0)
        {
            return;
        }

        var sessionId = string.IsNullOrWhiteSpace(selectedMachine.ActiveSessionId)
            ? null
            : selectedMachine.ActiveSessionId;

        var failedItems = new List<string>();
        var successActionCount = 0;
        var totalActionCount = adjustmentLines.Count;

        foreach (var line in adjustmentLines.Where(x => x.Quantity < 0))
        {
            var cancelQuantity = Math.Abs(line.Quantity);
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/services/pcs/{selectedMachine.Id}/orders/cancel"),
                new
                {
                    serviceItemId = line.ServiceItemId,
                    quantity = cancelQuantity,
                    sessionId,
                    note = orderInput.Note,
                    requestedBy = "admin.desktop",
                });

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                failedItems.Add(
                    string.IsNullOrWhiteSpace(errorBody)
                        ? $"Hủy {line.ServiceName} ({(int)response.StatusCode})"
                        : $"Hủy {line.ServiceName}: {errorBody}");
                continue;
            }

            successActionCount++;
            AppendServiceLog(
                $"[{DateTime.Now:HH:mm:ss}] {selectedMachine.Name}: -{cancelQuantity} x {line.ServiceName} = {Math.Abs(line.LineTotal):N0} VND");
        }

        foreach (var line in adjustmentLines.Where(x => x.Quantity > 0))
        {
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/services/pcs/{selectedMachine.Id}/orders"),
                new
                {
                    serviceItemId = line.ServiceItemId,
                    quantity = line.Quantity,
                    note = orderInput.Note,
                    requestedBy = "admin.desktop",
                });

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                failedItems.Add(
                    string.IsNullOrWhiteSpace(errorBody)
                        ? $"{line.ServiceName} ({(int)response.StatusCode})"
                        : $"{line.ServiceName}: {errorBody}");
                continue;
            }

            successActionCount++;
            AppendServiceLog(
                $"[{DateTime.Now:HH:mm:ss}] {selectedMachine.Name}: +{line.Quantity} x {line.ServiceName} = {line.LineTotal:N0} VND");
        }

        if (successActionCount > 0)
        {
            InvalidateServiceAmountCacheForMachine(selectedMachine);
            await RefreshMachinesAsync();
        }

        if (failedItems.Count > 0)
        {
            var errorPreview = string.Join(
                Environment.NewLine,
                failedItems.Take(8).Select(x => $"- {x}"));
            var hasMore = failedItems.Count > 8 ? $"{Environment.NewLine}... và {failedItems.Count - 8} lỗi khác" : string.Empty;
            MessageBox.Show(
                $"Cập nhật dịch vụ thành công {successActionCount}/{totalActionCount}.{Environment.NewLine}{errorPreview}{hasMore}",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task PayServiceForSelectedMachineAsync()
    {
        if (MachinesDataGrid.SelectedItem is not MachineRow selectedMachine)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedMachine.ActiveSessionId))
        {
            MessageBox.Show(
                "Máy chưa có phiên đang sử dụng để thanh toán dịch vụ.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        List<PcServiceOrderDto> unpaidOrders;
        try
        {
            unpaidOrders = await GetUnpaidServiceOrdersForMachineAsync(selectedMachine);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể tải tiền dịch vụ: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (unpaidOrders.Count == 0)
        {
            MessageBox.Show(
                "Máy này không còn dịch vụ chưa thanh toán.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await OpenServicePaymentDialogForMachineAsync(selectedMachine, unpaidOrders);
    }

    private async Task<List<PcServiceOrderDto>> GetUnpaidServiceOrdersForMachineAsync(MachineRow machine)
    {
        var response = await _httpClient.GetFromJsonAsync<PcServiceOrdersResponse>(
            BuildApiUrl($"/services/pcs/{machine.Id}/orders?limit=200"),
            JsonOptions());

        if (response?.Items is null || response.Items.Count == 0)
        {
            return new List<PcServiceOrderDto>();
        }

        IEnumerable<PcServiceOrderDto> scopedOrders = response.Items;
        if (!string.IsNullOrWhiteSpace(machine.ActiveSessionId))
        {
            scopedOrders = scopedOrders.Where(x =>
                string.Equals(x.SessionId, machine.ActiveSessionId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            scopedOrders = Enumerable.Empty<PcServiceOrderDto>();
        }

        return scopedOrders
            .Where(x => !x.IsPaid)
            .OrderBy(x => ParseDateLocal(x.CreatedAt) ?? DateTime.MaxValue)
            .ThenBy(x => x.ServiceItem?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Dictionary<string, ExistingServiceOrderSummary>> GetUnpaidServiceOrderSummaryForMachineAsync(MachineRow machine)
    {
        var orders = await GetUnpaidServiceOrdersForMachineAsync(machine);
        var summaryByServiceId = new Dictionary<string, ExistingServiceOrderSummary>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders)
        {
            var serviceItemId = order.ServiceItem?.Id;
            if (string.IsNullOrWhiteSpace(serviceItemId))
            {
                continue;
            }

            if (!summaryByServiceId.TryGetValue(serviceItemId, out var summary))
            {
                summary = new ExistingServiceOrderSummary();
                summaryByServiceId[serviceItemId] = summary;
            }

            summary.Quantity += Math.Max(0, order.Quantity);
            summary.Amount += Math.Max(0, order.LineTotal);
        }

        return summaryByServiceId;
    }

    private async Task<PayPcServiceOrdersResponse> PayServiceOrdersForMachineAsync(
        MachineRow machine,
        IReadOnlyList<string>? orderIds,
        string? note = null)
    {
        var payload = new
        {
            requestedBy = "admin.desktop",
            note,
            orderIds = orderIds is { Count: > 0 } ? orderIds : null,
        };

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/services/pcs/{machine.Id}/orders/pay"),
            payload,
            JsonOptions());

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            var errorText = string.IsNullOrWhiteSpace(errorBody)
                ? $"Thanh toán dịch vụ thất bại ({(int)response.StatusCode})"
                : errorBody;
            throw new InvalidOperationException(errorText);
        }

        var result = await response.Content.ReadFromJsonAsync<PayPcServiceOrdersResponse>(JsonOptions());
        if (result is null)
        {
            throw new InvalidOperationException("Backend không trả dữ liệu thanh toán dịch vụ.");
        }

        return result;
    }

    private async Task OpenServicePaymentDialogForMachineAsync(
        MachineRow machine,
        IReadOnlyList<PcServiceOrderDto> unpaidOrders)
    {
        var dialog = new Window
        {
            Title = $"Thanh toán dịch vụ - {machine.Name}",
            Width = 980,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            MinWidth = 900,
            MinHeight = 520,
            ShowInTaskbar = false,
            Owner = this,
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleTextBlock = new TextBlock
        {
            Text = $"Máy trạm: {machine.Name}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(titleTextBlock, 0);
        root.Children.Add(titleTextBlock);

        var summaryTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(summaryTextBlock, 1);
        root.Children.Add(summaryTextBlock);

        var orderRows = new ObservableCollection<ServicePaymentRow>();
        var ordersGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            CanUserResizeRows = false,
            IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            ItemsSource = orderRows,
            Margin = new Thickness(0, 0, 0, 10),
        };

        ordersGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Chọn",
            Width = 68,
            Binding = new Binding(nameof(ServicePaymentRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
        });
        ordersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Dịch vụ",
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(ServicePaymentRow.ServiceName)),
        });
        ordersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "SL",
            Width = 65,
            Binding = new Binding(nameof(ServicePaymentRow.Quantity)),
        });
        ordersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Đơn giá",
            Width = 120,
            Binding = new Binding(nameof(ServicePaymentRow.UnitPriceText)),
        });
        ordersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Thành tiền",
            Width = 130,
            Binding = new Binding(nameof(ServicePaymentRow.LineTotalText)),
        });
        ordersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Thời gian gọi",
            Width = 160,
            Binding = new Binding(nameof(ServicePaymentRow.CreatedAtText)),
        });
        ordersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ghi chú",
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(ServicePaymentRow.Note)),
        });

        Grid.SetRow(ordersGrid, 2);
        root.Children.Add(ordersGrid);

        var statusTextBlock = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Text = "Chọn món cần thu rồi bấm \"Thanh toán món đã chọn\". Có thể lặp lại nhiều lần cho đến khi hết món.",
        };
        Grid.SetRow(statusTextBlock, 3);
        root.Children.Add(statusTextBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var selectAllButton = new Button
        {
            Content = "Chọn tất cả",
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var clearSelectionButton = new Button
        {
            Content = "Bỏ chọn",
            Width = 90,
            Margin = new Thickness(0, 0, 16, 0),
        };
        var paySelectedButton = new Button
        {
            Content = "Thanh toán món đã chọn",
            Width = 190,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
        };
        var payAllButton = new Button
        {
            Content = "Thanh toán tất cả",
            Width = 140,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
        };
        var closeButton = new Button
        {
            Content = "Đóng",
            Width = 100,
            IsCancel = true,
        };

        actionPanel.Children.Add(selectAllButton);
        actionPanel.Children.Add(clearSelectionButton);
        actionPanel.Children.Add(paySelectedButton);
        actionPanel.Children.Add(payAllButton);
        actionPanel.Children.Add(closeButton);

        Grid.SetRow(actionPanel, 4);
        root.Children.Add(actionPanel);

        var isPaying = false;
        var hasSuccessfulPayment = false;

        void SyncActionButtons()
        {
            var hasRows = orderRows.Count > 0;
            var hasSelected = orderRows.Any(x => x.IsSelected);
            var canInteract = !isPaying && hasRows;

            selectAllButton.IsEnabled = canInteract;
            clearSelectionButton.IsEnabled = canInteract;
            payAllButton.IsEnabled = canInteract;
            paySelectedButton.IsEnabled = canInteract && hasSelected;
            closeButton.IsEnabled = !isPaying;
        }

        void RefreshSummary()
        {
            var totalAmount = orderRows.Sum(x => x.LineTotal);
            var selectedAmount = orderRows.Where(x => x.IsSelected).Sum(x => x.LineTotal);
            var selectedCount = orderRows.Count(x => x.IsSelected);

            summaryTextBlock.Text =
                $"Chưa thanh toán: {orderRows.Count} món ({totalAmount:N0} VND) | " +
                $"Đã chọn: {selectedCount} món ({selectedAmount:N0} VND)";
            SyncActionButtons();
        }

        void OnOrderRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServicePaymentRow.IsSelected))
            {
                RefreshSummary();
            }
        }

        void ReplaceOrders(IReadOnlyList<PcServiceOrderDto> sourceOrders)
        {
            foreach (var row in orderRows)
            {
                row.PropertyChanged -= OnOrderRowPropertyChanged;
            }

            orderRows.Clear();
            foreach (var source in sourceOrders)
            {
                var row = ServicePaymentRow.FromOrder(source);
                row.PropertyChanged += OnOrderRowPropertyChanged;
                orderRows.Add(row);
            }

            RefreshSummary();
        }

        async Task PayOrdersAsync(bool payAll)
        {
            if (isPaying)
            {
                return;
            }

            var targetRows = payAll
                ? orderRows.ToList()
                : orderRows.Where(x => x.IsSelected).ToList();
            if (targetRows.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất 1 món để thanh toán.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targetAmount = targetRows.Sum(x => x.LineTotal);
            var confirmText = payAll
                ? $"Thanh toán tất cả {targetRows.Count} món ({targetAmount:N0} VND) cho {machine.Name}?"
                : $"Thanh toán {targetRows.Count} món đã chọn ({targetAmount:N0} VND) cho {machine.Name}?";
            var confirm = MessageBox.Show(confirmText, "Server Admin", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                isPaying = true;
                statusTextBlock.Text = "Đang thanh toán dịch vụ...";
                statusTextBlock.Foreground = Brushes.SteelBlue;
                SyncActionButtons();

                var orderIds = payAll ? null : targetRows.Select(x => x.OrderId).ToList();
                var result = await PayServiceOrdersForMachineAsync(
                    machine,
                    orderIds,
                    note: payAll ? "pay_all_services" : "pay_selected_services");

                hasSuccessfulPayment = hasSuccessfulPayment || result.PaidOrderCount > 0;
                AppendServiceLog(
                    $"[{DateTime.Now:HH:mm:ss}] {machine.Name}: thanh toán dịch vụ {result.PaidOrderCount} món = {result.PaidAmount:N0} VND (còn {result.UnpaidAmount:N0} VND)");

                var latestUnpaidOrders = await GetUnpaidServiceOrdersForMachineAsync(machine);
                ReplaceOrders(latestUnpaidOrders);

                if (result.PaidOrderCount <= 0)
                {
                    statusTextBlock.Text = "Không có món hợp lệ để thanh toán (có thể đã được thanh toán trước đó).";
                    statusTextBlock.Foreground = Brushes.DarkGoldenrod;
                    return;
                }

                if (orderRows.Count == 0)
                {
                    statusTextBlock.Text = "Đã thanh toán hết các món trong phiên hiện tại.";
                    statusTextBlock.Foreground = Brushes.DarkGreen;
                    MessageBox.Show("Đã thanh toán hết dịch vụ cho máy này.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
                    dialog.Close();
                    return;
                }

                statusTextBlock.Text = $"Đã thanh toán {result.PaidOrderCount} món ({result.PaidAmount:N0} VND).";
                statusTextBlock.Foreground = Brushes.DarkGreen;
            }
            catch (Exception ex)
            {
                statusTextBlock.Text = $"Thanh toán lỗi: {ex.Message}";
                statusTextBlock.Foreground = Brushes.Firebrick;
            }
            finally
            {
                isPaying = false;
                SyncActionButtons();
            }
        }

        selectAllButton.Click += (_, _) =>
        {
            foreach (var row in orderRows)
            {
                row.IsSelected = true;
            }
            RefreshSummary();
        };

        clearSelectionButton.Click += (_, _) =>
        {
            foreach (var row in orderRows)
            {
                row.IsSelected = false;
            }
            RefreshSummary();
        };

        paySelectedButton.Click += async (_, _) => await PayOrdersAsync(payAll: false);
        payAllButton.Click += async (_, _) => await PayOrdersAsync(payAll: true);
        closeButton.Click += (_, _) => dialog.Close();

        ReplaceOrders(unpaidOrders);

        dialog.Content = root;
        _ = dialog.ShowDialog();

        if (hasSuccessfulPayment)
        {
            InvalidateServiceAmountCacheForMachine(machine);
            await RefreshMachinesAsync();
            await RefreshTransactionLogsAsync();
        }
    }

    private ServiceOrderBatchInput? PromptServiceOrder(
        MachineRow machine,
        IReadOnlyList<ServiceItemRow> items,
        IReadOnlyDictionary<string, ExistingServiceOrderSummary>? existingOrdersByServiceId = null)
    {
        var dialog = new Window
        {
            Title = $"Chọn dịch vụ - {machine.Name}",
            Width = 900,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            MinWidth = 820,
            MinHeight = 520,
            ShowInTaskbar = false,
            Owner = this,
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerText = new TextBlock
        {
            Text = $"Máy trạm: {machine.Name}",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(headerText, 0);
        root.Children.Add(headerText);

        var instructionText = new TextBlock
        {
            Text = "Bấm + để thêm mới và bấm - để hủy dịch vụ đã gọi trước (tối đa bằng số đã gọi chưa thanh toán).",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(instructionText, 1);
        root.Children.Add(instructionText);

        existingOrdersByServiceId ??= new Dictionary<string, ExistingServiceOrderSummary>(StringComparer.OrdinalIgnoreCase);
        var serviceItemById = items.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var previouslyOrderedRows = existingOrdersByServiceId
            .Where(x => x.Value.Quantity > 0 || x.Value.Amount > 0)
            .Select(x =>
            {
                serviceItemById.TryGetValue(x.Key, out var serviceItem);
                return new
                {
                    ServiceName = serviceItem?.Name ?? "Dịch vụ",
                    Quantity = Math.Max(0, x.Value.Quantity),
                    Amount = Math.Max(0, x.Value.Amount),
                };
            })
            .OrderByDescending(x => x.Quantity)
            .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var previouslyOrderedLines = previouslyOrderedRows
            .Select(x => $"{x.ServiceName}: {x.Quantity:N0} ({x.Amount:N0} VND)")
            .ToList();
        var previouslyOrderedTotalQuantity = previouslyOrderedRows.Sum(x => x.Quantity);
        var previouslyOrderedTotalAmount = previouslyOrderedRows.Sum(x => x.Amount);

        var selectionRows = new ObservableCollection<ServiceOrderSelectionRow>(
            items
                .Select(item =>
                {
                    existingOrdersByServiceId.TryGetValue(item.Id, out var existingSummary);
                    return ServiceOrderSelectionRow.FromServiceItem(item, existingSummary);
                })
                .OrderByDescending(x => x.PreviouslyOrderedQuantity)
                .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase));

        var serviceGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            CanUserResizeRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            ItemsSource = selectionRows,
            Margin = new Thickness(0),
        };

        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Dịch vụ",
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(ServiceOrderSelectionRow.ServiceName)),
            IsReadOnly = true,
        });
        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Danh mục",
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(ServiceOrderSelectionRow.Category)),
            IsReadOnly = true,
        });
        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Đơn giá",
            Width = 120,
            Binding = new Binding(nameof(ServiceOrderSelectionRow.UnitPriceText)),
            IsReadOnly = true,
        });
        var quantityTemplateColumn = new DataGridTemplateColumn
        {
            Header = "Số lượng",
            Width = 150,
        };

        var quantityPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
        quantityPanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        quantityPanelFactory.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Center);

        var decreaseButtonFactory = new FrameworkElementFactory(typeof(Button));
        decreaseButtonFactory.SetValue(Button.ContentProperty, "-");
        decreaseButtonFactory.SetValue(Button.WidthProperty, 30d);
        decreaseButtonFactory.SetValue(Button.HeightProperty, 28d);
        decreaseButtonFactory.SetValue(Button.PaddingProperty, new Thickness(0));
        decreaseButtonFactory.SetValue(Button.MarginProperty, new Thickness(0, 0, 6, 0));
        decreaseButtonFactory.SetValue(Button.FontWeightProperty, FontWeights.SemiBold);
        decreaseButtonFactory.SetValue(Button.FontSizeProperty, 14d);
        decreaseButtonFactory.SetValue(Button.ForegroundProperty, Brushes.White);
        decreaseButtonFactory.SetValue(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        decreaseButtonFactory.SetValue(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(185, 28, 28)));
        decreaseButtonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if ((sender as FrameworkElement)?.DataContext is ServiceOrderSelectionRow row)
            {
                row.DecreaseQuantity();
            }
        }));

        var quantityValueFactory = new FrameworkElementFactory(typeof(TextBlock));
        quantityValueFactory.SetValue(TextBlock.WidthProperty, 48d);
        quantityValueFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        quantityValueFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        quantityValueFactory.SetValue(TextBlock.FontSizeProperty, 14d);
        quantityValueFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        quantityValueFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ServiceOrderSelectionRow.Quantity)));

        var increaseButtonFactory = new FrameworkElementFactory(typeof(Button));
        increaseButtonFactory.SetValue(Button.ContentProperty, "+");
        increaseButtonFactory.SetValue(Button.WidthProperty, 30d);
        increaseButtonFactory.SetValue(Button.HeightProperty, 28d);
        increaseButtonFactory.SetValue(Button.PaddingProperty, new Thickness(0));
        increaseButtonFactory.SetValue(Button.MarginProperty, new Thickness(6, 0, 0, 0));
        increaseButtonFactory.SetValue(Button.FontWeightProperty, FontWeights.SemiBold);
        increaseButtonFactory.SetValue(Button.FontSizeProperty, 14d);
        increaseButtonFactory.SetValue(Button.ForegroundProperty, Brushes.White);
        increaseButtonFactory.SetValue(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(34, 197, 94)));
        increaseButtonFactory.SetValue(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(22, 163, 74)));
        increaseButtonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if ((sender as FrameworkElement)?.DataContext is ServiceOrderSelectionRow row)
            {
                row.IncreaseQuantity();
            }
        }));

        quantityPanelFactory.AppendChild(decreaseButtonFactory);
        quantityPanelFactory.AppendChild(quantityValueFactory);
        quantityPanelFactory.AppendChild(increaseButtonFactory);

        quantityTemplateColumn.CellTemplate = new DataTemplate
        {
            VisualTree = quantityPanelFactory,
        };
        serviceGrid.Columns.Add(quantityTemplateColumn);

        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Thành tiền",
            Width = 140,
            Binding = new Binding(nameof(ServiceOrderSelectionRow.LineTotalText)),
            IsReadOnly = true,
        });

        var contentGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10),
        };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var previouslyOrderedPanel = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
        };
        var previouslyOrderedPanelStack = new StackPanel();
        previouslyOrderedPanelStack.Children.Add(new TextBlock
        {
            Text = "Đã chọn trước đó:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        var previouslyOrderedListBox = new ListBox
        {
            Height = 250,
            MinHeight = 220,
            MaxHeight = 360,
            ItemsSource = previouslyOrderedLines,
            IsHitTestVisible = false,
            Focusable = false,
        };
        previouslyOrderedPanelStack.Children.Add(previouslyOrderedListBox);
        previouslyOrderedPanelStack.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Text = previouslyOrderedRows.Count == 0
                ? "Chưa có dịch vụ gọi trước."
                : $"Tổng gọi trước: {previouslyOrderedRows.Count} món | {previouslyOrderedTotalQuantity} SL | {previouslyOrderedTotalAmount:N0} VND",
        });
        previouslyOrderedPanel.Child = previouslyOrderedPanelStack;
        Grid.SetColumn(previouslyOrderedPanel, 0);
        contentGrid.Children.Add(previouslyOrderedPanel);

        Grid.SetColumn(serviceGrid, 1);
        contentGrid.Children.Add(serviceGrid);

        Grid.SetRow(contentGrid, 2);
        root.Children.Add(contentGrid);

        var summaryTextBlock = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var statusPanel = new StackPanel();
        statusPanel.Children.Add(summaryTextBlock);
        statusPanel.Children.Add(errorTextBlock);
        Grid.SetRow(statusPanel, 3);
        root.Children.Add(statusPanel);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var addButton = new Button
        {
            Content = "Cập nhật máy",
            Width = 140,
            Height = 34,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(29, 78, 216)),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Height = 34,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            IsCancel = true,
        };

        buttonPanel.Children.Add(addButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 4);
        root.Children.Add(buttonPanel);

        ServiceOrderBatchInput? result = null;

        void RefreshSummary()
        {
            var selectedRows = selectionRows.Where(x => x.Quantity != 0).ToList();
            var addedRows = selectedRows.Where(x => x.Quantity > 0).ToList();
            var canceledRows = selectedRows.Where(x => x.Quantity < 0).ToList();
            var addedQuantity = addedRows.Sum(x => x.Quantity);
            var canceledQuantity = canceledRows.Sum(x => Math.Abs(x.Quantity));
            var addedAmount = addedRows.Sum(x => x.LineTotal);
            var canceledAmount = canceledRows.Sum(x => Math.Abs(x.LineTotal));
            var netAmount = addedAmount - canceledAmount;

            summaryTextBlock.Text =
                $"Thêm: {addedRows.Count} món/{addedQuantity} SL/{addedAmount:N0} VND | Hủy: {canceledRows.Count} món/{canceledQuantity} SL/{canceledAmount:N0} VND | Chênh lệch: {netAmount:N0} VND";
        }

        void RowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServiceOrderSelectionRow.Quantity))
            {
                RefreshSummary();
            }
        }

        foreach (var row in selectionRows)
        {
            row.PropertyChanged += RowPropertyChanged;
        }

        addButton.Click += (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            var selectedRows = selectionRows
                .Where(x => x.Quantity != 0)
                .Select(x => new ServiceOrderLineInput
                {
                    ServiceItemId = x.ServiceItemId,
                    ServiceName = x.ServiceName,
                    Quantity = x.Quantity,
                    UnitPrice = x.UnitPrice,
                    LineTotal = x.LineTotal,
                })
                .ToList();

            if (selectedRows.Count == 0)
            {
                errorTextBlock.Text = "Vui lòng bấm + hoặc - để thêm/hủy ít nhất 1 dịch vụ.";
                return;
            }

            result = new ServiceOrderBatchInput
            {
                Lines = selectedRows,
                Note = null,
                Total = selectedRows.Sum(x => x.LineTotal),
            };

            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            RefreshSummary();
            serviceGrid.Focus();
        };

        _ = dialog.ShowDialog();

        foreach (var row in selectionRows)
        {
            row.PropertyChanged -= RowPropertyChanged;
        }

        return result;
    }

    private static bool TryParsePositiveMoney(string value, out decimal amount)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0)
        {
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) && amount > 0)
        {
            return true;
        }

        amount = 0;
        return false;
    }

    private async void RefreshServiceItemsButton_Click(object sender, RoutedEventArgs e) => await RefreshServiceItemsAsync();
    private async void CreateServiceItemButton_Click(object sender, RoutedEventArgs e) => await CreateServiceItemAsync();
    private async void UpdateServicePriceButton_Click(object sender, RoutedEventArgs e) => await UpdateSelectedServicePriceAsync();
    private async void ToggleServiceItemButton_Click(object sender, RoutedEventArgs e) => await ToggleSelectedServiceItemAsync();
    private async void ContextSelectServiceMenuItem_Click(object sender, RoutedEventArgs e) => await OpenServiceOrderDialogForSelectedMachineAsync();
    private async void ContextPayServiceMenuItem_Click(object sender, RoutedEventArgs e) => await PayServiceForSelectedMachineAsync();

    private void ServiceItemsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServiceItemsDataGrid.SelectedItem is not ServiceItemRow selected)
        {
            return;
        }

        _selectedServiceItemId = selected.Id;
    }

    private sealed class ServiceOrderBatchInput
    {
        public List<ServiceOrderLineInput> Lines { get; init; } = new();
        public string? Note { get; init; }
        public decimal Total { get; init; }
    }

    private sealed class ServiceOrderLineInput
    {
        public string ServiceItemId { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal LineTotal { get; init; }
    }

    private sealed class ServiceOrderSelectionRow : INotifyPropertyChanged
    {
        private int _quantity;
        private int MinQuantity => -Math.Max(0, PreviouslyOrderedQuantity);

        public string ServiceItemId { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public string Category { get; init; } = "-";
        public decimal UnitPrice { get; init; }
        public int PreviouslyOrderedQuantity { get; init; }
        public decimal PreviouslyOrderedAmount { get; init; }
        public string UnitPriceText => UnitPrice.ToString("N0", CultureInfo.InvariantCulture);
        public string PreviouslyOrderedText =>
            PreviouslyOrderedQuantity <= 0
                ? "-"
                : $"{PreviouslyOrderedQuantity:N0} ({PreviouslyOrderedAmount:N0})";

        public int Quantity
        {
            get => _quantity;
            set
            {
                var clamped = Math.Clamp(value, MinQuantity, 999);
                if (_quantity == clamped)
                {
                    return;
                }

                _quantity = clamped;
                NotifyQuantityChanged();
            }
        }

        public decimal LineTotal => UnitPrice * Quantity;

        public string LineTotalText => LineTotal.ToString("N0", CultureInfo.InvariantCulture);

        public event PropertyChangedEventHandler? PropertyChanged;

        public static ServiceOrderSelectionRow FromServiceItem(
            ServiceItemRow item,
            ExistingServiceOrderSummary? existingSummary = null)
        {
            var previouslyOrderedQuantity = existingSummary?.Quantity ?? 0;
            var previouslyOrderedAmount = existingSummary?.Amount ?? 0;
            return new ServiceOrderSelectionRow
            {
                ServiceItemId = item.Id,
                ServiceName = item.Name,
                Category = string.IsNullOrWhiteSpace(item.Category) ? "-" : item.Category,
                UnitPrice = item.UnitPrice,
                PreviouslyOrderedQuantity = previouslyOrderedQuantity,
                PreviouslyOrderedAmount = previouslyOrderedAmount,
                Quantity = 0,
            };
        }

        public void IncreaseQuantity()
        {
            Quantity = Math.Min(999, Quantity + 1);
        }

        public void DecreaseQuantity()
        {
            Quantity = Math.Max(MinQuantity, Quantity - 1);
        }

        private void NotifyQuantityChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotal)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotalText)));
        }
    }

    private sealed class ExistingServiceOrderSummary
    {
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class ServicePaymentRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string OrderId { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal LineTotal { get; init; }
        public string CreatedAtText { get; init; } = "-";
        public string Note { get; init; } = "-";
        public string UnitPriceText => UnitPrice.ToString("N0", CultureInfo.InvariantCulture);
        public string LineTotalText => LineTotal.ToString("N0", CultureInfo.InvariantCulture);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static ServicePaymentRow FromOrder(PcServiceOrderDto order)
        {
            return new ServicePaymentRow
            {
                OrderId = order.Id,
                ServiceName = string.IsNullOrWhiteSpace(order.ServiceItem?.Name) ? "Dịch vụ" : order.ServiceItem.Name,
                Quantity = order.Quantity,
                UnitPrice = order.UnitPrice,
                LineTotal = order.LineTotal,
                CreatedAtText = FormatDateTime(order.CreatedAt),
                Note = string.IsNullOrWhiteSpace(order.Note) ? "-" : order.Note.Trim(),
            };
        }
    }
}

