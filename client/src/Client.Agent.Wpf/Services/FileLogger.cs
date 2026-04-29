using System.Text;
using System.IO;

namespace Client.Agent.Wpf.Services;

public sealed class FileLogger
{
    private readonly string _path;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileLogger(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public async Task InfoAsync(string message)
    {
        await WriteLineAsync("INFO", message);
    }

    public async Task ErrorAsync(string message, Exception? exception = null)
    {
        var details = exception is null ? message : $"{message} | {exception}";
        await WriteLineAsync("ERROR", details);
    }

    private async Task WriteLineAsync(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";

        await _semaphore.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_path, line, Encoding.UTF8);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
