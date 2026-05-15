using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private readonly HashSet<string> _memberWithdrawHandledRequestIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoadingPendingMemberWithdrawRequests;

    private void HandleRealtimeMemberWithdrawRequested(MemberWithdrawRequestItem? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RequestId))
        {
            return;
        }

        if (!_memberWithdrawHandledRequestIds.Add(request.RequestId))
        {
            return;
        }

        _ = ShowMemberWithdrawApprovalDialogAsync(request);
    }

    private async Task LoadPendingMemberWithdrawRequestsAsync()
    {
        if (_isLoadingPendingMemberWithdrawRequests)
        {
            return;
        }

        _isLoadingPendingMemberWithdrawRequests = true;
        try
        {
            var response = await _httpClient.GetFromJsonAsync<MemberWithdrawPendingListResponse>(
                BuildApiUrl("/members/withdraw-requests/pending"),
                JsonOptions());

            if (response?.Items is null || response.Items.Count == 0)
            {
                return;
            }

            foreach (var item in response.Items
                         .OrderBy(x => ParseDateLocal(x.RequestedAt) ?? DateTime.MinValue))
            {
                HandleRealtimeMemberWithdrawRequested(item);
            }
        }
        catch (Exception ex)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Không tải được danh sách chờ duyệt rút tiền: {ex.Message}");
        }
        finally
        {
            _isLoadingPendingMemberWithdrawRequests = false;
        }
    }

    private async Task ShowMemberWithdrawApprovalDialogAsync(MemberWithdrawRequestItem request)
    {
        if (!IsLoaded)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            var memberName = string.IsNullOrWhiteSpace(request.FullName)
                ? request.Username
                : $"{request.FullName} ({request.Username})";
            var machineLabel = string.IsNullOrWhiteSpace(request.PcName)
                ? (string.IsNullOrWhiteSpace(request.AgentId) ? "-" : request.AgentId)
                : request.PcName;

            var dialog = new Window
            {
                Title = "Yêu cầu rút tiền hội viên",
                Width = 480,
                Height = 340,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.SingleBorderWindow,
                Topmost = true,
            };
            var decisionSubmitted = false;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = "Hội viên gửi yêu cầu rút tiền",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
            };
            Grid.SetRow(titleText, 0);
            root.Children.Add(titleText);

            var memberText = new TextBlock
            {
                Text = $"Hội viên: {memberName}",
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = Brushes.Black,
            };
            Grid.SetRow(memberText, 1);
            root.Children.Add(memberText);

            var amountText = new TextBlock
            {
                Text = $"Số tiền rút: {request.Amount:N0} VND",
                Margin = new Thickness(0, 0, 0, 6),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Firebrick,
            };
            Grid.SetRow(amountText, 2);
            root.Children.Add(amountText);

            var machineText = new TextBlock
            {
                Text = $"Máy trạm: {machineLabel}",
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = Brushes.DimGray,
            };
            Grid.SetRow(machineText, 3);
            root.Children.Add(machineText);

            var noteText = new TextBlock
            {
                Text = $"Ghi chú: {(string.IsNullOrWhiteSpace(request.Note) ? "-" : request.Note)}",
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(noteText, 4);
            root.Children.Add(noteText);

            var timeText = new TextBlock
            {
                Text = $"Gửi lúc: {FormatDateTime(request.RequestedAt)}",
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = Brushes.DimGray,
            };
            Grid.SetRow(timeText, 5);
            root.Children.Add(timeText);

            var errorText = new TextBlock
            {
                Foreground = Brushes.Firebrick,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            Grid.SetRow(errorText, 6);
            root.Children.Add(errorText);

            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            var rejectButton = new Button
            {
                Content = "Hủy",
                Width = 96,
                Margin = new Thickness(0, 0, 8, 0),
            };

            var approveButton = new Button
            {
                Content = "Chấp nhận",
                Width = 108,
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
            };

            rejectButton.Click += async (_, _) =>
            {
                rejectButton.IsEnabled = false;
                approveButton.IsEnabled = false;
                errorText.Text = string.Empty;
                try
                {
                    using var response = await _httpClient.PostAsJsonAsync(
                        BuildApiUrl($"/members/withdraw-requests/{request.RequestId}/reject"),
                        new
                        {
                            rejectedBy = "admin.desktop",
                            note = "Admin hủy yêu cầu",
                        });

                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        errorText.Text = string.IsNullOrWhiteSpace(body)
                            ? $"Hủy yêu cầu thất bại ({(int)response.StatusCode})."
                            : body;
                        return;
                    }

                    AppendServiceLog(
                        $"[{DateTime.Now:HH:mm:ss}] Đã hủy yêu cầu rút tiền {request.RequestId} của {memberName}: {request.Amount:N0} VND");
                    decisionSubmitted = true;
                    dialog.DialogResult = false;
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    errorText.Text = $"Không kết nối được backend: {ex.Message}";
                }
                finally
                {
                    if (dialog.IsVisible)
                    {
                        rejectButton.IsEnabled = true;
                        approveButton.IsEnabled = true;
                    }
                }
            };

            approveButton.Click += async (_, _) =>
            {
                rejectButton.IsEnabled = false;
                approveButton.IsEnabled = false;
                errorText.Text = string.Empty;
                try
                {
                    using var response = await _httpClient.PostAsJsonAsync(
                        BuildApiUrl($"/members/withdraw-requests/{request.RequestId}/approve"),
                        new
                        {
                            approvedBy = "admin.desktop",
                        });

                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        errorText.Text = string.IsNullOrWhiteSpace(body)
                            ? $"Chấp nhận thất bại ({(int)response.StatusCode})."
                            : body;
                        return;
                    }

                    AppendServiceLog(
                        $"[{DateTime.Now:HH:mm:ss}] Đã chấp nhận rút tiền {request.RequestId} của {memberName}: {request.Amount:N0} VND");

                    _ = RefreshMembersAsync();
                    _ = RefreshMachinesAsync();
                    decisionSubmitted = true;
                    dialog.DialogResult = true;
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    errorText.Text = $"Không kết nối được backend: {ex.Message}";
                }
                finally
                {
                    if (dialog.IsVisible)
                    {
                        rejectButton.IsEnabled = true;
                        approveButton.IsEnabled = true;
                    }
                }
            };

            actionPanel.Children.Add(rejectButton);
            actionPanel.Children.Add(approveButton);
            Grid.SetRow(actionPanel, 7);
            root.Children.Add(actionPanel);

            dialog.Closing += (_, e) =>
            {
                if (decisionSubmitted)
                {
                    return;
                }

                e.Cancel = true;
                errorText.Text = "Vui lòng bấm \"Chấp nhận\" hoặc \"Hủy\" để xử lý yêu cầu.";
            };

            dialog.Content = root;
            _ = dialog.ShowDialog();
        });
    }
}
