using MITAssetAgent.Core.Models;
using MITAssetAgent.Core.Services;
using Microsoft.Extensions.Options;

namespace MITAssetAgent.Service;

public sealed class HeartbeatWorker : BackgroundService
{
    private readonly IHeartbeatService _heartbeat;
    private readonly AgentOptions _options;
    private readonly ILogger<HeartbeatWorker> _log;

    public HeartbeatWorker(
        IHeartbeatService heartbeat,
        IOptions<AgentOptions> options,
        ILogger<HeartbeatWorker> logger)
    {
        _heartbeat = heartbeat;
        _options = options.Value;
        _log = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = Math.Clamp(_options.HeartbeatIntervalMinutes, 1, 60);
        _log.LogInformation("Heartbeat worker interval {Minutes} minute(s)", minutes);

        // Initial pulse shortly after boot
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _heartbeat.PulseAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Heartbeat cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
