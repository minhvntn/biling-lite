using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class ColorSettingsWindow : Window
{
    private readonly AdminShellSettings _settings;

    public ColorSettingsWindow(AdminShellSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadColorsToUi();
    }

    private void LoadColorsToUi()
    {
        UpdateBlockColor(OnlineBgBlock, _settings.ColorOnlineBg, "#ECFDF5");
        UpdateBlockColor(OnlineFgBlock, _settings.ColorOnlineFg, "#16A34A");

        UpdateBlockColor(AdminBgBlock, _settings.ColorAdminBg, "#FACC15");
        UpdateBlockColor(AdminFgBlock, _settings.ColorAdminFg, "#DC2626");

        UpdateBlockColor(OfflineBgBlock, _settings.ColorOfflineBg, "#DC2626");
        UpdateBlockColor(OfflineFgBlock, _settings.ColorOfflineFg, "#FFFFFF");

        UpdateBlockColor(InUseBgBlock, _settings.ColorInUseBg, "#EFF6FF");
        UpdateBlockColor(InUseFgBlock, _settings.ColorInUseFg, "#2563EB");

        UpdateBlockColor(UsedTimeBgBlock, _settings.ColorUsedTimeBg, "#FEF2F2");
        UpdateBlockColor(UsedTimeFgBlock, _settings.ColorUsedTimeFg, "#DC2626");

        UpdateBlockColor(RemainingTimeBgBlock, _settings.ColorRemainingTimeBg, "#EFF6FF");
        UpdateBlockColor(RemainingTimeFgBlock, _settings.ColorRemainingTimeFg, "#2563EB");

        UpdateBlockColor(MinorBgBlock, _settings.ColorMinorBg, "#FFFFFF");
        UpdateBlockColor(MinorFgBlock, _settings.ColorMinorFg, "#F97316");

        UpdateBlockColor(ServiceCallBgBlock, _settings.ColorServiceCallBg, "#8B5CF6");
        UpdateBlockColor(ServiceDebtBgBlock, _settings.ColorServiceDebtBg, "#0D9488");
        UpdateBlockColor(TransferPayBgBlock, _settings.ColorTransferPayBg, "#F59E0B");
    }

    private void UpdateBlockColor(Border block, string? hex, string fallbackHex)
    {
        var cleanHex = string.IsNullOrWhiteSpace(hex) ? fallbackHex : hex.Trim();
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(cleanHex);
            block.Background = new SolidColorBrush(color);
            block.Tag = cleanHex; // Store Hex string in Tag
        }
        catch
        {
            var color = (Color)ColorConverter.ConvertFromString(fallbackHex);
            block.Background = new SolidColorBrush(color);
            block.Tag = fallbackHex;
        }
    }

    private void ColorBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is not Border block) return;

        var currentHex = block.Tag as string ?? "#FFFFFF";
        var picker = new ColorPickerDialog(currentHex)
        {
            Owner = this
        };

        if (picker.ShowDialog() == true)
        {
            var selectedHex = picker.SelectedColorHex;
            UpdateBlockColor(block, selectedHex, currentHex);
            SaveBlockColorToSettings(block, selectedHex);
        }
    }

    private void SaveBlockColorToSettings(Border block, string hex)
    {
        if (block == OnlineBgBlock) _settings.ColorOnlineBg = hex;
        else if (block == OnlineFgBlock) _settings.ColorOnlineFg = hex;
        else if (block == AdminBgBlock) _settings.ColorAdminBg = hex;
        else if (block == AdminFgBlock) _settings.ColorAdminFg = hex;
        else if (block == OfflineBgBlock) _settings.ColorOfflineBg = hex;
        else if (block == OfflineFgBlock) _settings.ColorOfflineFg = hex;
        else if (block == InUseBgBlock) _settings.ColorInUseBg = hex;
        else if (block == InUseFgBlock) _settings.ColorInUseFg = hex;
        else if (block == UsedTimeBgBlock) _settings.ColorUsedTimeBg = hex;
        else if (block == UsedTimeFgBlock) _settings.ColorUsedTimeFg = hex;
        else if (block == RemainingTimeBgBlock) _settings.ColorRemainingTimeBg = hex;
        else if (block == RemainingTimeFgBlock) _settings.ColorRemainingTimeFg = hex;
        else if (block == MinorBgBlock) _settings.ColorMinorBg = hex;
        else if (block == MinorFgBlock) _settings.ColorMinorFg = hex;
        else if (block == ServiceCallBgBlock) _settings.ColorServiceCallBg = hex;
        else if (block == ServiceDebtBgBlock) _settings.ColorServiceDebtBg = hex;
        else if (block == TransferPayBgBlock) _settings.ColorTransferPayBg = hex;
    }

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ColorOnlineBg = "#ECFDF5";
        _settings.ColorOnlineFg = "#16A34A";
        _settings.ColorAdminBg = "#FACC15";
        _settings.ColorAdminFg = "#DC2626";
        _settings.ColorOfflineBg = "#DC2626";
        _settings.ColorOfflineFg = "#FFFFFF";
        _settings.ColorInUseBg = "#EFF6FF";
        _settings.ColorInUseFg = "#2563EB";
        _settings.ColorUsedTimeBg = "#FEF2F2";
        _settings.ColorUsedTimeFg = "#DC2626";
        _settings.ColorRemainingTimeBg = "#EFF6FF";
        _settings.ColorRemainingTimeFg = "#2563EB";
        _settings.ColorMinorBg = "#FFFFFF";
        _settings.ColorMinorFg = "#F97316";
        _settings.ColorServiceCallBg = "#8B5CF6";
        _settings.ColorServiceDebtBg = "#0D9488";
        _settings.ColorTransferPayBg = "#F59E0B";

        LoadColorsToUi();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
