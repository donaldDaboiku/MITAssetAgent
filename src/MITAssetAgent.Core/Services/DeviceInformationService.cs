using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MITAssetAgent.Core.Models;

namespace MITAssetAgent.Core.Services;

public interface IDeviceInformationService
{
    DeviceInventory Collect();
}

public sealed class DeviceInformationService : IDeviceInformationService
{
    private readonly ILogger<DeviceInformationService> _log;

    public DeviceInformationService(ILogger<DeviceInformationService> log) => _log = log;

    public DeviceInventory Collect()
    {
        var net = PreferPhysicalAdapter();
        var bios = WmiFirst("Win32_BIOS", "SerialNumber");
        var board = WmiFirst("Win32_BaseBoard", "SerialNumber");
        var cs = WmiProps("Win32_ComputerSystem", "Manufacturer", "Model", "TotalPhysicalMemory");
        var os = WmiProps("Win32_OperatingSystem", "Caption", "BuildNumber", "LastBootUpTime");
        var cpu = WmiFirst("Win32_Processor", "Name");
        var disks = WmiList("Win32_LogicalDisk", "DeviceID", "Size", "FreeSpace");

        var inventory = new DeviceInventory
        {
            Hostname = Environment.MachineName,
            WindowsUsername = Environment.UserName,
            Manufacturer = cs.GetValueOrDefault("Manufacturer"),
            Model = cs.GetValueOrDefault("Model"),
            BiosSerial = CleanSerial(bios),
            BaseboardSerial = CleanSerial(board),
            MacAddress = net?.Mac,
            IpAddress = net?.Ip,
            Gateway = net?.Gateway,
            AdapterName = net?.Name,
            WindowsVersion = os.GetValueOrDefault("Caption"),
            OsBuild = os.GetValueOrDefault("BuildNumber"),
            Cpu = cpu,
            TotalRamBytes = long.TryParse(cs.GetValueOrDefault("TotalPhysicalMemory"), out var ram) ? ram : 0,
            DiskSummary = string.Join("; ", disks),
            BootTime = ParseWmiDate(os.GetValueOrDefault("LastBootUpTime")),
        };

        inventory.DeviceFingerprint = ComputeFingerprint(
            inventory.BiosSerial, inventory.BaseboardSerial, inventory.MacAddress);

        _log.LogInformation("Collected inventory for {Host} fingerprint {Fp}",
            inventory.Hostname, inventory.DeviceFingerprint[..Math.Min(12, inventory.DeviceFingerprint.Length)]);

        return inventory;
    }

    public static string ComputeFingerprint(string? bios, string? board, string? mac)
    {
        var raw = $"{CleanSerial(bios)}|{CleanSerial(board)}|{NormalizeMac(mac)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CleanSerial(string? value)
    {
        var s = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)
            || s.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)
            || s.Equals("None", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Default string", StringComparison.OrdinalIgnoreCase))
            return "UNKNOWN";
        return s;
    }

    private static string NormalizeMac(string? mac) =>
        string.Concat((mac ?? "").Where(Uri.IsHexDigit)).ToLowerInvariant();

    private sealed record NetInfo(string Name, string Mac, string? Ip, string? Gateway);

    private static NetInfo? PreferPhysicalAdapter()
    {
        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .Where(n => !IsVirtual(n))
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
            .ThenBy(n => n.Name)
            .ToList();

        foreach (var nic in nics)
        {
            var mac = nic.GetPhysicalAddress()?.ToString();
            if (string.IsNullOrWhiteSpace(mac) || mac.All(c => c == '0')) continue;
            var ipProps = nic.GetIPProperties();
            var ipv4 = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var gw = ipProps.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var formatted = string.Join(":", Enumerable.Range(0, mac.Length / 2)
                .Select(i => mac.Substring(i * 2, 2)));
            return new NetInfo(nic.Name, formatted, ipv4, gw);
        }
        return null;
    }

    private static bool IsVirtual(NetworkInterface n)
    {
        var desc = $"{n.Name} {n.Description}".ToLowerInvariant();
        string[] deny =
        [
            "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "loopback",
            "bluetooth", "vpn", "tap-windows", "wsl", "docker", "npcap"
        ];
        return deny.Any(d => desc.Contains(d));
    }

    private static string? WmiFirst(string cls, string prop)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {prop} FROM {cls}");
            foreach (ManagementObject obj in searcher.Get())
                return obj[prop]?.ToString();
        }
        catch { /* WMI may be restricted */ }
        return null;
    }

    private static Dictionary<string, string?> WmiProps(string cls, params string[] props)
    {
        var map = props.ToDictionary(p => p, _ => (string?)null);
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {string.Join(",", props)} FROM {cls}");
            foreach (ManagementObject obj in searcher.Get())
            {
                foreach (var p in props)
                    map[p] = obj[p]?.ToString();
                break;
            }
        }
        catch { }
        return map;
    }

    private static List<string> WmiList(string cls, params string[] props)
    {
        var list = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {string.Join(",", props)} FROM {cls}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var parts = props.Select(p => $"{p}={obj[p]}");
                list.Add(string.Join(",", parts));
            }
        }
        catch { }
        return list;
    }

    private static DateTimeOffset? ParseWmiDate(string? wmi)
    {
        if (string.IsNullOrWhiteSpace(wmi) || wmi.Length < 14) return null;
        try
        {
            return ManagementDateTimeConverter.ToDateTime(wmi);
        }
        catch { return null; }
    }
}
