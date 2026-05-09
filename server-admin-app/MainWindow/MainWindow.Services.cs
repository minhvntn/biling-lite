using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

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

        var orderInput = PromptServiceOrder(selectedMachine, activeItems);
        if (orderInput is null)
        {
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/services/pcs/{selectedMachine.Id}/orders"),
            new
            {
                serviceItemId = orderInput.ServiceItemId,
                quantity = orderInput.Quantity,
                note = orderInput.Note,
                requestedBy = "admin.desktop",
            });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show(
                $"Thêm dịch vụ cho máy thất bại ({(int)response.StatusCode})",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog(
            $"[{DateTime.Now:HH:mm:ss}] {selectedMachine.Name}: +{orderInput.Quantity} x {orderInput.ServiceName} = {orderInput.LineTotal:N0} VND");
        InvalidateServiceAmountCacheForMachine(selectedMachine);
        await RefreshMachinesAsync();
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

        decimal unpaidAmount;
        try
        {
            unpaidAmount = await GetServiceAmountForMachineAsync(selectedMachine);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể tải tiền dịch vụ: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (unpaidAmount <= 0)
        {
            MessageBox.Show(
                "Máy này không còn dịch vụ chưa thanh toán.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Xác nhận thanh toán dịch vụ cho {selectedMachine.Name}: {unpaidAmount:N0} VND?",
            "Thanh toán dịch vụ",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/services/pcs/{selectedMachine.Id}/orders/pay"),
            new
            {
                requestedBy = "admin.desktop",
                note = "Thanh toan dich vu tai may",
            });

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            MessageBox.Show(
                string.IsNullOrWhiteSpace(error)
                    ? $"Thanh toán dịch vụ thất bại ({(int)response.StatusCode})"
                    : error,
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = await response.Content.ReadFromJsonAsync<PayPcServiceOrdersResponse>(JsonOptions());
        var paidAmount = result?.PaidAmount ?? unpaidAmount;
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] {selectedMachine.Name}: đã thanh toán dịch vụ {paidAmount:N0} VND");
        InvalidateServiceAmountCacheForMachine(selectedMachine);
        await RefreshMachinesAsync();
        await RefreshTransactionLogsAsync();
    }

    private ServiceOrderInput? PromptServiceOrder(MachineRow machine, IReadOnlyList<ServiceItemRow> items)
    {
        var dialog = new Window
        {
            Title = $"Chọn dịch vụ - {machine.Name}",
            Width = 560,
            Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
            Owner = this,
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = $"Máy trạm: {machine.Name}",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        content.Children.Add(new TextBlock
        {
            Text = "Chọn dịch vụ:",
            Margin = new Thickness(0, 0, 0, 4),
        });

        var serviceList = new ListBox
        {
            MinHeight = 220,
            ItemsSource = items,
            DisplayMemberPath = nameof(ServiceItemRow.DisplayText),
            SelectedIndex = 0,
        };
        content.Children.Add(serviceList);

        var quantityPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };

        quantityPanel.Children.Add(new TextBlock
        {
            Text = "Số lượng:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        var quantityTextBox = new TextBox
        {
            Width = 64,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Text = "1",
        };
        quantityPanel.Children.Add(quantityTextBox);
        var decreaseQuantityButton = new Button
        {
            Content = "-",
            Width = 30,
            Height = 26,
            Margin = new Thickness(8, 0, 4, 0),
        };
        quantityPanel.Children.Add(decreaseQuantityButton);
        var increaseQuantityButton = new Button
        {
            Content = "+",
            Width = 30,
            Height = 26,
        };
        quantityPanel.Children.Add(increaseQuantityButton);
        content.Children.Add(quantityPanel);

        content.Children.Add(new TextBlock
        {
            Text = "Ghi chú (không bắt buộc):",
            Margin = new Thickness(0, 10, 0, 4),
        });

        var noteTextBox = new TextBox
        {
            Height = 72,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        content.Children.Add(noteTextBox);

        var totalTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            FontWeight = FontWeights.SemiBold,
        };
        content.Children.Add(totalTextBlock);

        var errorTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = System.Windows.Media.Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
        };
        content.Children.Add(errorTextBlock);

        Grid.SetRow(content, 0);
        root.Children.Add(content);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var addButton = new Button
        {
            Content = "Thêm vào máy",
            Width = 120,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            IsCancel = true,
        };

        buttonPanel.Children.Add(addButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 1);
        root.Children.Add(buttonPanel);

        ServiceOrderInput? result = null;

        int GetCurrentQuantityOrDefault()
        {
            return TryParsePositiveInteger(quantityTextBox.Text.Trim(), out var quantity)
                ? quantity
                : 1;
        }

        void SetQuantity(int quantity)
        {
            var clamped = Math.Clamp(quantity, 1, 999);
            quantityTextBox.Text = clamped.ToString(CultureInfo.InvariantCulture);
        }

        void RefreshTotal()
        {
            if (serviceList.SelectedItem is not ServiceItemRow selectedItem ||
                !TryParsePositiveInteger(quantityTextBox.Text.Trim(), out var quantity))
            {
                totalTextBlock.Text = "Tổng tạm tính: -";
                return;
            }

            var total = selectedItem.UnitPrice * quantity;
            totalTextBlock.Text = $"Tổng tạm tính: {total:N0} VND";
        }

        serviceList.SelectionChanged += (_, _) => RefreshTotal();
        quantityTextBox.TextChanged += (_, _) => RefreshTotal();
        quantityTextBox.PreviewTextInput += (_, inputEvent) =>
        {
            inputEvent.Handled = inputEvent.Text.Any(character => !char.IsDigit(character));
        };
        DataObject.AddPastingHandler(quantityTextBox, (_, pasteEvent) =>
        {
            if (!pasteEvent.DataObject.GetDataPresent(DataFormats.Text))
            {
                pasteEvent.CancelCommand();
                return;
            }

            var pastedText = pasteEvent.DataObject.GetData(DataFormats.Text) as string;
            if (string.IsNullOrWhiteSpace(pastedText) || pastedText.Any(character => !char.IsDigit(character)))
            {
                pasteEvent.CancelCommand();
            }
        });
        quantityTextBox.GotFocus += (_, _) => quantityTextBox.SelectAll();
        quantityTextBox.LostFocus += (_, _) => SetQuantity(GetCurrentQuantityOrDefault());
        decreaseQuantityButton.Click += (_, _) => SetQuantity(GetCurrentQuantityOrDefault() - 1);
        increaseQuantityButton.Click += (_, _) => SetQuantity(GetCurrentQuantityOrDefault() + 1);

        addButton.Click += (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            if (serviceList.SelectedItem is not ServiceItemRow selectedItem)
            {
                errorTextBlock.Text = "Vui lòng chọn dịch vụ.";
                return;
            }

            if (!TryParsePositiveInteger(quantityTextBox.Text.Trim(), out var quantity))
            {
                errorTextBlock.Text = "Số lượng phải từ 1 đến 999.";
                return;
            }

            var lineTotal = selectedItem.UnitPrice * quantity;
            result = new ServiceOrderInput
            {
                ServiceItemId = selectedItem.Id,
                ServiceName = selectedItem.Name,
                Quantity = quantity,
                Note = string.IsNullOrWhiteSpace(noteTextBox.Text) ? null : noteTextBox.Text.Trim(),
                LineTotal = lineTotal,
            };

            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            RefreshTotal();
            serviceList.Focus();
        };

        _ = dialog.ShowDialog();
        return result;
    }

    private static bool TryParsePositiveInteger(string value, out int parsed)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed is >= 1 and <= 999;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            return parsed is >= 1 and <= 999;
        }

        return false;
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

    private sealed class ServiceOrderInput
    {
        public string ServiceItemId { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public string? Note { get; init; }
        public decimal LineTotal { get; init; }
    }
}

