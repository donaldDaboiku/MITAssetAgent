using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MITAssetAgent.Core.Models;

namespace MITAssetAgent.Core.Services;

public interface IRegistrationService
{
    Task EnsureRegisteredAsync(CancellationToken ct);
}

public sealed class RegistrationService : IRegistrationService
{
    private readonly IDeviceInformationService _devices;
    private readonly IIdentityStore _identity;
    private readonly ITokenStorageService _tokens;
    private readonly IAgentApiClient _api;
    private readonly AgentOptions _options;
    private readonly ILogger<RegistrationService> _log;

    public RegistrationService(
        IDeviceInformationService devices,
        IIdentityStore identity,
        ITokenStorageService tokens,
        IAgentApiClient api,
        IOptions<AgentOptions> options,
        ILogger<RegistrationService> log)
    {
        _devices = devices;
        _identity = identity;
        _tokens = tokens;
        _api = api;
        _options = options.Value;
        _log = log;
    }

    public async Task EnsureRegisteredAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_tokens.LoadToken()))
        {
            _log.LogDebug("Device token already present — skipping registration");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SupabaseUrl) || string.IsNullOrWhiteSpace(_options.EnrollmentKey))
            throw new InvalidOperationException("SupabaseUrl and EnrollmentKey must be configured before registration.");

        var device = _devices.Collect();
        var identity = _identity.GetOrCreate(device.DeviceFingerprint);
        _log.LogInformation("Registering agent {AgentId} with workspace {Ws}", identity.AgentId, _options.WorkspaceId);

        var result = await _api.RegisterAsync(identity, device, ct);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.Token))
            throw new InvalidOperationException(result.Error ?? "Registration failed");

        _tokens.SaveToken(result.Token);
        _log.LogInformation("Registered successfully. Linked asset tag: {Tag}", result.AssetTag ?? "(none)");
    }
}

public interface IHeartbeatService
{
    Task PulseAsync(CancellationToken ct);
}

public sealed class HeartbeatService : IHeartbeatService
{
    private readonly IDeviceInformationService _devices;
    private readonly IIdentityStore _identity;
    private readonly ITokenStorageService _tokens;
    private readonly IAgentApiClient _api;
    private readonly IOfflineQueueService _queue;
    private readonly IRegistrationService _registration;
    private readonly ILogger<HeartbeatService> _log;
    private int _backoffSeconds;

    public HeartbeatService(
        IDeviceInformationService devices,
        IIdentityStore identity,
        ITokenStorageService tokens,
        IAgentApiClient api,
        IOfflineQueueService queue,
        IRegistrationService registration,
        ILogger<HeartbeatService> log)
    {
        _devices = devices;
        _identity = identity;
        _tokens = tokens;
        _api = api;
        _queue = queue;
        _registration = registration;
        _log = log;
    }

    public async Task PulseAsync(CancellationToken ct)
    {
        await _registration.EnsureRegisteredAsync(ct);
        var token = _tokens.LoadToken();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("No device token available");

        var device = _devices.Collect();
        var identity = _identity.GetOrCreate(device.DeviceFingerprint);

        var result = await _api.SendHeartbeatAsync(token, identity, device, ct);
        if (result.Ok)
        {
            _backoffSeconds = 0;
            _log.LogInformation("Heartbeat OK for {AgentId} ip={Ip}", identity.AgentId, device.IpAddress);
            await FlushQueueAsync(token, identity, device, ct);
            return;
        }

        if (result.StatusCode is 401 or 403)
        {
            _log.LogError("Auth failure ({Code}) — clearing local token so re-enrollment can occur", result.StatusCode);
            _tokens.ClearToken();
            return;
        }

        if (result.ShouldQueue)
        {
            var payload = JsonSerializer.Serialize(new { queuedAt = DateTimeOffset.UtcNow });
            _queue.Enqueue(payload);
            _backoffSeconds = _backoffSeconds <= 0 ? 30 : Math.Min(_backoffSeconds * 2, 900);
            _log.LogWarning("Heartbeat failed; queued. Next backoff ~{Sec}s", _backoffSeconds);
            if (_backoffSeconds > 30)
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(_backoffSeconds, 60)), ct);
        }
    }

    private async Task FlushQueueAsync(string token, AgentIdentity identity, DeviceInventory device, CancellationToken ct)
    {
        foreach (var (id, _) in _queue.PeekBatch())
        {
            var r = await _api.SendHeartbeatAsync(token, identity, device, ct);
            if (!r.Ok) break;
            _queue.Remove(id);
        }
    }
}
