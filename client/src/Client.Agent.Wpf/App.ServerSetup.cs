using System.Net;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Client.Agent.Wpf.Models;

namespace Client.Agent.Wpf;

public partial class App : Application
{
    public async Task<LoginAttemptResult> UpdateServerEndpointFromLockScreenAsync(string input)
    {
        if (!TryNormalizeServerUrl(input, out var normalized, out var error))
        {
            return new LoginAttemptResult(false, error);
        }

        if (IsLocalServerEndpoint(normalized))
        {
            return new LoginAttemptResult(false, "Khong dung localhost/127.0.0.1. Hay nhap IP may chu.");
        }

        try
        {
            using var timeoutCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6));
            var probeUrl = BuildApiUrlFromServerBase(normalized, "/settings");
            using var response = await _httpClient.GetAsync(probeUrl, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new LoginAttemptResult(
                    false,
                    $"Khong ket noi duoc server ({(int)response.StatusCode}).");
            }
        }
        catch (Exception ex)
        {
            return new LoginAttemptResult(false, $"Khong ket noi duoc server: {ex.Message}");
        }

        _settings.ServerUrl = normalized;
        SaveWritableSettings(_settings);
        await ReconnectSocketServiceAsync();

        _lockScreenWindow?.SetCurrentServerUrl(_settings.ServerUrl);
        return new LoginAttemptResult(true, $"Da luu server: {_settings.ServerUrl}");
    }

    private bool EnsureServerEndpointConfigured()
    {
        if (TryNormalizeServerUrl(_settings.ServerUrl, out var normalized, out _) &&
            !IsLocalServerEndpoint(normalized))
        {
            _settings.ServerUrl = normalized;
            return true;
        }

        return ShowServerEndpointSetupDialog();
    }

    private bool ShowServerEndpointSetupDialog()
    {
        var dialog = new Window
        {
            Title = "Cau hinh may chu",
            Width = 520,
            Height = 270,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.SingleBorderWindow,
            Topmost = true,
            ShowInTaskbar = true,
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Nhap IP may chu de su dung phan mem",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleText, 0);
        root.Children.Add(titleText);

        var descText = new TextBlock
        {
            Text = "Vi du: 192.168.1.50 hoac 192.168.1.50:9000",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(descText, 1);
        root.Children.Add(descText);

        var serverLabel = new TextBlock
        {
            Text = "IP may chu:",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(serverLabel, 2);
        root.Children.Add(serverLabel);

        var serverTextBox = new TextBox
        {
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = string.Empty,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(serverTextBox, 3);
        root.Children.Add(serverTextBox);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 22,
        };
        Grid.SetRow(errorTextBlock, 4);
        root.Children.Add(errorTextBlock);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var saveButton = new Button
        {
            Content = "Luu va tiep tuc",
            Width = 130,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        var exitButton = new Button
        {
            Content = "Thoat",
            Width = 100,
            IsCancel = true,
        };
        actions.Children.Add(saveButton);
        actions.Children.Add(exitButton);
        Grid.SetRow(actions, 6);
        root.Children.Add(actions);

        saveButton.Click += (_, _) =>
        {
            var raw = serverTextBox.Text.Trim();
            if (!TryNormalizeServerUrl(raw, out var normalized, out var error))
            {
                errorTextBlock.Text = error;
                return;
            }

            if (IsLocalServerEndpoint(normalized))
            {
                errorTextBlock.Text = "Khong dung localhost/127.0.0.1. Hay nhap IP may chu.";
                return;
            }

            _settings.ServerUrl = normalized;
            try
            {
                SaveWritableSettings(_settings);
            }
            catch (Exception ex)
            {
                errorTextBlock.Text = $"Khong luu duoc cau hinh: {ex.Message}";
                return;
            }

            dialog.DialogResult = true;
            dialog.Close();
        };

        exitButton.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };

        dialog.Content = root;
        serverTextBox.Focus();
        return dialog.ShowDialog() == true;
    }

    private static bool TryNormalizeServerUrl(
        string? input,
        out string normalizedUrl,
        out string error)
    {
        normalizedUrl = string.Empty;
        error = string.Empty;

        var raw = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Vui long nhap IP may chu.";
            return false;
        }

        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            raw = "http://" + raw;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            error = "IP/URL may chu khong hop le.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Chi ho tro http hoac https.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "Thieu host may chu.";
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        if (builder.Port <= 0)
        {
            builder.Port = 9000;
        }

        normalizedUrl = builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

    private static bool IsLocalServerEndpoint(string normalizedServerUrl)
    {
        if (!Uri.TryCreate(normalizedServerUrl, UriKind.Absolute, out var uri))
        {
            return true;
        }

        var host = uri.Host.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip))
        {
            return true;
        }

        return false;
    }

    private static string GetWritableSettingsPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directory = Path.Combine(root, "ServerManagerBilling");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "client-agent.settings.json");
    }

    private static void SaveWritableSettings(AgentSettings settings)
    {
        var payload = new
        {
            Agent = settings,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        File.WriteAllText(GetWritableSettingsPath(), json);
    }

    private static string BuildApiUrlFromServerBase(string serverBase, string path)
    {
        var normalizedBase = serverBase.TrimEnd('/');
        var apiBase = normalizedBase.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)
            ? normalizedBase
            : $"{normalizedBase}/api/v1";

        var baseUri = new Uri($"{apiBase.TrimEnd('/')}/");
        return new Uri(baseUri, path.TrimStart('/')).ToString();
    }
}
