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
        _ = dialog.ShowDialog();

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
        _ = dialog.ShowDialog();

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
        IReadOnlyDictionary<string, ExistingServiceOrderSummary>? existingOrdersByServiceId = null,
        IReadOnlyList<PcServiceOrderDto>? pendingClientOrders = null)
    {
        var dialog = new Window
        {
            Title = $"Chọn dịch vụ - {machine.Name}",
            Width = 1120,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            MinWidth = 1040,
            MinHeight = 620,
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
        pendingClientOrders ??= Array.Empty<PcServiceOrderDto>();
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
                    ClientQuantity = Math.Max(0, x.Value.ClientQuantity),
                    ClientAmount = Math.Max(0, x.Value.ClientAmount),
                    ServerQuantity = Math.Max(0, x.Value.ServerQuantity),
                    ServerAmount = Math.Max(0, x.Value.ServerAmount),
                };
            })
            .OrderByDescending(x => x.Quantity)
            .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var previouslyOrderedLines = previouslyOrderedRows
            .Select(x =>
            {
                var sourceText = $"{x.ClientQuantity:N0} ({x.ClientAmount:N0} VND), Server: {x.ServerQuantity:N0} ({x.ServerAmount:N0} VND)";
                return $"{x.ServiceName}: {x.Quantity:N0} ({x.Amount:N0} VND) | {sourceText}";
            })
            .ToList();
        var previouslyOrderedTotalQuantity = previouslyOrderedRows.Sum(x => x.Quantity);
        var previouslyOrderedTotalAmount = previouslyOrderedRows.Sum(x => x.Amount);
        var previouslyOrderedTotalClientQuantity = previouslyOrderedRows.Sum(x => x.ClientQuantity);
        var previouslyOrderedTotalClientAmount = previouslyOrderedRows.Sum(x => x.ClientAmount);
        var previouslyOrderedTotalServerQuantity = previouslyOrderedRows.Sum(x => x.ServerQuantity);
        var previouslyOrderedTotalServerAmount = previouslyOrderedRows.Sum(x => x.ServerAmount);
        var pendingClientOrderRows = pendingClientOrders
            .Select(x => PendingClientServiceOrderRow.FromOrder(x))
            .ToList();

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
            FontSize = 14,
            RowHeight = 42,
        };
        serviceGrid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
        {
            Setters =
            {
                new Setter(Control.FontSizeProperty, 14d),
                new Setter(Control.FontWeightProperty, FontWeights.SemiBold),
            },
        };

        var serviceNameColumn = new DataGridTemplateColumn
        {
            Header = "Dịch vụ",
            Width = new DataGridLength(2.2, DataGridLengthUnitType.Star),
        };
        var serviceNameCellPanel = new FrameworkElementFactory(typeof(StackPanel));
        serviceNameCellPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        serviceNameCellPanel.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);

        var serviceImageBorder = new FrameworkElementFactory(typeof(Border));
        serviceImageBorder.SetValue(Border.WidthProperty, 34d);
        serviceImageBorder.SetValue(Border.HeightProperty, 34d);
        serviceImageBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        serviceImageBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(203, 213, 225)));
        serviceImageBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        serviceImageBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(248, 250, 252)));
        serviceImageBorder.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 0));

        var serviceImage = new FrameworkElementFactory(typeof(Image));
        serviceImage.SetBinding(Image.SourceProperty, new Binding(nameof(ServiceOrderSelectionRow.ServiceImageSource)));
        serviceImage.SetValue(Image.StretchProperty, Stretch.UniformToFill);
        serviceImage.SetValue(Image.SnapsToDevicePixelsProperty, true);
        serviceImageBorder.AppendChild(serviceImage);

        var serviceNameText = new FrameworkElementFactory(typeof(TextBlock));
        serviceNameText.SetBinding(TextBlock.TextProperty, new Binding(nameof(ServiceOrderSelectionRow.ServiceName)));
        serviceNameText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        serviceNameText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

        serviceNameCellPanel.AppendChild(serviceImageBorder);
        serviceNameCellPanel.AppendChild(serviceNameText);
        serviceNameColumn.CellTemplate = new DataTemplate { VisualTree = serviceNameCellPanel };
        serviceGrid.Columns.Add(serviceNameColumn);
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
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
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
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 4),
        });
        var previouslyOrderedListBox = new ListBox
        {
            Height = 150,
            MinHeight = 120,
            MaxHeight = 220,
            ItemsSource = previouslyOrderedLines,
            FontSize = 14,
        };
        previouslyOrderedPanelStack.Children.Add(previouslyOrderedListBox);
        previouslyOrderedPanelStack.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 14,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Text = previouslyOrderedRows.Count == 0
                ? "Chưa có dịch vụ gọi trước."
                : $"Tổng gọi trước: {previouslyOrderedRows.Count} món | {previouslyOrderedTotalQuantity} SL | {previouslyOrderedTotalAmount:N0} VND | {previouslyOrderedTotalClientQuantity} ({previouslyOrderedTotalClientAmount:N0} VND) | Server: {previouslyOrderedTotalServerQuantity} ({previouslyOrderedTotalServerAmount:N0} VND)",
        });

        previouslyOrderedPanelStack.Children.Add(new TextBlock
        {
            Text = "Dịch vụ chờ xác nhận:",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 10, 0, 4),
        });

        var pendingClientOrderRowsCollection = new ObservableCollection<PendingClientServiceOrderRow>(pendingClientOrderRows);
        var pendingOrdersGrid = new DataGrid
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
            Height = 200,
            MinHeight = 170,
            MaxHeight = 260,
            FontSize = 13,
            RowHeight = 30,
            ItemsSource = pendingClientOrderRowsCollection,
            Margin = new Thickness(0, 0, 0, 0),
        };
        var pendingOrderCheckBoxStyle = new Style(typeof(CheckBox));
        pendingOrderCheckBoxStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        pendingOrderCheckBoxStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        pendingOrderCheckBoxStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        pendingOrderCheckBoxStyle.Setters.Add(new Setter(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5)));
        pendingOrderCheckBoxStyle.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform(1.35, 1.35)));

        pendingOrdersGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Chọn",
            Width = 68,
            Binding = new Binding(nameof(PendingClientServiceOrderRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            ElementStyle = pendingOrderCheckBoxStyle,
            EditingElementStyle = pendingOrderCheckBoxStyle,
        });
        pendingOrdersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Dịch vụ",
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(PendingClientServiceOrderRow.ServiceName)),
            IsReadOnly = true,
        });
        pendingOrdersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "SL",
            Width = 38,
            Binding = new Binding(nameof(PendingClientServiceOrderRow.QuantityText)),
            IsReadOnly = true,
        });
        pendingOrdersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Tiền",
            Width = 72,
            Binding = new Binding(nameof(PendingClientServiceOrderRow.AmountText)),
            IsReadOnly = true,
        });
        pendingOrdersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Lúc",
            Width = 64,
            Binding = new Binding(nameof(PendingClientServiceOrderRow.CreatedAtText)),
            IsReadOnly = true,
        });
        previouslyOrderedPanelStack.Children.Add(pendingOrdersGrid);

        var pendingOrderSummaryTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 13,
            Foreground = Brushes.DarkViolet,
            TextWrapping = TextWrapping.Wrap,
        };
        previouslyOrderedPanelStack.Children.Add(pendingOrderSummaryTextBlock);
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

        var buttonPanel = new Grid
        {
            Margin = new Thickness(0, 6, 0, 0),
        };
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftButtonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var rightButtonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
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
        var confirmPendingOrdersButton = new Button
        {
            Content = "Xác nhận order đã chọn",
            Width = 180,
            Height = 34,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(147, 51, 234)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(126, 34, 206)),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Xác nhận các order chờ từ máy trạm đã chọn.",
            IsEnabled = false,
        };
        var cancelPendingOrdersButton = new Button
        {
            Content = "Hủy order đã chọn",
            Width = 170,
            Height = 34,
            FontWeight = FontWeights.SemiBold,
            Style = (Style)FindResource("DangerButtonStyle"),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Hủy các order chờ đã chọn (ví dụ khi hết hàng).",
            IsEnabled = false,
        };
        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Height = 34,
            FontWeight = FontWeights.SemiBold,
            Style = (Style)FindResource("DangerButtonStyle"),
            IsCancel = true,
        };

        leftButtonPanel.Children.Add(confirmPendingOrdersButton);
        leftButtonPanel.Children.Add(cancelPendingOrdersButton);
        rightButtonPanel.Children.Add(addButton);
        rightButtonPanel.Children.Add(cancelButton);
        Grid.SetColumn(leftButtonPanel, 0);
        Grid.SetColumn(rightButtonPanel, 1);
        buttonPanel.Children.Add(leftButtonPanel);
        buttonPanel.Children.Add(rightButtonPanel);
        Grid.SetRow(buttonPanel, 4);
        root.Children.Add(buttonPanel);

        ServiceOrderBatchInput? result = null;

        void RefreshPendingOrderButtons()
        {
            var hasPendingOrders = pendingClientOrderRowsCollection.Count > 0;
            confirmPendingOrdersButton.IsEnabled = hasPendingOrders;
            cancelPendingOrdersButton.IsEnabled = hasPendingOrders;
        }

        void RefreshPendingOrderSummary()
        {
            var totalPending = pendingClientOrderRowsCollection.Count;
            var selectedPending = pendingClientOrderRowsCollection.Where(x => x.IsSelected).ToList();
            var selectedAmount = selectedPending.Sum(x => x.Amount);

            pendingOrderSummaryTextBlock.Text = totalPending == 0
                ? "Không có order chờ từ máy trạm."
                : $"Đang chờ: {totalPending} | Đã chọn: {selectedPending.Count} | Tiền: {selectedAmount:N0} VND";
            RefreshPendingOrderButtons();
        }

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

        void PendingRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PendingClientServiceOrderRow.IsSelected))
            {
                RefreshPendingOrderSummary();
            }
        }

        foreach (var row in selectionRows)
        {
            row.PropertyChanged += RowPropertyChanged;
        }
        foreach (var row in pendingClientOrderRowsCollection)
        {
            row.PropertyChanged += PendingRowPropertyChanged;
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

        confirmPendingOrdersButton.Click += (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            var selectedPendingOrderIds = pendingClientOrderRowsCollection
                .Where(x => x.IsSelected)
                .Select(x => x.OrderId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedPendingOrderIds.Count == 0)
            {
                errorTextBlock.Text = "Vui lòng chọn ít nhất 1 order chờ để xác nhận.";
                return;
            }

            result = new ServiceOrderBatchInput
            {
                Lines = new List<ServiceOrderLineInput>(),
                Note = null,
                Total = 0,
                AcknowledgedClientOrderIds = selectedPendingOrderIds,
            };

            dialog.DialogResult = true;
            dialog.Close();
        };

        cancelPendingOrdersButton.Click += (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            var selectedPendingOrderIds = pendingClientOrderRowsCollection
                .Where(x => x.IsSelected)
                .Select(x => x.OrderId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedPendingOrderIds.Count == 0)
            {
                errorTextBlock.Text = "Vui lòng chọn ít nhất 1 order chờ để hủy.";
                return;
            }

            result = new ServiceOrderBatchInput
            {
                Lines = new List<ServiceOrderLineInput>(),
                Note = null,
                Total = 0,
                CanceledClientOrderIds = selectedPendingOrderIds,
            };

            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            RefreshSummary();
            RefreshPendingOrderSummary();
            serviceGrid.Focus();
        };

        _ = dialog.ShowDialog();

        foreach (var row in selectionRows)
        {
            row.PropertyChanged -= RowPropertyChanged;
        }
        foreach (var row in pendingClientOrderRowsCollection)
        {
            row.PropertyChanged -= PendingRowPropertyChanged;
        }

        return result;
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
                bitmap.DecodePixelHeight = 72;
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
            bitmap.DecodePixelHeight = 72;
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
        public List<string> AcknowledgedClientOrderIds { get; init; } = new();
        public List<string> CanceledClientOrderIds { get; init; } = new();
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
        public ImageSource? ServiceImageSource { get; init; }
        public bool HasImage => ServiceImageSource is not null;
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
                ServiceImageSource = item.ServiceImageSource,
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
        public int ClientQuantity { get; set; }
        public decimal ClientAmount { get; set; }
        public int ServerQuantity { get; set; }
        public decimal ServerAmount { get; set; }

        public void AddFromOrder(int quantity, decimal amount, bool fromClient)
        {
            var safeQuantity = Math.Max(0, quantity);
            var safeAmount = Math.Max(0, amount);
            Quantity += safeQuantity;
            Amount += safeAmount;

            if (fromClient)
            {
                ClientQuantity += safeQuantity;
                ClientAmount += safeAmount;
                return;
            }

            ServerQuantity += safeQuantity;
            ServerAmount += safeAmount;
        }
    }

    private sealed class PendingClientServiceOrderRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string OrderId { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal Amount { get; init; }
        public string CreatedAtText { get; init; } = "-";
        public string QuantityText => Quantity.ToString("N0");
        public string AmountText => Amount.ToString("N0");

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

        public static PendingClientServiceOrderRow FromOrder(PcServiceOrderDto order)
        {
            return new PendingClientServiceOrderRow
            {
                OrderId = order.Id,
                ServiceName = string.IsNullOrWhiteSpace(order.ServiceItem?.Name) ? "Dịch vụ" : order.ServiceItem.Name,
                Quantity = Math.Max(0, order.Quantity),
                Amount = Math.Max(0, order.LineTotal),
                CreatedAtText = FormatDateTime(order.CreatedAt),
            };
        }
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

