using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Server.Admin.App;
public partial class MainWindow : Window
{
    private async Task RefreshGroupsAsync(bool forceReloadPricing = false)
    {
        var selectedGroupId = _selectedGroupSummaryId ?? (GroupSummaryDataGrid.SelectedItem as GroupSummaryRow)?.GroupId;
        var selectedGroupMachinePcId = _selectedGroupMachinePcId ?? (GroupMachinesDataGrid.SelectedItem as GroupMachineRow)?.PcId;

        if (forceReloadPricing || _pricingSettings is null)
        {
            try
            {
                _pricingSettings = await _httpClient.GetFromJsonAsync<PricingSettingsResponse>(
                    BuildApiUrl("/pricing"),
                    JsonOptions());
            }
            catch
            {
                _pricingSettings = null;
            }
        }

        var pricing = _pricingSettings;
        var groups = pricing?.Groups?.ToList() ?? new List<PricingGroupItem>();
        if (groups.Count == 0)
        {
            groups.Add(new PricingGroupItem
            {
                Id = pricing?.DefaultGroupId ?? "default",
                Name = "M\u1eb7c \u0111\u1ecbnh",
                HourlyRate = pricing?.DefaultRatePerHour > 0 ? pricing.DefaultRatePerHour : 5000,
                MemberHourlyRate = pricing?.DefaultMemberRatePerHour > 0
                    ? pricing.DefaultMemberRatePerHour
                    : (pricing?.DefaultRatePerHour > 0 ? pricing.DefaultRatePerHour : 5000),
                IsDefault = true,
                MachineCount = 0,
            });
        }

        var defaultGroup = groups.FirstOrDefault(x => x.IsDefault) ?? groups.First();
        var groupById = groups.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        var assignmentRows = new List<(MachineRow Machine, PricingGroupItem Group)>();
        foreach (var machine in _allMachineRows)
        {
            var effectiveGroup = ResolveGroup(machine, groupById, defaultGroup);
            assignmentRows.Add((machine, effectiveGroup));
        }

        var groupSummaries = groups
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(group =>
            {
                var machines = assignmentRows
                    .Where(x => string.Equals(x.Group.Id, group.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Machine)
                    .ToList();

                return new GroupSummaryRow
                {
                    GroupId = group.Id,
                    GroupName = group.Name,
                    HourlyRate = group.HourlyRate,
                    MemberHourlyRate = group.MemberHourlyRate > 0 ? group.MemberHourlyRate : group.HourlyRate,
                    IsDefault = group.IsDefault,
                    Total = machines.Count,
                    Percentage = _allMachineRows.Count > 0 ? (double)machines.Count / _allMachineRows.Count * 100 : 0,
                    InUse = machines.Count(x => x.StatusCode == "IN_USE"),
                    Locked = machines.Count(x => x.StatusCode == "LOCKED"),
                    Online = machines.Count(x => x.StatusCode == "ONLINE"),
                    Offline = machines.Count(x => x.StatusCode is not "IN_USE" and not "LOCKED" and not "ONLINE"),
                };
            })
            .ToList();

        _groupSummaryRows.Clear();
        foreach (var row in groupSummaries)
        {
            _groupSummaryRows.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(selectedGroupId))
        {
            var selectedGroupRow = _groupSummaryRows.FirstOrDefault(x =>
                string.Equals(x.GroupId, selectedGroupId, StringComparison.OrdinalIgnoreCase));
            if (selectedGroupRow is not null)
            {
                GroupSummaryDataGrid.SelectedItem = selectedGroupRow;
                GroupSummaryDataGrid.ScrollIntoView(selectedGroupRow);
                _selectedGroupSummaryId = selectedGroupRow.GroupId;
            }
        }

        _groupMachineRows.Clear();
        foreach (var row in assignmentRows
                     .OrderBy(x => x.Group.Name)
                     .ThenBy(x => x.Machine.Name))
        {
            _groupMachineRows.Add(new GroupMachineRow
            {
                PcId = row.Machine.Id,
                GroupId = row.Group.Id,
                GroupName = row.Group.Name,
                HourlyRate = row.Group.HourlyRate,
                MemberHourlyRate = row.Group.MemberHourlyRate > 0 ? row.Group.MemberHourlyRate : row.Group.HourlyRate,
                MachineName = row.Machine.Name,
                AgentId = row.Machine.AgentId,
                StatusText = row.Machine.StatusText,
                IpAddress = row.Machine.IpAddress,
                LastSeenAtText = row.Machine.LastSeenAtText,
            });
        }

        if (!string.IsNullOrWhiteSpace(selectedGroupMachinePcId))
        {
            var selectedMachineRow = _groupMachineRows.FirstOrDefault(x =>
                string.Equals(x.PcId, selectedGroupMachinePcId, StringComparison.OrdinalIgnoreCase));
            if (selectedMachineRow is not null)
            {
                GroupMachinesDataGrid.SelectedItem = selectedMachineRow;
                GroupMachinesDataGrid.ScrollIntoView(selectedMachineRow);
                _selectedGroupMachinePcId = selectedMachineRow.PcId;
            }
        }

        var totalMachines = _allMachineRows.Count;
        var totalOnline = _allMachineRows.Count(x => x.StatusCode == "ONLINE" || x.StatusCode == "IN_USE");
        var totalOffline = _allMachineRows.Count(x => x.StatusCode != "ONLINE" && x.StatusCode != "IN_USE");

        DashStatTotalMachines.Text = totalMachines.ToString();
        DashStatOnline.Text = totalOnline.ToString();
        DashStatOffline.Text = totalOffline.ToString();

        var topGroups = groupSummaries.OrderByDescending(x => x.Total).ToList();
        DashStatTopGroup.Text = topGroups.Count > 0 ? $"{topGroups[0].Total} ({topGroups[0].GroupName})" : "-";
        DashStatSecondGroup.Text = topGroups.Count > 1 ? $"{topGroups[1].Total} ({topGroups[1].GroupName})" : "-";
    }

    private static PricingGroupItem ResolveGroup(
        MachineRow machine,
        IReadOnlyDictionary<string, PricingGroupItem> groupById,
        PricingGroupItem defaultGroup)
    {
        if (!string.IsNullOrWhiteSpace(machine.GroupId) &&
            groupById.TryGetValue(machine.GroupId, out var groupByMachineId))
        {
            return groupByMachineId;
        }

        return defaultGroup;
    }

    private async void RefreshGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshGroupsAsync(forceReloadPricing: true);
    }

