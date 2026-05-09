using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Server.Admin.App;
public partial class MainWindow : Window
{
    private async Task RefreshMembersAsync(bool forceRefresh = false)
    {
        try
        {
            var selectedMemberId = _selectedMemberId ?? (MembersDataGrid.SelectedItem as MemberRow)?.Id;
            var search = _memberSearchKeyword.Trim();
            var cacheKey = search.ToLowerInvariant();
            MemberListResponse? response;

            if (!forceRefresh &&
                _membersCacheResponse is not null &&
                string.Equals(_membersCacheKey, cacheKey, StringComparison.Ordinal) &&
                IsCacheValid(_membersCacheAtUtc, MembersCacheTtlSeconds))
            {
                response = _membersCacheResponse;
            }
            else
            {
                var url = BuildApiUrl("/members");
                if (!string.IsNullOrWhiteSpace(search))
                {
                    url += $"?search={Uri.EscapeDataString(search)}";
                }

                response = await _httpClient.GetFromJsonAsync<MemberListResponse>(url, JsonOptions());
                if (response is null)
                {
                    return;
                }

                _membersCacheResponse = response;
                _membersCacheKey = cacheKey;
                _membersCacheAtUtc = DateTime.UtcNow;
            }

            var mapped = response.Items
                .Select(ToMemberRow)
                .OrderBy(x => x.Username)
                .ToList();

            _memberRows.Clear();
            foreach (var row in mapped)
            {
                _memberRows.Add(row);
            }

            if (!string.IsNullOrWhiteSpace(selectedMemberId))
            {
                var current = mapped.FirstOrDefault(x => x.Id == selectedMemberId);
                if (current is null && mapped.Count > 0)
                {
                    _selectedMemberId = mapped[0].Id;
                }
                else
                {
                    _selectedMemberId = current?.Id;
                }
            }
            else if (mapped.Count > 0)
            {
                _selectedMemberId = mapped[0].Id;
            }

            if (!string.IsNullOrWhiteSpace(_selectedMemberId))
            {
                var selectedRow = mapped.FirstOrDefault(x => x.Id == _selectedMemberId);
                if (selectedRow is not null)
                {
                    MembersDataGrid.SelectedItem = selectedRow;
                    MembersDataGrid.ScrollIntoView(selectedRow);
                }
            }

            if (!string.IsNullOrWhiteSpace(_selectedMemberId))
            {
                await RefreshMemberTransactionsAsync(_selectedMemberId);
            }
            else
            {
                _memberTransactionRows.Clear();
                SelectedMemberTextBlock.Text = I18n.MemberNotSelected;
            }

            MembersLastSyncTextBlock.Text = $"{I18n.SyncPrefix}: {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            // Keep UI responsive.
        }
    }

    private static MemberRow ToMemberRow(MemberItem item)
    {
        return new MemberRow
        {
            Id = item.Id,
            Username = item.Username,
            FullName = item.FullName,
            Phone = string.IsNullOrWhiteSpace(item.Phone) ? "-" : item.Phone,
            IdentityNumber = string.IsNullOrWhiteSpace(item.IdentityNumber) ? "-" : item.IdentityNumber,
            HasPassword = item.HasPassword,
            IsActive = item.IsActive,
            BalanceRaw = item.Balance,
            PlayHoursRaw = item.PlayHours,
            BalanceText = item.Balance.ToString("N0", CultureInfo.InvariantCulture),
            PlayHoursText = item.PlayHours.ToString("0.##", CultureInfo.InvariantCulture),
            Rank = string.IsNullOrWhiteSpace(item.Rank) ? "S\u1eaft" : item.Rank,
            TotalTopupRaw = item.TotalTopup,
            AvailablePoints = item.AvailablePoints,
            TotalTopupText = item.TotalTopup.ToString("N0", CultureInfo.InvariantCulture),
            PasswordState = item.HasPassword ? "\u0110\u00e3 \u0111\u1eb7t" : "Ch\u01b0a \u0111\u1eb7t",
            ActiveText = item.IsActive ? "Ho\u1ea1t \u0111\u1ed9ng" : "T\u1ea1m kh\u00f3a",
            CreatedAtText = FormatDateTime(item.CreatedAt),
        };
    }

    private async Task RefreshMemberTransactionsAsync(string memberId)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<MemberTransactionsResponse>(
                BuildApiUrl($"/members/{memberId}/transactions"),
                JsonOptions());

            if (response is null)
            {
                return;
            }

            _memberTransactionRows.Clear();
            foreach (var tx in response.Items.Select(ToMemberTransactionRow))
            {
                _memberTransactionRows.Add(tx);
            }

