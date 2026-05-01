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
        var name = ServiceNameTextBox.Text.Trim();
        var category = ServiceCategoryTextBox.Text.Trim();
        var priceRaw = ServicePriceTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Vui lòng nhập tên dịch vụ.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryParsePositiveMoney(priceRaw, out var unitPrice))
        {
            MessageBox.Show("Giá dịch vụ không hợp lệ.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl("/services/items"),
            new
            {
                name,
                category = string.IsNullOrWhiteSpace(category) ? null : category,
                unitPrice = Convert.ToDouble(unitPrice),
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

        ServiceNameTextBox.Text = string.Empty;
        ServicePriceTextBox.Text = "15000";
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã thêm dịch vụ: {name} ({unitPrice:N0} VND)");
        await RefreshServiceItemsAsync();
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
        await RefreshMachinesAsync();
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
