using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient = new();
    private readonly DispatcherTimer _healthTimer = new();
    private readonly DispatcherTimer _machinesTimer = new();

    private readonly ObservableCollection<MachineRow> _machineRows = new();
    private readonly ObservableCollection<MemberRow> _memberRows = new();
    private readonly ObservableCollection<MemberTransactionRow> _memberTransactionRows = new();
    private readonly ObservableCollection<SystemLogRow> _systemLogRows = new();
    private readonly ObservableCollection<SessionLogRow> _sessionLogRows = new();
    private readonly ObservableCollection<GroupSummaryRow> _groupSummaryRows = new();
    private readonly ObservableCollection<GroupMachineRow> _groupMachineRows = new();

    private readonly List<MachineRow> _allMachineRows = new();

    private AdminShellSettings _settings = new();
    private string _searchKeyword = string.Empty;
    private string _statusFilter = I18n.StatusAll;
    private string _memberSearchKeyword = string.Empty;
    private string? _selectedMemberId;
    private bool _fontSizeInitialized;
    private bool _machineTableFontSizeInitialized;
    private bool _machineContextMenuPaddingInitialized;
    private bool _machineContextMenuFontSizeInitialized;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = LoadSettings();
        ApplyUiFontSize(_settings.UiFontSize);
        ApplyMachineTableFontSize(_settings.MachineTableFontSize);
        ApplyMachineContextMenuPadding(_settings.MachineContextMenuItemPadding);
        ApplyMachineContextMenuFontSize(_settings.MachineContextMenuFontSize);

        MachinesDataGrid.ItemsSource = _machineRows;
        MembersDataGrid.ItemsSource = _memberRows;
        MemberTransactionsDataGrid.ItemsSource = _memberTransactionRows;
        SystemLogsDataGrid.ItemsSource = _systemLogRows;
        SessionLogsDataGrid.ItemsSource = _sessionLogRows;
        GroupSummaryDataGrid.ItemsSource = _groupSummaryRows;
        GroupMachinesDataGrid.ItemsSource = _groupMachineRows;
        FontSizeSlider.Value = _settings.UiFontSize;
        FontSizeValueTextBlock.Text = _settings.UiFontSize.ToString("0");
        MachineTableFontSizeSlider.Value = _settings.MachineTableFontSize;
        MachineTableFontSizeValueTextBlock.Text = _settings.MachineTableFontSize.ToString("0");
        MachineContextMenuPaddingSlider.Value = _settings.MachineContextMenuItemPadding;
        MachineContextMenuPaddingValueTextBlock.Text = _settings.MachineContextMenuItemPadding.ToString("0");
        MachineContextMenuFontSizeSlider.Value = _settings.MachineContextMenuFontSize;
        MachineContextMenuFontSizeValueTextBlock.Text = _settings.MachineContextMenuFontSize.ToString("0");
        _fontSizeInitialized = true;
        _machineTableFontSizeInitialized = true;
        _machineContextMenuPaddingInitialized = true;
        _machineContextMenuFontSizeInitialized = true;

        _healthTimer.Interval = TimeSpan.FromSeconds(5);
        _healthTimer.Tick += HealthTimer_Tick;
        _healthTimer.Start();

        _machinesTimer.Interval = TimeSpan.FromSeconds(Math.Max(2, _settings.MachineRefreshSeconds));
        _machinesTimer.Tick += MachinesTimer_Tick;
        _machinesTimer.Start();

        await CheckBackendHealthAsync();
        await RefreshAllDataAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _healthTimer.Stop();
        _machinesTimer.Stop();
        _httpClient.Dispose();
    }

    private async void MachinesTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshMachinesAsync();
    }

    private async Task RefreshAllDataAsync()
    {
        await RefreshMachinesAsync();
        await RefreshMembersAsync();
        await RefreshSystemLogsAsync();
        await RefreshTransactionLogsAsync();
        RefreshGroupsFromMachines();
    }

    private async Task RefreshMachinesAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<PcListResponse>(BuildApiUrl("/pcs"), JsonOptions());
            if (response is null)
            {
                return;
            }

            var allRows = response.Items.Select(ToMachineRow).OrderBy(r => r.Name).ToList();
            _allMachineRows.Clear();
            _allMachineRows.AddRange(allRows);

            var filteredRows = allRows;
            if (!string.IsNullOrWhiteSpace(_searchKeyword))
            {
                filteredRows = filteredRows
                    .Where(r => r.Name.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase) ||
                                r.AgentId.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            filteredRows = ApplyStatusFilter(filteredRows);

            _machineRows.Clear();
            foreach (var row in filteredRows)
            {
                _machineRows.Add(row);
            }

            UpdateMachineSummary(filteredRows);
            RefreshGroupsFromMachines();
            LastSyncTextBlock.Text = $"{I18n.SyncPrefix}: {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            // Keep UI alive when API is temporarily unavailable.
        }
    }

    private static MachineRow ToMachineRow(PcListItem item)
    {
        var now = DateTime.Now;
        var statusText = item.Status switch
        {
            "IN_USE" => I18n.StatusInUse,
            "LOCKED" => I18n.StatusLocked,
            "ONLINE" => I18n.StatusReady,
            _ => "Offline",
        };

        var statusBrush = item.Status switch
        {
            "IN_USE" => Brushes.RoyalBlue,
            "LOCKED" => Brushes.Red,
            "ONLINE" => Brushes.DarkSlateBlue,
            _ => Brushes.Gray,
        };

        var startedAt = ParseDateLocal(item.ActiveSession?.StartedAt);
        var usedText = item.ActiveSession is null ? "-" : FormatUsed(item.ActiveSession.ElapsedSeconds);

        return new MachineRow
        {
            Id = item.Id,
            AgentId = item.AgentId,
            Name = item.Name,
            IpAddress = item.IpAddress ?? "-",
            LastSeenAtText = FormatDateTime(item.LastSeenAt),
            StatusText = statusText,
            StatusBrush = statusBrush,
            UserName = "-",
            StartedAtText = startedAt?.ToString("HH:mm:ss") ?? "-",
            UsedText = usedText,
            RemainingText = "-",
            MoneyText = item.ActiveSession is null ? "-" : item.ActiveSession.EstimatedAmount.ToString("N0"),
            DateText = now.ToString("dd-MM-yyyy"),
            VersionText = "0.1.0",
            GroupName = "Mặc định",
            StatusCode = item.Status,
        };
    }

    private List<MachineRow> ApplyStatusFilter(List<MachineRow> rows)
    {
        return _statusFilter switch
        {
            "Đang sử dụng" => rows.Where(r => r.StatusCode == "IN_USE").ToList(),
            "Đang tắt" => rows.Where(r => r.StatusCode == "LOCKED").ToList(),
            "Sẵn sàng" => rows.Where(r => r.StatusCode == "ONLINE").ToList(),
            "Offline" => rows.Where(r => r.StatusCode is not "IN_USE" and not "LOCKED" and not "ONLINE").ToList(),
            _ => rows,
        };
    }

    private void UpdateMachineSummary(IReadOnlyCollection<MachineRow> rows)
    {
        var total = rows.Count;
        var usingCount = rows.Count(r => r.StatusCode == "IN_USE");
        var lockedCount = rows.Count(r => r.StatusCode == "LOCKED");
        var runningMoney = rows.Sum(r => ParseMoney(r.MoneyText));

        SummaryTotalTextBlock.Text = $"{I18n.TotalPcPrefix}: {total}";
        SummaryUsingTextBlock.Text = $"{I18n.InUsePcPrefix}: {usingCount}";
        SummaryLockedTextBlock.Text = $"{I18n.LockedPcPrefix}: {lockedCount}";
        SummaryMoneyTextBlock.Text = $"{I18n.TempMoneyPrefix}: {runningMoney:N0}";
    }

    private async Task SendCommandAsync(string action)
    {
        if (MachinesDataGrid.SelectedItem is not MachineRow selected)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/pcs/{selected.Id}/{action}"),
                new { requestedBy = "admin.desktop" });

            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"{I18n.CommandFailed} ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã gửi lệnh {action.ToUpperInvariant()} cho {selected.Name}");
            await RefreshMachinesAsync();
            await RefreshTransactionLogsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{I18n.CommandError}: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshMembersAsync()
    {
        try
        {
            var url = BuildApiUrl("/members");
            if (!string.IsNullOrWhiteSpace(_memberSearchKeyword))
            {
                url += $"?search={Uri.EscapeDataString(_memberSearchKeyword)}";
            }

            var response = await _httpClient.GetFromJsonAsync<MemberListResponse>(url, JsonOptions());
            if (response is null)
            {
                return;
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

            if (!string.IsNullOrWhiteSpace(_selectedMemberId))
            {
                var current = mapped.FirstOrDefault(x => x.Id == _selectedMemberId);
                if (current is null && mapped.Count > 0)
                {
                    _selectedMemberId = mapped[0].Id;
                }
            }
            else if (mapped.Count > 0)
            {
                _selectedMemberId = mapped[0].Id;
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
            BalanceText = item.Balance.ToString("N0", CultureInfo.InvariantCulture),
            PlayHoursText = item.PlayHours.ToString("0.##", CultureInfo.InvariantCulture),
            PasswordState = item.HasPassword ? "Đã đặt" : "Chưa đặt",
            ActiveText = item.IsActive ? "Hoạt động" : "Tạm khóa",
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
                $"{I18n.MemberSelectedPrefix}: {response.Member.Username} - {I18n.MemberBalancePrefix} {response.Member.Balance:N0} - {I18n.MemberPlayHoursPrefix} {response.Member.PlayHours:0.##}";
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
            "TOPUP" => "Nạp tiền",
            "BUY_PLAYTIME" => "Mua giờ",
            _ => "Điều chỉnh",
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
        var password = MemberPasswordBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            MemberModalErrorTextBlock.Text = I18n.MemberUsernameRequired;
            return;
        }

        if (password.Length < 6)
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
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã tạo hội viên {username}");
            await RefreshMembersAsync();
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

        if (!decimal.TryParse(TopupAmountTextBox.Text.Trim(), out var amount) || amount <= 0)
        {
            MessageBox.Show(I18n.InvalidTopupAmount, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/members/{_selectedMemberId}/topups"),
            new { amount = Convert.ToDouble(amount), createdBy = "admin.desktop" });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show($"Nạp tiền thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Nạp tiền {amount:N0} cho hội viên {_selectedMemberId}");
        await RefreshMembersAsync();
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
            MessageBox.Show($"Mua giờ thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Mua {hours:0.##} giờ cho hội viên {_selectedMemberId}");
        await RefreshMembersAsync();
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
        await RefreshMembersAsync();
    }

    private async Task RefreshSystemLogsAsync()
    {
        try
        {
            var limit = GetSelectedSystemLogLimit();
            var response = await _httpClient.GetFromJsonAsync<SystemEventsResponse>(
                BuildApiUrl($"/reports/events/system?limit={limit}"),
                JsonOptions());

            if (response is null)
            {
                return;
            }

            _systemLogRows.Clear();
            foreach (var item in response.Items.Select(ToSystemLogRow))
            {
                _systemLogRows.Add(item);
            }

            SystemLogInfoTextBlock.Text = $"{I18n.LogsCountPrefix}: {response.Total} - {I18n.UpdatedAtPrefix} {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            // Ignore temporary errors.
        }
    }

    private static SystemLogRow ToSystemLogRow(SystemEventItem item)
    {
        var payloadText = "-";
        if (item.Payload is not null)
        {
            var json = JsonSerializer.Serialize(item.Payload);
            payloadText = json.Length > 220 ? json[..220] + "..." : json;
        }

        var pcText = item.PcName is null
            ? "-"
            : string.IsNullOrWhiteSpace(item.AgentId)
                ? item.PcName
                : $"{item.PcName} ({item.AgentId})";

        return new SystemLogRow
        {
            CreatedAtText = FormatDateTime(item.CreatedAt),
            Source = item.Source,
            EventType = item.EventType,
            PcText = pcText,
            PayloadText = payloadText,
        };
    }

    private async Task RefreshTransactionLogsAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<SessionsListResponse>(
                BuildApiUrl("/sessions?status=CLOSED"),
                JsonOptions());

            if (response is null)
            {
                return;
            }

            var rows = response.Items.Select(ToSessionLogRow).ToList();
            _sessionLogRows.Clear();
            foreach (var row in rows)
            {
                _sessionLogRows.Add(row);
            }

            var total = rows.Sum(x => x.AmountRaw);
            TransactionSummaryTextBlock.Text = $"{I18n.TotalRevenuePrefix}: {total:N0} VND - {I18n.SessionCountPrefix}: {rows.Count}";
            TransactionsInfoTextBlock.Text = $"{I18n.UpdatedAtPrefix} lúc {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            // Ignore temporary errors.
        }
    }

    private static SessionLogRow ToSessionLogRow(SessionItem item)
    {
        return new SessionLogRow
        {
            PcName = item.PcName,
            AgentId = item.AgentId,
            StartedAtText = FormatDateTime(item.StartedAt),
            EndedAtText = FormatDateTime(item.EndedAt),
            BillableMinutesText = item.BillableMinutes?.ToString() ?? "-",
            AmountRaw = item.Amount ?? 0,
            AmountText = (item.Amount ?? 0).ToString("N0", CultureInfo.InvariantCulture),
            Status = item.Status,
            ClosedReason = string.IsNullOrWhiteSpace(item.ClosedReason) ? "-" : item.ClosedReason,
        };
    }

    private void RefreshGroupsFromMachines()
    {
        var groups = _allMachineRows
            .GroupBy(x => x.GroupName)
            .OrderBy(x => x.Key)
            .Select(group => new GroupSummaryRow
            {
                GroupName = group.Key,
                Total = group.Count(),
                InUse = group.Count(x => x.StatusCode == "IN_USE"),
                Locked = group.Count(x => x.StatusCode == "LOCKED"),
                Online = group.Count(x => x.StatusCode == "ONLINE"),
                Offline = group.Count(x => x.StatusCode is not "IN_USE" and not "LOCKED" and not "ONLINE"),
            })
            .ToList();

        _groupSummaryRows.Clear();
        foreach (var item in groups)
        {
            _groupSummaryRows.Add(item);
        }

        _groupMachineRows.Clear();
        foreach (var pc in _allMachineRows.OrderBy(x => x.GroupName).ThenBy(x => x.Name))
        {
            _groupMachineRows.Add(new GroupMachineRow
            {
                GroupName = pc.GroupName,
                MachineName = pc.Name,
                AgentId = pc.AgentId,
                StatusText = pc.StatusText,
                IpAddress = pc.IpAddress,
                LastSeenAtText = pc.LastSeenAtText,
            });
        }

        GroupInfoTextBlock.Text = $"{I18n.GroupCountPrefix}: {groups.Count} - {I18n.TotalMachinePrefix}: {_allMachineRows.Count}";
    }

    private async Task CheckBackendHealthAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(BuildApiUrl("/pcs"));
            if (response.IsSuccessStatusCode)
            {
                SetHealthStatus(I18n.BackendOnline, "#16a34a");
                return;
            }

            SetHealthStatus($"Backend: {((int)response.StatusCode)}", "#ef4444");
        }
        catch
        {
            SetHealthStatus(I18n.BackendOffline, "#ef4444");
        }
    }

    private void SetHealthStatus(string text, string colorHex)
    {
        HealthTextBlock.Text = text;
        HealthIndicator.Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private string BuildApiUrl(string path)
    {
        var baseUri = new Uri(_settings.BackendApiBaseUrl.TrimEnd('/') + "/");
        return new Uri(baseUri, path.TrimStart('/')).ToString();
    }

    private string BuildPageUrl(string path)
    {
        var baseUri = new Uri(_settings.AdminBaseUrl.TrimEnd('/') + "/");
        return new Uri(baseUri, path.TrimStart('/')).ToString();
    }

    private static AdminShellSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path))
            {
                return new AdminShellSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AdminShellSettings>(json, JsonOptions()) ?? new AdminShellSettings();
        }
        catch
        {
            return new AdminShellSettings();
        }
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            var outputSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            File.WriteAllText(outputSettingsPath, json);

            var projectSettingsPath = Path.Combine(Environment.CurrentDirectory, "appsettings.json");
            if (!string.Equals(outputSettingsPath, projectSettingsPath, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(projectSettingsPath, json);
            }
        }
        catch
        {
            // Ignore save failures to keep app responsive.
        }
    }

    private static DateTime? ParseDateLocal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, out var parsed) ? parsed.ToLocalTime() : null;
    }

    private static string FormatDateTime(string? value)
    {
        var parsed = ParseDateLocal(value);
        return parsed?.ToString("dd-MM-yyyy HH:mm:ss") ?? "-";
    }

    private static string FormatUsed(int elapsedSeconds)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(elapsedSeconds / 60.0));
        if (minutes >= 60)
        {
            var hours = minutes / 60;
            var remain = minutes % 60;
            return remain == 0 ? $"{hours} giờ" : $"{hours} giờ {remain} phút";
        }

        return $"{minutes} phút";
    }

    private static decimal ParseMoney(string moneyText)
    {
        return decimal.TryParse(moneyText, out var value) ? value : 0m;
    }

    private static double ClampFontSize(double value)
    {
        return Math.Clamp(value, 10, 22);
    }

    private static double ClampMachineTableFontSize(double value)
    {
        return Math.Clamp(value, 11, 30);
    }

    private void ApplyUiFontSize(double value)
    {
        var clamped = ClampFontSize(value);
        FontSize = clamped;
        _settings.UiFontSize = clamped;
        if (FontSizeValueTextBlock is not null)
        {
            FontSizeValueTextBlock.Text = clamped.ToString("0");
        }
    }

    private void ApplyMachineTableFontSize(double value)
    {
        var clamped = ClampMachineTableFontSize(value);
        MachinesDataGrid.FontSize = clamped;
        MachinesDataGrid.RowHeight = Math.Max(24, clamped * 2.1);
        _settings.MachineTableFontSize = clamped;
        if (MachineTableFontSizeValueTextBlock is not null)
        {
            MachineTableFontSizeValueTextBlock.Text = clamped.ToString("0");
        }
    }

    private static double ClampMachineContextMenuPadding(double value)
    {
        return Math.Clamp(value, 6, 24);
    }

    private void ApplyMachineContextMenuPadding(double value)
    {
        var clamped = ClampMachineContextMenuPadding(value);
        _settings.MachineContextMenuItemPadding = clamped;
        Resources["MachineContextMenuItemPadding"] = new Thickness(clamped);
        if (MachineContextMenuPaddingValueTextBlock is not null)
        {
            MachineContextMenuPaddingValueTextBlock.Text = clamped.ToString("0");
        }
    }

    private static double ClampMachineContextMenuFontSize(double value)
    {
        return Math.Clamp(value, 10, 30);
    }

    private void ApplyMachineContextMenuFontSize(double value)
    {
        var clamped = ClampMachineContextMenuFontSize(value);
        _settings.MachineContextMenuFontSize = clamped;
        Resources["MachineContextMenuFontSize"] = clamped;
        if (MachineContextMenuFontSizeValueTextBlock is not null)
        {
            MachineContextMenuFontSizeValueTextBlock.Text = clamped.ToString("0");
        }
    }

    private int GetSelectedSystemLogLimit()
    {
        if (SystemLogLimitComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var limit))
        {
            return limit;
        }

        return 100;
    }

    private void ShowAddMemberModal()
    {
        MemberUsernameTextBox.Text = string.Empty;
        MemberPasswordBox.Password = string.Empty;
        MemberModalErrorTextBlock.Text = string.Empty;
        MemberModalOverlay.Visibility = Visibility.Visible;
        MemberUsernameTextBox.Focus();
    }

    private void HideAddMemberModal()
    {
        MemberModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void AppendServiceLog(string message)
    {
        var existing = ServiceOutputTextBox.Text;
        ServiceOutputTextBox.Text = string.IsNullOrWhiteSpace(existing)
            ? message
            : existing + Environment.NewLine + message;
        ServiceOutputTextBox.ScrollToEnd();
    }

    private async void HealthTimer_Tick(object? sender, EventArgs e) => await CheckBackendHealthAsync();

    private async void RefreshMachinesButton_Click(object sender, RoutedEventArgs e) => await RefreshMachinesAsync();

    private async void OpenMachineButton_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("open");

    private async void LockMachineButton_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("lock");

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchKeyword = SearchTextBox.Text.Trim();
        _ = RefreshMachinesAsync();
    }

    private void StatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusFilterComboBox.SelectedItem is ComboBoxItem item)
        {
            _statusFilter = item.Content?.ToString() ?? I18n.StatusAll;
            _ = RefreshMachinesAsync();
        }
    }

    private void UnlockAllFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Text = string.Empty;
        _searchKeyword = string.Empty;
        _statusFilter = I18n.StatusAll;
        StatusFilterComboBox.SelectedIndex = 0;
        _ = RefreshMachinesAsync();
    }

    private void MachinesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MachinesDataGrid.SelectedItem is MachineRow selected)
        {
            SelectionTextBlock.Text = $"{I18n.SelectedPcPrefix}: {selected.Name} ({selected.StatusText})";
            return;
        }

        SelectionTextBlock.Text = I18n.NotSelectedPc;
    }

    private void MachinesDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();
    }

    private async void ContextOpenMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("open");

    private async void ContextLockMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("lock");

    private async void ContextRestartMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("restart");

    private async void ContextShutdownMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("shutdown");

    private async void ContextCloseAppsMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("close-apps");
    private async void ContextPauseMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("pause");
    private async void ContextResumeMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("resume");
    private async void ContextTransferMachineMenuItem_Click(object sender, RoutedEventArgs e) => await TransferMachineAsync();

    private async void ContextNotifyMachineMenuItem_Click(object sender, RoutedEventArgs e) => await NotifyPcAsync();

    private async void ContextRefreshMachinesMenuItem_Click(object sender, RoutedEventArgs e) => await RefreshMachinesAsync();

    private void ContextClearMachineFiltersMenuItem_Click(object sender, RoutedEventArgs e) => UnlockAllFilterButton_Click(sender, e);

    private async Task NotifyPcAsync()
    {
        if (MachinesDataGrid.SelectedItem is not MachineRow selected)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = PromptText("Gửi thông báo", "Nhập nội dung thông báo gửi đến máy trạm:", "Vui lòng liên hệ quầy để được hỗ trợ.");
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/pcs/{selected.Id}/notify"),
                new { message, requestedBy = "admin.desktop" });

            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Gửi thông báo thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã gửi thông báo đến {selected.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gửi thông báo lỗi: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task TransferMachineAsync()
    {
        if (MachinesDataGrid.SelectedItem is not MachineRow selected)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targetPcId = PromptText(
            "Chuyển máy",
            "Nhập PC ID máy đích (id trong hệ thống):",
            string.Empty);

        if (string.IsNullOrWhiteSpace(targetPcId))
        {
            return;
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/sessions/transfer/{selected.Id}"),
                new { targetPcId = targetPcId.Trim(), requestedBy = "admin.desktop" });

            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Chuyển máy thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã chuyển phiên từ {selected.Name} sang PC ID {targetPcId}");
            await RefreshMachinesAsync();
            await RefreshTransactionLogsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Chuyển máy lỗi: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? PromptText(string title, string label, string defaultValue = "")
    {
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(labelBlock, 0);
        root.Children.Add(labelBlock);

        var textBox = new TextBox
        {
            Text = defaultValue,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(textBox, 1);
        root.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var okButton = new Button { Content = "Gửi", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelButton = new Button { Content = "Hủy", Width = 88, IsCancel = true };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 2);
        root.Children.Add(buttonPanel);

        string? result = null;
        okButton.Click += (_, _) =>
        {
            result = textBox.Text?.Trim();
            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) => textBox.Focus();
        _ = dialog.ShowDialog();
        return result;
    }

    private async void RefreshMembersButton_Click(object sender, RoutedEventArgs e) => await RefreshMembersAsync();

    private void AddMemberButton_Click(object sender, RoutedEventArgs e) => ShowAddMemberModal();

    private async void TopupMemberButton_Click(object sender, RoutedEventArgs e) => await TopupMemberAsync();

    private async void BuyHoursButton_Click(object sender, RoutedEventArgs e) => await BuyHoursAsync();
    private async void RefundMemberButton_Click(object sender, RoutedEventArgs e) => await AdjustMemberBalanceAsync(true);
    private async void GiftMemberButton_Click(object sender, RoutedEventArgs e) => await AdjustMemberBalanceAsync(false);

    private void MembersSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _memberSearchKeyword = MembersSearchTextBox.Text.Trim();
        _ = RefreshMembersAsync();
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

    private async void CreateMemberConfirmButton_Click(object sender, RoutedEventArgs e) => await CreateMemberAsync();

    private void CancelMemberModalButton_Click(object sender, RoutedEventArgs e) => HideAddMemberModal();

    private async void RefreshSystemLogsButton_Click(object sender, RoutedEventArgs e) => await RefreshSystemLogsAsync();

    private async void SystemLogLimitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await RefreshSystemLogsAsync();
    }

    private async void RefreshTransactionLogsButton_Click(object sender, RoutedEventArgs e) => await RefreshTransactionLogsAsync();

    private void RefreshGroupsButton_Click(object sender, RoutedEventArgs e) => RefreshGroupsFromMachines();

    private void OpenPcsWebButton_Click(object sender, RoutedEventArgs e) => OpenExternalPage("/pcs");

    private void OpenMembersWebButton_Click(object sender, RoutedEventArgs e) => OpenExternalPage("/members");

    private async void CheckBackendNowButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckBackendHealthAsync();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] {HealthTextBlock.Text}");
    }

    private async void RefreshAllDataButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllDataAsync();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã tải lại toàn bộ dữ liệu");
    }

    private void OpenExternalPage(string path)
    {
        var url = BuildPageUrl(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Mở {url}");
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_fontSizeInitialized)
        {
            return;
        }

        ApplyUiFontSize(e.NewValue);
    }

    private void MachineTableFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_machineTableFontSizeInitialized)
        {
            return;
        }

        ApplyMachineTableFontSize(e.NewValue);
    }

    private void MachineContextMenuPaddingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_machineContextMenuPaddingInitialized)
        {
            return;
        }

        ApplyMachineContextMenuPadding(e.NewValue);
    }

    private void MachineContextMenuFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_machineContextMenuFontSizeInitialized)
        {
            return;
        }

        ApplyMachineContextMenuFontSize(e.NewValue);
    }

    private void SaveFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã lưu: cỡ chữ app = {_settings.UiFontSize:0}, bảng máy = {_settings.MachineTableFontSize:0}, padding menu = {_settings.MachineContextMenuItemPadding:0}, chữ menu = {_settings.MachineContextMenuFontSize:0}");
    }

    private void ResetFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        _fontSizeInitialized = false;
        FontSizeSlider.Value = 13;
        _fontSizeInitialized = true;
        ApplyUiFontSize(13);

        _machineTableFontSizeInitialized = false;
        MachineTableFontSizeSlider.Value = 14;
        _machineTableFontSizeInitialized = true;
        ApplyMachineTableFontSize(14);

        _machineContextMenuPaddingInitialized = false;
        MachineContextMenuPaddingSlider.Value = 12;
        _machineContextMenuPaddingInitialized = true;
        ApplyMachineContextMenuPadding(12);

        _machineContextMenuFontSizeInitialized = false;
        MachineContextMenuFontSizeSlider.Value = 14;
        _machineContextMenuFontSizeInitialized = true;
        ApplyMachineContextMenuFontSize(14);

        SaveSettings();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã đặt lại cỡ chữ mặc định");
    }
}

