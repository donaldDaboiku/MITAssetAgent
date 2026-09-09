using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MITAssetAgent.Core.Models;

namespace MITAssetAgent.Core.Services;

public interface IAgentApiClient
{
    Task<RegistrationResult> RegisterAsync(AgentIdentity identity, DeviceInventory device, CancellationToken ct);
    Task<HeartbeatResult> SendHeartbeatAsync(string token, AgentIdentity identity, DeviceInventory device, CancellationToken ct);
}

public sealed class AgentApiClient : IAgentApiClient
{
    private readonly HttpClient _http;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentApiClient> _log;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AgentApiClient(HttpClient http, IOptions<AgentOptions> options, ILogger<AgentApiClient> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    private string Base => _options.SupabaseUrl.TrimEnd('/');

    public async Task<RegistrationResult> RegisterAsync(AgentIdentity identity, DeviceInventory device, CancellationToken ct)
    {
        var url = $"{Base}/functions/v1/register-agent";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("x-enrollment-key", _options.EnrollmentKey);
        req.Content = JsonContent.Create(new
        {
            agentId = identity.AgentId,
            hostname = device.Hostname,
            serialNumber = device.BiosSerial,
            macAddress = device.MacAddress,
            manufacturer = device.Manufacturer,
            model = device.Model,
            deviceFingerprint = device.DeviceFingerprint,
            workspaceId = _options.WorkspaceId,
            agentVersion = _options.AgentVersion,
            systemInformation = ToSystemInfo(device),
        }, options: JsonOpts);

        try
        {
            using var res = await _http.SendAsync(req, ct);
            var payload = await res.Content.ReadFromJsonAsync<RegisterResponse>(JsonOpts, ct);
            if (!res.IsSuccessStatusCode)
            {
                return new RegistrationResult { Ok = false, Error = payload?.Error ?? $"HTTP {(int)res.StatusCode}" };
            }
            return new RegistrationResult
            {
                Ok = true,
                AgentId = payload?.Agent?.AgentId ?? identity.AgentId,
                Token = payload?.Token,
                AssetTag = payload?.Agent?.AssetTag,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Registration failed");
            return new RegistrationResult { Ok = false, Error = ex.Message };
        }
    }

    public async Task<HeartbeatResult> SendHeartbeatAsync(string token, AgentIdentity identity, DeviceInventory device, CancellationToken ct)
    {
        var url = $"{Base}/functions/v1/agent-heartbeat";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new
        {
            agentId = identity.AgentId,
            hostname = device.Hostname,
            serialNumber = device.BiosSerial,
            macAddress = device.MacAddress,
            ipAddress = device.IpAddress,
            workspaceId = _options.WorkspaceId,
            agentVersion = _options.AgentVersion,
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            systemInformation = ToSystemInfo(device),
        }, options: JsonOpts);

        try
        {
            using var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
                return new HeartbeatResult { Ok = true, StatusCode = (int)res.StatusCode };

            var body = await res.Content.ReadAsStringAsync(ct);
            var queue = (int)res.StatusCode >= 500 || (int)res.StatusCode == 0;
            _log.LogWarning("Heartbeat HTTP {Code}: {Body}", (int)res.StatusCode, body);
            return new HeartbeatResult
            {
                Ok = false,
                StatusCode = (int)res.StatusCode,
                Error = body,
                ShouldQueue = queue || (int)res.StatusCode == 408 || (int)res.StatusCode == 429,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Heartbeat network failure — will queue");
            return new HeartbeatResult { Ok = false, Error = ex.Message, ShouldQueue = true };
        }
    }

    private static object ToSystemInfo(DeviceInventory d) => new
    {
        d.WindowsUsername,
        d.Manufacturer,
        d.Model,
        d.BiosSerial,
        d.BaseboardSerial,
        d.WindowsVersion,
        d.OsBuild,
        d.Cpu,
        d.TotalRamBytes,
        d.DiskSummary,
        d.BootTime,
        d.AdapterName,
        d.Gateway,
        d.IpAddress,
        d.MacAddress,
    };

    private sealed class RegisterResponse
    {
        public bool Ok { get; set; }
        public string? Token { get; set; }
        public string? Error { get; set; }
        public AgentDto? Agent { get; set; }
    }

    private sealed class AgentDto
    {
        public string? AgentId { get; set; }
        public string? AssetTag { get; set; }
    }
}
