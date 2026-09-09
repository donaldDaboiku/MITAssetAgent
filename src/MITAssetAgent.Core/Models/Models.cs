namespace MITAssetAgent.Core.Models;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Supabase project URL, e.g. https://xxxx.supabase.co</summary>
    public string SupabaseUrl { get; set; } = "";

    public string WorkspaceId { get; set; } = "main";

    /// <summary>Org enrollment key — registration only (not used for heartbeats).</summary>
    public string EnrollmentKey { get; set; } = "";

    public int HeartbeatIntervalMinutes { get; set; } = 5;

    public string DataDirectory { get; set; } = @"C:\ProgramData\MITAssetAgent";

    public string AgentVersion { get; set; } = "1.0.0";
}

public sealed class DeviceInventory
{
    public string Hostname { get; set; } = "";
    public string? WindowsUsername { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? BiosSerial { get; set; }
    public string? BaseboardSerial { get; set; }
    public string? MacAddress { get; set; }
    public string? IpAddress { get; set; }
    public string? Gateway { get; set; }
    public string? AdapterName { get; set; }
    public string? WindowsVersion { get; set; }
    public string? OsBuild { get; set; }
    public string? Cpu { get; set; }
    public long TotalRamBytes { get; set; }
    public string? DiskSummary { get; set; }
    public DateTimeOffset? BootTime { get; set; }
    public string DeviceFingerprint { get; set; } = "";
}

public sealed class AgentIdentity
{
    public string AgentId { get; set; } = "";
    public string DeviceFingerprint { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RegistrationResult
{
    public bool Ok { get; set; }
    public string? AgentId { get; set; }
    public string? Token { get; set; }
    public string? AssetTag { get; set; }
    public string? Error { get; set; }
}

public sealed class HeartbeatResult
{
    public bool Ok { get; set; }
    public int StatusCode { get; set; }
    public string? Error { get; set; }
    public bool ShouldQueue { get; set; }
}
