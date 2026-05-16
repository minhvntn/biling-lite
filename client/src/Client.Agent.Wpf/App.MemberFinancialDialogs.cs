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
            Title = $"Chuyá»ƒn tiá»n - {activeSession.Username}",
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
            Text = "Chuyá»ƒn tiá»n cho há»™i viÃªn khÃ¡c",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"TÃ i khoáº£n gá»­i: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"Sá»‘ dÆ° hiá»‡n táº¡i: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var targetLabel = new TextBlock
        {
            Text = "TÃ i khoáº£n nháº­n:",
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
            Text = "Sá»‘ tiá»n chuyá»ƒn (VND):",
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
            Text = "Ghi chÃº (khÃ´ng báº¯t buá»™c):",
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
            Text = "Tá»‘i thiá»ƒu 1.000 VND cho má»—i láº§n chuyá»ƒn.",
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
            Content = "Há»§y",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var transferButton = new Button
        {
            Content = "Chuyá»ƒn tiá»n",
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
                errorTextBlock.Text = "Vui lÃ²ng nháº­p tÃ i khoáº£n nháº­n.";
                return;
            }

            if (string.Equals(targetUsername, sourceMember.Username, StringComparison.OrdinalIgnoreCase))
            {
                errorTextBlock.Text = "KhÃ´ng thá»ƒ chuyá»ƒn tiá»n cho chÃ­nh mÃ¬nh.";
                return;
            }

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "Sá»‘ tiá»n chuyá»ƒn khÃ´ng há»£p lá»‡.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Sá»‘ tiá»n chuyá»ƒn tá»‘i thiá»ƒu lÃ  1.000 VND.";
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
                        ? $"Chuyá»ƒn tiá»n tháº¥t báº¡i ({(int)response.StatusCode})"
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
                    $"CHUYá»‚N TIá»€N {amount:N0} -> {targetUsername} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Chuyá»ƒn tiá»n thÃ nh cÃ´ng.\n\nÄÃ£ chuyá»ƒn: {amount:N0} VND\nÄáº¿n: {targetUsername}\nSá»‘ dÆ° cÃ²n láº¡i: {nextBalance:N0} VND",
                    "Chuyá»ƒn tiá»n há»™i viÃªn",
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
            Title = $"RÃºt tiá»n - {activeSession.Username}",
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
            Text = "RÃºt tiá»n tá»« tÃ i khoáº£n há»™i viÃªn",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"TÃ i khoáº£n: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"Sá»‘ dÆ° hiá»‡n táº¡i: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var amountLabel = new TextBlock
        {
            Text = "Sá»‘ tiá»n rÃºt (VND):",
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
            Text = "Ghi chÃº (khÃ´ng báº¯t buá»™c):",
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
            Text = "Tá»‘i thiá»ƒu 1.000 VND cho má»—i láº§n rÃºt.",
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
            Content = "Há»§y",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var withdrawButton = new Button
        {
            Content = "RÃºt tiá»n",
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
                errorTextBlock.Text = "Sá»‘ tiá»n rÃºt khÃ´ng há»£p lá»‡.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Sá»‘ tiá»n rÃºt tá»‘i thiá»ƒu lÃ  1.000 VND.";
                return;
            }

            if (amount > sourceMember.Balance)
            {
                errorTextBlock.Text = "Sá»‘ dÆ° hiá»‡n táº¡i khÃ´ng Ä‘á»§ Ä‘á»ƒ rÃºt.";
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
                        ? $"RÃºt tiá»n tháº¥t báº¡i ({(int)response.StatusCode})"
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
                    $"ÄÃ£ gá»­i yÃªu cáº§u rÃºt tiá»n.\n\nSá»‘ tiá»n: {amount:N0} VND\nMÃ£ yÃªu cáº§u: {requestId}\nBÃªn app server sáº½ hiá»‡n popup cÃ³ nÃºt Cháº¥p nháº­n/Há»§y.",
                    "RÃºt tiá»n há»™i viÃªn",
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
            Title = $"Náº¡p tiá»n - {activeSession.Username}",
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
            Text = "Gá»­i yÃªu cáº§u náº¡p tiá»n há»™i viÃªn",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"TÃ i khoáº£n: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"Sá»‘ dÆ° hiá»‡n táº¡i: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var amountLabel = new TextBlock
        {
            Text = "Sá»‘ tiá»n cáº§n náº¡p (VND):",
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
            Text = "Ghi chÃº (khÃ´ng báº¯t buá»™c):",
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
            Text = "Tá»‘i thiá»ƒu 1.000 VND cho má»—i yÃªu cáº§u.",
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
            Content = "Há»§y",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var requestButton = new Button
        {
            Content = "Gá»­i yÃªu cáº§u",
            Width = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };

        requestButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "Sá»‘ tiá»n náº¡p khÃ´ng há»£p lá»‡.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Sá»‘ tiá»n náº¡p tá»‘i thiá»ƒu lÃ  1.000 VND.";
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
                        ? $"Gá»­i yÃªu cáº§u tháº¥t báº¡i ({(int)response.StatusCode})"
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
                    $"ÄÃ£ gá»­i yÃªu cáº§u náº¡p tiá»n.\n\nSá»‘ tiá»n: {amount:N0} VND\nMÃ£ yÃªu cáº§u: {requestId}\nBÃªn app server sáº½ hiá»‡n popup cÃ³ nÃºt Cháº¥p nháº­n/Há»§y.",
                    "Náº¡p tiá»n há»™i viÃªn",
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

        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(requestButton);
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
