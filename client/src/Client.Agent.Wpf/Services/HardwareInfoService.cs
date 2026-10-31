using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Client.Agent.Wpf.Services;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class HardwareInfoService
{
    public static object GetHardwareInfo()
    {
        return new
        {
            cpu = GetCpuName(),
            ram = GetTotalRamGb(),
            gpu = GetGpuName(),
            motherboard = GetMotherboardInfo(),
            storage = GetStorageInfo(),
            monitor = GetMonitorInfo(),
            lan = GetLanInfo(),
            temperature = GetTemperatureInfo()
        };
    }

    private static string GetCpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                name = Regex.Replace(name, @"\s+", " ");
                return name;
            }
        }
        catch { }
        return "Unknown CPU";
    }

    private static string GetTotalRamGb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out ulong bytes))
                {
                    double gb = bytes / (1024.0 * 1024.0 * 1024.0);
                    return $"{Math.Ceiling(gb)} GB";
                }
            }
        }
        catch { }
        return "Unknown RAM";
    }

    private static string GetGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            var gpus = new List<string>();
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    gpus.Add(name);
                }
            }

            // 1. Prioritize dedicated gaming / discrete GPUs (NVIDIA GeForce, RTX, GTX, AMD Radeon, Intel Arc)
            var dedicatedGpu = gpus.FirstOrDefault(g =>
                (g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                 g.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                 g.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                 g.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                 g.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                 g.Contains("Arc", StringComparison.OrdinalIgnoreCase)) &&
                !g.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Indirect", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Parsec", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("skyPC", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("SpaceDesk", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Basic", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Remote", StringComparison.OrdinalIgnoreCase));

            if (dedicatedGpu != null) return dedicatedGpu;

            // 2. Physical non-virtual GPUs (Intel UHD/Iris Xe, AMD Integrated)
            var physicalGpu = gpus.FirstOrDefault(g =>
                !g.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Basic", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Remote", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("LuminonCore", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Parsec", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Indirect", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("skyPC", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("SpaceDesk", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("TeamViewer", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("AnyDesk", StringComparison.OrdinalIgnoreCase));

            if (physicalGpu != null) return physicalGpu;
            if (gpus.Count > 0) return gpus[0];
        }
        catch { }
        return "Unknown GPU";
    }

    private static string GetMotherboardInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                string manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                string product = obj["Product"]?.ToString()?.Trim() ?? "";

                if (manufacturer.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase)) manufacturer = "MSI";
                else if (manufacturer.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase)) manufacturer = "ASUS";
                else if (manufacturer.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase)) manufacturer = "Gigabyte";
                else if (manufacturer.Contains("Colorful", StringComparison.OrdinalIgnoreCase)) manufacturer = "Colorful";
                else if (manufacturer.Contains("ASRock", StringComparison.OrdinalIgnoreCase)) manufacturer = "ASRock";

                return $"{manufacturer} {product}".Trim();
            }
        }
        catch { }
        return "Unknown Motherboard";
    }

    private static string GetStorageInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive");
            var drives = new List<string>();
            foreach (var obj in searcher.Get())
            {
                var model = obj["Model"]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(model)) continue;

                string sizeStr = string.Empty;
                if (ulong.TryParse(obj["Size"]?.ToString(), out ulong bytes) && bytes > 0)
                {
                    double gb = bytes / (1024.0 * 1024.0 * 1024.0);
                    sizeStr = gb >= 1000 ? $"{gb / 1024.0:0.#} TB" : $"{Math.Round(gb)} GB";
                }

                model = model.Replace("NVMe", "").Replace("SCSI Disk Device", "").Trim();
                drives.Add(string.IsNullOrEmpty(sizeStr) ? model : $"{model} ({sizeStr})");
            }

            if (drives.Count > 0)
            {
                var ssd = drives.FirstOrDefault(d =>
                    d.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                    d.Contains("Samsung", StringComparison.OrdinalIgnoreCase) ||
                    d.Contains("Kingston", StringComparison.OrdinalIgnoreCase) ||
                    d.Contains("KIOXIA", StringComparison.OrdinalIgnoreCase) ||
                    d.Contains("Crucial", StringComparison.OrdinalIgnoreCase) ||
                    d.Contains("WD", StringComparison.OrdinalIgnoreCase));

                return ssd ?? drives[0];
            }

            return "Diskless (BootROM)";
        }
        catch { }
        return "Diskless (BootROM)";
    }

    private static string GetMonitorInfo()
    {
        try
        {
            var monitors = new List<string>();

            // Query EDID UserFriendlyName from WmiMonitorID
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT UserFriendlyName FROM WmiMonitorID");
                foreach (var obj in searcher.Get())
                {
                    if (obj["UserFriendlyName"] is ushort[] nameChars)
                    {
                        var name = new string(nameChars.Where(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                        if (!string.IsNullOrEmpty(name) && !monitors.Contains(name))
                        {
                            monitors.Add(name);
                        }
                    }
                }
            }
            catch { }

            string resText = string.Empty;
            try
            {
                int width = (int)SystemParameters.PrimaryScreenWidth;
                int height = (int)SystemParameters.PrimaryScreenHeight;
                if (width > 0 && height > 0)
                {
                    resText = $"{width}x{height}";
                }
            }
            catch { }

            if (monitors.Count > 0)
            {
                var monitorNames = string.Join(" + ", monitors);
                return !string.IsNullOrEmpty(resText) ? $"{monitorNames} ({resText})" : monitorNames;
            }

            // Fallback to Win32_DesktopMonitor
            using var monitorSearcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_DesktopMonitor WHERE Caption IS NOT NULL");
            foreach (var obj in monitorSearcher.Get())
            {
                var caption = obj["Caption"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(caption) && caption != "Default Monitor" && caption != "Generic PnP Monitor")
                {
                    return !string.IsNullOrEmpty(resText) ? $"{caption} ({resText})" : caption;
                }
            }

            if (!string.IsNullOrEmpty(resText))
            {
                return $"Màn hình {resText}";
            }
        }
        catch { }
        return "Generic PnP Monitor";
    }

    private static string GetLanInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Speed, NetConnectionStatus FROM Win32_NetworkAdapter " +
                "WHERE PhysicalAdapter=True AND NetConnectionStatus = 2");

            var adapters = new List<string>();
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                if (name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("TAP", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ulong.TryParse(obj["Speed"]?.ToString(), out ulong speedBps) && speedBps > 0)
                {
                    if (speedBps >= 1_000_000_000)
                    {
                        double gbps = speedBps / 1_000_000_000.0;
                        adapters.Add($"{name} ({gbps:0.#} Gbps)");
                    }
                    else if (speedBps >= 1_000_000)
                    {
                        double mbps = speedBps / 1_000_000.0;
                        adapters.Add($"{name} ({mbps:0} Mbps)");
                    }
                    else
                    {
                        adapters.Add(name);
                    }
                }
                else
                {
                    adapters.Add(name);
                }
            }

            var ethernet = adapters.FirstOrDefault(a =>
                a.Contains("Ethernet", StringComparison.OrdinalIgnoreCase) ||
                a.Contains("GbE", StringComparison.OrdinalIgnoreCase) ||
                a.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
                a.Contains("Intel", StringComparison.OrdinalIgnoreCase));
            if (ethernet != null) return ethernet;

            if (adapters.Count > 0) return adapters[0];

            // Fallback to NetworkInterface
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                            !n.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) &&
                            !n.Description.Contains("Xbox", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nics.Count > 0)
            {
                var nic = nics.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet) ?? nics[0];
                var speedGbps = nic.Speed / 1_000_000_000.0;
                return speedGbps >= 1 ? $"{nic.Description} ({speedGbps:0.#} Gbps)" : nic.Description;
            }
        }
        catch { }
        return "Unknown LAN";
    }

    private static string GetTemperatureInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (var obj in searcher.Get())
            {
                if (uint.TryParse(obj["CurrentTemperature"]?.ToString(), out uint tempDeciKelvin))
                {
                    double celsius = (tempDeciKelvin / 10.0) - 273.15;
                    if (celsius > 10 && celsius < 120)
                    {
                        return $"{Math.Round(celsius)}°C";
                    }
                }
            }
        }
        catch { }

        // Fallback: CPU Load
        try
        {
            using var cpuSearcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
            foreach (var obj in cpuSearcher.Get())
            {
                if (int.TryParse(obj["LoadPercentage"]?.ToString(), out int load))
                {
                    return $"Tải: {load}%";
                }
            }
        }
        catch { }

        return "Ổn định";
    }
}
