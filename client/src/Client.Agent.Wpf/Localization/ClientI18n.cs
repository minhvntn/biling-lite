using System.Text.Json;
using System.IO;

namespace Client.Agent.Wpf.Localization;

public static class ClientI18n
{
    private static readonly object SyncRoot = new();
    private static IReadOnlyDictionary<string, string>? _cached;

    public static string Get(string key, string fallback)
    {
        var table = EnsureLoaded();
        return table.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static IReadOnlyDictionary<string, string> EnsureLoaded()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        lock (SyncRoot)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var filePath = Path.Combine(AppContext.BaseDirectory, "i18n", "vi.json");
            if (!File.Exists(filePath))
            {
                _cached = new Dictionary<string, string>();
                return _cached;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                _cached = parsed ?? new Dictionary<string, string>();
            }
            catch
            {
                _cached = new Dictionary<string, string>();
            }

            return _cached;
        }
    }
}
