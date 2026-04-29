using System.Text;

namespace Client.Watchdog.Service.Services;

public sealed class FileLogger
{
    private readonly string _path;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileLogger(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public async Task WriteInfoAsync(string message)
    {
        await WriteAsync("INFO", message);
    }

    public async Task WriteErrorAsync(string message, Exception? exception = null)
    {
        var finalMessage = exception is null ? message : $"{message} | {exception}";
        await WriteAsync("ERROR", finalMessage);
    }

    private async Task WriteAsync(string level, string message)
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
