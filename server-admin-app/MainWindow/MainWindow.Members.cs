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
    private const decimal DefaultTopupRatePerHour = 15000m;
    private decimal _topupModalRatePerHour = DefaultTopupRatePerHour;
    private bool _isTopupModalInputSync;

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

            if (string.IsNullOrWhiteSpace(search))
            {
                _cachedMachineSummaryMemberCount =
                    response.Total > 0 ? response.Total : response.Items.Count;
                _cachedMachineSummaryMemberCountAtUtc = DateTime.UtcNow;
                UpdateMachineSummary(_machineRows);
            }

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
            MemberType = item.MemberType ?? "REGULAR",
            MemberTypeText = string.Equals(item.MemberType, "VIP", StringComparison.OrdinalIgnoreCase) ? "VIP" : "Thường",
            CreatedAtText = FormatDateTime(item.CreatedAt),
        };
    }

    private async Task RefreshMemberTransactionsAsync(string memberId)
    {
        try
        {
            var response = await GetMemberTransactionsAsync(memberId);

            if (response is null)
            {
                return;
            }

            var displayItems = PrepareMemberTransactionsForDisplay(
                response.Items,
                aggregateSessionUsage: !IsMemberCurrentlyOnline(memberId));

            _memberTransactionRows.Clear();
            var filteredItems = displayItems
                .Select(ToMemberTransactionRow);

            foreach (var tx in filteredItems)
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

    private async Task<MemberTransactionsResponse?> GetMemberTransactionsAsync(string memberId, string? startDate = null, string? endDate = null)
    {
        var url = $"/members/{memberId}/transactions";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(startDate)) queryParams.Add($"startDate={Uri.EscapeDataString(startDate)}");
        if (!string.IsNullOrWhiteSpace(endDate)) queryParams.Add($"endDate={Uri.EscapeDataString(endDate)}");
        if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);

        return await _httpClient.GetFromJsonAsync<MemberTransactionsResponse>(
            BuildApiUrl(url),
            JsonOptions());
    }

    private async Task<MemberUsageSummaryResponse?> GetMemberUsageSummaryAsync(string memberId)
    {
        return await _httpClient.GetFromJsonAsync<MemberUsageSummaryResponse>(
            BuildApiUrl($"/members/{memberId}/usage-summary"),
            JsonOptions());
    }

    private async Task<SystemEventsResponse?> GetSystemEventsAsync(int limit = 500)
    {
        var safeLimit = Math.Clamp(limit, 20, 500);
        return await _httpClient.GetFromJsonAsync<SystemEventsResponse>(
            BuildApiUrl($"/reports/events/system?limit={safeLimit}"),
            JsonOptions());
    }

    private static MemberTransactionRow ToMemberTransactionRow(MemberTransactionItem item)
    {
        var note = item.Note ?? string.Empty;
        var isSessionUsage = note.StartsWith("SESSION_USAGE", StringComparison.OrdinalIgnoreCase);
        var typeText = item.Type switch
        {
            "TOPUP" => "N\u1ea1p ti\u1ec1n",
            "BUY_PLAYTIME" => "Mua gi\u1edd",
            _ => "\u0110i\u1ec1u ch\u1ec9nh",
        };

        if (isSessionUsage)
        {
            typeText = "Ph\u00ed d\u00f9ng m\u00e1y";
        }

        if (item.Type == "ADJUSTMENT" || item.Type == "TRANSFER")
        {
            if (note.Contains("Chuyen tien") || note.Contains("Nhan tien") || note.Contains("TRANSFER"))
            {
                typeText = "Chuy\u1ec3n ti\u1ec1n";
            }
            else if (note.Contains("Rut tien") || note.Contains("WITHDRAW") || note.Contains("tra lai") || note.Contains("Refund"))
            {
                typeText = "R\u00fat ti\u1ec1n";
            }
            else if (note.Contains("UPFRONT_LOGIN_CHARGE"))
            {
                typeText = "Ph\u00ed \u0111\u0103ng nh\u1eadp";
            }
        }

        return new MemberTransactionRow
        {
            CreatedAtText = FormatDateTime(item.CreatedAt),
            TypeText = typeText,
            AmountDeltaText = item.AmountDelta.ToString("N0", CultureInfo.InvariantCulture),
            PlayMinutesDeltaText = (item.PlaySecondsDelta / 60.0).ToString("0.##", CultureInfo.InvariantCulture),
            CreatedBy = item.CreatedBy,
            Note = string.IsNullOrWhiteSpace(item.Note) ? "-" : item.Note,
        };
    }

    private bool IsMemberCurrentlyOnline(string memberId)
    {
        if (string.IsNullOrWhiteSpace(memberId))
        {
            return false;
        }

        return _machineRows.Any(machine =>
            string.Equals(machine.ActiveMemberId, memberId, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(machine.StatusCode, "IN_USE", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(machine.StatusCode, "PAUSED", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsSessionUsageTransaction(MemberTransactionItem item)
    {
        var note = item.Note ?? string.Empty;
        return note.StartsWith("SESSION_USAGE", StringComparison.OrdinalIgnoreCase);
    }

    private static List<MemberTransactionItem> PrepareMemberTransactionsForDisplay(
        IEnumerable<MemberTransactionItem> sourceItems,
        bool aggregateSessionUsage)
    {
        var ordered = sourceItems
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        if (!aggregateSessionUsage)
        {
            return ordered;
        }

        var result = new List<MemberTransactionItem>();
        var sessionUsageBucket = new List<MemberTransactionItem>();

        void FlushSessionUsageBucket()
        {
            if (sessionUsageBucket.Count == 0)
            {
                return;
            }

            if (sessionUsageBucket.Count == 1)
            {
                result.Add(sessionUsageBucket[0]);
                sessionUsageBucket.Clear();
                return;
            }

            var newest = sessionUsageBucket[0];
            var oldest = sessionUsageBucket[^1];
            var totalAmount = sessionUsageBucket.Sum(x => x.AmountDelta);
            var totalPlaySeconds = sessionUsageBucket.Sum(x => x.PlaySecondsDelta);
            var count = sessionUsageBucket.Count;

            result.Add(new MemberTransactionItem
            {
                Type = newest.Type,
                AmountDelta = totalAmount,
                PlaySecondsDelta = totalPlaySeconds,
                CreatedBy = newest.CreatedBy,
                CreatedAt = newest.CreatedAt,
                Note = $"SESSION_USAGE:GOM_{count}_LAN ({oldest.CreatedAt} -> {newest.CreatedAt})",
            });

            sessionUsageBucket.Clear();
        }

        foreach (var item in ordered)
        {
            if (IsSessionUsageTransaction(item))
            {
                sessionUsageBucket.Add(item);
                continue;
            }

            FlushSessionUsageBucket();
            result.Add(item);
        }

        FlushSessionUsageBucket();
        return result;
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

        if (username.Length < 1)
        {
            MemberModalErrorTextBlock.Text = I18n.MemberUsernameTooShort;
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

            var memberType = "REGULAR";
            if (MemberTypeComboBox.SelectedItem is ComboBoxItem selectedType &&
                selectedType.Tag is string tag &&
                !string.IsNullOrWhiteSpace(tag))
            {
                memberType = tag;
            }

            var body = new
            {
                username,
                password,
                fullName = username,
                phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
                identityNumber = string.IsNullOrWhiteSpace(identityNumber) ? null : identityNumber,
                memberType,
            };

            using var response = await _httpClient.PostAsJsonAsync(BuildApiUrl("/members"), body);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                MemberModalErrorTextBlock.Text = FormatMemberCreateErrorMessage(err, (int)response.StatusCode);
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

    private static string FormatMemberCreateErrorMessage(string rawError, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return $"{I18n.MemberCreateFailed} ({statusCode})";
        }

        var normalized = rawError.Trim();

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var messageElement))
            {
                if (messageElement.ValueKind == JsonValueKind.Array)
                {
                    var parts = messageElement.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => (x.GetString() ?? string.Empty).Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                    if (parts.Count > 0)
                    {
                        normalized = string.Join(Environment.NewLine, parts);
                    }
                }
                else if (messageElement.ValueKind == JsonValueKind.String)
                {
                    normalized = (messageElement.GetString() ?? string.Empty).Trim();
                }
            }
        }
        catch
        {
            // Keep original string if payload is not JSON.
        }

        var lowered = normalized.ToLowerInvariant();
        if (lowered.Contains("username da ton tai") || lowered.Contains("username already exists"))
        {
            return "Tên đăng nhập đã tồn tại. Vui lòng chọn tên khác.";
        }

        if (lowered.Contains("identity number") && lowered.Contains("ton tai"))
        {
            return "CCCD/CMND đã tồn tại trong hệ thống.";
        }

        if (lowered.Contains("phone") && lowered.Contains("ton tai"))
        {
            return "Số điện thoại đã tồn tại trong hệ thống.";
        }

        return normalized;
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

    private async Task TopupMemberByRowAsync(MemberRow? selectedMember, decimal? ratePerHour = null)
    {
        if (selectedMember is null)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selectedMemberId = selectedMember.Id;
        var amount = await ShowTopupModalAsync(selectedMember, ratePerHour: ratePerHour);
        if (!amount.HasValue)
        {
            return;
        }

        TopupAmountTextBox.Text = amount.Value.ToString("0", CultureInfo.InvariantCulture);

        if (Math.Abs(amount.Value) <= 0)
        {
            MessageBox.Show("Số tiền thao tác phải lớn hơn 0 VND.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private void ResetMemberModalTimer()
    {
        _memberModalAutoCloseTimer.Stop();
        _memberModalAutoCloseTimer.Start();
    }

    private void StopMemberModalTimer()
    {
        _memberModalAutoCloseTimer.Stop();
    }

    private async Task<decimal?> ShowTopupModalAsync(
        MemberRow? member,
        string? title = null,
        string? memberPrompt = null,
        string? currentBalanceText = null,
        bool allowDeduct = true,
        decimal? ratePerHour = null)
    {
        if (_topupModalTcs is not null)
        {
            return await _topupModalTcs.Task;
        }

        _topupModalAmount = 0m;
        _topupModalHistory.Clear();
        _topupModalAllowDeduct = allowDeduct;
        _topupModalIsDeduct = false;
        _topupModalRatePerHour = ResolveTopupRatePerHour(ratePerHour);

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
        TopupMinutesTextBox.Text = string.Empty;
        TopupModalErrorTextBlock.Text = string.Empty;
        UpdateTopupMinutesRateHint();
        
        ResetMemberModalTimer();
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

        var bonus = CalculatePromotionBonus(_topupModalAmount);
        if (!_topupModalIsDeduct && bonus > 0)
        {
            TopupModalBonusTextBlock.Text = $"+ Khuyến mãi: {bonus:N0} VND";
            TopupModalBonusTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            TopupModalBonusTextBlock.Visibility = Visibility.Collapsed;
        }

        TopupModeAddButton.Visibility = _topupModalAllowDeduct ? Visibility.Visible : Visibility.Collapsed;
        TopupModeSubtractButton.Visibility = _topupModalAllowDeduct ? Visibility.Visible : Visibility.Collapsed;

        if (_topupModalIsDeduct && _topupModalAllowDeduct)
        {
            TopupModeSubtractButton.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TopupModeSubtractButton.BorderBrush = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            TopupModeSubtractButton.Foreground = Brushes.White;

            TopupModeAddButton.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            TopupModeAddButton.BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            TopupModeAddButton.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));

            TopupSubmitButton.Content = "Trừ";
            TopupSubmitButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            TopupSubmitButton.BorderBrush = new SolidColorBrush(Color.FromRgb(153, 27, 27));
            TopupSubmitButton.Foreground = Brushes.White;
        }
        else
        {
            if (_topupModalAllowDeduct)
            {
                TopupModeAddButton.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                TopupModeAddButton.BorderBrush = new SolidColorBrush(Color.FromRgb(21, 128, 61));
                TopupModeAddButton.Foreground = Brushes.White;

                TopupModeSubtractButton.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
                TopupModeSubtractButton.BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                TopupModeSubtractButton.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));
            }

            TopupSubmitButton.Content = "Nạp";
            TopupSubmitButton.Background = new SolidColorBrush(Color.FromRgb(121, 201, 89));
            TopupSubmitButton.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46));
            TopupSubmitButton.Foreground = Brushes.Black;
        }

        TopupUndoButton.IsEnabled = _topupModalHistory.Count > 0;
        TopupClearButton.IsEnabled = _topupModalAmount > 0;
        TopupSubmitButton.IsEnabled = _topupModalAmount > 0m;
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
        StopMemberModalTimer();
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
        ResetMemberModalTimer();
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
        if (_topupModalAmount <= 0m)
        {
            TopupModalErrorTextBlock.Text = "Vui lòng nhập số tiền lớn hơn 0 VND.";
            return;
        }

        var signedAmount = _topupModalIsDeduct ? -_topupModalAmount : _topupModalAmount;
        if (!_topupModalIsDeduct)
        {
            signedAmount += CalculatePromotionBonus(_topupModalAmount);
        }
        CloseTopupModal(signedAmount);
    }

    private decimal CalculatePromotionBonus(decimal amount)
    {
        if (!_topupPromoEnabled || _topupPromoTierRows.Count == 0)
        {
            return 0m;
        }

        var validTiers = _topupPromoTierRows
            .Select(r => new { 
                MinAmount = decimal.TryParse(r.MinAmountText, out var m) ? m : 0, 
                BonusRate = decimal.TryParse(r.BonusRateText, out var b) ? b : 0 
            })
            .Where(t => t.MinAmount > 0 && t.BonusRate > 0 && amount >= t.MinAmount)
            .OrderByDescending(t => t.MinAmount)
            .ToList();

        if (validTiers.Count > 0)
        {
            var bestTier = validTiers.First();
            return amount * (bestTier.BonusRate / 100m);
        }

        return 0m;
    }

    private void TopupCustomAmountTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isTopupModalInputSync || _topupModalTcs is null)
        {
            return;
        }

        ApplyCustomTopupAmount(showValidationMessage: false, clearMinutesInput: true);
    }

    private void TopupMinutesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isTopupModalInputSync || _topupModalTcs is null)
        {
            return;
        }

        ApplyTopupAmountFromMinutes(showValidationMessage: false);
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

    private void TopupMinutesTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyTopupAmountFromMinutes();
        e.Handled = true;
    }

    private void ApplyCustomTopupAmount(
        bool showValidationMessage = true,
        bool clearMinutesInput = false)
    {
        if (_topupModalTcs is null)
        {
            return;
        }

        var raw = TopupCustomAmountTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            _topupModalAmount = 0m;
            _topupModalHistory.Clear();
            TopupModalErrorTextBlock.Text = string.Empty;

            if (clearMinutesInput && !string.IsNullOrWhiteSpace(TopupMinutesTextBox.Text))
            {
                _isTopupModalInputSync = true;
                TopupMinutesTextBox.Text = string.Empty;
                _isTopupModalInputSync = false;
            }

            UpdateTopupModalUi();
            return;
        }

        if (!TryParsePositiveMoney(raw, out var customAmount))
        {
            if (showValidationMessage)
            {
                TopupModalErrorTextBlock.Text = "Số tiền nhập không hợp lệ.";
            }
            return;
        }

        if (clearMinutesInput && !string.IsNullOrWhiteSpace(TopupMinutesTextBox.Text))
        {
            _isTopupModalInputSync = true;
            TopupMinutesTextBox.Text = string.Empty;
            _isTopupModalInputSync = false;
        }

        _topupModalAmount = customAmount;
        _topupModalHistory.Clear();
        TopupModalErrorTextBlock.Text = string.Empty;
        ResetMemberModalTimer();
        UpdateTopupModalUi();
    }

    private void ApplyTopupAmountFromMinutes(bool showValidationMessage = true)
    {
        if (_topupModalTcs is null)
        {
            return;
        }

        var rawMinutes = TopupMinutesTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawMinutes))
        {
            _topupModalAmount = 0m;
            _topupModalHistory.Clear();
            TopupModalErrorTextBlock.Text = string.Empty;

            _isTopupModalInputSync = true;
            TopupCustomAmountTextBox.Text = string.Empty;
            _isTopupModalInputSync = false;

            UpdateTopupModalUi();
            return;
        }

        if (!TryParseNonNegativeDouble(rawMinutes, out var minutes) || minutes <= 0)
        {
            if (showValidationMessage)
            {
                TopupModalErrorTextBlock.Text = "Số phút nhập không hợp lệ.";
            }
            return;
        }

        if (_topupModalRatePerHour <= 0)
        {
            if (showValidationMessage)
            {
                TopupModalErrorTextBlock.Text = "Không xác định được đơn giá giờ để quy đổi.";
            }
            return;
        }

        var convertedAmount = ConvertMinutesToTopupAmount((decimal)minutes, _topupModalRatePerHour);

        _topupModalAmount = convertedAmount;
        _topupModalHistory.Clear();
        _isTopupModalInputSync = true;
        TopupCustomAmountTextBox.Text = convertedAmount.ToString("0", CultureInfo.InvariantCulture);
        _isTopupModalInputSync = false;
        TopupModalErrorTextBlock.Text = string.Empty;
        ResetMemberModalTimer();
        UpdateTopupModalUi();
    }

    private decimal ResolveTopupRatePerHour(decimal? preferredRatePerHour)
    {
        if (preferredRatePerHour.HasValue && preferredRatePerHour.Value > 0)
        {
            return preferredRatePerHour.Value;
        }



        if (_pricingSettings?.DefaultRatePerHour > 0)
        {
            return _pricingSettings.DefaultRatePerHour;
        }

        return DefaultTopupRatePerHour;
    }

    private static decimal ConvertMinutesToTopupAmount(decimal minutes, decimal ratePerHour)
    {
        if (minutes <= 0 || ratePerHour <= 0)
        {
            return 0;
        }

        var rawAmount = (minutes / 60m) * ratePerHour;
        return Math.Max(1m, Math.Ceiling(rawAmount));
    }

    private void UpdateTopupMinutesRateHint()
    {
        var ratePerMinute = _topupModalRatePerHour / 60m;
        TopupMinutesRateHintTextBlock.Text =
            $"Đơn giá quy đổi: {_topupModalRatePerHour:N0} VND/giờ (~{ratePerMinute:N2} VND/phút).";
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


    private async Task GiftMemberAsync()
    {
        const string actionLabel = "Tặng tiền miễn phí";

        if (string.IsNullOrWhiteSpace(_selectedMemberId))
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedMember =
            MembersDataGrid.SelectedItem as MemberRow ??
            _memberRows.FirstOrDefault(x => string.Equals(x.Id, _selectedMemberId, StringComparison.OrdinalIgnoreCase));

        if (selectedMember is null)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var amount = await ShowTopupModalAsync(
            selectedMember,
            title: "T\u1eb7ng ti\u1ec1n mi\u1ec5n ph\u00ed",
            memberPrompt: $"H\u1ed9i vi\u00ean: {selectedMember.Username} - ch\u1ecdn s\u1ed1 ti\u1ec1n t\u1eb7ng:",
            allowDeduct: false);

        if (!amount.HasValue || amount.Value <= 0)
        {
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/members/{_selectedMemberId}/adjust"),
            new
            {
                amountDelta = Convert.ToDouble(amount.Value),
                createdBy = "admin.desktop",
                note = "Tang tien mien phi"
            });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show(
                $"{actionLabel} thất bại ({(int)response.StatusCode})",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] {actionLabel} {amount:N0} cho hội viên {_selectedMemberId}");
        InvalidateMembersCache();
        await RefreshMembersAsync(forceRefresh: true);
    }

    private async Task BuyHoursForMemberAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedMemberId))
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedMember =
            MembersDataGrid.SelectedItem as MemberRow ??
            _memberRows.FirstOrDefault(x => string.Equals(x.Id, _selectedMemberId, StringComparison.OrdinalIgnoreCase));

        if (selectedMember is null)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ratePerHour = ResolveTopupRatePerHour(null);
        var amount = await ShowTopupModalAsync(
            selectedMember,
            title: "Mua giờ hội viên",
            memberPrompt: $"Hội viên: {selectedMember.Username} - nhập số tiền mua giờ:",
            allowDeduct: false,
            ratePerHour: ratePerHour);

        if (!amount.HasValue || amount.Value <= 0)
        {
            return;
        }

        var boughtHours = Math.Round(amount.Value / ratePerHour, 2, MidpointRounding.AwayFromZero);
        if (boughtHours < 0.5m)
        {
            MessageBox.Show(
                "Số tiền quá nhỏ để mua giờ (tối thiểu 0.5 giờ).",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/members/{_selectedMemberId}/buy-hours"),
            new
            {
                hours = Convert.ToDouble(boughtHours),
                ratePerHour = Convert.ToDouble(ratePerHour),
                note = "Mua giờ tại admin",
                createdBy = "admin.desktop"
            });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show(
                $"Mua giờ thất bại ({(int)response.StatusCode})",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Mua giờ {boughtHours:0.##}h cho hội viên {_selectedMemberId}");
        InvalidateMembersCache();
        await RefreshMembersAsync(forceRefresh: true);
    }

    private async Task RefundMemberAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedMemberId))
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedMember =
            MembersDataGrid.SelectedItem as MemberRow ??
            _memberRows.FirstOrDefault(x => string.Equals(x.Id, _selectedMemberId, StringComparison.OrdinalIgnoreCase));

        if (selectedMember is null)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var amount = await ShowTopupModalAsync(
            selectedMember,
            title: "Tiền trả lại",
            memberPrompt: $"Hội viên: {selectedMember.Username} - nhập số tiền cần trả lại:",
            allowDeduct: false);

        if (!amount.HasValue || amount.Value <= 0)
        {
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/members/{_selectedMemberId}/withdraw"),
            new
            {
                amount = Convert.ToDouble(amount.Value),
                note = "Tra lai tai quan",
                createdBy = "admin.desktop"
            });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show(
                $"Trả lại tiền thất bại ({(int)response.StatusCode})",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Trả lại {amount.Value:N0} cho hội viên {_selectedMemberId}");
        InvalidateMembersCache();
        await RefreshMembersAsync(forceRefresh: true);
    }

    private async Task DeleteMemberAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedMemberId))
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedMember =
            MembersDataGrid.SelectedItem as MemberRow ??
            _memberRows.FirstOrDefault(x => string.Equals(x.Id, _selectedMemberId, StringComparison.OrdinalIgnoreCase));

        if (selectedMember is null)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selectedMember.MemberType == "VIP")
        {
            MessageBox.Show("Không thể xóa hội viên VIP.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (selectedMember.BalanceRaw >= 1000m)
        {
            MessageBox.Show("Chỉ được xóa hội viên có số dư dưới 1,000 VND.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa hội viên '{selectedMember.Username}' không?",
            "Xác nhận xóa hội viên",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            using var response = await _httpClient.DeleteAsync(BuildApiUrl($"/members/{selectedMember.Id}"));
            if (response.IsSuccessStatusCode)
            {
                AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã xóa hội viên {selectedMember.Username} ({selectedMember.Id})");
                InvalidateMembersCache();
                await RefreshMembersAsync(forceRefresh: true);
                MessageBox.Show($"Đã xóa hội viên '{selectedMember.Username}' thành công.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                var message = "Xóa hội viên thất bại.";
                if (!string.IsNullOrWhiteSpace(errorText))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(errorText);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("message", out var msgProp))
                        {
                            if (msgProp.ValueKind == JsonValueKind.Array)
                            {
                                message = string.Join("\n", msgProp.EnumerateArray().Select(x => x.GetString()));
                            }
                            else
                            {
                                message = msgProp.GetString() ?? message;
                            }
                        }
                    }
                    catch
                    {
                        message = errorText;
                    }
                }
                MessageBox.Show(message, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Đã xảy ra lỗi khi xóa hội viên: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowAddMemberModal()
    {
        MemberUsernameTextBox.Text = string.Empty;
        MemberPasswordBox.Password = string.Empty;
        MemberPhoneTextBox.Text = string.Empty;
        MemberIdentityTextBox.Text = string.Empty;
        MemberModalErrorTextBlock.Text = string.Empty;
        MemberTypeComboBox.SelectedIndex = 0;
        ResetMemberModalTimer();
        MemberModalOverlay.Visibility = Visibility.Visible;
        MemberUsernameTextBox.Focus();
    }

    private void HideAddMemberModal()
    {
        StopMemberModalTimer();
        MemberModalOverlay.Visibility = Visibility.Collapsed;
    }

    private async void RefreshMembersButton_Click(object sender, RoutedEventArgs e) => await RefreshMembersAsync(forceRefresh: true);

    private void AddMemberButton_Click(object sender, RoutedEventArgs e) => ShowAddMemberModal();

    private async void TopupMemberButton_Click(object sender, RoutedEventArgs e) => await TopupMemberAsync();
    private async void ViewMemberTransactionsButton_Click(object sender, RoutedEventArgs e)
        => await OpenSelectedMemberTransactionsDialogAsync();

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

    private async void MembersAutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsMembersTabActive() || _isRefreshingMembersAuto)
        {
            return;
        }

        _isRefreshingMembersAuto = true;
        try
        {
            await RefreshMembersAsync(forceRefresh: true);
        }
        finally
        {
            _isRefreshingMembersAuto = false;
        }
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
    private async void ContextBuyHoursMenuItem_Click(object sender, RoutedEventArgs e) => await BuyHoursForMemberAsync();
    private async void ContextGiftMemberMenuItem_Click(object sender, RoutedEventArgs e) => await GiftMemberAsync();
    private async void ContextTransferMemberMenuItem_Click(object sender, RoutedEventArgs e) => await TransferMemberBalanceAsync();
    private async void ContextRefundMemberMenuItem_Click(object sender, RoutedEventArgs e) => await RefundMemberAsync();
    private async void ContextDeleteMemberMenuItem_Click(object sender, RoutedEventArgs e) => await DeleteMemberAsync();
    private async void ContextMemberTransactionsMenuItem_Click(object sender, RoutedEventArgs e)
        => await OpenSelectedMemberTransactionsDialogAsync();

    private async Task OpenSelectedMemberTransactionsDialogAsync()
    {
        var selectedMember =
            MembersDataGrid.SelectedItem as MemberRow ??
            _memberRows.FirstOrDefault(x => string.Equals(x.Id, _selectedMemberId, StringComparison.OrdinalIgnoreCase));

        if (selectedMember is null)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await OpenMemberTransactionsDialogAsync(selectedMember);
    }

    private async void ContextMemberUsageLogsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedMember =
            MembersDataGrid.SelectedItem as MemberRow ??
            _memberRows.FirstOrDefault(x => string.Equals(x.Id, _selectedMemberId, StringComparison.OrdinalIgnoreCase));

        if (selectedMember is null)
        {
            MessageBox.Show(I18n.PleaseSelectMember, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await OpenMemberUsageLogsDialogAsync(selectedMember);
    }

    private async void CreateMemberConfirmButton_Click(object sender, RoutedEventArgs e) => await CreateMemberAsync();

    private void CancelMemberModalButton_Click(object sender, RoutedEventArgs e) => HideAddMemberModal();

    private async Task OpenMemberTransactionsDialogAsync(MemberRow member)
    {
        var startDate = DateTime.Now.AddMonths(-1).Date;
        var endDate = DateTime.Now.Date;

        var dialog = new Window
        {
            Title = $"Nhật ký giao dịch - {member.Username}",
            Width = 920,
            Height = 620,
            MinWidth = 760,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)), // Slate 50
        };

        // Add 2-minute auto-close timer to this window
        var autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        autoCloseTimer.Tick += (s, e) => dialog.Close();
        dialog.PreviewMouseMove += (s, e) => { autoCloseTimer.Stop(); autoCloseTimer.Start(); };
        dialog.PreviewKeyDown += (s, e) => { autoCloseTimer.Stop(); autoCloseTimer.Start(); };
        autoCloseTimer.Start();

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Slate 900
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(headerBorder, 0);

        var headerTextBlock = new TextBlock
        {
            Text = $"Hội viên: {member.Username} | Số dư: {member.BalanceRaw:N0} VND | Giờ chơi: {member.PlayHoursRaw:0.##} | Điểm: {member.AvailablePoints}",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
        };
        headerBorder.Child = headerTextBlock;
        root.Children.Add(headerBorder);

        var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(filterPanel, 1);
        root.Children.Add(filterPanel);

        var startDatePicker = new DatePicker { SelectedDate = startDate, Width = 120, Margin = new Thickness(0, 0, 12, 0) };
        var endDatePicker = new DatePicker { SelectedDate = endDate, Width = 120, Margin = new Thickness(0, 0, 12, 0) };
        var filterButton = new Button { Content = "Lọc", Width = 80, Height = 28 };

        filterPanel.Children.Add(new TextBlock { Text = "Từ ngày: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        filterPanel.Children.Add(startDatePicker);
        filterPanel.Children.Add(new TextBlock { Text = "Đến ngày: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        filterPanel.Children.Add(endDatePicker);
        filterPanel.Children.Add(filterButton);

        var summaryTextBlock = new TextBlock
        {
            Text = $"Tổng giao dịch: 0",
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), // Slate 500
            FontSize = 13,
            Margin = new Thickness(4, 0, 0, 8),
        };
        Grid.SetRow(summaryTextBlock, 2);
        root.Children.Add(summaryTextBlock);

        var transactionsGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)), // Slate 200
            RowHeaderWidth = 0,
            AlternationCount = 2,
            FontSize = 14,
            RowHeight = 38,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)), // Slate 300
            BorderThickness = new Thickness(1),
        };
        transactionsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Thời gian",
            Width = 170,
            Binding = new System.Windows.Data.Binding(nameof(MemberTransactionRow.CreatedAtText)),
        });
        transactionsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Loại",
            Width = 120,
            Binding = new System.Windows.Data.Binding(nameof(MemberTransactionRow.TypeText)),
        });
        transactionsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Tiền thay đổi",
            Width = 140,
            Binding = new System.Windows.Data.Binding(nameof(MemberTransactionRow.AmountDeltaText)),
        });
        transactionsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Phút thay đổi",
            Width = 120,
            Binding = new System.Windows.Data.Binding(nameof(MemberTransactionRow.PlayMinutesDeltaText)),
        });
        transactionsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Người tạo",
            Width = 130,
            Binding = new System.Windows.Data.Binding(nameof(MemberTransactionRow.CreatedBy)),
        });
        transactionsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ghi chú",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            Binding = new System.Windows.Data.Binding(nameof(MemberTransactionRow.Note)),
        });
        Grid.SetRow(transactionsGrid, 3);
        root.Children.Add(transactionsGrid);

        var closeButton = new Button
        {
            Content = "Đóng",
            Width = 110,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            Margin = new Thickness(0, 16, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // Slate 800
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0)
        };
        closeButton.Click += (_, _) => dialog.Close();
        Grid.SetRow(closeButton, 4);
        root.Children.Add(closeButton);

        async Task LoadDataAsync()
        {
            filterButton.IsEnabled = false;
            try
            {
                var sDate = startDatePicker.SelectedDate?.ToString("yyyy-MM-dd");
                var eDate = endDatePicker.SelectedDate?.ToString("yyyy-MM-dd");
                var response = await GetMemberTransactionsAsync(member.Id, sDate, eDate);
                if (response != null)
                {
                    var displayItems = PrepareMemberTransactionsForDisplay(
                        response.Items,
                        aggregateSessionUsage: !IsMemberCurrentlyOnline(member.Id));

                    var transactionRows = displayItems
                        .Select(ToMemberTransactionRow)
                        .ToList();

                    transactionsGrid.ItemsSource = transactionRows;
                    summaryTextBlock.Text = $"Tổng giao dịch: {transactionRows.Count}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể tải nhật ký giao dịch: {ex.Message}",
                    "Server Admin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                filterButton.IsEnabled = true;
            }
        }

        filterButton.Click += async (_, _) => await LoadDataAsync();

        dialog.Content = root;
        dialog.Loaded += async (_, _) => await LoadDataAsync();
        _ = dialog.ShowDialog();
    }

    private async Task OpenMemberUsageLogsDialogAsync(MemberRow member)
    {
        SystemEventsResponse? response;
        try
        {
            response = await GetSystemEventsAsync(500);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không thể tải nhật ký sử dụng máy: {ex.Message}",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (response is null)
        {
            MessageBox.Show(
                "Không tải được dữ liệu nhật ký sử dụng máy.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var events = response.Items
            .Select(item => TryMapMemberUsageEvent(item, member))
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x.Timestamp)
            .ToList();

        // Standardize Usage Log Aggregation Logic
        var usageRows = new List<MemberUsageLogRow>();
        var pendingSessions = new Dictionary<string, MemberUsageEvent>();

        foreach (var ev in events)
        {
            if (ev.IsActive)
            {
                // Login event
                if (pendingSessions.TryGetValue(ev.PcText, out var existingLogin))
                {
                    // If already have a pending login for this PC, close it as incomplete before starting new one
                    usageRows.Add(new MemberUsageLogRow
                    {
                        SortAt = existingLogin.Timestamp,
                        LoginTimeText = existingLogin.Timestamp.ToString("dd-MM-yyyy HH:mm:ss"),
                        LogoutTimeText = "-",
                        DurationText = "-",
                        PcText = existingLogin.PcText,
                        SourceText = existingLogin.Source,
                        Details = existingLogin.Details
                    });
                }
                pendingSessions[ev.PcText] = ev;
            }
            else
            {
                // Logout event
                if (pendingSessions.TryGetValue(ev.PcText, out var loginEvent))
                {
                    var duration = ev.Timestamp - loginEvent.Timestamp;
                    usageRows.Add(new MemberUsageLogRow
                    {
                        SortAt = loginEvent.Timestamp,
                        LoginTimeText = loginEvent.Timestamp.ToString("dd-MM-yyyy HH:mm:ss"),
                        LogoutTimeText = ev.Timestamp.ToString("dd-MM-yyyy HH:mm:ss"),
                        DurationText = FormatDuration(duration),
                        PcText = loginEvent.PcText,
                        SourceText = loginEvent.Source,
                        Details = loginEvent.Details
                    });
                    pendingSessions.Remove(ev.PcText);
                }
                else
                {
                    // Logout without matching login
                    usageRows.Add(new MemberUsageLogRow
                    {
                        SortAt = ev.Timestamp,
                        LoginTimeText = "-",
                        LogoutTimeText = ev.Timestamp.ToString("dd-MM-yyyy HH:mm:ss"),
                        DurationText = "-",
                        PcText = ev.PcText,
                        SourceText = ev.Source,
                        Details = ev.Details
                    });
                }
            }
        }

        // Handle remaining pending logins (currently using)
        foreach (var pending in pendingSessions.Values)
        {
            usageRows.Add(new MemberUsageLogRow
            {
                SortAt = pending.Timestamp,
                LoginTimeText = pending.Timestamp.ToString("dd-MM-yyyy HH:mm:ss"),
                LogoutTimeText = "\u0110ang s\u1eed d\u1ee5ng",
                DurationText = FormatDuration(DateTime.Now - pending.Timestamp),
                PcText = pending.PcText,
                SourceText = pending.Source,
                Details = pending.Details
            });
        }

        usageRows = usageRows.OrderByDescending(x => x.SortAt).ToList();

        var dialog = new Window
        {
            Title = $"Nh\u1eadt k\u00fd s\u1eed d\u1ee5ng m\u00e1y - {member.Username}",
            Width = 1080,
            Height = 650,
            MinWidth = 820,
            MinHeight = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)), // Slate 50
        };

        // Add 2-minute auto-close timer to this window
        var autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        autoCloseTimer.Tick += (s, e) => dialog.Close();
        dialog.PreviewMouseMove += (s, e) => { autoCloseTimer.Stop(); autoCloseTimer.Start(); };
        dialog.PreviewKeyDown += (s, e) => { autoCloseTimer.Stop(); autoCloseTimer.Start(); };
        autoCloseTimer.Start();

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Slate 900
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(headerBorder, 0);

        var headerTextBlock = new TextBlock
        {
            Text = $"H\u1ed9i vi\u00ean: {member.Username} | S\u1ed1 d\u01b0: {member.BalanceRaw:N0} VND | Gi\u1edd ch\u01a1i c\u00f2n: {member.PlayHoursRaw:0.##}",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
        };
        headerBorder.Child = headerTextBlock;
        root.Children.Add(headerBorder);

        var summaryTextBlock = new TextBlock
        {
            Text = $"T\u1ed5ng phi\u00ean s\u1eed d\u1ee5ng: {usageRows.Count} (ph\u00e2n t\u00edch t\u1eeb 500 s\u1ef1 ki\u1ec7n g\u1ea7n nh\u1ea5t)",
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), // Slate 500
            FontSize = 13,
            Margin = new Thickness(4, 0, 0, 8),
        };
        Grid.SetRow(summaryTextBlock, 1);
        root.Children.Add(summaryTextBlock);

        var usageGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)), // Slate 200
            RowHeaderWidth = 0,
            AlternationCount = 2,
            ItemsSource = usageRows,
            FontSize = 14,
            RowHeight = 38,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)), // Slate 300
            BorderThickness = new Thickness(1),
        };
        
        usageGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "\u0110\u0103ng nh\u1eadp",
            Width = 180,
            Binding = new System.Windows.Data.Binding(nameof(MemberUsageLogRow.LoginTimeText)),
        });
        usageGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "\u0110\u0103ng xu\u1ea5t",
            Width = 180,
            Binding = new System.Windows.Data.Binding(nameof(MemberUsageLogRow.LogoutTimeText)),
        });
        usageGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Th\u1eddi gian ch\u01a1i",
            Width = 130,
            Binding = new System.Windows.Data.Binding(nameof(MemberUsageLogRow.DurationText)),
        });
        usageGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "M\u00e1y",
            Width = 180,
            Binding = new System.Windows.Data.Binding(nameof(MemberUsageLogRow.PcText)),
        });
        usageGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Chi ti\u1ebft",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            Binding = new System.Windows.Data.Binding(nameof(MemberUsageLogRow.Details)),
        });
        Grid.SetRow(usageGrid, 2);
        root.Children.Add(usageGrid);

        var closeButton = new Button
        {
            Content = "\u0110\u00f3ng",
            Width = 110,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            Margin = new Thickness(0, 16, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // Slate 800
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0)
        };
        closeButton.Click += (_, _) => dialog.Close();
        Grid.SetRow(closeButton, 3);
        root.Children.Add(closeButton);

        dialog.Content = root;
        _ = dialog.ShowDialog();
    }

    private static MemberUsageEvent? TryMapMemberUsageEvent(SystemEventItem item, MemberRow member)
    {
        if (!string.Equals(item.EventType, "member.pc.presence", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (item.Payload is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var payloadMemberId = ReadUsageJsonString(json, "memberId");
        var payloadUsername = ReadUsageJsonString(json, "username");
        var matchedByMemberId = !string.IsNullOrWhiteSpace(payloadMemberId) &&
            string.Equals(payloadMemberId, member.Id, StringComparison.OrdinalIgnoreCase);
        var matchedByUsername = !string.IsNullOrWhiteSpace(payloadUsername) &&
            string.Equals(payloadUsername, member.Username, StringComparison.OrdinalIgnoreCase);
        if (!matchedByMemberId && !matchedByUsername)
        {
            return null;
        }

        var fullName = ReadUsageJsonString(json, "fullName");
        var transferredFromPcId = ReadUsageJsonString(json, "transferredFromPcId");
        var transferredToPcId = ReadUsageJsonString(json, "transferredToPcId");
        var isActive = ReadUsageJsonBool(json, "isActive");

        var detailsParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            detailsParts.Add($"T\u00ean \u0111\u1ea7y \u0111\u1ee7: {fullName}");
        }
        if (!string.IsNullOrWhiteSpace(transferredFromPcId))
        {
            detailsParts.Add($"Chuy\u1ec3n t\u1eeb m\u00e1y ID: {transferredFromPcId}");
        }
        if (!string.IsNullOrWhiteSpace(transferredToPcId))
        {
            detailsParts.Add($"Chuy\u1ec3n sang m\u00e1y ID: {transferredToPcId}");
        }

        var details = detailsParts.Count == 0 ? "-" : string.Join(" | ", detailsParts);

        return new MemberUsageEvent
        {
            Timestamp = ParseDateLocal(item.CreatedAt) ?? DateTime.MinValue,
            IsActive = isActive,
            PcText = BuildMemberUsagePcText(item),
            Source = string.IsNullOrWhiteSpace(item.Source) ? "-" : item.Source,
            Details = details,
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 0) return "0s";
        
        var parts = new List<string>();
        if (duration.Days > 0) parts.Add($"{duration.Days}d");
        if (duration.Hours > 0) parts.Add($"{duration.Hours}h");
        if (duration.Minutes > 0) parts.Add($"{duration.Minutes}m");
        if (duration.Seconds > 0 || parts.Count == 0) parts.Add($"{duration.Seconds}s");
        
        return string.Join(" ", parts);
    }

    private static string FormatUsageDuration(int totalSeconds)
    {
        var safeSeconds = Math.Max(0, totalSeconds);
        var span = TimeSpan.FromSeconds(safeSeconds);
        var totalHours = (int)span.TotalHours;
        var minutes = span.Minutes;
        var seconds = span.Seconds;

        if (totalHours > 0)
        {
            if (minutes > 0)
            {
                return $"{totalHours} gi\u1edd {minutes} ph\u00fat";
            }

            return $"{totalHours} gi\u1edd";
        }

        if (minutes > 0)
        {
            if (seconds > 0)
            {
                return $"{minutes} ph\u00fat {seconds} gi\u00e2y";
            }

            return $"{minutes} ph\u00fat";
        }

        return $"{seconds} gi\u00e2y";
    }

    private static string BuildMemberUsagePcText(SystemEventItem item)
    {
        if (string.IsNullOrWhiteSpace(item.PcName) && string.IsNullOrWhiteSpace(item.AgentId))
        {
            return "-";
        }

        if (string.IsNullOrWhiteSpace(item.AgentId))
        {
            return item.PcName ?? "-";
        }

        if (string.IsNullOrWhiteSpace(item.PcName))
        {
            return item.AgentId ?? "-";
        }

        return $"{item.PcName} ({item.AgentId})";
    }

    private static string ReadUsageJsonString(JsonElement json, string key)
    {
        if (!json.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return prop.GetString()?.Trim() ?? string.Empty;
    }

    private static bool ReadUsageJsonBool(JsonElement json, string key)
    {
        if (!json.TryGetProperty(key, out var prop))
        {
            return false;
        }

        if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
        {
            return prop.GetBoolean();
        }

        if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out var parsed))
        {
            return parsed;
        }

        return false;
    }

    private async Task OpenEditMemberDialogAsync(MemberRow member)
    {
        var lifetimeTopup = member.TotalTopupRaw;
        MemberUsageSummaryResponse? usageSummary = null;
        try
        {
            usageSummary = await GetMemberUsageSummaryAsync(member.Id);
        }
        catch
        {
            // Keep dialog usable even if usage summary API is temporarily unavailable.
        }

        var lastLoginText = FormatDateTime(usageSummary?.LastLoginAt);
        if (lastLoginText == "-")
        {
            lastLoginText = "Ch\u01b0a c\u00f3";
        }

        var loginMachineParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(usageSummary?.LastLoginPcName))
        {
            loginMachineParts.Add(usageSummary.LastLoginPcName.Trim());
        }
        if (!string.IsNullOrWhiteSpace(usageSummary?.LastLoginAgentId))
        {
            loginMachineParts.Add(usageSummary.LastLoginAgentId.Trim());
        }
        if (loginMachineParts.Count > 0)
        {
            lastLoginText = $"{lastLoginText} ({string.Join(" - ", loginMachineParts)})";
        }

        var totalUsageSeconds = Math.Max(0, usageSummary?.TotalUsageSeconds ?? 0);
        var totalUsageText = usageSummary is null
            ? "-"
            : $"{FormatUsageDuration(totalUsageSeconds)} ({(totalUsageSeconds / 3600d):0.##} gi\u1edd)";

        var dialog = new Window
        {
            Title = $"Th\u00f4ng tin h\u1ed9i vi\u00ean - {member.Username}",
            Width = 780,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
            Owner = this,
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var formGrid = new Grid();
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
        {
            formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        static void AddField(Grid container, string label, UIElement editor, int row, int column)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(column == 0 ? 0 : 10, row == 0 ? 0 : 10, column == 0 ? 10 : 0, 0),
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 4),
            });
            panel.Children.Add(editor);
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            container.Children.Add(panel);
        }

        var usernameBox = new TextBox
        {
            Text = member.Username,
            Height = 32,
            IsReadOnly = true,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var fullNameBox = new TextBox
        {
            Text = member.FullName,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var phoneBox = new TextBox
        {
            Text = member.Phone == "-" ? string.Empty : member.Phone,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var identityBox = new TextBox
        {
            Text = member.IdentityNumber == "-" ? string.Empty : member.IdentityNumber,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

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

        var lastLoginBox = new TextBox
        {
            Text = lastLoginText,
            Height = 32,
            IsReadOnly = true,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.WhiteSmoke,
        };

        var totalUsageBox = new TextBox
        {
            Text = totalUsageText,
            Height = 32,
            IsReadOnly = true,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.WhiteSmoke,
        };

        var pointsBox = new TextBox
        {
            Text = member.AvailablePoints.ToString(),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var totalTopupBox = new TextBox
        {
            Text = lifetimeTopup.ToString("0.##", CultureInfo.InvariantCulture),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var memberTypeComboBox = new ComboBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        memberTypeComboBox.Items.Add(new ComboBoxItem { Content = "Th\u01b0\u1eddng", Tag = "REGULAR" });
        memberTypeComboBox.Items.Add(new ComboBoxItem { Content = "VIP", Tag = "VIP" });

        if (string.Equals(member.MemberType, "VIP", StringComparison.OrdinalIgnoreCase))
        {
            memberTypeComboBox.SelectedIndex = 1;
        }
        else
        {
            memberTypeComboBox.SelectedIndex = 0;
        }

        AddField(formGrid, "Username", usernameBox, 0, 0);
        AddField(formGrid, "H\u1ecd t\u00ean", fullNameBox, 0, 1);
        AddField(formGrid, "S\u1ed1 \u0111i\u1ec7n tho\u1ea1i", phoneBox, 1, 0);
        AddField(formGrid, "CCCD/CMND", identityBox, 1, 1);
        AddField(formGrid, "S\u1ed1 d\u01b0 (VND)", balancePanel, 2, 0);
        AddField(formGrid, "\u0110i\u1ec3m t\u00edch l\u0169y", pointsBox, 2, 1);
        AddField(formGrid, "T\u1ed5ng n\u1ea1p (VND)", totalTopupBox, 3, 0);
        AddField(formGrid, "Lo\u1ea1i h\u1ed9i vi\u00ean", memberTypeComboBox, 3, 1);
        AddField(formGrid, "L\u1ea7n \u0111\u0103ng nh\u1eadp g\u1ea7n \u0111\u00e2y", lastLoginBox, 4, 0);
        AddField(formGrid, "T\u1ed5ng th\u1eddi gian s\u1eed d\u1ee5ng m\u00e1y", totalUsageBox, 4, 1);

        Grid.SetRow(formGrid, 0);
        root.Children.Add(formGrid);

        var statusCheckBox = new CheckBox
        {
            Content = "T\u00e0i kho\u1ea3n \u0111ang ho\u1ea1t \u0111\u1ed9ng",
            IsChecked = member.IsActive,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Grid.SetRow(statusCheckBox, 1);
        root.Children.Add(statusCheckBox);

        var passwordLabel = new TextBlock
        {
            Text = "\u0110\u1ed5i m\u1eadt kh\u1ea9u (\u0111\u1ec3 tr\u1ed1ng n\u1ebfu kh\u00f4ng \u0111\u1ed5i)",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(passwordLabel, 2);
        root.Children.Add(passwordLabel);

        var passwordBox = new PasswordBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(passwordBox, 3);
        root.Children.Add(passwordBox);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(errorTextBlock, 4);
        root.Children.Add(errorTextBlock);

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var saveButton = new Button
        {
            Content = "L\u01b0u",
            Width = 130,
            Height = 42,
            Margin = new Thickness(0, 0, 10, 0),
            IsDefault = true,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        var cancelButton = new Button
        {
            Content = "H\u1ee7y",
            Width = 130,
            Height = 42,
            IsCancel = true,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Style = TryFindResource("DangerButtonStyle") as Style,
        };
        actionsPanel.Children.Add(saveButton);
        actionsPanel.Children.Add(cancelButton);
        Grid.SetRow(actionsPanel, 5);
        root.Children.Add(actionsPanel);

        saveButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            if (!TryParseNonNegativeMoney(balanceBox.Text.Trim(), out var balance))
            {
                errorTextBlock.Text = "S\u1ed1 d\u01b0 kh\u00f4ng h\u1ee3p l\u1ec7.";
                return;
            }
            if (!int.TryParse(pointsBox.Text.Trim(), out var points) || points < 0)
            {
                errorTextBlock.Text = "\u0110i\u1ec3m t\u00edch l\u0169y kh\u00f4ng h\u1ee3p l\u1ec7.";
                return;
            }
            if (!TryParseNonNegativeMoney(totalTopupBox.Text.Trim(), out var totalTopup))
            {
                errorTextBlock.Text = "T\u1ed5ng n\u1ea1p kh\u00f4ng h\u1ee3p l\u1ec7.";
                return;
            }

            var fullName = fullNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            {
                errorTextBlock.Text = "H\u1ecd t\u00ean kh\u00f4ng \u0111\u01b0\u1ee3c \u0111\u1ec3 tr\u1ed1ng.";
                return;
            }

            var selectedMemberType = (memberTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "REGULAR";

            var payload = new Dictionary<string, object?>
            {
                ["fullName"] = fullName,
                ["phone"] = string.IsNullOrWhiteSpace(phoneBox.Text) ? null : phoneBox.Text.Trim(),
                ["identityNumber"] = string.IsNullOrWhiteSpace(identityBox.Text) ? null : identityBox.Text.Trim(),
                ["isActive"] = statusCheckBox.IsChecked == true,
                ["balance"] = Convert.ToDouble(balance),
                ["totalTopup"] = Convert.ToDouble(totalTopup),
                ["availablePoints"] = points,
                ["memberType"] = selectedMemberType,
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
                        ? $"C\u1eadp nh\u1eadt th\u1ea5t b\u1ea1i ({(int)response.StatusCode})"
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

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] \u0110\u00e3 c\u1eadp nh\u1eadt h\u1ed9i vi\u00ean {member.Username}");
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
            Style = TryFindResource("DangerButtonStyle") as Style,
        };
        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            Style = TryFindResource("DangerButtonStyle") as Style,
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

    private sealed class MemberUsageEvent
    {
        public DateTime Timestamp { get; init; }
        public bool IsActive { get; init; }
        public string PcText { get; init; } = "-";
        public string Source { get; init; } = "-";
        public string Details { get; init; } = "-";
    }

    private sealed class MemberUsageLogRow
    {
        public DateTime SortAt { get; set; } = DateTime.MinValue;
        public string LoginTimeText { get; set; } = "-";
        public string LogoutTimeText { get; set; } = "-";
        public string DurationText { get; set; } = "-";
        public string PcText { get; set; } = "-";
        public string SourceText { get; set; } = "-";
        public string Details { get; set; } = "-";
    }
}