    private async void SetDefaultRateGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var current = _pricingSettings?.DefaultRatePerHour ?? 5000;
        var currentMember = _pricingSettings?.DefaultMemberRatePerHour > 0
            ? _pricingSettings.DefaultMemberRatePerHour
            : current;
        var raw = PromptText(
            "Gi\u00e1 m\u1eb7c \u0111\u1ecbnh",
            "Nh\u1eadp gi\u00e1 gi\u1edd ch\u01a1i m\u1eb7c \u0111\u1ecbnh (VND/gi\u1edd):",
            current.ToString("0"));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var memberRaw = PromptText(
            "Giá giờ hội viên mặc định",
            "Nhập giá giờ hội viên mặc định (VND/giờ):",
            currentMember.ToString("0"));
        if (string.IsNullOrWhiteSpace(memberRaw))
        {
            return;
        }

        if (!decimal.TryParse(raw.Trim(), out var hourlyRate) || hourlyRate <= 0)
        {
            MessageBox.Show("Gi\u00e1 gi\u1edd ch\u01a1i kh\u00f4ng h\u1ee3p l\u1ec7.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!decimal.TryParse(memberRaw.Trim(), out var memberHourlyRate) || memberHourlyRate <= 0)
        {
            MessageBox.Show("Giá giờ hội viên không hợp lệ.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var response = await _httpClient.PutAsJsonAsync(
            BuildApiUrl("/pricing/default-rate"),
            new
            {
                hourlyRate = Convert.ToDouble(hourlyRate),
                memberHourlyRate = Convert.ToDouble(memberHourlyRate),
            });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show($"Kh\u00f4ng th\u1ec3 l\u01b0u gi\u00e1 m\u1eb7c \u0111\u1ecbnh ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Cập nhật giá mặc định: {hourlyRate:N0} VND/giờ | Giá hội viên: {memberHourlyRate:N0} VND/giờ");
        _pricingSettings = null;
        await RefreshMachinesAsync();
    }

    private async void CreatePricingGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptText("Th\u00eam nh\u00f3m m\u00e1y", "T\u00ean nh\u00f3m m\u00e1y m\u1edbi:", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var rateDefault = (_pricingSettings?.DefaultRatePerHour ?? 5000).ToString("0");
        var rateRaw = PromptText(
            "Gi\u00e1 nh\u00f3m m\u00e1y",
            $"Nh\u1eadp gi\u00e1 cho nh\u00f3m \"{name.Trim()}\" (VND/gi\u1edd):",
            rateDefault);
        if (string.IsNullOrWhiteSpace(rateRaw))
        {
            return;
        }

        if (!decimal.TryParse(rateRaw.Trim(), out var hourlyRate) || hourlyRate <= 0)
        {
            MessageBox.Show("Gi\u00e1 gi\u1edd ch\u01a1i kh\u00f4ng h\u1ee3p l\u1ec7.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var memberRateRaw = PromptText(
            "Giá giờ hội viên",
            $"Nhập giá giờ hội viên cho nhóm \"{name.Trim()}\" (VND/giờ):",
            hourlyRate.ToString("0"));
        if (string.IsNullOrWhiteSpace(memberRateRaw))
        {
            return;
        }
        if (!decimal.TryParse(memberRateRaw.Trim(), out var memberHourlyRate) || memberHourlyRate <= 0)
        {
            MessageBox.Show("Giá giờ hội viên không hợp lệ.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl("/pricing/groups"),
            new
            {
                name = name.Trim(),
                hourlyRate = Convert.ToDouble(hourlyRate),
                memberHourlyRate = Convert.ToDouble(memberHourlyRate),
            });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show($"T\u1ea1o nh\u00f3m m\u00e1y th\u1ea5t b\u1ea1i ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Tạo nhóm \"{name.Trim()}\" - Giá mặc định: {hourlyRate:N0} VND/giờ | Giá hội viên: {memberHourlyRate:N0} VND/giờ");
        _pricingSettings = null;
        await RefreshGroupsAsync(forceReloadPricing: true);
    }

    private async void UpdateSelectedGroupRateButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupSummaryDataGrid.SelectedItem is not GroupSummaryRow selectedGroup)
        {
            MessageBox.Show("Vui l\u00f2ng ch\u1ecdn nh\u00f3m m\u00e1y tr\u01b0\u1edbc.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var raw = PromptText(
            "\u0110\u1ed5i gi\u00e1 nh\u00f3m m\u00e1y",
            $"Nh\u1eadp gi\u00e1 m\u1edbi cho nh\u00f3m \"{selectedGroup.GroupName}\" (VND/gi\u1edd):",
            selectedGroup.HourlyRate.ToString("0"));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (!decimal.TryParse(raw.Trim(), out var hourlyRate) || hourlyRate <= 0)
        {
            MessageBox.Show("Gi\u00e1 gi\u1edd ch\u01a1i kh\u00f4ng h\u1ee3p l\u1ec7.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentMemberHourlyRate = selectedGroup.MemberHourlyRate > 0
            ? selectedGroup.MemberHourlyRate
            : selectedGroup.HourlyRate;
        var memberRaw = PromptText(
            "Đổi giá giờ hội viên",
            $"Nhập giá giờ hội viên mới cho nhóm \"{selectedGroup.GroupName}\" (VND/giờ):",
            currentMemberHourlyRate.ToString("0"));
        if (string.IsNullOrWhiteSpace(memberRaw))
        {
            return;
        }
        if (!decimal.TryParse(memberRaw.Trim(), out var memberHourlyRate) || memberHourlyRate <= 0)
        {
            MessageBox.Show("Giá giờ hội viên không hợp lệ.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        HttpResponseMessage response;
        if (selectedGroup.IsDefault)
        {
            response = await _httpClient.PutAsJsonAsync(
                BuildApiUrl("/pricing/default-rate"),
                new
                {
                    hourlyRate = Convert.ToDouble(hourlyRate),
                    memberHourlyRate = Convert.ToDouble(memberHourlyRate),
                });
        }
        else
        {
            response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl($"/pricing/groups/{selectedGroup.GroupId}"),
                new
                {
                    hourlyRate = Convert.ToDouble(hourlyRate),
                    memberHourlyRate = Convert.ToDouble(memberHourlyRate),
                });
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Kh\u00f4ng th\u1ec3 c\u1eadp nh\u1eadt gi\u00e1 nh\u00f3m ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Cập nhật giá nhóm \"{selectedGroup.GroupName}\": Giá mặc định {hourlyRate:N0} VND/giờ | Giá hội viên {memberHourlyRate:N0} VND/giờ");
        _pricingSettings = null;
        await RefreshMachinesAsync();
    }

    private void GroupMachinesDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _groupMachineDragStartPoint = e.GetPosition(this);
        _draggingGroupMachine = TryGetDataGridRowItem<GroupMachineRow>(e.OriginalSource as DependencyObject);

        if (_draggingGroupMachine is not null)
        {
            if (!GroupMachinesDataGrid.SelectedItems.Contains(_draggingGroupMachine))
            {
                GroupMachinesDataGrid.SelectedItem = _draggingGroupMachine;
            }
            _selectedGroupMachinePcId = _draggingGroupMachine.PcId;
        }
    }

    private void GroupMachinesDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();
        if (row.Item is GroupMachineRow machine)
        {
            _selectedGroupMachinePcId = machine.PcId;
        }
    }

    private async void GroupMachinesDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            e.Handled = true;
            return;
        }

        GroupMachineRow? selectedMachine = dataGrid.SelectedItem as GroupMachineRow;
        if (selectedMachine is null)
        {
            var rowFromPoint = TryGetDataGridRowItem<GroupMachineRow>(e.OriginalSource as DependencyObject);
            if (rowFromPoint is not null)
            {
                dataGrid.SelectedItem = rowFromPoint;
                selectedMachine = rowFromPoint;
            }
        }

        if (selectedMachine is null)
        {
            e.Handled = true;
            return;
        }

        if (_groupSummaryRows.Count == 0)
        {
            await RefreshGroupsAsync(forceReloadPricing: true);
        }

        PopulateGroupMachinesContextMenu(selectedMachine);
    }

    private void PopulateGroupMachinesContextMenu(GroupMachineRow selectedMachine)
    {
        if (GroupMachinesAssignGroupMenuItem is null)
        {
            return;
        }

        GroupMachinesAssignGroupMenuItem.Items.Clear();
        var groups = _groupSummaryRows
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.GroupName)
            .ToList();

        if (groups.Count == 0)
        {
            GroupMachinesAssignGroupMenuItem.Items.Add(new MenuItem
            {
                Header = "Chưa có nhóm máy",
                IsEnabled = false,
            });
            return;
        }

        foreach (var group in groups)
        {
            var isCurrent = string.Equals(selectedMachine.GroupId, group.GroupId, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem
            {
                Header = $"{group.GroupName} (Mặc định {group.HourlyRate:N0}, Hội viên {group.MemberHourlyRate:N0})",
                IsCheckable = true,
                IsChecked = isCurrent,
                IsEnabled = !isCurrent,
                Tag = group,
            };

            item.Click += GroupMachinesAssignGroupDynamicMenuItem_Click;
            GroupMachinesAssignGroupMenuItem.Items.Add(item);
        }
    }

    private async void GroupMachinesAssignGroupDynamicMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not GroupSummaryRow targetGroup)
        {
            return;
        }

        if (GroupMachinesDataGrid.SelectedItem is not GroupMachineRow selectedMachine)
        {
            MessageBox.Show("Vui lòng chọn máy trước.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        GroupSummaryDataGrid.SelectedItem = targetGroup;
        await AssignMachineToGroupAsync(selectedMachine, targetGroup);
    }

    private void GroupSummaryDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();
        if (row.Item is GroupSummaryRow group)
        {
            _selectedGroupSummaryId = group.GroupId;
        }
    }

    private void GroupSummaryDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            e.Handled = true;
            return;
        }

