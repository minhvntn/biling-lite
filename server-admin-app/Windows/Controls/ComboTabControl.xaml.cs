using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

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
        }

        public class ComboCardItem
        {
            public string id { get; set; } = string.Empty;
            public string username { get; set; } = string.Empty;
            public string plainPassword { get; set; } = string.Empty;
            public DateTime createdAt { get; set; }
            public DateTime? comboExpiresAt { get; set; }
            public string status => (comboExpiresAt.HasValue && comboExpiresAt.Value > DateTime.Now) ? "Khả dụng" : "Đã hết hạn";
        }

        public class ComboCardResponse
        {
            public List<ComboCardItem> items { get; set; } = new();
            public int total { get; set; }
        }

        private List<GeneratedCombo> _preGeneratedCombos = new();

        public ComboTabControl()
        {
            InitializeComponent();
            this.Loaded += ComboTabControl_Loaded;
        }

        private async void ComboTabControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCombosAsync();
            await LoadComboCardsAsync();
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
                    GridCombos.ItemsSource = combos;
                    CboComboPackages.ItemsSource = combos;
                    
                    if (combos != null && combos.Count > 0)
                    {
                        CboComboPackages.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi tải danh sách Combo: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAddCombo_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = new AddComboWindow();
            addWindow.Owner = Window.GetWindow(this);
            if (addWindow.ShowDialog() == true)
            {
                await LoadCombosAsync();
            }
        }

        private async void BtnDeleteCombo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string comboId)
            {
                if (MessageBox.Show("Bạn có chắc chắn muốn xóa gói Combo này?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
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

        private async void MenuItemEditCombo_Click(object sender, RoutedEventArgs e)
        {
            if (GridCombos.SelectedItem is ComboPackage selectedCombo)
            {
                var editWindow = new AddComboWindow(selectedCombo);
                editWindow.Owner = Window.GetWindow(this);
                if (editWindow.ShowDialog() == true)
                {
                    await LoadCombosAsync();
                }
            }
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (CboComboPackages.SelectedValue is string comboId)
            {
                if (!int.TryParse(TxtQuantity.Text, out int qty) || qty < 1 || qty > 100)
                {
                    MessageBox.Show("Vui lòng nhập số lượng hợp lệ (1-100)", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                BtnGenerate.IsEnabled = false;
                BtnGenerate.Content = "Đang tạo...";

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
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine("=== THẺ COMBO ĐƯỢC TẠO ===");
                            sb.AppendLine($"Gói: {generated[0].comboName}");
                            sb.AppendLine($"Hạn dùng đến: {generated[0].expiresAt.ToString("dd/MM/yyyy HH:mm")}");
                            sb.AppendLine("--------------------------------");
                            
                            foreach (var acc in generated)
                            {
                                sb.AppendLine($"Tài khoản: {acc.username}");
                                sb.AppendLine($"Mật khẩu:  {acc.password}");
                                sb.AppendLine("--------------------------------");
                            }

                            TxtGeneratedResults.Text = sb.ToString();
                            MessageBox.Show($"Đã tạo thành công {qty} thẻ. Đã lưu hóa đơn.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                            await LoadComboCardsAsync();
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
                    BtnGenerate.Content = "Tạo Thẻ (Bán Mới)";
                }
            }
            else
            {
                MessageBox.Show("Vui lòng chọn một gói Combo", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                // Query only COMBO members
                var url = mainWindow.BuildApiUrl("/members?memberType=COMBO");
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ComboCardResponse>();
                    if (result != null)
                    {
                        GridComboCards.ItemsSource = result.items;
                    }
                }
            }
            catch (Exception)
            {
                // Optionally handle silent errors or just show a warning
            }
        }

        private async void BtnDeleteComboCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string memberId)
            {
                if (MessageBox.Show("Bạn có chắc chắn muốn xóa tài khoản Combo này?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
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

        private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl && TabPreGeneratedCombos.IsSelected)
            {
                if (_preGeneratedCombos.Count == 0)
                {
                    await LoadOrGeneratePreCombosAsync();
                }
            }
        }

        private async void BtnGeneratePreCombos_Click(object sender, RoutedEventArgs e)
        {
            await LoadOrGeneratePreCombosAsync();
        }

        private async void BtnDeleteAllPreCombos_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ các Combo đã tạo để tạo lại mới không?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                BtnDeletePreCombos.IsEnabled = false;
                BtnDeletePreCombos.Content = "Đang xóa...";
                
                try
                {
                    var mainWindow = (MainWindow)Application.Current.MainWindow;
                    var client = mainWindow._httpClient;
                    var url = mainWindow.BuildApiUrl("/members?memberType=COMBO");
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<ComboCardResponse>();
                        if (result != null && result.items != null)
                        {
                            foreach (var item in result.items)
                            {
                                await client.DeleteAsync(mainWindow.BuildApiUrl($"/members/{item.id}"));
                            }
                        }
                    }
                    
                    // Reload and regenerate
                    await LoadOrGeneratePreCombosAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi xóa: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnDeletePreCombos.IsEnabled = true;
                    BtnDeletePreCombos.Content = "Xóa & Tạo Lại";
                }
            }
        }

        private async Task LoadOrGeneratePreCombosAsync()
        {
            BtnGeneratePreCombos.IsEnabled = false;
            BtnGeneratePreCombos.Content = "Đang tải...";
            _preGeneratedCombos.Clear();
            GridPreGeneratedCombos.ItemsSource = null;

            try
            {
                var mainWindow = (MainWindow)Application.Current.MainWindow;
                var client = mainWindow._httpClient;
                
                // First, check if there are already valid combos for today
                var url = mainWindow.BuildApiUrl("/members?memberType=COMBO");
                var response = await client.GetAsync(url);
                bool needsGeneration = true;
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ComboCardResponse>();
                    if (result != null && result.items != null)
                    {
                        var validCombos = result.items.Where(c => c.comboExpiresAt != null && c.comboExpiresAt > DateTime.Now).ToList();
                        if (validCombos.Count > 0)
                        {
                            needsGeneration = false;
                            foreach (var vc in validCombos)
                            {
                                _preGeneratedCombos.Add(new GeneratedCombo
                                {
                                    comboName = "Combo (Đã tạo)",
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
                    BtnGeneratePreCombos.Content = "Đang tạo...";
                    // Fetch packages
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
                } // End if needsGeneration
                
                GridPreGeneratedCombos.ItemsSource = null;
                GridPreGeneratedCombos.ItemsSource = _preGeneratedCombos;
                await LoadComboCardsAsync(); // Also refresh the main list
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi tạo sẵn Combo: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnGeneratePreCombos.IsEnabled = true;
                BtnGeneratePreCombos.Content = "Tạo mới toàn bộ";
            }
        }

        private void GridPreGeneratedCombos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (GridPreGeneratedCombos.SelectedItem is GeneratedCombo selected)
            {
                var popup = new ComboCardInfoPopup(selected.comboName, selected.username, selected.password, selected.expiresAt);
                popup.Owner = Window.GetWindow(this);
                popup.ShowDialog();
            }
        }
    }
}
