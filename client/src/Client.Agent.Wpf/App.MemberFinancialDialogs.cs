using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Client.Agent.Wpf;

public partial class App : Application
{
    private void ShowTransferBalanceDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        var dialog = new Window
        {
            Title = $"Chuyển tiền - {activeSession.Username}",
            Width = 460,
            Height = 480,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
        };
        for (var i = 0; i < 11; i++)
        {
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
            });
        }
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });

        var titleBlock = new TextBlock
        {
            Text = "Chuyển tiền cho hội viên khác",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"Tài khoản gửi: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"Số dư hiện tại: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var targetLabel = new TextBlock
        {
            Text = "Tài khoản nhận:",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(targetLabel, 3);
        root.Children.Add(targetLabel);

        var targetUsernameBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(targetUsernameBox, 4);
        root.Children.Add(targetUsernameBox);

        var amountLabel = new TextBlock
        {
            Text = "Số tiền chuyển (VND):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(amountLabel, 5);
        root.Children.Add(amountLabel);

        var amountBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "1000",
        };
        Grid.SetRow(amountBox, 6);
        root.Children.Add(amountBox);

        var noteLabel = new TextBlock
        {
            Text = "Ghi chú (không bắt buộc):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(noteLabel, 7);
        root.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(noteBox, 8);
        root.Children.Add(noteBox);

        var hintBlock = new TextBlock
        {
            Text = "Tối thiểu 1.000 VND cho mỗi lần chuyển.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(hintBlock, 9);
        root.Children.Add(hintBlock);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(errorTextBlock, 10);
        root.Children.Add(errorTextBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var transferButton = new Button
        {
            Content = "Chuyển tiền",
            Width = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };

        transferButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;
            var targetUsername = targetUsernameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                errorTextBlock.Text = "Vui lòng nhập tài khoản nhận.";
                return;
            }

            if (string.Equals(targetUsername, sourceMember.Username, StringComparison.OrdinalIgnoreCase))
            {
                errorTextBlock.Text = "Không thể chuyển tiền cho chính mình.";
                return;
            }

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "Số tiền chuyển không hợp lệ.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Số tiền chuyển tối thiểu là 1.000 VND.";
                return;
            }

            transferButton.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/transfer"),
                    new
                    {
                        targetUsername,
                        amount = Convert.ToDouble(amount, CultureInfo.InvariantCulture),
                        note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim(),
                        createdBy = "client.member.transfer",
                        agentId = _settings.AgentId,
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"Chuyển tiền thất bại ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberTransferBalanceResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                var nextBalance = payload?.SourceMember?.Balance ?? Math.Max(0, sourceMember.Balance - amount);
                _mainWindow?.SetLastCommand(
                    $"CHUYỂN TIỀN {amount:N0} -> {targetUsername} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Chuyển tiền thành công.\n\nĐã chuyển: {amount:N0} VND\nĐến: {targetUsername}\nSố dư còn lại: {nextBalance:N0} VND",
                    "Chuyển tiền hội viên",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                transferButton.IsEnabled = true;
            }
        };

        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(transferButton);
        Grid.SetRow(actionPanel, 12);
        root.Children.Add(actionPanel);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            targetUsernameBox.Focus();
            amountBox.SelectAll();
        };

        dialog.ShowDialog();
    }

    private void ShowWithdrawBalanceDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        var dialog = new Window
        {
            Title = $"Rút tiền - {activeSession.Username}",
            Width = 430,
            Height = 360,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
        };
        for (var i = 0; i < 9; i++)
        {
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
            });
        }
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });

        var titleBlock = new TextBlock
        {
            Text = "Rút tiền từ tài khoản hội viên",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"Tài khoản: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"Số dư hiện tại: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var amountLabel = new TextBlock
        {
            Text = "Số tiền rút (VND):",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(amountLabel, 3);
        root.Children.Add(amountLabel);

        var amountBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "1000",
        };
        Grid.SetRow(amountBox, 4);
        root.Children.Add(amountBox);

        var noteLabel = new TextBlock
        {
            Text = "Ghi chú (không bắt buộc):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(noteLabel, 5);
        root.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(noteBox, 6);
        root.Children.Add(noteBox);

        var hintBlock = new TextBlock
        {
            Text = "Tối thiểu 1.000 VND cho mỗi lần rút.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(hintBlock, 7);
        root.Children.Add(hintBlock);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(errorTextBlock, 8);
        root.Children.Add(errorTextBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var withdrawButton = new Button
        {
            Content = "Rút tiền",
            Width = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };

        withdrawButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "Số tiền rút không hợp lệ.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Số tiền rút tối thiểu là 1.000 VND.";
                return;
            }

            if (amount > sourceMember.Balance)
            {
                errorTextBlock.Text = "Số dư hiện tại không đủ để rút.";
                return;
            }

            withdrawButton.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/withdraw"),
                    new
                    {
                        amount = Convert.ToDouble(amount, CultureInfo.InvariantCulture),
                        note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim(),
                        createdBy = "client.member.withdraw",
                        agentId = _settings.AgentId,
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"Rút tiền thất bại ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberWithdrawRequestResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                var requestId = payload?.Request?.RequestId ?? "-";

                _mainWindow?.SetLastCommand(
                    $"GUI YEU CAU RUT TIEN {amount:N0} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Đã gửi yêu cầu rút tiền.\n\nSố tiền: {amount:N0} VND\nMã yêu cầu: {requestId}\nBên app server sẽ hiện popup có nút Chấp nhận/Hủy.",
                    "Rút tiền hội viên",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                withdrawButton.IsEnabled = true;
            }
        };

        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(withdrawButton);
        Grid.SetRow(actionPanel, 10);
        root.Children.Add(actionPanel);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            amountBox.Focus();
            amountBox.SelectAll();
        };

        dialog.ShowDialog();
    }

    private void ShowTopupRequestDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        var dialog = new Window
        {
            Title = $"Nạp tiền - {activeSession.Username}",
            Width = 650,
            Height = 420,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var mainGrid = new Grid
        {
            Margin = new Thickness(16),
        };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });

        var formGrid = new Grid
        {
            Margin = new Thickness(0, 0, 16, 0)
        };
        for (var i = 0; i < 9; i++)
        {
            formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        formGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = "Gửi yêu cầu nạp tiền hội viên",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        formGrid.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"Tài khoản: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        formGrid.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"Số dư hiện tại: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        formGrid.Children.Add(balanceBlock);

        var amountLabel = new TextBlock
        {
            Text = "Số tiền cần nạp (VND):",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(amountLabel, 3);
        formGrid.Children.Add(amountLabel);

        var amountBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "20000",
        };
        Grid.SetRow(amountBox, 4);
        formGrid.Children.Add(amountBox);

        var noteLabel = new TextBlock
        {
            Text = "Ghi chú (không bắt buộc):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(noteLabel, 5);
        formGrid.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(noteBox, 6);
        formGrid.Children.Add(noteBox);

        var hintBlock = new TextBlock
        {
            Text = "Tối thiểu 1.000 VND cho mỗi yêu cầu.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(hintBlock, 7);
        formGrid.Children.Add(hintBlock);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(errorTextBlock, 8);
        formGrid.Children.Add(errorTextBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var requestButton = new Button
        {
            Content = "Gửi yêu cầu",
            Width = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };

        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(requestButton);
        Grid.SetRow(actionPanel, 10);
        formGrid.Children.Add(actionPanel);

        // QR Panel
        var qrGrid = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        
        var qrTitle = new TextBlock
        {
            Text = "Quét mã để nạp tiền",
            FontSize = 16,
            FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        qrGrid.Children.Add(qrTitle);

        var qrImage = new Image
        {
            Width = 200,
            Height = 200,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 0, 10)
        };
        qrGrid.Children.Add(qrImage);

        var qrHintBlock = new TextBlock
        {
            Text = "Mã QR sẽ tự động cập nhật số tiền\nSau khi chuyển khoản, bấm Gửi yêu cầu",
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.DimGray,
            FontSize = 12
        };
        qrGrid.Children.Add(qrHintBlock);

        Grid.SetColumn(formGrid, 0);
        mainGrid.Children.Add(formGrid);

        Grid.SetColumn(qrGrid, 1);
        mainGrid.Children.Add(qrGrid);

        Action updateQrImage = () =>
        {
            if (TryParsePositiveMoney(amountBox.Text.Trim(), out var amount) && amount >= 1000)
            {
                var memo = $"NAP {sourceMember.Username}";
                var url = $"https://img.vietqr.io/image/{_vietQrBankId}-{_vietQrAccountNo}-compact2.png?amount={(int)amount}&addInfo={Uri.EscapeDataString(memo)}&accountName={Uri.EscapeDataString(_vietQrAccountName)}";
                
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                qrImage.Source = bitmap;
            }
            else
            {
                qrImage.Source = null;
            }
        };

        amountBox.TextChanged += (_, _) => updateQrImage();
        
        requestButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "Số tiền nạp không hợp lệ.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Số tiền nạp tối thiểu là 1.000 VND.";
                return;
            }

            requestButton.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/topup-request"),
                    new
                    {
                        amount = Convert.ToDouble(amount, CultureInfo.InvariantCulture),
                        note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim(),
                        createdBy = "client.member.topup.request",
                        agentId = _settings.AgentId,
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"Gửi yêu cầu thất bại ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberTopupRequestResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                var requestId = payload?.Request?.RequestId ?? "-";

                _mainWindow?.SetLastCommand(
                    $"GUI YEU CAU NAP TIEN {amount:N0} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Đã gửi yêu cầu nạp tiền.\n\nSố tiền: {amount:N0} VND\nMã yêu cầu: {requestId}\nBên app server sẽ hiện popup có nút Chấp nhận/Hủy.",
                    "Nạp tiền hội viên",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                requestButton.IsEnabled = true;
            }
        };

        dialog.Content = mainGrid;
        dialog.Loaded += (_, _) =>
        {
            amountBox.Focus();
            amountBox.SelectAll();
            updateQrImage(); // Initialize QR image on load
        };

        dialog.ShowDialog();
    }

    private static bool TryParsePositiveMoney(string value, out decimal amount)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) && amount > 0)
        {
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0)
        {
            return true;
        }

        amount = 0;
        return false;
    }

}

