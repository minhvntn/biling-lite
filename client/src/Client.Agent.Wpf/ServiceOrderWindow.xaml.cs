using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Client.Agent.Wpf;

public partial class ServiceOrderWindow : Window
{
    public ObservableCollection<ClientServiceOrderSelectionRow> Rows { get; }
    public ObservableCollection<ClientOrderedServiceRow> OrderedRows { get; }
    public string OrderNote => NoteTextBox.Text;
    private ICollectionView _servicesView;

    public ServiceOrderWindow(string pcName, ObservableCollection<ClientServiceOrderSelectionRow> rows, string orderedPreview, ObservableCollection<ClientOrderedServiceRow>? orderedRows = null)
    {
        InitializeComponent();
        Rows = rows;
        OrderedRows = orderedRows ?? new ObservableCollection<ClientOrderedServiceRow>();

        TitleTextBlock.Text = $"Máy trạm: {pcName}";
        OrderedPreviewTextBlock.Text = $"Đã gọi: {orderedPreview}";

        _servicesView = CollectionViewSource.GetDefaultView(Rows);
        _servicesView.Filter = FilterServiceItem;
        ServicesItemsControl.ItemsSource = _servicesView;
        
        CartItemsControl.ItemsSource = Rows;
        OrderedItemsControl.ItemsSource = OrderedRows;
        RefreshOrderedSummary();

        var defaultCategories = new System.Collections.Generic.List<string> { "Tất cả", "Nước", "Đồ ăn", "Ăn vặt", "Thuốc" };
        var otherCategories = Rows.Select(x => string.IsNullOrWhiteSpace(x.Category) ? "Khác" : x.Category)
                             .Where(x => !defaultCategories.Contains(x))
                             .Distinct()
                             .OrderBy(x => x)
                             .ToList();
                             
        var categories = defaultCategories.Concat(otherCategories).ToList();
        CategoryListBox.ItemsSource = categories;
        CategoryListBox.SelectedIndex = 0;

        foreach (var row in Rows)
        {
            row.PropertyChanged += RowPropertyChanged;
        }

        RefreshSummary();
    }

    private void RowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClientServiceOrderSelectionRow.Quantity))
        {
            RefreshSummary();
        }
    }

    private void RefreshSummary()
    {
        var selectedRows = Rows.Where(x => x.Quantity != 0).ToList();
        var selectedItemCount = selectedRows.Count;
        
        var totalAddedAmount = selectedRows.Where(x => x.Quantity > 0).Sum(x => x.LineTotal);
        var totalCanceledAmount = Math.Abs(selectedRows.Where(x => x.Quantity < 0).Sum(x => x.LineTotal));
        var netAmount = selectedRows.Sum(x => x.LineTotal);
        
        if (SummaryItemCountTextBlock != null)
            SummaryItemCountTextBlock.Text = $"Đã chọn {selectedItemCount} món";
            
        if (SummaryNetTextBlock != null)
            SummaryNetTextBlock.Text = $"Tạm tính: {totalAddedAmount:N0} đ";
            
        if (SummaryAddedTextBlock != null)
            SummaryAddedTextBlock.Text = $"{totalAddedAmount:N0} đ";
            
        if (SummaryCanceledTextBlock != null)
            SummaryCanceledTextBlock.Text = $"{totalCanceledAmount:N0} đ";

        if (SummaryDiffTextBlock != null)
            SummaryDiffTextBlock.Text = $"{netAmount:N0} đ";
            
        if (SummaryTotalTextBlock != null)
            SummaryTotalTextBlock.Text = $"{netAmount:N0} đ";

        if (EmptyCartPanel != null && CartItemsScrollViewer != null)
        {
            if (selectedItemCount == 0)
            {
                EmptyCartPanel.Visibility = Visibility.Visible;
                CartItemsScrollViewer.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyCartPanel.Visibility = Visibility.Collapsed;
                CartItemsScrollViewer.Visibility = Visibility.Visible;
            }
        }
    }

    private bool FilterServiceItem(object item)
    {
        if (item is not ClientServiceOrderSelectionRow row)
            return false;

        var searchText = SearchTextBox.Text.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(searchText) && !row.ServiceName.ToLowerInvariant().Contains(searchText))
            return false;

        var selectedCategory = CategoryListBox.SelectedItem as string;
        if (selectedCategory != null && selectedCategory != "Tất cả")
        {
            var rowCategory = string.IsNullOrWhiteSpace(row.Category) ? "Khác" : row.Category;
            if (rowCategory != selectedCategory)
                return false;
        }

        return true;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _servicesView?.Refresh();
    }

    private void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _servicesView?.Refresh();
    }

    private void DecreaseQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is ClientServiceOrderSelectionRow row)
        {
            row.DecreaseQuantity();
        }
    }

    private void IncreaseQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is ClientServiceOrderSelectionRow row)
        {
            row.IncreaseQuantity();
        }
    }

    private void RemoveFromCart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is ClientServiceOrderSelectionRow row)
        {
            // Set quantity to 0 to remove from cart
            while (row.Quantity > 0) row.DecreaseQuantity();
            while (row.Quantity < 0) row.IncreaseQuantity();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public event EventHandler? OrderRequested;

    private void OrderButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorTextBlock.Visibility = Visibility.Collapsed;

        var selectedRows = Rows.Where(x => x.Quantity != 0).ToList();
        if (selectedRows.Count == 0)
        {
            ShowError("Hãy chọn số lượng để gọi (+) hoặc hủy (-).");
            return;
        }

        OrderRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    public void SetProcessing(bool isProcessing)
    {
        // Disable or enable buttons during processing
        if (ErrorTextBlock.Parent is StackPanel actionPanel)
        {
            foreach (var child in actionPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.IsEnabled = !isProcessing;
                }
            }
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void RefreshOrderedSummary()
    {
        var count = OrderedRows.Sum(x => x.Quantity);
        var total = OrderedRows.Sum(x => x.LineTotal);
        OrderedTabButtonText.Text = count > 0 ? $"Đã gọi ({count})" : "Đã gọi";

        if (OrderedRows.Count == 0)
        {
            EmptyOrderedPanel.Visibility = Visibility.Visible;
            OrderedItemsScrollViewer.Visibility = Visibility.Collapsed;
            OrderedTotalTextBlock.Text = "0 đ";
        }
        else
        {
            EmptyOrderedPanel.Visibility = Visibility.Collapsed;
            OrderedItemsScrollViewer.Visibility = Visibility.Visible;
            OrderedTotalTextBlock.Text = $"{total:N0} đ";
        }
    }

    private void CartTabButton_Click(object sender, RoutedEventArgs e)
    {
        CartTabBorder.Background = System.Windows.Media.Brushes.White;
        CartTabBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240));
        CartTabTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 92, 246));

        OrderedTabBorder.Background = System.Windows.Media.Brushes.Transparent;
        OrderedTabBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
        OrderedTabButtonText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));

        CartPanelGrid.Visibility = Visibility.Visible;
        OrderedPanelGrid.Visibility = Visibility.Collapsed;
    }

    private void OrderedTabButton_Click(object sender, RoutedEventArgs e)
    {
        OrderedTabBorder.Background = System.Windows.Media.Brushes.White;
        OrderedTabBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240));
        OrderedTabButtonText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 92, 246));

        CartTabBorder.Background = System.Windows.Media.Brushes.Transparent;
        CartTabBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
        CartTabTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));

        CartPanelGrid.Visibility = Visibility.Collapsed;
        OrderedPanelGrid.Visibility = Visibility.Visible;
    }
}
