using MITAssetAgent.Core;
using MITAssetAgent.Core.Models;
using MITAssetAgent.Core.Services;
using MITAssetAgent.Service;
using Serilog;

Directory.CreateDirectory(@"C:\ProgramData\MITAssetAgent\logs");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(@"C:\ProgramData\MITAssetAgent\logs\agent-.log", rollingInterval: RollingInterval.Day)
    .WriteTo.EventLog("MIT Asset Agent", manageEventSource: true)
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "MITAssetAgent";
    });
    builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
    builder.Services.AddMitAssetAgentCore();
    builder.Services.AddHostedService<HeartbeatWorker>();
    builder.Services.AddSerilog();

    var host = builder.Build();
    Log.Information("MIT Asset Agent starting");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "MIT Asset Agent terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
