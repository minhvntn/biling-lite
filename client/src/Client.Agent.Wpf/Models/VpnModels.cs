namespace Client.Agent.Wpf.Models;

public enum VpnStatus
{
    NotInstalled,
    Installing,
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Error
}

public sealed class VpnSettings
{
    public bool Enabled { get; set; } = true;
    public string LocalInstallerPath { get; set; } = @"D:\Games\Tools\Cloudflare_WARP_Release-x64.msi";
    public string DownloadUrl { get; set; } = "https://1111-releases.cloudflareclient.com/windows/Cloudflare_WARP_Release-x64.msi";
    public bool AutoDisconnectOnSessionEnd { get; set; } = true;
}
