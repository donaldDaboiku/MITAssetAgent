using Microsoft.Extensions.DependencyInjection;
using MITAssetAgent.Core.Services;

namespace MITAssetAgent.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMitAssetAgentCore(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceInformationService, DeviceInformationService>();
        services.AddSingleton<ITokenStorageService, DpapiTokenStorageService>();
        services.AddSingleton<IIdentityStore, FileIdentityStore>();
        services.AddSingleton<IOfflineQueueService, SqliteOfflineQueueService>();
        services.AddSingleton<IRegistrationService, RegistrationService>();
        services.AddSingleton<IHeartbeatService, HeartbeatService>();
        services.AddHttpClient<IAgentApiClient, AgentApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        return services;
    }
}
