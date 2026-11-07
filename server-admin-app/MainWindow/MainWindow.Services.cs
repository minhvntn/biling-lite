using System.Globalization;
using System.Net.Http.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

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

            if (response is null) return;

            var allMapped = response.Items
                .Select(ToServiceItemRow)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Category)
                .ThenBy(x => x.Name)
                .ToList();

            _serviceCategoryRows.Clear();
            var allCount = allMapped.Count;
            _serviceCategoryRows.Add(new ServiceCategoryViewModel { Name = "Tất cả dịch vụ", Icon = "📁", Count = allCount, IsSelected = _currentSelectedServiceCategory == "Tất cả dịch vụ" });
            
            var groups = allMapped.GroupBy(x => x.Category).OrderBy(x => x.Key).ToList();
            foreach (var g in groups)
            {
                var catName = g.Key;
                var icon = "📌";
                if (catName.Contains("Đồ ăn", StringComparison.OrdinalIgnoreCase) || catName.Contains("Food", StringComparison.OrdinalIgnoreCase)) icon = "🍔";
                else if (catName.Contains("Nước", StringComparison.OrdinalIgnoreCase) || catName.Contains("Drink", StringComparison.OrdinalIgnoreCase)) icon = "🥤";
                else if (catName.Contains("Thẻ", StringComparison.OrdinalIgnoreCase) || catName.Contains("Card", StringComparison.OrdinalIgnoreCase)) icon = "💳";

                _serviceCategoryRows.Add(new ServiceCategoryViewModel { Name = catName, Icon = icon, Count = g.Count(), IsSelected = _currentSelectedServiceCategory == catName });
            }

            if (_serviceCategoryRows.All(x => !x.IsSelected))
            {
                _currentSelectedServiceCategory = "Tất cả dịch vụ";
                _serviceCategoryRows[0].IsSelected = true;
            }

            IEnumerable<ServiceItemRow> filtered = allMapped;
            if (_currentSelectedServiceCategory != "Tất cả dịch vụ")
            {
                filtered = allMapped.Where(x => x.Category == _currentSelectedServiceCategory);
            }

            _serviceItemRows.Clear();
            foreach (var row in filtered)
            {
                _serviceItemRows.Add(row);
            }

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                var selected = _serviceItemRows.FirstOrDefault(x => x.Id == selectedId);
                if (selected is not null)
                {
                    if (!ReferenceEquals(ServiceItemsDataGrid.SelectedItem, selected))
                    {
                        ServiceItemsDataGrid.SelectedItem = selected;
                    }
                    _selectedServiceItemId = selected.Id;
                }
            }

            var activeCount = allMapped.Count(x => x.IsActive);
            var inactiveCount = allMapped.Count(x => !x.IsActive);
            var lastUpdated = allMapped.Count > 0 ? allMapped.Max(x => x.UpdatedAtText) : "-";

            StatsTotalCountTextBlock.Text = allMapped.Count.ToString();
            StatsActiveCountTextBlock.Text = activeCount.ToString();
            StatsInactiveCountTextBlock.Text = inactiveCount.ToString();
            StatsLastUpdatedTextBlock.Text = lastUpdated;

            ServiceInfoTextBlock.Text = $"Đã tải {allMapped.Count} dịch vụ.";

            await FetchServiceStatsAsync();
        }
        catch
        {
            // Keep UI responsive when backend is temporarily unavailable.
        }
    }

    private async Task FetchServiceStatsAsync()
    {
        try
        {
            var dashResponse = await _httpClient.GetFromJsonAsync<DashboardStatsResponse>(
                BuildApiUrl("/reports/dashboard?period=day"),
                JsonOptions());

            if (dashResponse is null) return;

            StatsRevenueTextBlock.Text = dashResponse.ServiceRevenue.ToString("N0", CultureInfo.InvariantCulture);

            _servicesTabTopServiceRows.Clear();
            var topItems = dashResponse.TopServiceItems.Take(5).ToList();
            for (int i = 0; i < topItems.Count; i++)
            {
                var item = topItems[i];
                var color = i switch
                {
                    0 => ("#FEF2F2", "#DC2626"),
                    1 => ("#FFFBEB", "#D97706"),
                    2 => ("#F0FDF4", "#16A34A"),
                    _ => ("#F3F4F6", "#475569")
                };

                _servicesTabTopServiceRows.Add(new ServiceTopItemViewModel
                {
                    RankNumber = (i + 1).ToString(),
                    RankBackground = color.Item1,
                    RankForeground = color.Item2,
                    ServiceName = item.Name,
                    MetaText = $"{item.Quantity} ly/phần",
                    RevenueText = item.Revenue.ToString("N0", CultureInfo.InvariantCulture)
                });
            }

            _servicesTabCategoryRevenueRows.Clear();
            if (dashResponse.ServiceRevenue > 0)
            {
                var catGroups = dashResponse.TopServiceItems.GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "Khác" : x.Category)
                                  .Select(g => new { Category = g.Key, Revenue = g.Sum(i => i.Revenue) })
                                  .OrderByDescending(g => g.Revenue)
                                  .ToList();

                var colors = new[] { "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6" };
                for (int i = 0; i < catGroups.Count; i++)
                {
                    var catGroup = catGroups[i];
                    var pct = (double)catGroup.Revenue / (double)dashResponse.ServiceRevenue * 100;
                    _servicesTabCategoryRevenueRows.Add(new ServiceCategoryRevenueViewModel
                    {
                        CategoryName = catGroup.Category,
                        RevenueText = catGroup.Revenue.ToString("N0", CultureInfo.InvariantCulture),
                        PercentageText = $"{pct:F1}%",
                        ProgressValue = pct,
                        BarColor = colors[i % colors.Length],
                        DotColor = colors[i % colors.Length]
                    });
                }
            }
        }
        catch { }
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
            ImageDataUrl = item.ImageDataUrl,
            ServiceImageSource = BuildServiceImageSource(item.ImageDataUrl),
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
                imageDataUrl = input.ImageDataUrl,
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
            Width = 540,
            Height = 520,
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
        var categoryComboBox = new ComboBox 
        { 
            Height = 28, 
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEditable = true 
        };
        categoryComboBox.Items.Add("Nước");
        categoryComboBox.Items.Add("Đồ ăn");
        categoryComboBox.Items.Add("Ăn vặt");
        categoryComboBox.Items.Add("Thuốc");
        colLeft.Children.Add(categoryComboBox);
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

        // 4. Hình ảnh dịch vụ
        string? imageDataUrl = null;
        content.Children.Add(new TextBlock { Text = "Hình ảnh dịch vụ:", Margin = new Thickness(0, 0, 0, 4) });

        var imagePickerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        imagePickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        imagePickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        imagePickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var imagePathTextBox = new TextBox
        {
            Height = 28,
            IsReadOnly = true,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "Chưa chọn ảnh",
        };
        Grid.SetColumn(imagePathTextBox, 0);
        imagePickerGrid.Children.Add(imagePathTextBox);

        var browseImageButton = new Button
        {
            Content = "Chọn ảnh...",
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2),
        };
        Grid.SetColumn(browseImageButton, 1);
        imagePickerGrid.Children.Add(browseImageButton);

        var clearImageButton = new Button
        {
            Content = "Xóa",
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2),
            IsEnabled = false,
            Style = (Style)FindResource("DangerButtonStyle"),
        };
        Grid.SetColumn(clearImageButton, 2);
        imagePickerGrid.Children.Add(clearImageButton);

        content.Children.Add(imagePickerGrid);

        var imagePreviewBorder = new Border
        {
            Width = 92,
            Height = 92,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Margin = new Thickness(0, 0, 0, 4),
            Child = new TextBlock
            {
                Text = "Không ảnh",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.DimGray,
                FontSize = 12,
            },
        };
        content.Children.Add(imagePreviewBorder);

        var imageStatusTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Brushes.DimGray,
            Text = "Có thể bỏ trống nếu dịch vụ không cần ảnh.",
            TextWrapping = TextWrapping.Wrap,
        };
        content.Children.Add(imageStatusTextBlock);

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
            Style = (Style)FindResource("DangerButtonStyle"),
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

        browseImageButton.Click += (_, _) =>
        {
            var picker = new OpenFileDialog
            {
                CheckFileExists = true,
                Multiselect = false,
                Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*",
                Title = "Chọn hình ảnh dịch vụ",
            };

            if (picker.ShowDialog(this) != true)
            {
                return;
            }

            if (!TryBuildServiceImageDataUrl(picker.FileName, out var dataUrl, out var error))
            {
                errorTextBlock.Text = error;
                return;
            }

            var previewSource = BuildServiceImageSource(dataUrl);
            if (previewSource is null)
            {
                errorTextBlock.Text = "Không thể đọc ảnh đã chọn. Vui lòng dùng ảnh khác.";
                return;
            }

            errorTextBlock.Text = string.Empty;
            imageDataUrl = dataUrl;
            imagePathTextBox.Text = picker.FileName;
            clearImageButton.IsEnabled = true;
            imageStatusTextBlock.Text = $"Đã chọn ảnh: {Path.GetFileName(picker.FileName)}";

            imagePreviewBorder.Child = new Image
            {
                Source = previewSource,
                Stretch = Stretch.UniformToFill,
                SnapsToDevicePixels = true,
            };
        };

        clearImageButton.Click += (_, _) =>
        {
            imageDataUrl = null;
            imagePathTextBox.Text = "Chưa chọn ảnh";
            clearImageButton.IsEnabled = false;
            imageStatusTextBlock.Text = "Có thể bỏ trống nếu dịch vụ không cần ảnh.";
            imagePreviewBorder.Child = new TextBlock
            {
                Text = "Không ảnh",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.DimGray,
                FontSize = 12,
            };
            errorTextBlock.Text = string.Empty;
        };

        CreateServiceItemInput? result = null;

        createButton.Click += (s, e) =>
        {
            errorTextBlock.Text = string.Empty;

            var name = nameTextBox.Text.Trim();
            var category = categoryComboBox.Text.Trim();
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
                ImageDataUrl = imageDataUrl,
            };

            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.Content = root;
        dialog.Loaded += (s, e) => nameTextBox.Focus();
        _ = dialog.ShowModal(this);

        return result;
    }

    private sealed class CreateServiceItemInput
    {
        public string Name { get; init; } = string.Empty;
        public string? Category { get; init; }
        public decimal UnitPrice { get; init; }
        public string Quantity { get; init; } = "Không giới hạn";
        public string? ImageDataUrl { get; init; }
    }

    private async Task UpdateSelectedServiceItemAsync()
    {
        if (ServiceItemsDataGrid.SelectedItem is not ServiceItemRow selected)
        {
            MessageBox.Show("Vui lòng chọn dịch vụ trước.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptUpdateServiceItem(selected);
        if (input is null)
        {
            return;
        }

        using var response = await _httpClient.PatchAsJsonAsync(
            BuildApiUrl($"/services/items/{selected.Id}"),
            new
            {
                name = input.Name,
                category = input.Category,
                unitPrice = Convert.ToDouble(input.UnitPrice),
                imageDataUrl = input.ImageDataUrl,
            });

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            MessageBox.Show(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Sửa dịch vụ thất bại ({(int)response.StatusCode})"
                    : errorBody,
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog(
            $"[{DateTime.Now:HH:mm:ss}] Đã sửa dịch vụ: {selected.Name} -> {input.Name} ({input.UnitPrice:N0} VND)");
        await RefreshServiceItemsAsync();
    }

    private UpdateServiceItemInput? PromptUpdateServiceItem(ServiceItemRow selected)
    {
        var dialog = new Window
        {
            Title = "Sửa dịch vụ",
            Width = 540,
            Height = 500,
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
            Text = "Cập nhật thông tin dịch vụ",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16),
        });

        content.Children.Add(new TextBlock { Text = "Tên dịch vụ:", Margin = new Thickness(0, 0, 0, 4) });
        var nameTextBox = new TextBox
        {
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
            Text = selected.Name,
        };
        content.Children.Add(nameTextBox);

        var gridFields = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        gridFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        gridFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var colLeft = new StackPanel();
        colLeft.Children.Add(new TextBlock { Text = "Danh mục:", Margin = new Thickness(0, 0, 0, 4) });
        var categoryComboBox = new ComboBox
        {
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = selected.Category == "-" ? string.Empty : selected.Category,
            IsEditable = true
        };
        categoryComboBox.Items.Add("Nước");
        categoryComboBox.Items.Add("Đồ ăn");
        categoryComboBox.Items.Add("Ăn vặt");
        categoryComboBox.Items.Add("Thuốc");
        colLeft.Children.Add(categoryComboBox);
        Grid.SetColumn(colLeft, 0);
        gridFields.Children.Add(colLeft);

        var colRight = new StackPanel();
        colRight.Children.Add(new TextBlock { Text = "Giá bán (VND):", Margin = new Thickness(0, 0, 0, 4) });
        var priceTextBox = new TextBox
        {
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = selected.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture),
        };
        colRight.Children.Add(priceTextBox);
        Grid.SetColumn(colRight, 2);
        gridFields.Children.Add(colRight);

        content.Children.Add(gridFields);

        string? imageDataUrl = selected.ImageDataUrl;
        content.Children.Add(new TextBlock { Text = "Hình ảnh dịch vụ:", Margin = new Thickness(0, 0, 0, 4) });

        var imagePickerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        imagePickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        imagePickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        imagePickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var imagePathTextBox = new TextBox
        {
            Height = 28,
            IsReadOnly = true,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = string.IsNullOrWhiteSpace(imageDataUrl) ? "Chưa chọn ảnh" : "Ảnh hiện tại",
        };
        Grid.SetColumn(imagePathTextBox, 0);
        imagePickerGrid.Children.Add(imagePathTextBox);

        var browseImageButton = new Button
        {
            Content = "Chọn ảnh...",
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2),
        };
        Grid.SetColumn(browseImageButton, 1);
        imagePickerGrid.Children.Add(browseImageButton);

        var clearImageButton = new Button
        {
            Content = "Xóa",
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2),
            IsEnabled = !string.IsNullOrWhiteSpace(imageDataUrl),
            Style = (Style)FindResource("DangerButtonStyle"),
        };
        Grid.SetColumn(clearImageButton, 2);
        imagePickerGrid.Children.Add(clearImageButton);

        content.Children.Add(imagePickerGrid);

        var imagePreviewBorder = new Border
        {
            Width = 92,
            Height = 92,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Margin = new Thickness(0, 0, 0, 4),
        };
        content.Children.Add(imagePreviewBorder);

        var imageStatusTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Brushes.DimGray,
            Text = string.IsNullOrWhiteSpace(imageDataUrl)
                ? "Có thể bỏ trống nếu dịch vụ không cần ảnh."
                : "Đang dùng ảnh hiện tại.",
            TextWrapping = TextWrapping.Wrap,
        };
        content.Children.Add(imageStatusTextBlock);

        var errorTextBlock = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        content.Children.Add(errorTextBlock);

        UIElement BuildNoImagePlaceholder()
        {
            return new TextBlock
            {
                Text = "Không ảnh",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.DimGray,
                FontSize = 12,
            };
        }

        void SetPreview(ImageSource? source)
        {
            if (source is null)
            {
                imagePreviewBorder.Child = BuildNoImagePlaceholder();
                return;
            }

            imagePreviewBorder.Child = new Image
            {
                Source = source,
                Stretch = Stretch.UniformToFill,
                SnapsToDevicePixels = true,
            };
        }

        SetPreview(BuildServiceImageSource(imageDataUrl));

        Grid.SetRow(content, 0);
        root.Children.Add(content);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var saveButton = new Button
        {
            Content = "Lưu",
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
            Style = (Style)FindResource("DangerButtonStyle"),
        };
        buttonPanel.Children.Add(saveButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 1);
        root.Children.Add(buttonPanel);

        browseImageButton.Click += (_, _) =>
        {
            var picker = new OpenFileDialog
            {
                CheckFileExists = true,
                Multiselect = false,
                Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*",
                Title = "Chọn hình ảnh dịch vụ",
            };

            if (picker.ShowDialog(this) != true)
            {
                return;
            }

            if (!TryBuildServiceImageDataUrl(picker.FileName, out var dataUrl, out var error))
            {
                errorTextBlock.Text = error;
                return;
            }

            var previewSource = BuildServiceImageSource(dataUrl);
            if (previewSource is null)
            {
                errorTextBlock.Text = "Không thể đọc ảnh đã chọn. Vui lòng dùng ảnh khác.";
                return;
            }

            errorTextBlock.Text = string.Empty;
            imageDataUrl = dataUrl;
            imagePathTextBox.Text = picker.FileName;
            clearImageButton.IsEnabled = true;
            imageStatusTextBlock.Text = $"Đã chọn ảnh: {Path.GetFileName(picker.FileName)}";
            SetPreview(previewSource);
        };

        clearImageButton.Click += (_, _) =>
        {
            imageDataUrl = null;
            imagePathTextBox.Text = "Chưa chọn ảnh";
            clearImageButton.IsEnabled = false;
            imageStatusTextBlock.Text = "Có thể bỏ trống nếu dịch vụ không cần ảnh.";
            errorTextBlock.Text = string.Empty;
            SetPreview(null);
        };

        UpdateServiceItemInput? result = null;

        saveButton.Click += (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            var name = nameTextBox.Text.Trim();
            var category = categoryComboBox.Text.Trim();
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

            result = new UpdateServiceItemInput
            {
                Name = name,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                UnitPrice = unitPrice,
                ImageDataUrl = imageDataUrl,
            };

            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) => nameTextBox.Focus();
        _ = dialog.ShowModal(this);

        return result;
    }

    private sealed class UpdateServiceItemInput
    {
        public string Name { get; init; } = string.Empty;
        public string? Category { get; init; }
        public decimal UnitPrice { get; init; }
        public string? ImageDataUrl { get; init; }
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

    private async void DeleteServiceItemMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ServiceItemsDataGrid.SelectedItem is not ServiceItemRow selected)
        {
            return;
        }

        var confirm = MessageBox.Show($"Bạn có chắc chắn muốn xóa dịch vụ \"{selected.Name}\" không?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        using var response = await _httpClient.DeleteAsync(BuildApiUrl($"/services/items/{selected.Id}"));
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            MessageBox.Show(string.IsNullOrWhiteSpace(err) ? "Không thể xóa dịch vụ này. Có thể dịch vụ đã được sử dụng trong các đơn hàng." : err, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã xóa dịch vụ: {selected.Name}");
        await RefreshServiceItemsAsync();
    }

    private void ServiceItemsDataGridRow_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            row.IsSelected = true;
        }
    }

    private async void ServiceItemsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ServiceItemsDataGrid.SelectedItem is ServiceItemRow)
        {
            await UpdateSelectedServiceItemAsync();
        }
    }

    private async Task OpenServiceOrderDialogForSelectedMachineAsync()
    {
        if (MachinesDataGrid.SelectedItem is not MachineRow selectedMachine)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!IsServiceOrderEligibleMachine(selectedMachine))
        {
            MessageBox.Show("Tính năng chọn dịch vụ chỉ dành cho khách vãng lai và hội viên VIP.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
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

        List<PcServiceOrderDto> unpaidOrders;
        try
        {
            unpaidOrders = await GetUnpaidServiceOrdersForMachineAsync(selectedMachine);
        }
        catch
        {
            unpaidOrders = new List<PcServiceOrderDto>();
        }

        var existingOrdersByServiceId = BuildUnpaidServiceOrderSummary(unpaidOrders);
        var pendingClientOrders = unpaidOrders
            .Where(x => IsClientServiceRequester(x.CreatedBy))
            .Where(x => !IsClientServiceOrderAcknowledged(x.Id))
            .OrderBy(x => ParseDateLocal(x.CreatedAt) ?? DateTime.MaxValue)
            .ToList();

        var orderInput = PromptServiceOrder(
            selectedMachine,
            activeItems,
            existingOrdersByServiceId,
            pendingClientOrders);
        if (orderInput is null)
        {
            return;
        }

        var acknowledgedPendingOrderIds = orderInput.AcknowledgedClientOrderIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var canceledPendingOrderIds = orderInput.CanceledClientOrderIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sessionId = string.IsNullOrWhiteSpace(selectedMachine.ActiveSessionId)
            ? null
            : selectedMachine.ActiveSessionId;

        if (acknowledgedPendingOrderIds.Count > 0)
        {
            AcknowledgeClientServiceOrders(acknowledgedPendingOrderIds);
            AppendServiceLog(
                $"[{DateTime.Now:HH:mm:ss}] {selectedMachine.Name}: đã xác nhận {acknowledgedPendingOrderIds.Count} order dịch vụ chờ từ máy trạm.");
        }

        if (canceledPendingOrderIds.Count > 0)
        {
            using var cancelPendingResponse = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/services/pcs/{selectedMachine.Id}/orders/cancel"),
                new
                {
                    orderIds = canceledPendingOrderIds,
                    sessionId,
                    note = "Hủy order chờ do server xử lý (ví dụ hết hàng).",
                    requestedBy = "admin.desktop",
                });

            if (!cancelPendingResponse.IsSuccessStatusCode)
            {
                var errorBody = await cancelPendingResponse.Content.ReadAsStringAsync();
                var errorText = string.IsNullOrWhiteSpace(errorBody)
                    ? $"Hủy order chờ thất bại ({(int)cancelPendingResponse.StatusCode})"
                    : errorBody;
                MessageBox.Show(errorText, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendServiceLog(
                $"[{DateTime.Now:HH:mm:ss}] {selectedMachine.Name}: đã hủy {canceledPendingOrderIds.Count} order dịch vụ chờ từ máy trạm.");
        }

        var adjustmentLines = orderInput.Lines
            .Where(x => x.Quantity != 0)
            .ToList();
        if (adjustmentLines.Count == 0)
        {
            if (acknowledgedPendingOrderIds.Count > 0 || canceledPendingOrderIds.Count > 0)
            {
                InvalidateServiceAmountCacheForMachine(selectedMachine);
                await RefreshMachinesAsync();
            }
            return;
        }

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
        else if (acknowledgedPendingOrderIds.Count > 0 || canceledPendingOrderIds.Count > 0)
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
        return BuildUnpaidServiceOrderSummary(orders);
    }

    private static Dictionary<string, ExistingServiceOrderSummary> BuildUnpaidServiceOrderSummary(
        IReadOnlyCollection<PcServiceOrderDto> orders)
    {
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

            summary.AddFromOrder(order.Quantity, order.LineTotal, IsClientServiceRequester(order.CreatedBy));
        }

        return summaryByServiceId;
    }

    private static bool IsClientServiceRequester(string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(createdBy))
        {
            return false;
        }

        var normalized = createdBy.Trim().ToLowerInvariant();
        if (normalized == "admin" || normalized.StartsWith("admin.", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private async Task<PayPcServiceOrdersResponse> PayServiceOrdersForMachineAsync(
        MachineRow machine,
        IReadOnlyList<string>? orderIds,
        string? note = null)
    {
        if (machine.IsVirtual)
        {
            machine.ServiceAmountRaw = 0;
            machine.ServiceAmountText = "0";
            machine.HasPendingClientServiceOrderHighlight = false;
            machine.HasServiceDebtHighlight = false;
            return new PayPcServiceOrdersResponse
            {
                PcId = machine.Id,
                SessionId = machine.ActiveSessionId,
                PaidOrderCount = 1,
                PaidAmount = 0,
                UnpaidAmount = 0,
            };
        }

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

    private async Task<CancelPcServiceOrdersResponse> CancelServiceOrdersForMachineAsync(
        MachineRow machine,
        IReadOnlyList<string>? orderIds,
        string? note = null)
    {
        var payload = new
        {
            requestedBy = "admin.desktop",
            note = note ?? "Xóa dịch vụ do cộng nhầm",
            orderIds = orderIds is { Count: > 0 } ? orderIds : null,
        };

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/services/pcs/{machine.Id}/orders/cancel"),
            payload,
            JsonOptions());

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            var errorText = string.IsNullOrWhiteSpace(errorBody)
                ? $"Xóa dịch vụ thất bại ({(int)response.StatusCode})"
                : errorBody;
            throw new InvalidOperationException(errorText);
        }

        var result = await response.Content.ReadFromJsonAsync<CancelPcServiceOrdersResponse>(JsonOptions());
        if (result is null)
        {
            throw new InvalidOperationException("Backend không trả dữ liệu xóa dịch vụ.");
        }

        return result;
    }

    private async Task OpenServicePaymentDialogForMachineAsync(
        MachineRow machine,
        IReadOnlyList<PcServiceOrderDto> unpaidOrders)
    {
        var dialog = new Windows.PayServiceWindow
        {
            Title = $"Thanh toán dịch vụ - {machine.Name}",
            Owner = this,
        };

        dialog.TitleTextBlock.Text = $"Máy trạm: {machine.Name}";
        
        var orderRows = new ObservableCollection<ServicePaymentRow>();
        dialog.OrdersGrid.ItemsSource = orderRows;

        var isPaying = false;
        var hasSuccessfulPayment = false;

        void SyncActionButtons()
        {
            var hasRows = orderRows.Count > 0;
            var hasSelected = orderRows.Any(x => x.IsSelected);
            var canInteract = !isPaying && hasRows;

            dialog.SelectAllButton.IsEnabled = canInteract;
            dialog.ClearSelectionButton.IsEnabled = canInteract;
            dialog.CancelSelectedButton.IsEnabled = canInteract && hasSelected;
            dialog.PayAllButton.IsEnabled = canInteract;
            dialog.PaySelectedButton.IsEnabled = canInteract && hasSelected;
            dialog.CloseButton.IsEnabled = !isPaying;
        }

        void RefreshSummary()
        {
            var totalAmount = orderRows.Sum(x => x.LineTotal);
            var selectedAmount = orderRows.Where(x => x.IsSelected).Sum(x => x.LineTotal);
            var selectedCount = orderRows.Count(x => x.IsSelected);

            dialog.SummaryTextBlock.Text =
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
                dialog.StatusTextBlock.Text = "Đang thanh toán dịch vụ...";
                dialog.StatusTextBlock.Foreground = Brushes.SteelBlue;
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
                    dialog.StatusTextBlock.Text = "Không có món hợp lệ để thanh toán (có thể đã được thanh toán trước đó).";
                    dialog.StatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                    return;
                }

                if (orderRows.Count == 0)
                {
                    dialog.StatusTextBlock.Text = "Đã thanh toán hết các món trong phiên hiện tại.";
                    dialog.StatusTextBlock.Foreground = Brushes.DarkGreen;
                    MessageBox.Show("Đã thanh toán hết dịch vụ cho máy này.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
                    dialog.Close();
                    return;
                }

                dialog.StatusTextBlock.Text = $"Đã thanh toán {result.PaidOrderCount} món ({result.PaidAmount:N0} VND).";
                dialog.StatusTextBlock.Foreground = Brushes.DarkGreen;
            }
            catch (Exception ex)
            {
                dialog.StatusTextBlock.Text = $"Thanh toán lỗi: {ex.Message}";
                dialog.StatusTextBlock.Foreground = Brushes.Firebrick;
            }
            finally
            {
                isPaying = false;
                SyncActionButtons();
            }
        }

        dialog.SelectAllButton.Click += (_, _) =>
        {
            foreach (var row in orderRows)
            {
                row.IsSelected = true;
            }
            RefreshSummary();
        };

        dialog.ClearSelectionButton.Click += (_, _) =>
        {
            foreach (var row in orderRows)
            {
                row.IsSelected = false;
            }
            RefreshSummary();
        };

        dialog.PaySelectedButton.Click += async (_, _) => await PayOrdersAsync(payAll: false);
        async Task CancelSelectedOrdersAsync()
        {
            if (isPaying)
            {
                return;
            }

            var targetRows = orderRows.Where(x => x.IsSelected).ToList();
            if (targetRows.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất 1 món để xóa.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targetAmount = targetRows.Sum(x => x.LineTotal);
            var confirmText = $"Bạn có chắc chắn muốn XÓA BỎ {targetRows.Count} món dịch vụ đã chọn ({targetAmount:N0} VND) cho {machine.Name} do cộng nhầm?";
            var confirm = MessageBox.Show(confirmText, "Xác nhận xóa dịch vụ", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                isPaying = true;
                dialog.StatusTextBlock.Text = "Đang xóa bỏ dịch vụ...";
                dialog.StatusTextBlock.Foreground = Brushes.SteelBlue;
                SyncActionButtons();

                var orderIds = targetRows.Select(x => x.OrderId).ToList();
                var result = await CancelServiceOrdersForMachineAsync(
                    machine,
                    orderIds,
                    note: "Xóa dịch vụ do cộng nhầm");

                hasSuccessfulPayment = true;
                AppendServiceLog(
                    $"[{DateTime.Now:HH:mm:ss}] {machine.Name}: xóa bỏ {result.CanceledOrderCount} món = {result.CanceledAmount:N0} VND");

                var latestUnpaidOrders = await GetUnpaidServiceOrdersForMachineAsync(machine);
                ReplaceOrders(latestUnpaidOrders);

                if (orderRows.Count == 0)
                {
                    dialog.StatusTextBlock.Text = "Đã xóa hết các món trong phiên hiện tại.";
                    dialog.StatusTextBlock.Foreground = Brushes.DarkGreen;
                    MessageBox.Show("Đã xóa hết các món dịch vụ cho máy này.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
                    dialog.Close();
                    return;
                }

                dialog.StatusTextBlock.Text = $"Đã xóa bỏ {result.CanceledOrderCount} món ({result.CanceledAmount:N0} VND).";
                dialog.StatusTextBlock.Foreground = Brushes.DarkGreen;
            }
            catch (Exception ex)
            {
                dialog.StatusTextBlock.Text = $"Lỗi xóa dịch vụ: {ex.Message}";
                dialog.StatusTextBlock.Foreground = Brushes.Firebrick;
            }
            finally
            {
                isPaying = false;
                SyncActionButtons();
            }
        }

        dialog.CancelSelectedButton.Click += async (_, _) => await CancelSelectedOrdersAsync();
        dialog.PayAllButton.Click += async (_, _) => await PayOrdersAsync(payAll: true);
        dialog.CloseButton.Click += (_, _) => dialog.Close();

        ReplaceOrders(unpaidOrders);

        _ = dialog.ShowModal(this);

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
        IReadOnlyDictionary<string, ExistingServiceOrderSummary>? existingOrdersByServiceId = null,
        IReadOnlyList<PcServiceOrderDto>? pendingClientOrders = null)
    {
        var dialog = new Windows.SelectServiceWindow(
            machine,
            items,
            existingOrdersByServiceId,
            pendingClientOrders)
        {
            Owner = this
        };

        if (dialog.ShowModal(this) == true)
        {
            return dialog.Result;
        }

        return null;
    }

    private static ImageSource? BuildServiceImageSource(string? imageDataUrl)
    {
        var normalized = imageDataUrl?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = normalized.IndexOf(',');
            if (commaIndex <= 0)
            {
                return null;
            }

            var metadata = normalized[..commaIndex];
            if (!metadata.Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var bytes = Convert.FromBase64String(normalized[(commaIndex + 1)..]);
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelHeight = 144;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var imageUri))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelHeight = 144;
            bitmap.UriSource = imageUri;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryBuildServiceImageDataUrl(
        string filePath,
        out string dataUrl,
        out string error)
    {
        dataUrl = string.Empty;
        error = string.Empty;

        try
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                error = "Không tìm thấy file ảnh đã chọn.";
                return false;
            }

            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length == 0)
            {
                error = "File ảnh rỗng.";
                return false;
            }

            const int maxInputBytes = 20 * 1024 * 1024;
            if (bytes.Length > maxInputBytes)
            {
                error = "Ảnh gốc quá lớn (tối đa 20MB).";
                return false;
            }

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            var contentType = GuessImageContentType(extension);
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Định dạng ảnh chưa được hỗ trợ.";
                return false;
            }

            if (!TryOptimizeImageForUpload(bytes, out var optimizedBytes))
            {
                error = "Không thể tối ưu ảnh để upload. Vui lòng chọn ảnh khác.";
                return false;
            }

            dataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(optimizedBytes)}";
            return true;
        }
        catch (Exception ex)
        {
            error = $"Không thể đọc ảnh: {ex.Message}";
            return false;
        }
    }

    private static bool TryOptimizeImageForUpload(byte[] sourceBytes, out byte[] optimizedBytes)
    {
        optimizedBytes = Array.Empty<byte>();

        try
        {
            using var sourceStream = new MemoryStream(sourceBytes);
            var sourceImage = new BitmapImage();
            sourceImage.BeginInit();
            sourceImage.CacheOption = BitmapCacheOption.OnLoad;
            sourceImage.StreamSource = sourceStream;
            sourceImage.EndInit();
            sourceImage.Freeze();

            if (sourceImage.PixelWidth <= 0 || sourceImage.PixelHeight <= 0)
            {
                return false;
            }

            const int maxDimension = 1280;
            const int targetBytes = 500 * 1024;
            var qualityLevels = new[] { 90, 82, 74, 66, 58 };

            var baseScale = Math.Min(
                1d,
                maxDimension / (double)Math.Max(sourceImage.PixelWidth, sourceImage.PixelHeight));

            for (var scale = baseScale; scale >= 0.35d; scale *= 0.82d)
            {
                BitmapSource frameSource = sourceImage;
                if (scale < 0.999d)
                {
                    var transformed = new TransformedBitmap(
                        sourceImage,
                        new ScaleTransform(scale, scale));
                    transformed.Freeze();
                    frameSource = transformed;
                }

                foreach (var quality in qualityLevels)
                {
                    var encoded = EncodeJpeg(frameSource, quality);
                    if (encoded.Length <= targetBytes)
                    {
                        optimizedBytes = encoded;
                        return true;
                    }

                    if (optimizedBytes.Length == 0 || encoded.Length < optimizedBytes.Length)
                    {
                        optimizedBytes = encoded;
                    }
                }
            }

            return optimizedBytes.Length > 0;
        }
        catch
        {
            optimizedBytes = Array.Empty<byte>();
            return false;
        }
    }

    private static byte[] EncodeJpeg(BitmapSource imageSource, int quality)
    {
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = quality,
        };
        encoder.Frames.Add(BitmapFrame.Create(imageSource));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
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
    private async void UpdateServiceItemButton_Click(object sender, RoutedEventArgs e) => await UpdateSelectedServiceItemAsync();
    private async void ToggleServiceItemButton_Click(object sender, RoutedEventArgs e) => await ToggleSelectedServiceItemAsync();
    private async void ContextSelectServiceMenuItem_Click(object sender, RoutedEventArgs e) => await OpenServiceOrderDialogForSelectedMachineAsync();
    private async void ContextPayServiceMenuItem_Click(object sender, RoutedEventArgs e) => await PayServiceForSelectedMachineAsync();

    private void FilterServiceItemsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Not used anymore in the new layout, but keeping to avoid missing references
    }

    private void ServiceCategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServiceCategoryListBox.SelectedItem is ServiceCategoryViewModel selected)
        {
            if (selected.Name == _currentSelectedServiceCategory) return;
            _currentSelectedServiceCategory = selected.Name;
            
            foreach (var item in _serviceCategoryRows)
            {
                item.IsSelected = item.Name == _currentSelectedServiceCategory;
            }

            _ = RefreshServiceItemsAsync();
        }
    }

    private void ServiceRowOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ServiceItemRow row)
        {
            ServiceItemsDataGrid.SelectedItem = row;
            var cm = ServiceItemsDataGrid.ContextMenu;
            if (cm != null)
            {
                cm.PlacementTarget = btn;
                cm.IsOpen = true;
            }
        }
    }

    private void ViewServicesStatsDetailButton_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedItem = StatisticsTabItem;
        StatsTabControl.SelectedItem = RevenueStatsTabItem;
    }

    private void ServiceItemsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServiceItemsDataGrid.SelectedItem is not ServiceItemRow selected)
        {
            return;
        }

        _selectedServiceItemId = selected.Id;
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

