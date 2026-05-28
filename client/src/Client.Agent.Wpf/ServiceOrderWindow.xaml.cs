using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Client.Agent.Wpf;

public partial class ServiceOrderWindow : Window
{
    public ObservableCollection<ClientServiceOrderSelectionRow> Rows { get; }
    public string OrderNote => NoteTextBox.Text;

    public ServiceOrderWindow(string pcName, ObservableCollection<ClientServiceOrderSelectionRow> rows, string orderedPreview)
    {
        InitializeComponent();
        Rows = rows;

        TitleTextBlock.Text = $"Máy trạm: {pcName}";
        OrderedPreviewTextBlock.Text = $"Đã gọi: {orderedPreview}";

        ServicesItemsControl.ItemsSource = Rows;

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
        var totalAdded = selectedRows.Where(x => x.Quantity > 0).Sum(x => x.Quantity);
        var totalCanceled = selectedRows.Where(x => x.Quantity < 0).Sum(x => -x.Quantity);
        var netAmount = selectedRows.Sum(x => x.LineTotal);
        
        SummaryTextBlock.Text = $"Đã chọn {selectedItemCount} món | Gọi thêm: {totalAdded} | Hủy: {totalCanceled} | Chênh lệch: {netAmount:N0} VND";
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
        var buttonsPanel = (StackPanel)SummaryTextBlock.Parent;
        if (buttonsPanel.Children.Count >= 3 && buttonsPanel.Children[2] is StackPanel actionPanel)
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
}