        GroupSummaryRow? selectedGroup = dataGrid.SelectedItem as GroupSummaryRow;
        if (selectedGroup is null)
        {
            var rowFromPoint = TryGetDataGridRowItem<GroupSummaryRow>(e.OriginalSource as DependencyObject);
            if (rowFromPoint is not null)
            {
                dataGrid.SelectedItem = rowFromPoint;
                selectedGroup = rowFromPoint;
            }
        }

        var canEdit = selectedGroup is not null;
        if (GroupSummaryEditGuestRateMenuItem is not null)
        {
            GroupSummaryEditGuestRateMenuItem.IsEnabled = canEdit;
        }

        if (GroupSummaryEditMemberRateMenuItem is not null)
        {
            GroupSummaryEditMemberRateMenuItem.IsEnabled = canEdit;
        }

        if (GroupSummaryDeleteGroupMenuItem is not null)
        {
            GroupSummaryDeleteGroupMenuItem.IsEnabled = selectedGroup is not null && !selectedGroup.IsDefault;
        }

        if (!canEdit)
        {
            e.Handled = true;
        }
    }

    private async void GroupSummaryEditGuestRateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GroupSummaryDataGrid.SelectedItem is not GroupSummaryRow selectedGroup)
        {
            MessageBox.Show("Vui lòng chọn nhóm máy trước.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var raw = PromptText(
            "Sửa giá khách",
            $"Nhập giá khách mới cho nhóm \"{selectedGroup.GroupName}\" (VND/giờ):",
            selectedGroup.HourlyRate.ToString("0"));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (!decimal.TryParse(raw.Trim(), out var guestRate) || guestRate <= 0)
        {
            MessageBox.Show("Giá khách không hợp lệ.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        HttpResponseMessage response;
        if (selectedGroup.IsDefault)
        {
            response = await _httpClient.PutAsJsonAsync(
                BuildApiUrl("/pricing/default-rate"),
                new
                {
                    hourlyRate = Convert.ToDouble(guestRate),
                });
        }
        else
        {
            response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl($"/pricing/groups/{selectedGroup.GroupId}"),
                new
                {
                    hourlyRate = Convert.ToDouble(guestRate),
                });
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Cập nhật giá khách thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Sửa giá khách nhóm \"{selectedGroup.GroupName}\": {guestRate:N0} VND/giờ");
        _pricingSettings = null;
        await RefreshMachinesAsync();
    }

    private async void GroupSummaryEditMemberRateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GroupSummaryDataGrid.SelectedItem is not GroupSummaryRow selectedGroup)
        {
            MessageBox.Show("Vui lòng chọn nhóm máy trước.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var currentMemberHourlyRate = selectedGroup.MemberHourlyRate > 0
            ? selectedGroup.MemberHourlyRate
            : selectedGroup.HourlyRate;
        var raw = PromptText(
            "Sửa giá hội viên",
            $"Nhập giá hội viên mới cho nhóm \"{selectedGroup.GroupName}\" (VND/giờ):",
            currentMemberHourlyRate.ToString("0"));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (!decimal.TryParse(raw.Trim(), out var memberRate) || memberRate <= 0)
        {
            MessageBox.Show("Giá hội viên không hợp lệ.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        HttpResponseMessage response;
        if (selectedGroup.IsDefault)
        {
            response = await _httpClient.PutAsJsonAsync(
                BuildApiUrl("/pricing/default-rate"),
                new
                {
                    hourlyRate = Convert.ToDouble(selectedGroup.HourlyRate),
                    memberHourlyRate = Convert.ToDouble(memberRate),
                });
        }
        else
        {
            response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl($"/pricing/groups/{selectedGroup.GroupId}"),
                new
                {
                    memberHourlyRate = Convert.ToDouble(memberRate),
                });
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Cập nhật giá hội viên thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Sửa giá hội viên nhóm \"{selectedGroup.GroupName}\": {memberRate:N0} VND/giờ");
        _pricingSettings = null;
        await RefreshMachinesAsync();
    }

    private async void GroupSummaryDeleteGroupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GroupSummaryDataGrid.SelectedItem is not GroupSummaryRow selectedGroup)
        {
            MessageBox.Show("Vui lòng chọn nhóm máy trước.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selectedGroup.IsDefault)
        {
            MessageBox.Show("Không thể xóa nhóm mặc định.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Xóa nhóm \"{selectedGroup.GroupName}\"?\n\nCác máy trong nhóm sẽ được chuyển về nhóm mặc định.",
            "Server Admin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        using var response = await _httpClient.DeleteAsync(
            BuildApiUrl($"/pricing/groups/{selectedGroup.GroupId}"));

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show(
                $"Xóa nhóm thất bại ({(int)response.StatusCode})",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Xóa nhóm \"{selectedGroup.GroupName}\" và chuyển máy về nhóm mặc định");
        _pricingSettings = null;
        await RefreshMachinesAsync();
    }

    private void GroupSummaryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupSummaryDataGrid.SelectedItem is GroupSummaryRow row)
        {
            _selectedGroupSummaryId = row.GroupId;
        }
    }

    private void GroupMachinesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupMachinesDataGrid.SelectedItem is GroupMachineRow row)
        {
            _selectedGroupMachinePcId = row.PcId;
        }
    }

    private void GroupMachinesSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = GroupMachinesSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            GroupMachinesDataGrid.ItemsSource = _groupMachineRows;
        }
        else
        {
            var lower = text.ToLowerInvariant();
            GroupMachinesDataGrid.ItemsSource = _groupMachineRows.Where(x => 
                (x.MachineName?.ToLowerInvariant().Contains(lower) == true) ||
                (x.GroupName?.ToLowerInvariant().Contains(lower) == true) ||
                (x.IpAddress?.ToLowerInvariant().Contains(lower) == true)
            ).ToList();
        }
    }



    private void GroupMachinesDataGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggingGroupMachine is null)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - _groupMachineDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _groupMachineDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var selectedMachines = GroupMachinesDataGrid.SelectedItems.Cast<GroupMachineRow>().ToList();
        if (selectedMachines.Count == 0 || !selectedMachines.Contains(_draggingGroupMachine))
        {
            selectedMachines = new System.Collections.Generic.List<GroupMachineRow> { _draggingGroupMachine };
        }

        var payload = new DataObject(typeof(System.Collections.Generic.List<GroupMachineRow>), selectedMachines);
        DragDrop.DoDragDrop(GroupMachinesDataGrid, payload, DragDropEffects.Move);
        _draggingGroupMachine = null;
    }

    private void GroupSummaryDataGrid_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(System.Collections.Generic.List<GroupMachineRow>)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var target = GetGroupSummaryFromPoint(e.GetPosition(GroupSummaryDataGrid));
        e.Effects = target is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private async void GroupSummaryDataGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(System.Collections.Generic.List<GroupMachineRow>)))
        {
            return;
        }

        var sourceMachines = e.Data.GetData(typeof(System.Collections.Generic.List<GroupMachineRow>)) as System.Collections.Generic.List<GroupMachineRow>;
        var targetGroup = GetGroupSummaryFromPoint(e.GetPosition(GroupSummaryDataGrid));
        if (sourceMachines is null || sourceMachines.Count == 0 || targetGroup is null)
        {
            return;
        }

        GroupSummaryDataGrid.SelectedItem = targetGroup;
        await AssignMachinesToGroupAsync(sourceMachines, targetGroup);
    }

    private async Task AssignMachineToGroupAsync(GroupMachineRow machine, GroupSummaryRow targetGroup)
    {
        if (string.Equals(machine.GroupId, targetGroup.GroupId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            BuildApiUrl($"/pricing/pcs/{machine.PcId}/group"),
            new { groupId = targetGroup.GroupId });

        if (!response.IsSuccessStatusCode)
        {
            MessageBox.Show($"Chuyển nhóm máy thất bại ({(int)response.StatusCode})", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Chuyển {machine.MachineName} sang nhóm \"{targetGroup.GroupName}\"");
        _pricingSettings = null;
        await RefreshMachinesAsync();
    }

    private async Task AssignMachinesToGroupAsync(System.Collections.Generic.List<GroupMachineRow> machines, GroupSummaryRow targetGroup)
    {
        bool anyFailed = false;
        int successCount = 0;
        foreach (var machine in machines)
        {
            if (string.Equals(machine.GroupId, targetGroup.GroupId, StringComparison.OrdinalIgnoreCase)) continue;

            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/pricing/pcs/{machine.PcId}/group"),
                new { groupId = targetGroup.GroupId });

            if (!response.IsSuccessStatusCode)
            {
                AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi khi chuyển máy {machine.MachineName}: {(int)response.StatusCode}");
                anyFailed = true;
            }
            else
            {
                successCount++;
            }
        }

        if (successCount > 0)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã chuyển {successCount} máy sang nhóm \"{targetGroup.GroupName}\"");
        }
        
        if (anyFailed)
        {
            MessageBox.Show($"Chuyển nhóm máy hoàn tất nhưng có một số máy bị lỗi.", "Server Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _pricingSettings = null;
        await RefreshMachinesAsync();
    }

    private GroupSummaryRow? GetGroupSummaryFromPoint(Point point)
    {
        var source = GroupSummaryDataGrid.InputHitTest(point) as DependencyObject;
        
        var dataGridRow = FindAncestor<DataGridRow>(source);
        if (dataGridRow != null) return dataGridRow.Item as GroupSummaryRow;

        var listBoxItem = FindAncestor<ListBoxItem>(source);
        if (listBoxItem != null) return listBoxItem.Content as GroupSummaryRow;

        return null;
    }

    private static T? TryGetDataGridRowItem<T>(DependencyObject? source) where T : class
    {
        var row = FindAncestor<DataGridRow>(source);
        return row?.Item as T;
    }

    private void GroupCardOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is GroupSummaryRow groupRow)
        {
            GroupSummaryDataGrid.SelectedItem = groupRow;
            if (GroupSummaryDataGrid.ContextMenu != null)
            {
                GroupSummaryDeleteGroupMenuItem.IsEnabled = !groupRow.IsDefault;
                GroupSummaryDataGrid.ContextMenu.PlacementTarget = button;
                GroupSummaryDataGrid.ContextMenu.IsOpen = true;
            }
        }
    }
}