public sealed class AdminShellSettings
{
    public string AdminBaseUrl { get; set; } = "http://localhost:5173";

    public string BackendApiBaseUrl { get; set; } = "http://localhost:9000/api/v1";

    public string StartPath { get; set; } = "/pcs";

    public int MachineRefreshSeconds { get; set; } = 3;

    public double UiFontSize { get; set; } = 13;

    public double MachineTableFontSize { get; set; } = 14;

    public double MachineContextMenuItemPadding { get; set; } = 12;

    public double MachineContextMenuFontSize { get; set; } = 14;
}

public sealed class PcListResponse
{
    public List<PcListItem> Items { get; set; } = new();
}

public sealed class PcListItem
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? LastSeenAt { get; set; }
    public ActiveSessionInfo? ActiveSession { get; set; }
}

public sealed class ActiveSessionInfo
{
    public string Id { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public int ElapsedSeconds { get; set; }
    public int EstimatedAmount { get; set; }
}

public sealed class MachineRow
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = "-";
    public string LastSeenAtText { get; set; } = "-";
    public string StatusCode { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public Brush StatusBrush { get; set; } = Brushes.Gray;
    public string UserName { get; set; } = "-";
    public string StartedAtText { get; set; } = "-";
    public string UsedText { get; set; } = "-";
    public string RemainingText { get; set; } = "-";
    public string MoneyText { get; set; } = "-";
    public string DateText { get; set; } = "-";
    public string VersionText { get; set; } = "0.1.0";
    public string GroupName { get; set; } = "Mặc định";
}

public sealed class MemberListResponse
{
    public List<MemberItem> Items { get; set; } = new();
}

public sealed class MemberItem
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool HasPassword { get; set; }
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
    public double PlayHours { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class MemberTransactionsResponse
{
    public MemberItem Member { get; set; } = new();
    public List<MemberTransactionItem> Items { get; set; } = new();
}

public sealed class MemberTransactionItem
{
    public string Type { get; set; } = string.Empty;
    public decimal AmountDelta { get; set; }
    public int PlaySecondsDelta { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class MemberRow
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = "-";
    public string BalanceText { get; set; } = "0";
    public string PlayHoursText { get; set; } = "0";
    public string PasswordState { get; set; } = "Chưa đặt";
    public string ActiveText { get; set; } = "Hoạt động";
    public string CreatedAtText { get; set; } = "-";
}

public sealed class MemberTransactionRow
{
    public string CreatedAtText { get; set; } = "-";
    public string TypeText { get; set; } = "-";
    public string AmountDeltaText { get; set; } = "0";
    public string PlayHoursDeltaText { get; set; } = "0";
    public string CreatedBy { get; set; } = "-";
    public string Note { get; set; } = "-";
}

public sealed class SystemEventsResponse
{
    public List<SystemEventItem> Items { get; set; } = new();
    public int Total { get; set; }
}

public sealed class SystemEventItem
{
    public string Source { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? PcName { get; set; }
    public string? AgentId { get; set; }
    public object? Payload { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class SystemLogRow
{
    public string CreatedAtText { get; set; } = "-";
    public string Source { get; set; } = "-";
    public string EventType { get; set; } = "-";
    public string PcText { get; set; } = "-";
    public string PayloadText { get; set; } = "-";
}

public sealed class SessionsListResponse
{
    public List<SessionItem> Items { get; set; } = new();
}

public sealed class SessionItem
{
    public string PcName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public string? EndedAt { get; set; }
    public int? BillableMinutes { get; set; }
    public decimal? Amount { get; set; }
    public string? ClosedReason { get; set; }
}

public sealed class SessionLogRow
{
    public string PcName { get; set; } = "-";
    public string AgentId { get; set; } = "-";
    public string StartedAtText { get; set; } = "-";
    public string EndedAtText { get; set; } = "-";
    public string BillableMinutesText { get; set; } = "-";
    public decimal AmountRaw { get; set; }
    public string AmountText { get; set; } = "0";
    public string Status { get; set; } = "-";
    public string ClosedReason { get; set; } = "-";
}

public sealed class GroupSummaryRow
{
    public string GroupName { get; set; } = "Mặc định";
    public int Total { get; set; }
    public int InUse { get; set; }
    public int Locked { get; set; }
    public int Online { get; set; }
    public int Offline { get; set; }
}

public sealed class GroupMachineRow
{
    public string GroupName { get; set; } = "Mặc định";
    public string MachineName { get; set; } = "-";
    public string AgentId { get; set; } = "-";
    public string StatusText { get; set; } = "-";
    public string IpAddress { get; set; } = "-";
    public string LastSeenAtText { get; set; } = "-";
}
