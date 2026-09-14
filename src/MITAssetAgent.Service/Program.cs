using System.Diagnostics;
using System.Security;
using MITAssetAgent.Core;
using MITAssetAgent.Core.Models;
using MITAssetAgent.Core.Services;
using MITAssetAgent.Service;
using Serilog;
using Serilog.Core;

Directory.CreateDirectory(@"C:\ProgramData\MITAssetAgent\logs");

Log.Logger = CreateLogger();

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

/// <summary>
/// File logging always. Event Log is optional — SourceExists/CreateEventSource can throw
/// SecurityException for non-admin users (Security log ACL), which used to crash startup.
/// </summary>
static Logger CreateLogger()
{
    const string logFile = @"C:\ProgramData\MITAssetAgent\logs\agent-.log";
    const string eventSource = "MIT Asset Agent";

    var fileOnly = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.File(logFile, rollingInterval: RollingInterval.Day)
        .CreateLogger();

    try
    {
        // Installer should create the source as admin. Never use manageEventSource here —
        // that calls SourceExists and can throw on locked Security/State logs.
        if (EventLog.SourceExists(eventSource))
        {
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(logFile, rollingInterval: RollingInterval.Day)
                .WriteTo.EventLog(eventSource, manageEventSource: false)
                .CreateLogger();
        }
    }
    catch (SecurityException)
    {
        // Non-admin / restricted Event Log ACL — file logging only.
    }
    catch (InvalidOperationException)
    {
        // Source missing or Event Log unavailable.
    }

    return fileOnly;
}
