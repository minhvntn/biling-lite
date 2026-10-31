using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Server.Admin.App.Windows.Controls
{
    public partial class ComboTabControl : UserControl
    {
        public class ComboPackage
        {
            public string id { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public decimal price { get; set; }
            public string startTime { get; set; } = string.Empty;
            public string endTime { get; set; } = string.Empty;
            public int validityDays { get; set; }
            public int durationHours { get; set; }
            public bool isActive { get; set; }

            public string PriceFormatted => $"{price:N0} đ";
            public string TimeRangeText => $"{startTime} - {endTime}";
            public string DurationText => durationHours > 0 ? $"{durationHours} giờ" : "Không giới hạn";
            public string ValidityDaysText => $"{validityDays} ngày";
        }

        public class GeneratedCombo
        {
            public string username { get; set; } = string.Empty;
            public string password { get; set; } = string.Empty;
            public string comboName { get; set; } = string.Empty;
            public DateTime expiresAt { get; set; }
            public decimal price { get; set; }
            public string startTime { get; set; } = string.Empty;
            public string endTime { get; set; } = string.Empty;
            public int durationHours { get; set; }
            public int validityDays { get; set; }

            public string PriceFormatted => $"{price:N0} đ";
            public string ExpiresFormatted => expiresAt.ToString("dd/MM/yyyy HH:mm");
            public string TimeRangeText => !string.IsNullOrEmpty(startTime) && !string.IsNullOrEmpty(endTime) ? $"{startTime} - {endTime}" : "Khung giờ linh hoạt";
        }

        public class ComboCardItem
        {
            public string id { get; set; } = string.Empty;
            public string username { get; set; } = string.Empty;
            public string fullName { get; set; } = string.Empty;
            public string plainPassword { get; set; } = string.Empty;
            public decimal balance { get; set; }
            public int playSeconds { get; set; }
            public bool isLimitedCombo { get; set; }
            public DateTime createdAt { get; set; }
            public DateTime? comboExpiresAt { get; set; }

            public string comboName => !string.IsNullOrWhiteSpace(fullName) ? fullName.Replace("Combo ", "") : "Combo";
            public string displayPassword => !string.IsNullOrWhiteSpace(plainPassword) ? plainPassword : "****";
            public bool isAvailable => comboExpiresAt.HasValue && comboExpiresAt.Value > DateTime.Now;
            public string status => isAvailable ? "Khả dụng" : "Đã hết hạn";
            public string statusBg => isAvailable ? "#ECFDF5" : "#F1F5F9";
            public string statusFg => isAvailable ? "#047857" : "#64748B";
            public string statusBorder => isAvailable ? "#A7F3D0" : "#CBD5E1";
            public string durationText => isLimitedCombo && playSeconds > 0 ? $"{playSeconds / 3600} giờ" : "Theo khung giờ";
        }

        public class ComboCardResponse
        {
            public List<ComboCardItem> items { get; set; } = new();
            public int total { get; set; }
        }

        private List<ComboPackage> _combosList = new();
        private List<ComboCardItem> _allComboCards = new();
        private readonly ObservableCollection<ComboCardItem> _filteredComboCards = new();
        private readonly List<GeneratedCombo> _preGeneratedCombos = new();
        private readonly ObservableCollection<GeneratedCombo> _lastGeneratedBatch = new();

        public ComboTabControl()
        {
            InitializeComponent();
            GridComboCards.ItemsSource = _filteredComboCards;
            ItemsGeneratedCards.ItemsSource = _lastGeneratedBatch;
            this.Loaded += ComboTabControl_Loaded;
        }

        private async void ComboTabControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCombosAsync();
            await LoadComboCardsAsync();
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is not TabControl) return;

            var idx = MainTabControl.SelectedIndex;
            if (TabContent1 != null) TabContent1.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (TabContent2 != null) TabContent2.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (TabContent3 != null) TabContent3.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;

            if (idx == 1)
            {
                _ = LoadComboCardsAsync();
            }
            else if (idx == 2)
            {
                if (_preGeneratedCombos.Count == 0)
                {
                    _ = LoadOrGeneratePreCombosAsync();
                }
            }
        }

        private async Task LoadCombosAsync()
        {
            try
            {
                var mainWindow = (MainWindow)Application.Current.MainWindow;
                var client = mainWindow._httpClient;
                var url = mainWindow.BuildApiUrl("/combos");
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var combos = await response.Content.ReadFromJsonAsync<List<ComboPackage>>();
                    _combosList = combos ?? new List<ComboPackage>();
                    GridCombos.ItemsSource = _combosList;
                    CboComboPackages.ItemsSource = _combosList;
                    
                    if (_combosList.Count > 0 && CboComboPackages.SelectedIndex < 0)
                    {
                        CboComboPackages.SelectedIndex = 0;
                    }
                    UpdatePosPricing();
                }
            }
            catch (Exception ex)
            {
                ConnectionErrorDialog.Show(Window.GetWindow(this), "Lỗi tải danh sách Combo", ex, async () =>
                {
                    await LoadCombosAsync();
                });
            }
        }

        private void CboComboPackages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePosPricing();
        }

        private void TxtQuantity_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePosPricing();
        }

        private void QuickQty_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && TxtQuantity != null)
            {
                TxtQuantity.Text = tag;
            }
        }

        private void UpdatePosPricing()
        {
            if (TxtTotalPrice == null || TxtSummaryDetails == null) return;

            ComboPackage? pkg = CboComboPackages?.SelectedItem as ComboPackage;
            if (pkg == null && _combosList.Count > 0)
            {
                pkg = _combosList[0];
            }

            int qty = 1;
            if (TxtQuantity != null && int.TryParse(TxtQuantity.Text, out int q) && q > 0)
            {
                qty = Math.Min(100, q);
            }

            if (pkg != null)
            {
                var total = pkg.price * qty;
                TxtSummaryDetails.Text = $"{qty} thẻ x {pkg.price:N0} đ";
                TxtTotalPrice.Text = $"{total:N0} đ";
            }
            else
            {
                TxtSummaryDetails.Text = $"{qty} thẻ";
                TxtTotalPrice.Text = "0 đ";
            }
        }

        private async void BtnAddCombo_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = new AddComboWindow();
            addWindow.Owner = Window.GetWindow(this);
            if (addWindow.ShowModal() == true)
            {
                await LoadCombosAsync();
            }
        }

        private async void BtnEditCombo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ComboPackage pkg)
            {
                var editWindow = new AddComboWindow(pkg);
                editWindow.Owner = Window.GetWindow(this);
                if (editWindow.ShowModal() == true)
                {
                    await LoadCombosAsync();
                }
            }
        }

        private async void MenuItemEditCombo_Click(object sender, RoutedEventArgs e)
        {
            if (GridCombos.SelectedItem is ComboPackage selectedCombo)
            {
                var editWindow = new AddComboWindow(selectedCombo);
                editWindow.Owner = Window.GetWindow(this);
                if (editWindow.ShowModal() == true)
                {
                    await LoadCombosAsync();
                }
            }
        }

        private async void BtnDeleteCombo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string comboId)
            {
                if (MessageBox.Show("Bạn có chắc chắn muốn xóa gói Combo này?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        var mainWindow = (MainWindow)Application.Current.MainWindow;
                        var client = mainWindow._httpClient;
                        var url = mainWindow.BuildApiUrl($"/combos/{comboId}");
                        var response = await client.DeleteAsync(url);
                        if (response.IsSuccessStatusCode)
                        {
                            await LoadCombosAsync();
                        }
                        else
                        {
                            MessageBox.Show("Lỗi khi xóa Combo.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi hệ thống: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (CboComboPackages.SelectedValue is string comboId)
            {
                if (!int.TryParse(TxtQuantity.Text, out int qty) || qty < 1 || qty > 100)
                {
                    MessageBox.Show("Vui lòng nhập số lượng thẻ hợp lệ (1-100)", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                BtnGenerate.IsEnabled = false;
                BtnGenerateText.Text = "Đang tạo...";

                try
                {
                    var payload = new { comboId, quantity = qty };
                    var mainWindow = (MainWindow)Application.Current.MainWindow;
                    var client = mainWindow._httpClient;
                    var url = mainWindow.BuildApiUrl("/combos/generate");
                    
                    var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(url, jsonContent);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var generated = await response.Content.ReadFromJsonAsync<List<GeneratedCombo>>();
                        if (generated != null && generated.Count > 0)
                        {
                            _lastGeneratedBatch.Clear();
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine("=== THẺ COMBO ĐÃ BÁN ===");
                            sb.AppendLine($"Gói: {generated[0].comboName}");
                            sb.AppendLine($"Hạn dùng đến: {generated[0].expiresAt:dd/MM/yyyy HH:mm}");
                            sb.AppendLine("--------------------------------");

                            foreach (var acc in generated)
                            {
                                _lastGeneratedBatch.Add(acc);
                                sb.AppendLine($"Tài khoản: {acc.username}");
                                sb.AppendLine($"Mật khẩu:  {acc.password}");
                                sb.AppendLine("--------------------------------");
                            }

                            TxtGeneratedResults.Text = sb.ToString();
                            TxtNoResultsPlaceholder.Visibility = Visibility.Collapsed;
                            ItemsGeneratedCards.Visibility = Visibility.Visible;
                            BtnCopyAllResults.Visibility = Visibility.Visible;

                            MessageBox.Show($"Đã tạo & bán thành công {generated.Count} thẻ combo. Đã ghi nhận doanh thu.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                            await LoadComboCardsAsync();
                        }
                        else
                        {
                            MessageBox.Show("Không có thẻ nào được tạo. Vui lòng kiểm tra lại cấu hình gói.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Lỗi tạo thẻ: " + await response.Content.ReadAsStringAsync(), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi hệ thống: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnGenerate.IsEnabled = true;
                    BtnGenerateText.Text = "Tạo Thẻ (Bán Mới)";
                }
            }
            else
            {
                MessageBox.Show("Vui lòng chọn một gói Combo", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCopySingleCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is GeneratedCombo card)
            {
                try
                {
                    Clipboard.SetText($"Tài khoản: {card.username}\nMật khẩu: {card.password}\nGói: {card.comboName}\nHạn dùng: {card.ExpiresFormatted}");
                    MessageBox.Show($"Đã sao chép tài khoản {card.username} vào bộ nhớ tạm!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch { }
            }
        }

        private void BtnCopyAllResults_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtGeneratedResults.Text))
            {
                try
                {
                    Clipboard.SetText(TxtGeneratedResults.Text);
                    if (BtnCopyAllText != null)
                    {
                        BtnCopyAllText.Text = "✓ Đã sao chép!";
                    }
                }
                catch { }
            }
        }

        private async void BtnRefreshCards_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshCards.IsEnabled = false;
            await LoadComboCardsAsync();
            BtnRefreshCards.IsEnabled = true;
        }

        private async Task LoadComboCardsAsync()
        {
            try
            {
                var mainWindow = (MainWindow)Application.Current.MainWindow;
                var client = mainWindow._httpClient;
                var url = mainWindow.BuildApiUrl("/members?memberType=COMBO");
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ComboCardResponse>();
                    if (result != null)
                    {
                        _allComboCards = result.items ?? new List<ComboCardItem>();
                        ApplyComboCardsFilter();
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void TxtSearchCards_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyComboCardsFilter();
        }

        private void CboCardsStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyComboCardsFilter();
        }

        private void ApplyComboCardsFilter()
        {
            _filteredComboCards.Clear();
            var search = TxtSearchCards?.Text?.Trim() ?? string.Empty;
            var filterIdx = CboCardsStatusFilter?.SelectedIndex ?? 0;

            foreach (var card in _allComboCards)
            {
                if (!string.IsNullOrEmpty(search))
                {
                    if (!card.username.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                        !card.comboName.Contains(search, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (filterIdx == 1 && !card.isAvailable) continue; // Chỉ khả dụng
                if (filterIdx == 2 && card.isAvailable) continue;  // Chỉ hết hạn

                _filteredComboCards.Add(card);
            }
        }

        private async void BtnDeleteComboCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string memberId)
            {
                if (MessageBox.Show("Bạn có chắc chắn muốn xóa tài khoản Combo này?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        var mainWindow = (MainWindow)Application.Current.MainWindow;
                        var client = mainWindow._httpClient;
                        var url = mainWindow.BuildApiUrl($"/members/{memberId}");
                        var response = await client.DeleteAsync(url);
                        if (response.IsSuccessStatusCode)
                        {
                            await LoadComboCardsAsync();
                        }
                        else
                        {
                            MessageBox.Show("Lỗi khi xóa tài khoản.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi hệ thống: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void BtnGeneratePreCombos_Click(object sender, RoutedEventArgs e)
        {
            await LoadOrGeneratePreCombosAsync();
        }

        private async void BtnDeleteAllPreCombos_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ các Combo đã tạo để tạo mới không?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                BtnDeletePreCombos.IsEnabled = false;
                try
                {
                    var mainWindow = (MainWindow)Application.Current.MainWindow;
                    var client = mainWindow._httpClient;
                    var url = mainWindow.BuildApiUrl("/members?memberType=COMBO");
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<ComboCardResponse>();
                        if (result?.items != null)
                        {
                            foreach (var item in result.items)
                            {
                                await client.DeleteAsync(mainWindow.BuildApiUrl($"/members/{item.id}"));
                            }
                        }
                    }
                    
                    await LoadOrGeneratePreCombosAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi xóa: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnDeletePreCombos.IsEnabled = true;
                }
            }
        }

        private async Task LoadOrGeneratePreCombosAsync()
        {
            BtnGeneratePreCombos.IsEnabled = false;
            _preGeneratedCombos.Clear();
            GridPreGeneratedCombos.ItemsSource = null;

            try
            {
                var mainWindow = (MainWindow)Application.Current.MainWindow;
                var client = mainWindow._httpClient;
                
                var url = mainWindow.BuildApiUrl("/members?memberType=COMBO");
                var response = await client.GetAsync(url);
                bool needsGeneration = true;
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ComboCardResponse>();
                    if (result?.items != null)
                    {
                        var validCombos = result.items.Where(c => c.comboExpiresAt != null && c.comboExpiresAt > DateTime.Now).ToList();
                        if (validCombos.Count > 0)
                        {
                            needsGeneration = false;
                            foreach (var vc in validCombos)
                            {
                                _preGeneratedCombos.Add(new GeneratedCombo
                                {
                                    comboName = vc.comboName,
                                    username = vc.username,
                                    password = string.IsNullOrWhiteSpace(vc.plainPassword) ? "***" : vc.plainPassword,
                                    expiresAt = vc.comboExpiresAt ?? DateTime.Now,
                                    price = 0,
                                    startTime = "N/A"
                                });
                            }
                        }
                    }
                }

                if (needsGeneration)
                {
                    var pkgsUrl = mainWindow.BuildApiUrl("/combos");
                    var pkgsResp = await client.GetAsync(pkgsUrl);
                    if (pkgsResp.IsSuccessStatusCode)
                    {
                        var packages = await pkgsResp.Content.ReadFromJsonAsync<List<ComboPackage>>();
                        if (packages != null)
                        {
                            foreach (var pkg in packages)
                            {
                                var payload = new { comboId = pkg.id, quantity = 5 };
                                var genUrl = mainWindow.BuildApiUrl("/combos/generate");
                                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                                var genResp = await client.PostAsync(genUrl, jsonContent);
                                if (genResp.IsSuccessStatusCode)
                                {
                                    var generated = await genResp.Content.ReadFromJsonAsync<List<GeneratedCombo>>();
                                    if (generated != null)
                                    {
                                        _preGeneratedCombos.AddRange(generated);
                                    }
                                }
                            }
                        }
                    }
                }
                
                GridPreGeneratedCombos.ItemsSource = null;
                GridPreGeneratedCombos.ItemsSource = _preGeneratedCombos;
                await LoadComboCardsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi tải kho combo: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnGeneratePreCombos.IsEnabled = true;
            }
        }

        private void GridPreGeneratedCombos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (GridPreGeneratedCombos.SelectedItem is GeneratedCombo selected)
            {
                var popup = new ComboCardInfoPopup(selected.comboName, selected.username, selected.password, selected.expiresAt);
                popup.Owner = Window.GetWindow(this);
                popup.ShowModal();
            }
        }
    }
}
