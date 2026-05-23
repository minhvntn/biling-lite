using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class ColorPickerDialog : Window
{
    private static readonly string[] SwatchColors = new[]
    {
        "#FFFFFF", "#F8FAFC", "#F1F5F9", "#E2E8F0", "#CBD5E1", "#94A3B8", "#64748B", "#475569", "#334155", "#1E293B", "#0F172A", "#000000",
        "#FEF2F2", "#FEE2E2", "#FECACA", "#FCA5A5", "#F87171", "#EF4444", "#DC2626", "#B91C1C", "#991B1B", "#7F1D1D",
        "#FFF7ED", "#FFEDD5", "#FED7AA", "#FDBA74", "#FB923C", "#F97316", "#EA580C", "#C2410C", "#9A3412", "#7C2D12",
        "#FEF9C3", "#FEF08A", "#FDE047", "#FACC15", "#EAB308", "#CA8A04", "#A16207", "#854D0E", "#713F12",
        "#ECFDF5", "#D1FAE5", "#A7F3D0", "#6EE7B7", "#34D399", "#10B981", "#059669", "#047857", "#065F46", "#064E3B",
        "#EFF6FF", "#DBEAFE", "#BFDBFE", "#93C5FD", "#60A5FA", "#3B82F6", "#2563EB", "#1D4ED8", "#1E40AF", "#1E3A8A",
        "#F5F3FF", "#DDD6FE", "#C7D2FE", "#A5B4FC", "#818CF8", "#6366F1", "#4F46E5", "#4338CA", "#3730A3", "#312E81",
        "#FDF2F8", "#FCE7F3", "#FBCFE8", "#F472B6", "#EC4899", "#D946EF", "#C084FC", "#A855F7", "#8B5CF6", "#7C3AED"
    };

    public string SelectedColorHex { get; private set; } = "#FFFFFF";

    public ColorPickerDialog(string initialColorHex)
    {
        InitializeComponent();
        SelectedColorHex = string.IsNullOrWhiteSpace(initialColorHex) ? "#FFFFFF" : initialColorHex;
        HexTextBox.Text = SelectedColorHex;
        PopulateSwatches();
    }

    private void PopulateSwatches()
    {
        foreach (var hex in SwatchColors)
        {
            var border = new Border
            {
                Style = (Style)Resources["ColorSwatchStyle"],
                ToolTip = hex
            };

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                border.Background = new SolidColorBrush(color);
            }
            catch
            {
                border.Background = Brushes.Transparent;
            }

            border.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    HexTextBox.Text = hex;
                }
            };

            SwatchesPanel.Children.Add(border);
        }
    }

    private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = HexTextBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(text) && !text.StartsWith("#"))
        {
            text = "#" + text;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(text);
            PreviewBorder.Background = new SolidColorBrush(color);
            SelectedColorHex = text;
        }
        catch
        {
            // If invalid hex, don't update preview, but keep textbox typing
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var text = HexTextBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(text) && !text.StartsWith("#"))
        {
            text = "#" + text;
        }

        try
        {
            ColorConverter.ConvertFromString(text);
            SelectedColorHex = text;
            DialogResult = true;
            Close();
        }
        catch
        {
            MessageBox.Show("Mã màu không hợp lệ! Vui lòng nhập mã Hex hợp lệ (ví dụ: #FF0000).", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
