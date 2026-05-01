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
    private async Task RefreshMachinesAsync()
    {
        try
        {
            _isRefreshingMachines = true;
            var selectedPcId = _selectedMachineId ?? (MachinesDataGrid.SelectedItem as MachineRow)?.Id;
            var response = await _httpClient.GetFromJsonAsync<PcListResponse>(BuildApiUrl("/pcs"), JsonOptions());
            if (response is null)
            {
                return;
            }

            var allRows = response.Items.Select(ToMachineRow).OrderBy(r => r.Name).ToList();
            _allMachineRows.Clear();
            _allMachineRows.AddRange(allRows);
            RefreshWebsiteLogMachineFilterOptions();

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

            if (!string.IsNullOrWhiteSpace(selectedPcId))
            {
                var selectedRow = _machineRows.FirstOrDefault(x => x.Id == selectedPcId);
                if (selectedRow is not null)
                {
                    MachinesDataGrid.SelectedItem = selectedRow;
                    MachinesDataGrid.ScrollIntoView(selectedRow);
                    _selectedMachineId = selectedRow.Id;
                }
            }

            UpdateMachineSummary(filteredRows);
            await RefreshGroupsAsync();
            LastSyncTextBlock.Text = $"{I18n.SyncPrefix}: {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            // Keep UI alive when API is temporarily unavailable.
        }
        finally
        {
            _isRefreshingMachines = false;
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
        var activeMember = item.ActiveMember;
        var activeGuest = item.ActiveGuest;
        var guestDisplayName = string.IsNullOrWhiteSpace(activeGuest?.DisplayName)
            ? "Khách vãng lai"
            : activeGuest!.DisplayName;
        var isGuestSession = activeMember is null && activeGuest is not null;
        var userName = !string.IsNullOrWhiteSpace(activeMember?.Username)
            ? activeMember!.Username
            : isGuestSession
                ? guestDisplayName
                : "-";

        return new MachineRow
        {
            Id = item.Id,
            AgentId = item.AgentId,
            Name = item.Name,
            GroupId = item.GroupId,
            HourlyRate = item.HourlyRate,
            IpAddress = item.IpAddress ?? "-",
            LastSeenAtText = FormatDateTime(item.LastSeenAt),
            StatusText = statusText,
            StatusBrush = statusBrush,
            UserName = userName,
            StartedAtText = startedAt?.ToString("HH:mm:ss") ?? "-",
            UsedText = usedText,
            RemainingText = "-",
            MoneyText = item.ActiveSession is null ? "-" : item.ActiveSession.EstimatedAmount.ToString("N0"),
            DateText = now.ToString("dd-MM-yyyy"),
            VersionText = "0.1.0",
            GroupName = string.IsNullOrWhiteSpace(item.GroupName) ? "Mặc định" : item.GroupName,
            StatusCode = item.Status,
            ActiveSessionId = item.ActiveSession?.Id,
            ActiveSessionElapsedSeconds = item.ActiveSession?.ElapsedSeconds ?? 0,
            ActiveSessionEstimatedAmount = item.ActiveSession?.EstimatedAmount ?? 0,
            ActiveMemberId = activeMember?.MemberId,
            ActiveMemberUsername = activeMember?.Username,
            ActiveMemberFullName = activeMember?.FullName,
            IsGuestSession = isGuestSession,
            ActiveGuestDisplayName = isGuestSession ? guestDisplayName : null,
            ActiveGuestPrepaidAmount = isGuestSession ? activeGuest!.PrepaidAmount : 0,
        };
    }

    private List<MachineRow> ApplyStatusFilter(List<MachineRow> rows)
    {
        return _statusFilter switch
        {
            "\u0110ang s\u1eed d\u1ee5ng" => rows.Where(r => r.StatusCode == "IN_USE").ToList(),
            "\u0110ang t\u1eaft" => rows.Where(r => r.StatusCode == "LOCKED").ToList(),
            "S\u1eb5n s\u00e0ng" => rows.Where(r => r.StatusCode == "ONLINE").ToList(),
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

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] \u0110\u00e3 g\u1eedi l\u1ec7nh {action.ToUpperInvariant()} cho {selected.Name}");
            await RefreshMachinesAsync();
            await RefreshTransactionLogsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{I18n.CommandError}: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


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
            _selectedMachineId = selected.Id;
            SelectionTextBlock.Text = $"{I18n.SelectedPcPrefix}: {selected.Name} ({selected.StatusText})";
            return;
        }

        if (_isRefreshingMachines)
        {
            return;
        }

        _selectedMachineId = null;
        SelectionTextBlock.Text = I18n.NotSelectedPc;
    }

    private async void MachinesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MachinesDataGrid.SelectedItem is not MachineRow selected)
        {
            return;
        }

        var status = selected.StatusCode?.Trim().ToUpperInvariant();
        switch (status)
        {
            case "ONLINE":
            case "AVAILABLE":
                await OpenGuestMachineAsync(selected);
                return;

            case "IN_USE":
                if (!string.IsNullOrWhiteSpace(selected.ActiveMemberId) ||
                    !string.IsNullOrWhiteSpace(selected.ActiveMemberUsername))
                {
                    await OpenMemberDetailsFromMachineAsync(selected);
                    return;
                }

                await ShowMachineBillingDetailsAsync(selected);
                return;
        }
    }

    private async Task OpenMemberDetailsFromMachineAsync(MachineRow machine)
    {
        var member = await ResolveMemberForMachineAsync(machine);
        if (member is null)
        {
            MessageBox.Show(
                "Không tìm thấy thông tin hội viên đang sử dụng máy này.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _selectedMemberId = member.Id;
        await OpenEditMemberDialogAsync(member);
    }

    private async Task<MemberRow?> ResolveMemberForMachineAsync(MachineRow machine)
    {
        if (!string.IsNullOrWhiteSpace(machine.ActiveMemberId))
        {
            var byId = _memberRows.FirstOrDefault(x =>
                string.Equals(x.Id, machine.ActiveMemberId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(machine.ActiveMemberUsername))
        {
            var byUsername = _memberRows.FirstOrDefault(x =>
                string.Equals(x.Username, machine.ActiveMemberUsername, StringComparison.OrdinalIgnoreCase));
            if (byUsername is not null)
            {
                return byUsername;
            }
        }

        var searchKeyword =
            !string.IsNullOrWhiteSpace(machine.ActiveMemberUsername)
                ? machine.ActiveMemberUsername
                : machine.UserName;
        if (string.IsNullOrWhiteSpace(searchKeyword) || searchKeyword == "-")
        {
            return null;
        }

        try
        {
            var response = await _httpClient.GetFromJsonAsync<MemberListResponse>(
                BuildApiUrl($"/members?search={Uri.EscapeDataString(searchKeyword)}"),
                JsonOptions());
            var firstMatched = response?.Items.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(machine.ActiveMemberId) &&
                 string.Equals(x.Id, machine.ActiveMemberId, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(x.Username, searchKeyword, StringComparison.OrdinalIgnoreCase));
            return firstMatched is null ? null : ToMemberRow(firstMatched);
        }
        catch
        {
            return null;
        }
    }

    private void MachinesDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();
        if (row.Item is MachineRow selected)
        {
            _selectedMachineId = selected.Id;
        }
    }

    private async void MachinesDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            e.Handled = true;
            return;
        }

        if (dataGrid.SelectedItem is not MachineRow && !string.IsNullOrWhiteSpace(_selectedMachineId))
        {
            var selectedRow = _machineRows.FirstOrDefault(x => x.Id == _selectedMachineId);
            if (selectedRow is not null)
            {
                dataGrid.SelectedItem = selectedRow;
            }
        }

        if (dataGrid.SelectedItem is not MachineRow)
        {
            e.Handled = true;
            return;
        }

        if (_groupSummaryRows.Count == 0)
        {
            await RefreshGroupsAsync(forceReloadPricing: true);
        }

        if (dataGrid.SelectedItem is MachineRow selectedMachine)
        {
            PopulateGroupContextMenuForMachine(selectedMachine);
            ApplyMachineContextMenuState(selectedMachine);
        }
    }

    private void ApplyMachineContextMenuState(MachineRow selectedMachine)
    {
        var status = selectedMachine.StatusCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var isOffline = status == "OFFLINE";
        var isReady = status is "ONLINE" or "AVAILABLE";
        var isInUse = status == "IN_USE";
        var isPaused = status == "PAUSED";
        var isLocked = status == "LOCKED";
        var hasActiveMember =
            !string.IsNullOrWhiteSpace(selectedMachine.ActiveMemberId) ||
            !string.IsNullOrWhiteSpace(selectedMachine.ActiveMemberUsername);

        if (isOffline)
        {
            SetMachineContextMenuEnabled(
                open: false,
                openGuest: false,
                lockMachine: false,
                topupMember: false,
                restart: true,
                shutdown: false,
                closeApps: false,
                pause: false,
                resume: false,
                transfer: false,
                assignGroup: false,
                viewBilling: false,
                selectService: false,
                notify: false);
            return;
        }

        SetMachineContextMenuEnabled(
            open: !isInUse && !isLocked,
            openGuest: isReady,
            lockMachine: !isLocked,
            topupMember: isInUse && hasActiveMember,
            restart: true,
            shutdown: true,
            closeApps: true,
            pause: isInUse,
            resume: isPaused,
            transfer: isInUse,
            assignGroup: true,
            viewBilling: isInUse,
            selectService: true,
            notify: true);
    }

    private void SetMachineContextMenuEnabled(
        bool open,
        bool openGuest,
        bool lockMachine,
        bool topupMember,
        bool restart,
        bool shutdown,
        bool closeApps,
        bool pause,
        bool resume,
        bool transfer,
        bool assignGroup,
        bool viewBilling,
        bool selectService,
        bool notify)
    {
        if (ContextOpenMachineMenuItem is not null)
        {
            ContextOpenMachineMenuItem.IsEnabled = open;
        }

        if (ContextOpenGuestMachineMenuItem is not null)
        {
            ContextOpenGuestMachineMenuItem.IsEnabled = openGuest;
        }

        if (ContextLockMachineMenuItem is not null)
        {
            ContextLockMachineMenuItem.IsEnabled = lockMachine;
        }

        if (ContextTopupMachineMemberMenuItem is not null)
        {
            ContextTopupMachineMemberMenuItem.IsEnabled = topupMember;
        }

        if (ContextRestartMachineMenuItem is not null)
        {
            ContextRestartMachineMenuItem.IsEnabled = restart;
        }

        if (ContextShutdownMachineMenuItem is not null)
        {
            ContextShutdownMachineMenuItem.IsEnabled = shutdown;
        }

        if (ContextCloseAppsMenuItem is not null)
        {
            ContextCloseAppsMenuItem.IsEnabled = closeApps;
        }

        if (ContextPauseMachineMenuItem is not null)
        {
            ContextPauseMachineMenuItem.IsEnabled = pause;
        }

        if (ContextResumeMachineMenuItem is not null)
        {
            ContextResumeMachineMenuItem.IsEnabled = resume;
        }

        if (ContextTransferMachineMenuItem is not null)
        {
            ContextTransferMachineMenuItem.IsEnabled = transfer;
        }

        if (ContextAssignGroupMenuItem is not null)
        {
            ContextAssignGroupMenuItem.IsEnabled = assignGroup;
        }

        if (ContextViewMachineBillingMenuItem is not null)
        {
            ContextViewMachineBillingMenuItem.IsEnabled = viewBilling;
        }

        if (ContextSelectServiceMenuItem is not null)
        {
            ContextSelectServiceMenuItem.IsEnabled = selectService;
        }

        if (ContextNotifyMachineMenuItem is not null)
        {
            ContextNotifyMachineMenuItem.IsEnabled = notify;
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        if (source is null)
        {
            return;
        }

        if (FindAncestor<DataGridRow>(source) is not null)
        {
            return;
        }

        if (FindAncestor<DataGrid>(source) == MachinesDataGrid)
        {
            return;
        }

        MachinesDataGrid.UnselectAll();
        _selectedMachineId = null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T matched)
            {
                return matched;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private async void ContextOpenMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("open");

    private async void ContextOpenGuestMachineMenuItem_Click(object sender, RoutedEventArgs e) => await OpenGuestMachineAsync();

    private async void ContextLockMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("lock");

    private async void ContextTopupMachineMemberMenuItem_Click(object sender, RoutedEventArgs e) => await TopupActiveMemberFromMachineAsync();

    private async void ContextRestartMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("restart");

    private async void ContextShutdownMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("shutdown");

    private async void ContextCloseAppsMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("close-apps");
    private async void ContextPauseMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("pause");
    private async void ContextResumeMachineMenuItem_Click(object sender, RoutedEventArgs e) => await SendCommandAsync("resume");
    private async void ContextTransferMachineMenuItem_Click(object sender, RoutedEventArgs e) => await TransferMachineAsync();

    private async void ContextNotifyMachineMenuItem_Click(object sender, RoutedEventArgs e) => await NotifyPcAsync();

    private async void ContextRefreshMachinesMenuItem_Click(object sender, RoutedEventArgs e) => await RefreshMachinesAsync();

    private void ContextClearMachineFiltersMenuItem_Click(object sender, RoutedEventArgs e) => UnlockAllFilterButton_Click(sender, e);
    private async void ContextViewMachineBillingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MachinesDataGrid.SelectedItem is not MachineRow selected)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ShowMachineBillingDetailsAsync(selected);
    }

    private async Task ShowMachineBillingDetailsAsync(MachineRow machine)
    {
        decimal serviceAmount;
        try
        {
            serviceAmount = await GetServiceAmountForMachineAsync(machine);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể tải tiền dịch vụ: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var playAmount = machine.ActiveSessionEstimatedAmount;
        var totalAmount = playAmount + serviceAmount;
        var hasActiveSession = !string.IsNullOrWhiteSpace(machine.ActiveSessionId);
        var playDurationText = hasActiveSession ? FormatUsed(machine.ActiveSessionElapsedSeconds) : "0 phút";

        var dialog = new Window
        {
            Title = $"Chi tiết thanh toán - {machine.Name}",
            Width = 560,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
            Owner = this,
        };

        var root = new Grid { Margin = new Thickness(16) };
        for (var i = 0; i < 10; i++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = $"Máy trạm: {machine.Name}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        AddBillingLine(root, 1, "Trạng thái", machine.StatusText);
        AddBillingLine(root, 2, "Nhóm máy", machine.GroupName);
        AddBillingLine(root, 3, "Bắt đầu", machine.StartedAtText);
        AddBillingLine(root, 4, "Thời gian chơi", playDurationText);
        AddBillingLine(root, 5, "Giá giờ chơi", $"{machine.HourlyRate:N0} VND/giờ");
        AddBillingLine(root, 6, "Tiền giờ chơi", $"{playAmount:N0} VND");
        AddBillingLine(root, 7, "Tiền dịch vụ", $"{serviceAmount:N0} VND");

        var totalBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(238, 242, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 10, 0, 0),
            Child = new TextBlock
            {
                Text = $"Tổng thanh toán: {totalAmount:N0} VND",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
            },
        };
        Grid.SetRow(totalBorder, 8);
        root.Children.Add(totalBorder);

        var noteText = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Text = hasActiveSession
                ? "Đây là tiền tạm tính của phiên đang chạy (bao gồm dịch vụ trong phiên hiện tại)."
                : "Máy chưa có phiên đang chạy. Tổng thanh toán hiện tại bằng 0 nếu chưa có dịch vụ gắn phiên.",
        };
        Grid.SetRow(noteText, 9);
        root.Children.Add(noteText);

        var closeButton = new Button
        {
            Content = "Đóng",
            Width = 100,
            Height = 34,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        closeButton.Click += (_, _) => dialog.Close();
        Grid.SetRow(closeButton, 11);
        root.Children.Add(closeButton);

        dialog.Content = root;
        _ = dialog.ShowDialog();
    }

    private async Task OpenGuestMachineAsync(MachineRow? targetMachine = null)
    {
        var selected = targetMachine ?? MachinesDataGrid.SelectedItem as MachineRow;
        if (selected is null)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var status = selected.StatusCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (status is not ("ONLINE" or "AVAILABLE"))
        {
            MessageBox.Show(
                "Chỉ mở máy khách vãng lai khi máy đang ở trạng thái Sẵn sàng.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var amount = await ShowTopupModalAsync(
            member: null,
            title: $"Mở máy khách vãng lai - {selected.Name}",
            memberPrompt: $"Máy trạm: {selected.Name} - nhập số tiền khách trả trước:",
            currentBalanceText: "Số tiền khách trả trước: - VND",
            allowDeduct: false);

        if (!amount.HasValue)
        {
            return;
        }

        if (amount.Value < 1000)
        {
            MessageBox.Show(
                "Số tiền mở máy khách vãng lai tối thiểu là 1.000 VND.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/pcs/{selected.Id}/guest-open"),
                new
                {
                    amount = Convert.ToDouble(amount.Value, CultureInfo.InvariantCulture),
                    requestedBy = "admin.desktop",
                });

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                MessageBox.Show(
                    string.IsNullOrWhiteSpace(error)
                        ? $"Mở máy khách vãng lai thất bại ({(int)response.StatusCode})"
                        : error,
                    "Server Admin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Mở máy khách vãng lai {selected.Name} ({amount.Value:N0} VND)");
            await RefreshMachinesAsync();
            await RefreshTransactionLogsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Mở máy khách vãng lai lỗi: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task TopupActiveMemberFromMachineAsync(MachineRow? targetMachine = null)
    {
        var selected = targetMachine ?? MachinesDataGrid.SelectedItem as MachineRow;
        if (selected is null)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var status = selected.StatusCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (status != "IN_USE")
        {
            MessageBox.Show(
                "Máy chưa ở trạng thái Đang sử dụng.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var member = await ResolveMemberForMachineAsync(selected);
        if (member is null)
        {
            MessageBox.Show(
                "Máy này không có hội viên đang sử dụng để nạp tiền.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await TopupMemberByRowAsync(member);
    }

    private async Task<decimal> GetServiceAmountForMachineAsync(MachineRow machine)
    {
        var response = await _httpClient.GetFromJsonAsync<PcServiceOrdersResponse>(
            BuildApiUrl($"/services/pcs/{machine.Id}/orders?limit=200"),
            JsonOptions());

        if (response?.Items is null || response.Items.Count == 0)
        {
            return 0;
        }

        IEnumerable<PcServiceOrderDto> scopedOrders = response.Items;
        if (!string.IsNullOrWhiteSpace(machine.ActiveSessionId))
        {
            scopedOrders = scopedOrders.Where(x =>
                string.Equals(x.SessionId, machine.ActiveSessionId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            scopedOrders = Enumerable.Empty<PcServiceOrderDto>();
        }

        return scopedOrders.Sum(x => x.LineTotal);
    }

    private static void AddBillingLine(Grid root, int rowIndex, string label, string value)
    {
        var rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new TextBlock
        {
            Text = $"{label}:",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
        };
        Grid.SetColumn(labelText, 0);
        rowGrid.Children.Add(labelText);

        var valueText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
            Foreground = Brushes.Black,
        };
        Grid.SetColumn(valueText, 1);
        rowGrid.Children.Add(valueText);

        Grid.SetRow(rowGrid, rowIndex);
        root.Children.Add(rowGrid);
    }

    private async Task NotifyPcAsync()
    {
        if (MachinesDataGrid.SelectedItem is not MachineRow selected)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = PromptText(
            "G\u1eedi th\u00f4ng b\u00e1o",
            "Nh\u1eadp n\u1ed9i dung th\u00f4ng b\u00e1o g\u1eedi \u0111\u1ebfn m\u00e1y tr\u1ea1m:",
            "Vui l\u00f2ng li\u00ean h\u1ec7 qu\u1ea7y \u0111\u1ec3 \u0111\u01b0\u1ee3c h\u1ed7 tr\u1ee3.");
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
                MessageBox.Show($"G\u1eedi th\u00f4ng b\u00e1o th\u1ea5t b\u1ea1i ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] \u0110\u00e3 g\u1eedi th\u00f4ng b\u00e1o \u0111\u1ebfn {selected.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"G\u1eedi th\u00f4ng b\u00e1o l\u1ed7i: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Error);
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
            "Chuy\u1ec3n m\u00e1y",
            "Nh\u1eadp PC ID m\u00e1y \u0111\u00edch (id trong h\u1ec7 th\u1ed1ng):",
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
                MessageBox.Show($"Chuy\u1ec3n m\u00e1y th\u1ea5t b\u1ea1i ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] \u0110\u00e3 chuy\u1ec3n phi\u00ean t\u1eeb {selected.Name} sang PC ID {targetPcId}");
            await RefreshMachinesAsync();
            await RefreshTransactionLogsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Chuy\u1ec3n m\u00e1y l\u1ed7i: {ex.Message}", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PopulateGroupContextMenuForMachine(MachineRow selectedMachine)
    {
        if (ContextAssignGroupMenuItem is null)
        {
            return;
        }

        ContextAssignGroupMenuItem.Items.Clear();
        var groups = _groupSummaryRows
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.GroupName)
            .ToList();

        if (groups.Count == 0)
        {
            ContextAssignGroupMenuItem.Items.Add(new MenuItem
            {
                Header = "Chưa có nhóm máy",
                IsEnabled = false,
            });
            return;
        }

        var currentGroupId = ResolveCurrentMachineGroupId(selectedMachine);
        foreach (var group in groups)
        {
            var isCurrent = string.Equals(currentGroupId, group.GroupId, StringComparison.OrdinalIgnoreCase);
            var menuItem = new MenuItem
            {
                Header = $"{group.GroupName} ({group.HourlyRate:N0} VND/giờ)",
                IsCheckable = true,
                IsChecked = isCurrent,
                IsEnabled = !isCurrent,
                Tag = group,
            };

            menuItem.Click += ContextAssignGroupDynamicMenuItem_Click;
            ContextAssignGroupMenuItem.Items.Add(menuItem);
        }
    }

    private string? ResolveCurrentMachineGroupId(MachineRow machine)
    {
        if (!string.IsNullOrWhiteSpace(machine.GroupId))
        {
            return machine.GroupId;
        }

        return _groupSummaryRows.FirstOrDefault(x => x.IsDefault)?.GroupId;
    }

    private async void ContextAssignGroupDynamicMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not GroupSummaryRow targetGroup)
        {
            return;
        }

        if (MachinesDataGrid.SelectedItem is not MachineRow selectedMachine)
        {
            MessageBox.Show(I18n.PleaseSelectPc, "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sourceMachine = new GroupMachineRow
        {
            PcId = selectedMachine.Id,
            GroupId = ResolveCurrentMachineGroupId(selectedMachine) ?? string.Empty,
            GroupName = selectedMachine.GroupName,
            HourlyRate = selectedMachine.HourlyRate,
            MachineName = selectedMachine.Name,
            AgentId = selectedMachine.AgentId,
            StatusText = selectedMachine.StatusText,
            IpAddress = selectedMachine.IpAddress,
            LastSeenAtText = selectedMachine.LastSeenAtText,
        };

        await AssignMachineToGroupAsync(sourceMachine, targetGroup);
    }

    private static string? PromptText(string title, string label, string defaultValue = "")
    {
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(labelBlock, 0);
        root.Children.Add(labelBlock);

        var textBox = new TextBox
        {
            Text = defaultValue,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Grid.SetRow(textBox, 1);
        root.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var okButton = new Button { Content = "Lưu", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelButton = new Button { Content = "H\u1ee7y", Width = 88, IsCancel = true };
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
        dialog.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };
        _ = dialog.ShowDialog();
        return result;
    }
}

