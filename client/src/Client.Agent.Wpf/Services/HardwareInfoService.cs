using System;
using System.Management;

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
                return obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
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
                    return $"{Math.Ceiling(gb)}GB";
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
            var gpus = new System.Collections.Generic.List<string>();
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    gpus.Add(name);
                }
            }

            var physicalGpu = gpus.FirstOrDefault(g => 
                !g.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && 
                !g.Contains("Basic", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Remote", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("LuminonCore", StringComparison.OrdinalIgnoreCase) &&
                !g.Contains("Parsec", StringComparison.OrdinalIgnoreCase));
            
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
                return $"{manufacturer} {product}".Trim();
            }
        }
        catch { }
        return "Unknown Motherboard";
    }

    private static string GetMonitorInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_DesktopMonitor WHERE Caption IS NOT NULL");
            foreach (var obj in searcher.Get())
            {
                var caption = obj["Caption"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(caption) && caption != "Default Monitor")
                {
                    return caption;
                }
            }
        }
        catch { }
        return "Unknown Monitor";
    }

    private static string GetLanInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, MACAddress FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True AND MACAddress IS NOT NULL");
            foreach (var obj in searcher.Get())
            {
                return obj["Name"]?.ToString()?.Trim() ?? "Unknown LAN";
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
                    return $"{Math.Round(celsius)} °C";
                }
            }
        }
        catch { }
        return "N/A";
    }
}
