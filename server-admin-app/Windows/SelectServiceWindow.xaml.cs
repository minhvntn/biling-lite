using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Server.Admin.App.Windows
{
    public partial class SelectServiceWindow : Window
    {
        private readonly MachineRow _machine;
        private readonly ObservableCollection<ServiceOrderSelectionRow> _selectionRows;
        private readonly ObservableCollection<PendingClientServiceOrderRow> _pendingRows;
        private readonly ObservableCollection<CategoryFilterChip> _categoryChips = new();
        private readonly ICollectionView _servicesView;
        private ScrollViewer? _servicesScrollViewer;
        private DispatcherTimer? _searchDebounceTimer;
        private string _selectedCategory = "Tất cả";
        private string _searchTerm = string.Empty;

        public ServiceOrderBatchInput? Result { get; private set; }

        public SelectServiceWindow(
            MachineRow machine,
            IReadOnlyList<ServiceItemRow> items,
            IReadOnlyDictionary<string, ExistingServiceOrderSummary>? existingOrdersByServiceId = null,
            IReadOnlyList<PcServiceOrderDto>? pendingClientOrders = null)
        {
            InitializeComponent();

            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            existingOrdersByServiceId ??= new Dictionary<string, ExistingServiceOrderSummary>(StringComparer.OrdinalIgnoreCase);
            pendingClientOrders ??= Array.Empty<PcServiceOrderDto>();

            // 1. Header info
            MachineNameText.Text = string.IsNullOrWhiteSpace(_machine.Name) ? _machine.AgentId : _machine.Name;

            // 2. Previously Ordered Items
            var serviceItemById = items.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var previouslyOrderedItems = existingOrdersByServiceId
                .Where(x => x.Value.Quantity > 0 || x.Value.Amount > 0)
                .Select(x =>
                {
                    serviceItemById.TryGetValue(x.Key, out var serviceItem);
                    return new PreviouslyOrderedDisplayItem
                    {
                        ServiceItemId = x.Key,
                        ServiceName = serviceItem?.Name ?? "Dịch vụ",
                        Category = serviceItem?.Category ?? "Dịch vụ",
                        ServiceImageSource = serviceItem?.ServiceImageSource,
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

            PreviousOrdersItemsControl.ItemsSource = previouslyOrderedItems;
            var prevTotalQty = previouslyOrderedItems.Sum(x => x.Quantity);
            var prevTotalAmount = previouslyOrderedItems.Sum(x => x.Amount);
            PreviousOrdersCountText.Text = $"{previouslyOrderedItems.Count} món ({prevTotalQty} SL)";
            PreviousOrdersTotalAmountText.Text = $"{prevTotalAmount:N0} đ";
            PreviousOrdersEmptyState.Visibility = previouslyOrderedItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 3. Pending Client Orders
            var pendingOrderRows = pendingClientOrders
                .Select(PendingClientServiceOrderRow.FromOrder)
                .ToList();
            _pendingRows = new ObservableCollection<PendingClientServiceOrderRow>(pendingOrderRows);
            PendingOrdersDataGrid.ItemsSource = _pendingRows;
            foreach (var p in _pendingRows)
            {
                p.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(PendingClientServiceOrderRow.IsSelected))
                    {
                        RefreshPendingOrderSummary();
                    }
                };
            }
            RefreshPendingOrderSummary();

            // 4. Main Service Catalog & Filter
            var rows = items
                .Select(item =>
                {
                    existingOrdersByServiceId.TryGetValue(item.Id, out var existingSummary);
                    return ServiceOrderSelectionRow.FromServiceItem(item, existingSummary);
                })
                .OrderByDescending(x => x.PreviouslyOrderedQuantity)
                .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _selectionRows = new ObservableCollection<ServiceOrderSelectionRow>(rows);
            foreach (var r in _selectionRows)
            {
                r.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ServiceOrderSelectionRow.Quantity))
                    {
                        RefreshSummary();
                        ErrorTextBlock.Text = string.Empty;
                    }
                };
            }

            ServicesDataGrid.ItemsSource = _selectionRows;
            _servicesView = CollectionViewSource.GetDefaultView(_selectionRows);
            _servicesView.Filter = FilterServiceItem;

            // 5. Category filter chips
            InitializeCategoryChips(items);

            // 6. Initial Refresh
            RefreshSummary();
        }

        private void InitializeCategoryChips(IReadOnlyList<ServiceItemRow> items)
        {
            var categories = items
                .Select(x => string.IsNullOrWhiteSpace(x.Category) ? "Khác" : x.Category.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            _categoryChips.Clear();
            _categoryChips.Add(new CategoryFilterChip
            {
                Name = "Tất cả",
                Count = items.Count,
                IsSelected = true,
            });

            foreach (var cat in categories)
            {
                var catCount = items.Count(x => string.Equals(string.IsNullOrWhiteSpace(x.Category) ? "Khác" : x.Category.Trim(), cat, StringComparison.OrdinalIgnoreCase));
                _categoryChips.Add(new CategoryFilterChip
                {
                    Name = cat,
                    Count = catCount,
                    IsSelected = false,
                });
            }

            CategoryChipsItemsControl.ItemsSource = _categoryChips;
        }

        private bool FilterServiceItem(object item)
        {
            if (item is not ServiceOrderSelectionRow row)
            {
                return false;
            }

            // Category filter
            if (!string.Equals(_selectedCategory, "Tất cả", StringComparison.OrdinalIgnoreCase))
            {
                var rowCat = string.IsNullOrWhiteSpace(row.Category) ? "Khác" : row.Category.Trim();
                if (!string.Equals(rowCat, _selectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Search text filter
            if (!string.IsNullOrWhiteSpace(_searchTerm))
            {
                var term = _searchTerm.Trim();
                var nameMatch = row.ServiceName.Contains(term, StringComparison.OrdinalIgnoreCase);
                var catMatch = row.Category.Contains(term, StringComparison.OrdinalIgnoreCase);
                if (!nameMatch && !catMatch)
                {
                    return false;
                }
            }

            return true;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTerm = SearchTextBox.Text?.Trim() ?? string.Empty;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchTerm) ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchButton.Visibility = string.IsNullOrEmpty(_searchTerm) ? Visibility.Collapsed : Visibility.Visible;

            if (string.IsNullOrEmpty(_searchTerm))
            {
                _searchDebounceTimer?.Stop();
                _servicesView.Refresh();
                UpdateCatalogEmptyState();
                return;
            }

            if (_searchDebounceTimer == null)
            {
                _searchDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(80)
                };
                _searchDebounceTimer.Tick += (s, args) =>
                {
                    _searchDebounceTimer.Stop();
                    _servicesView.Refresh();
                    UpdateCatalogEmptyState();
                };
            }
            else
            {
                _searchDebounceTimer.Stop();
            }

            _searchDebounceTimer.Start();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            SearchTextBox.Focus();
        }

        private void CategoryChip_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CategoryFilterChip chip)
            {
                foreach (var c in _categoryChips)
                {
                    c.IsSelected = c == chip;
                }
                _selectedCategory = chip.Name;
                _servicesView.Refresh();
                UpdateCatalogEmptyState();
            }
        }

        private void UpdateCatalogEmptyState()
        {
            CatalogEmptyState.Visibility = _servicesView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ServicesDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _servicesScrollViewer ??= FindVisualChild<ScrollViewer>(ServicesDataGrid);
            if (_servicesScrollViewer != null)
            {
                // Smooth and responsive scroll: 75 pixels per mouse wheel step
                double scrollDelta = (e.Delta / 120.0) * 75.0;
                double newOffset = _servicesScrollViewer.VerticalOffset - scrollDelta;
                if (newOffset < 0) newOffset = 0;
                if (newOffset > _servicesScrollViewer.ScrollableHeight) newOffset = _servicesScrollViewer.ScrollableHeight;
                _servicesScrollViewer.ScrollToVerticalOffset(newOffset);
                e.Handled = true;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                var found = FindVisualChild<T>(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        private void IncreaseQuantityButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ServiceOrderSelectionRow row)
            {
                row.IncreaseQuantity();
            }
        }

        private void DecreaseQuantityButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ServiceOrderSelectionRow row)
            {
                row.DecreaseQuantity();
            }
        }

        private void RefreshSummary()
        {
            var selectedRows = _selectionRows.Where(x => x.Quantity != 0).ToList();
            var addedRows = selectedRows.Where(x => x.Quantity > 0).ToList();
            var canceledRows = selectedRows.Where(x => x.Quantity < 0).ToList();

            var addedQty = addedRows.Sum(x => x.Quantity);
            var addedAmount = addedRows.Sum(x => x.LineTotal);
            var canceledQty = canceledRows.Sum(x => Math.Abs(x.Quantity));
            var canceledAmount = canceledRows.Sum(x => Math.Abs(x.LineTotal));
            var netAmount = addedAmount - canceledAmount;

            AddSummaryText.Text = $"{addedRows.Count} món • {addedQty} SL (+{addedAmount:N0} đ)";
            CancelSummaryText.Text = $"{canceledRows.Count} món • {canceledQty} SL (-{canceledAmount:N0} đ)";

            if (netAmount > 0)
            {
                NetAmountText.Text = $"+{netAmount:N0} đ";
                NetAmountText.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
            else if (netAmount < 0)
            {
                NetAmountText.Text = $"{netAmount:N0} đ";
                NetAmountText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            }
            else
            {
                NetAmountText.Text = "0 đ";
                NetAmountText.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            }
        }

        private void RefreshPendingOrderSummary()
        {
            var totalPending = _pendingRows.Count;
            var selectedPending = _pendingRows.Where(x => x.IsSelected).ToList();
            var selectedAmount = selectedPending.Sum(x => x.Amount);

            PendingOrdersBadgeText.Text = $"{totalPending} chờ duyệt";
            PendingOrdersEmptyState.Visibility = totalPending == 0 ? Visibility.Visible : Visibility.Collapsed;
            PendingOrdersDataGrid.Visibility = totalPending > 0 ? Visibility.Visible : Visibility.Collapsed;

            PendingOrderSummaryTextBlock.Text = totalPending == 0
                ? "Không có yêu cầu gọi món nào đang chờ xác nhận."
                : $"Tổng: {totalPending} order | Đã chọn: {selectedPending.Count} | Tổng tiền: {selectedAmount:N0} đ";

            ConfirmPendingButton.IsEnabled = selectedPending.Count > 0;
            CancelPendingButton.IsEnabled = selectedPending.Count > 0;

            if (SelectAllPendingCheckBox != null)
            {
                SelectAllPendingCheckBox.IsChecked = totalPending > 0 && selectedPending.Count == totalPending;
            }
        }

        private void SelectAllPendingCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var checkAll = SelectAllPendingCheckBox.IsChecked == true;
            foreach (var p in _pendingRows)
            {
                p.IsSelected = checkAll;
            }
            RefreshPendingOrderSummary();
        }

        private void ConfirmPendingButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Text = string.Empty;
            var selectedIds = _pendingRows
                .Where(x => x.IsSelected)
                .Select(x => x.OrderId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedIds.Count == 0)
            {
                ErrorTextBlock.Text = "Vui lòng chọn ít nhất 1 order để xác nhận.";
                return;
            }

            Result = new ServiceOrderBatchInput
            {
                Lines = new List<ServiceOrderLineInput>(),
                Note = null,
                Total = 0,
                AcknowledgedClientOrderIds = selectedIds,
            };

            DialogResult = true;
            Close();
        }

        private void CancelPendingButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Text = string.Empty;
            var selectedIds = _pendingRows
                .Where(x => x.IsSelected)
                .Select(x => x.OrderId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedIds.Count == 0)
            {
                ErrorTextBlock.Text = "Vui lòng chọn ít nhất 1 order để hủy.";
                return;
            }

            Result = new ServiceOrderBatchInput
            {
                Lines = new List<ServiceOrderLineInput>(),
                Note = null,
                Total = 0,
                CanceledClientOrderIds = selectedIds,
            };

            DialogResult = true;
            Close();
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Text = string.Empty;

            var changedRows = _selectionRows
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

            if (changedRows.Count == 0)
            {
                ErrorTextBlock.Text = "Vui lòng bấm + hoặc − để thêm/hủy ít nhất 1 dịch vụ.";
                return;
            }

            Result = new ServiceOrderBatchInput
            {
                Lines = changedRows,
                Note = null,
                Total = changedRows.Sum(x => x.LineTotal),
            };

            DialogResult = true;
            Close();
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    ToggleMaximize();
                }
                else
                {
                    DragMove();
                }
            }
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeButtonIcon.Text = "🗖";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeButtonIcon.Text = "🗗";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            DialogResult = false;
            Close();
        }
    }
}