            SelectedMemberTextBlock.Text =
                $"{I18n.MemberSelectedPrefix}: {response.Member.Username} - {I18n.MemberBalancePrefix} {response.Member.Balance:N0} - {I18n.MemberPlayHoursPrefix} {response.Member.PlayHours:0.##} - Điểm: {response.Member.AvailablePoints}";
        }
        catch
        {
            // Ignore transaction panel failures.
        }
    }

    private static MemberTransactionRow ToMemberTransactionRow(MemberTransactionItem item)
    {
        var typeText = item.Type switch
        {
            "TOPUP" => "N\u1ea1p ti\u1ec1n",
            "BUY_PLAYTIME" => "Mua gi\u1edd",
            _ => "\u0110i\u1ec1u ch\u1ec9nh",
        };

        return new MemberTransactionRow
        {
            CreatedAtText = FormatDateTime(item.CreatedAt),
            TypeText = typeText,
            AmountDeltaText = item.AmountDelta.ToString("N0", CultureInfo.InvariantCulture),
            PlayHoursDeltaText = (item.PlaySecondsDelta / 3600.0).ToString("0.##", CultureInfo.InvariantCulture),
            CreatedBy = item.CreatedBy,
            Note = string.IsNullOrWhiteSpace(item.Note) ? "-" : item.Note,
        };
    }

    private async Task CreateMemberAsync()
    {
        var username = MemberUsernameTextBox.Text.Trim();
        var password = MemberPasswordBox.Password;
        var phone = MemberPhoneTextBox.Text.Trim();
        var identityNumber = MemberIdentityTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            MemberModalErrorTextBlock.Text = I18n.MemberUsernameRequired;
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            MemberModalErrorTextBlock.Text = I18n.MemberPasswordTooShort;
            return;
        }

        try
        {
            MemberModalErrorTextBlock.Text = string.Empty;

            var body = new
            {
                username,
                password,
                fullName = username,
                phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
                identityNumber = string.IsNullOrWhiteSpace(identityNumber) ? null : identityNumber,
            };

            using var response = await _httpClient.PostAsJsonAsync(BuildApiUrl("/members"), body);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                MemberModalErrorTextBlock.Text = string.IsNullOrWhiteSpace(err)
                    ? $"{I18n.MemberCreateFailed} ({(int)response.StatusCode})"
                    : err;
                return;
            }

            HideAddMemberModal();
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] \u0110\u00e3 t\u1ea1o h\u1ed9i vi\u00ean {username}");
            InvalidateMembersCache();
            await RefreshMembersAsync(forceRefresh: true);
        }
        catch (Exception ex)
        {
            MemberModalErrorTextBlock.Text = ex.Message;
        }
    }

    private async Task TopupMemberAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedMemberId))
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedMember =
            MembersDataGrid.SelectedItem as MemberRow ??
            _memberRows.FirstOrDefault(x => string.Equals(x.Id, _selectedMemberId, StringComparison.OrdinalIgnoreCase));

        await TopupMemberByRowAsync(selectedMember);
    }

    private async Task TopupMemberByRowAsync(MemberRow? selectedMember)
    {
        if (selectedMember is null)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selectedMemberId = selectedMember.Id;
        var amount = await ShowTopupModalAsync(selectedMember);
        if (!amount.HasValue)
        {
            return;
        }

        TopupAmountTextBox.Text = amount.Value.ToString("0", CultureInfo.InvariantCulture);

        if (Math.Abs(amount.Value) < 1000)
        {
            MessageBox.Show("Số tiền thao tác tối thiểu là 1.000 VND.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        HttpResponseMessage response;
        if (amount.Value >= 0)
        {
            response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/members/{_selectedMemberId}/topups"),
                new { amount = Convert.ToDouble(amount.Value), createdBy = "admin.desktop" });
        }
        else
        {
            response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/members/{_selectedMemberId}/adjust"),
                new
                {
                    amountDelta = Convert.ToDouble(amount.Value),
                    createdBy = "admin.desktop",
                    note = "Trừ tiền từ màn hình nạp tiền",
                });
        }

        if (!response.IsSuccessStatusCode)
        {
            var actionLabel = amount.Value >= 0 ? "Nạp tiền" : "Trừ tiền";
            MessageBox.Show($"{actionLabel} thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (amount.Value >= 0)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] N\u1ea1p ti\u1ec1n {amount.Value:N0} cho h\u1ed9i vi\u00ean {_selectedMemberId}");
        }
        else
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Tr\u1eeb ti\u1ec1n {Math.Abs(amount.Value):N0} cho h\u1ed9i vi\u00ean {_selectedMemberId}");
        }

        InvalidateMembersCache();
        await RefreshMembersAsync(forceRefresh: true);
    }

    private async Task<decimal?> ShowTopupModalAsync(
        MemberRow? member,
        string? title = null,
        string? memberPrompt = null,
        string? currentBalanceText = null,
        bool allowDeduct = true)
    {
        if (_topupModalTcs is not null)
        {
            return await _topupModalTcs.Task;
        }

        _topupModalAmount = 0m;
        _topupModalHistory.Clear();
        _topupModalAllowDeduct = allowDeduct;
        _topupModalIsDeduct = false;

        TopupModalTitleTextBlock.Text =
            !string.IsNullOrWhiteSpace(title)
                ? title
                : member is null
                    ? "Nạp tiền hội viên"
                    : $"Nạp tiền - {member.Username}";
        TopupModalMemberTextBlock.Text =
            !string.IsNullOrWhiteSpace(memberPrompt)
                ? memberPrompt
                : member is null
                    ? "Chọn số tiền nạp:"
                    : $"Hội viên: {member.Username} - chọn số tiền nạp:";
        TopupModalCurrentBalanceTextBlock.Text =
            !string.IsNullOrWhiteSpace(currentBalanceText)
                ? currentBalanceText
                : member is null
                    ? "Số dư hiện tại: - VND"
                    : $"Số dư hiện tại: {member.BalanceRaw:N0} VND - Điểm: {member.AvailablePoints}";
        TopupCustomAmountTextBox.Text = string.Empty;
        TopupModalErrorTextBlock.Text = string.Empty;
        TopupModalOverlay.Visibility = Visibility.Visible;

        _topupModalTcs = new TaskCompletionSource<decimal?>();
        UpdateTopupModalUi();
        return await _topupModalTcs.Task;
    }

    private void UpdateTopupModalUi()
    {
        if (!_topupModalAllowDeduct && _topupModalIsDeduct)
        {
            _topupModalIsDeduct = false;
        }

        var signedAmount = _topupModalIsDeduct ? -_topupModalAmount : _topupModalAmount;
        TopupModalAmountTextBlock.Text = $"{signedAmount:N0} VND";
        TopupModalAmountTextBlock.Foreground = _topupModalIsDeduct
            ? new SolidColorBrush(Color.FromRgb(185, 28, 28))
            : new SolidColorBrush(Color.FromRgb(9, 36, 140));

        TopupModeAddButton.Visibility = _topupModalAllowDeduct ? Visibility.Visible : Visibility.Collapsed;
        TopupModeSubtractButton.Visibility = _topupModalAllowDeduct ? Visibility.Visible : Visibility.Collapsed;

        if (_topupModalIsDeduct && _topupModalAllowDeduct)
        {
            TopupModeSubtractButton.Background = new SolidColorBrush(Color.FromRgb(254, 226, 226));
            TopupModeSubtractButton.BorderBrush = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            TopupModeAddButton.Background = Brushes.White;
            TopupModeAddButton.BorderBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            TopupSubmitButton.Content = "Trừ";
            TopupSubmitButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            TopupSubmitButton.BorderBrush = new SolidColorBrush(Color.FromRgb(153, 27, 27));
            TopupSubmitButton.Foreground = Brushes.White;
        }
        else
        {
            if (_topupModalAllowDeduct)
            {
                TopupModeAddButton.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
                TopupModeAddButton.BorderBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TopupModeSubtractButton.Background = Brushes.White;
                TopupModeSubtractButton.BorderBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            }

            TopupSubmitButton.Content = "Nạp";
            TopupSubmitButton.Background = new SolidColorBrush(Color.FromRgb(121, 201, 89));
            TopupSubmitButton.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46));
            TopupSubmitButton.Foreground = Brushes.Black;
        }

        TopupUndoButton.IsEnabled = _topupModalHistory.Count > 0;
        TopupClearButton.IsEnabled = _topupModalAmount > 0;
        TopupSubmitButton.IsEnabled = _topupModalAmount >= 1000m;
    }

    private void CloseTopupModal(decimal? amount)
    {
        if (_topupModalTcs is null)
        {
            TopupModalOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var tcs = _topupModalTcs;
        _topupModalTcs = null;
        TopupModalOverlay.Visibility = Visibility.Collapsed;
        TopupModalErrorTextBlock.Text = string.Empty;
        tcs.TrySetResult(amount);
    }

    private void TopupQuickAmountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topupModalTcs is null || sender is not Button button || button.Tag is null)
        {
            return;
        }

        if (!decimal.TryParse(button.Tag.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return;
        }

        _topupModalAmount += value;
        _topupModalHistory.Push(value);
        TopupModalErrorTextBlock.Text = string.Empty;
        UpdateTopupModalUi();
    }

    private void TopupUndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topupModalTcs is null || _topupModalHistory.Count == 0)
        {
            return;
        }

        _topupModalAmount -= _topupModalHistory.Pop();
        if (_topupModalAmount < 0)
        {
            _topupModalAmount = 0;
        }

        TopupModalErrorTextBlock.Text = string.Empty;
        UpdateTopupModalUi();
    }

    private void TopupClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topupModalTcs is null)
        {
            return;
        }

        _topupModalAmount = 0;
        _topupModalHistory.Clear();
        TopupModalErrorTextBlock.Text = string.Empty;
        UpdateTopupModalUi();
    }

    private void TopupCancelButton_Click(object sender, RoutedEventArgs e)
    {
        CloseTopupModal(null);
    }

    private void TopupSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topupModalAmount < 1000m)
        {
            TopupModalErrorTextBlock.Text = "Vui lòng chọn số tiền tối thiểu 1.000 VND.";
            return;
        }

        var signedAmount = _topupModalIsDeduct ? -_topupModalAmount : _topupModalAmount;
        CloseTopupModal(signedAmount);
    }

    private void TopupApplyCustomAmountButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCustomTopupAmount();
    }

    private void TopupCustomAmountTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyCustomTopupAmount();
        e.Handled = true;
    }

    private void ApplyCustomTopupAmount()
    {
        if (_topupModalTcs is null)
        {
            return;
        }

        var raw = TopupCustomAmountTextBox.Text.Trim();
        if (!TryParsePositiveMoney(raw, out var customAmount))
        {
            TopupModalErrorTextBlock.Text = "Số tiền nhập không hợp lệ.";
            return;
        }

        _topupModalAmount = customAmount;
        _topupModalHistory.Clear();
        TopupModalErrorTextBlock.Text = string.Empty;
        UpdateTopupModalUi();
    }

    private void TopupModeAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_topupModalAllowDeduct)
        {
            return;
        }

        _topupModalIsDeduct = false;
        TopupModalErrorTextBlock.Text = string.Empty;
        UpdateTopupModalUi();
    }

    private void TopupModeSubtractButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_topupModalAllowDeduct)
        {
            return;
        }

        _topupModalIsDeduct = true;
        TopupModalErrorTextBlock.Text = string.Empty;
        UpdateTopupModalUi();
    }

    private async Task BuyHoursAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedMemberId))
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!double.TryParse(BuyHoursTextBox.Text.Trim(), out var hours) || hours <= 0)
        {
            MessageBox.Show(I18n.InvalidBuyHours, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(RatePerHourTextBox.Text.Trim(), out var ratePerHour) || ratePerHour <= 0)
        {
            MessageBox.Show(I18n.InvalidRatePerHour, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/members/{_selectedMemberId}/buy-hours"),
            new { hours, ratePerHour, createdBy = "admin.desktop" });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show($"Mua gi\u1edd th\u1ea5t b\u1ea1i ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Mua {hours:0.##} gi\u1edd cho h\u1ed9i vi\u00ean {_selectedMemberId}");
        InvalidateMembersCache();
        await RefreshMembersAsync(forceRefresh: true);
    }

    private async Task AdjustMemberBalanceAsync(bool isRefund)
    {
        if (string.IsNullOrWhiteSpace(_selectedMemberId))
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!decimal.TryParse(TopupAmountTextBox.Text.Trim(), out var amount) || amount <= 0)
        {
            MessageBox.Show(I18n.InvalidTopupAmount, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var signedAmount = isRefund ? -amount : amount;
        var note = isRefund ? "Tien tra lai" : "Tang tien mien phi";

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/members/{_selectedMemberId}/adjust"),
            new { amountDelta = Convert.ToDouble(signedAmount), createdBy = "admin.desktop", note });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show($"{note} that bai ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] {note} {amount:N0} cho hoi vien {_selectedMemberId}");
        InvalidateMembersCache();
        await RefreshMembersAsync(forceRefresh: true);
    }


    private void ShowAddMemberModal()
    {
        MemberUsernameTextBox.Text = string.Empty;
        MemberPasswordBox.Password = string.Empty;
        MemberPhoneTextBox.Text = string.Empty;
        MemberIdentityTextBox.Text = string.Empty;
        MemberModalErrorTextBlock.Text = string.Empty;
        MemberModalOverlay.Visibility = Visibility.Visible;
        MemberUsernameTextBox.Focus();
    }

    private void HideAddMemberModal()
    {
        MemberModalOverlay.Visibility = Visibility.Collapsed;
    }

    private async void RefreshMembersButton_Click(object sender, RoutedEventArgs e) => await RefreshMembersAsync(forceRefresh: true);

    private void AddMemberButton_Click(object sender, RoutedEventArgs e) => ShowAddMemberModal();

    private async void TopupMemberButton_Click(object sender, RoutedEventArgs e) => await TopupMemberAsync();

    private async void BuyHoursButton_Click(object sender, RoutedEventArgs e) => await BuyHoursAsync();
    private async void RefundMemberButton_Click(object sender, RoutedEventArgs e) => await AdjustMemberBalanceAsync(true);
    private async void GiftMemberButton_Click(object sender, RoutedEventArgs e) => await AdjustMemberBalanceAsync(false);

    private void MembersSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _memberSearchKeyword = MembersSearchTextBox.Text.Trim();
        _memberSearchDebounceTimer.Stop();
        _memberSearchDebounceTimer.Start();
    }

    private async void MemberSearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _memberSearchDebounceTimer.Stop();
        if (!IsMembersTabActive())
        {
            return;
        }

        await RefreshMembersAsync();
    }

    private async void MembersDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MembersDataGrid.SelectedItem is not MemberRow row)
        {
            return;
        }

        _selectedMemberId = row.Id;
        await RefreshMemberTransactionsAsync(row.Id);
    }

    private void MembersDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();
        if (row.Item is MemberRow member)
        {
            _selectedMemberId = member.Id;
        }
    }

    private void MembersDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            e.Handled = true;
            return;
        }

        if (dataGrid.SelectedItem is not MemberRow && !string.IsNullOrWhiteSpace(_selectedMemberId))
        {
            var selected = _memberRows.FirstOrDefault(x => x.Id == _selectedMemberId);
            if (selected is not null)
            {
                dataGrid.SelectedItem = selected;
            }
        }

        if (dataGrid.SelectedItem is not MemberRow)
        {
            e.Handled = true;
        }
    }

    private async void MembersDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MembersDataGrid.SelectedItem is not MemberRow selected)
        {
            return;
        }

        await OpenEditMemberDialogAsync(selected);
    }

    private async void ContextEditMemberMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MembersDataGrid.SelectedItem is not MemberRow selected)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await OpenEditMemberDialogAsync(selected);
    }

    private async void ContextTopupMemberMenuItem_Click(object sender, RoutedEventArgs e) => await TopupMemberAsync();
    private async void ContextBuyHoursMenuItem_Click(object sender, RoutedEventArgs e) => await BuyHoursAsync();
    private async void ContextRefundMemberMenuItem_Click(object sender, RoutedEventArgs e) => await AdjustMemberBalanceAsync(true);
    private async void ContextGiftMemberMenuItem_Click(object sender, RoutedEventArgs e) => await AdjustMemberBalanceAsync(false);
    private async void ContextTransferMemberMenuItem_Click(object sender, RoutedEventArgs e) => await TransferMemberBalanceAsync();

    private async void CreateMemberConfirmButton_Click(object sender, RoutedEventArgs e) => await CreateMemberAsync();

    private void CancelMemberModalButton_Click(object sender, RoutedEventArgs e) => HideAddMemberModal();

    private async Task OpenEditMemberDialogAsync(MemberRow member)
    {
        var lifetimeTopup = member.TotalTopupRaw;

        var dialog = new Window
        {
            Title = $"Thông tin hội viên - {member.Username}",
            Width = 520,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
            Owner = this,
        };

        var root = new Grid { Margin = new Thickness(16) };
        for (var i = 0; i < 18; i++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var usernameLabel = new TextBlock { Text = "Username", Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(usernameLabel, 0);
        root.Children.Add(usernameLabel);

        var usernameBox = new TextBox
        {
            Text = member.Username,
            Height = 32,
            IsReadOnly = true,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(usernameBox, 1);
        root.Children.Add(usernameBox);

        var fullNameLabel = new TextBlock { Text = "Họ tên", Margin = new Thickness(0, 10, 0, 4) };
        Grid.SetRow(fullNameLabel, 2);
        root.Children.Add(fullNameLabel);

        var fullNameBox = new TextBox
        {
            Text = member.FullName,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(fullNameBox, 3);
        root.Children.Add(fullNameBox);

        var phoneLabel = new TextBlock { Text = "Số điện thoại", Margin = new Thickness(0, 10, 0, 4) };
        Grid.SetRow(phoneLabel, 4);
        root.Children.Add(phoneLabel);

        var phoneBox = new TextBox
        {
            Text = member.Phone == "-" ? string.Empty : member.Phone,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(phoneBox, 5);
        root.Children.Add(phoneBox);

        var identityLabel = new TextBlock { Text = "CCCD/CMND", Margin = new Thickness(0, 10, 0, 4) };
        Grid.SetRow(identityLabel, 6);
        root.Children.Add(identityLabel);

        var identityBox = new TextBox
        {
            Text = member.IdentityNumber == "-" ? string.Empty : member.IdentityNumber,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(identityBox, 7);
        root.Children.Add(identityBox);

        var balanceLabel = new TextBlock { Text = "Số dư (VND)", Margin = new Thickness(0, 10, 0, 4) };
        Grid.SetRow(balanceLabel, 8);
        root.Children.Add(balanceLabel);

        var balanceBox = new TextBox
        {
            Text = member.BalanceRaw.ToString("0.##", CultureInfo.InvariantCulture),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var totalTopupTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = Brushes.DimGray,
            Text = $"B\u1eadc VIP: {member.Rank} (T\u1ed5ng n\u1ea1p: {lifetimeTopup:N0} VND)",
            FontWeight = FontWeights.SemiBold,
        };
        var balancePanel = new StackPanel();
        balancePanel.Children.Add(balanceBox);
        balancePanel.Children.Add(totalTopupTextBlock);
        Grid.SetRow(balancePanel, 9);
        root.Children.Add(balancePanel);

        var playHoursLabel = new TextBlock { Text = "Giờ chơi", Margin = new Thickness(0, 10, 0, 4) };
        Grid.SetRow(playHoursLabel, 10);
        root.Children.Add(playHoursLabel);

        var playHoursBox = new TextBox
        {
            Text = member.PlayHoursRaw.ToString("0.##", CultureInfo.InvariantCulture),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(playHoursBox, 11);
        root.Children.Add(playHoursBox);

        var pointsLabel = new TextBlock { Text = "Điểm tích lũy", Margin = new Thickness(0, 10, 0, 4) };
        Grid.SetRow(pointsLabel, 12);
        root.Children.Add(pointsLabel);

        var pointsBox = new TextBox
        {
            Text = member.AvailablePoints.ToString(),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(pointsBox, 13);
        root.Children.Add(pointsBox);

        var statusCheckBox = new CheckBox
        {
            Content = "Tài khoản đang hoạt động",
            IsChecked = member.IsActive,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Grid.SetRow(statusCheckBox, 14);
        root.Children.Add(statusCheckBox);

        var passwordLabel = new TextBlock
        {
            Text = "Đổi mật khẩu (để trống nếu không đổi)",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(passwordLabel, 15);
        root.Children.Add(passwordLabel);

        var passwordBox = new PasswordBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(passwordBox, 16);
        root.Children.Add(passwordBox);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(errorTextBlock, 17);
        root.Children.Add(errorTextBlock);

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var saveButton = new Button
        {
            Content = "Luu",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            IsCancel = true,
        };
        actionsPanel.Children.Add(saveButton);
        actionsPanel.Children.Add(cancelButton);
        Grid.SetRow(actionsPanel, 19);
        root.Children.Add(actionsPanel);

        saveButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            if (!TryParseNonNegativeMoney(balanceBox.Text.Trim(), out var balance))
            {
                errorTextBlock.Text = "Số dư không hợp lệ.";
                return;
            }

            if (!TryParseNonNegativeDouble(playHoursBox.Text.Trim(), out var playHours))
            {
                errorTextBlock.Text = "Giờ chơi không hợp lệ.";
                return;
            }

            if (!int.TryParse(pointsBox.Text.Trim(), out var points) || points < 0)
            {
                errorTextBlock.Text = "Điểm tích lũy không hợp lệ.";
                return;
            }

            var fullName = fullNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            {
                errorTextBlock.Text = "Họ tên không được để trống.";
                return;
            }

            var payload = new Dictionary<string, object?>
            {
                ["fullName"] = fullName,
                ["phone"] = string.IsNullOrWhiteSpace(phoneBox.Text) ? null : phoneBox.Text.Trim(),
                ["identityNumber"] = string.IsNullOrWhiteSpace(identityBox.Text) ? null : identityBox.Text.Trim(),
                ["isActive"] = statusCheckBox.IsChecked == true,
                ["balance"] = Convert.ToDouble(balance),
                ["playHours"] = playHours,
                ["availablePoints"] = points,
                ["updatedBy"] = "admin.desktop",
                ["note"] = "Cap nhat tu app server admin",
            };

            var newPassword = passwordBox.Password;
            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                payload["password"] = newPassword;
            }

            try
            {
                using var response = await _httpClient.PatchAsJsonAsync(
                    BuildApiUrl($"/members/{member.Id}"),
                    payload);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(err)
                        ? $"Cập nhật thất bại ({(int)response.StatusCode})"
                        : err;
                    return;
                }

                dialog.DialogResult = true;
                dialog.Close();
            }
            catch (Exception ex)
            {
                errorTextBlock.Text = ex.Message;
            }
        };

        dialog.Content = root;
        var submitted = dialog.ShowDialog() == true;
        if (!submitted)
        {
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã cập nhật hội viên {member.Username}");
        InvalidateMembersCache();
        await RefreshMembersAsync(forceRefresh: true);
    }


    private async Task TransferMemberBalanceAsync()
    {
        if (MembersDataGrid.SelectedItem is not MemberRow sourceMember)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targets = _memberRows
            .Where(x => !string.Equals(x.Id, sourceMember.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Username)
            .ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("Không có hội viên đích để chuyển tiền.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var transferPayload = ShowTransferMemberModal(sourceMember, targets);
        if (transferPayload is null)
        {
            return;
        }

        TopupAmountTextBox.Text = transferPayload.Value.Amount.ToString("0", CultureInfo.InvariantCulture);

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/members/{sourceMember.Id}/transfer"),
            new
            {
                targetUsername = transferPayload.Value.TargetMember.Username,
                amount = Convert.ToDouble(transferPayload.Value.Amount),
                note = transferPayload.Value.Note,
                createdBy = "admin.desktop",
            });

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            MessageBox.Show(
                string.IsNullOrWhiteSpace(err)
                    ? $"Chuyển tiền thất bại ({(int)response.StatusCode})"
                    : err,
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog(
            $"[{DateTime.Now:HH:mm:ss}] Đã chuyển {transferPayload.Value.Amount:N0} từ {sourceMember.Username} sang {transferPayload.Value.TargetMember.Username}");
        InvalidateMembersCache();
        await RefreshMembersAsync(forceRefresh: true);
    }

    private (MemberRow TargetMember, decimal Amount, string? Note)? ShowTransferMemberModal(
        MemberRow sourceMember,
        List<MemberRow> targets)
    {
        var amount = 0m;
        var history = new Stack<decimal>();
        (MemberRow TargetMember, decimal Amount, string? Note)? result = null;

        var dialog = new Window
        {
            Title = $"Chuyển tiền - {sourceMember.Username}",
            Width = 560,
            Height = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
            Owner = this,
        };

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = $"Hội viên nguồn: {sourceMember.Username} (Số dư: {sourceMember.BalanceRaw:N0} VND)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var targetLabel = new TextBlock
        {
            Text = "Hội viên nhận tiền:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(targetLabel, 1);
        root.Children.Add(targetLabel);

        var targetComboBox = new ComboBox
        {
            Height = 34,
            DisplayMemberPath = nameof(MemberRow.Username),
            ItemsSource = targets,
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(targetComboBox, 2);
        root.Children.Add(targetComboBox);

        var amountBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(231, 243, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(151, 184, 226)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var amountText = new TextBlock
        {
            Text = "0 VND",
            FontSize = 38,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(9, 36, 140)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        amountBorder.Child = amountText;
        Grid.SetRow(amountBorder, 3);
        root.Children.Add(amountBorder);

        var hint = new TextBlock
        {
            Text = "Bấm nhiều lần để cộng dồn số tiền chuyển.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(hint, 4);
        root.Children.Add(hint);

        var customGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var customLabel = new TextBlock
        {
            Text = "Nhập số tiền:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(customLabel, 0);
        customGrid.Children.Add(customLabel);

        var customBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(customBox, 1);
        customGrid.Children.Add(customBox);

        var applyCustomButton = new Button
        {
            Content = "Áp dụng",
            Width = 90,
            Height = 32,
        };
        Grid.SetColumn(applyCustomButton, 2);
        customGrid.Children.Add(applyCustomButton);

        Grid.SetRow(customGrid, 5);
        root.Children.Add(customGrid);

        var quickGreenGrid = new UniformGrid
        {
            Columns = 3,
            Margin = new Thickness(0, 0, 0, 8),
        };
        foreach (var v in new[] { 1000m, 2000m, 3000m, 4000m, 5000m, 6000m, 7000m, 8000m, 9000m })
        {
            var button = new Button
            {
                Content = v.ToString("N0", CultureInfo.InvariantCulture),
                Tag = v,
                Height = 44,
                Margin = new Thickness(3),
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Background = new SolidColorBrush(Color.FromRgb(131, 217, 92)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            };
            quickGreenGrid.Children.Add(button);
        }
        Grid.SetRow(quickGreenGrid, 6);
        root.Children.Add(quickGreenGrid);

        var quickRedGrid = new UniformGrid
        {
            Columns = 3,
            Margin = new Thickness(0, 0, 0, 10),
        };
        foreach (var v in new[] { 10000m, 20000m, 30000m, 40000m, 50000m, 60000m, 70000m, 80000m, 90000m })
        {
            var button = new Button
            {
                Content = v.ToString("N0", CultureInfo.InvariantCulture),
                Tag = v,
                Height = 44,
                Margin = new Thickness(3),
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 238, 0)),
                Background = new SolidColorBrush(Color.FromRgb(208, 34, 23)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            };
            quickRedGrid.Children.Add(button);
        }
        Grid.SetRow(quickRedGrid, 7);
        root.Children.Add(quickRedGrid);

        var noteLabel = new TextBlock { Text = "Ghi chú (không bắt buộc)", Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(noteLabel, 8);
        root.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "Chuyển tiền hội viên",
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(noteBox, 9);
        root.Children.Add(noteBox);

        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(errorText, 10);
        root.Children.Add(errorText);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var undoButton = new Button
        {
            Content = "-",
            Width = 68,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
        };
        var clearButton = new Button
        {
            Content = "Xóa",
            Width = 90,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.SemiBold,
        };
        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        var submitButton = new Button
        {
            Content = "Chuyển",
            Width = 100,
            Height = 36,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };
        actionPanel.Children.Add(undoButton);
        actionPanel.Children.Add(clearButton);
        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(submitButton);
        Grid.SetRow(actionPanel, 11);
        root.Children.Add(actionPanel);

        void UpdateUi()
        {
            amountText.Text = $"{amount:N0} VND";
            undoButton.IsEnabled = history.Count > 0;
            clearButton.IsEnabled = amount > 0;
            submitButton.IsEnabled = amount >= 1000m;
        }

        void AddAmount(decimal value)
        {
            if (value <= 0)
            {
                return;
            }

            amount += value;
            history.Push(value);
            errorText.Text = string.Empty;
            UpdateUi();
        }

        foreach (var button in quickGreenGrid.Children.OfType<Button>())
        {
            button.Click += (_, _) =>
            {
                if (button.Tag is decimal v)
                {
                    AddAmount(v);
                }
            };
        }

        foreach (var button in quickRedGrid.Children.OfType<Button>())
        {
            button.Click += (_, _) =>
            {
                if (button.Tag is decimal v)
                {
                    AddAmount(v);
                }
            };
        }

        applyCustomButton.Click += (_, _) =>
        {
            var raw = customBox.Text.Trim();
            if (!TryParsePositiveMoney(raw, out var customAmount))
            {
                errorText.Text = "Số tiền nhập không hợp lệ.";
                return;
            }

            amount = customAmount;
            history.Clear();
            errorText.Text = string.Empty;
            UpdateUi();
        };

        customBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter)
            {
                return;
            }

            applyCustomButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            args.Handled = true;
        };

        undoButton.Click += (_, _) =>
        {
            if (history.Count == 0)
            {
                return;
            }

            amount -= history.Pop();
            if (amount < 0)
            {
                amount = 0;
            }

            errorText.Text = string.Empty;
            UpdateUi();
        };

        clearButton.Click += (_, _) =>
        {
            amount = 0;
            history.Clear();
            errorText.Text = string.Empty;
            UpdateUi();
        };

        submitButton.Click += (_, _) =>
        {
            errorText.Text = string.Empty;

            if (targetComboBox.SelectedItem is not MemberRow targetMember)
            {
                errorText.Text = "Vui lòng chọn hội viên nhận tiền.";
                return;
            }

            if (amount < 1000m)
            {
                errorText.Text = "Số tiền chuyển tối thiểu là 1.000 VND.";
                return;
            }

            result = (
                targetMember,
                amount,
                string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim());
            dialog.DialogResult = true;
            dialog.Close();
        };

        UpdateUi();
        dialog.Content = root;
        var submitted = dialog.ShowDialog() == true;
        return submitted ? result : null;
    }

    private static bool TryParseNonNegativeDouble(string value, out double parsed)
    {
        if (double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) && parsed >= 0)
        {
            return true;
        }

        if (double.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed) && parsed >= 0)
        {
            return true;
        }

        parsed = 0;
        return false;
    }

    private static bool TryParseNonNegativeMoney(string value, out decimal amount)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount >= 0)
        {
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) && amount >= 0)
        {
            return true;
        }

        amount = 0;
        return false;
    }
}




