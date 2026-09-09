using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MITAssetAgent.Core.Models;

namespace MITAssetAgent.Core.Services;

public interface ITokenStorageService
{
    void SaveToken(string token);
    string? LoadToken();
    void ClearToken();
}

/// <summary>Protects the device token with Windows DPAPI (LocalMachine scope for service accounts).</summary>
public sealed class DpapiTokenStorageService : ITokenStorageService
{
    private readonly AgentOptions _options;
    private readonly ILogger<DpapiTokenStorageService> _log;

    public DpapiTokenStorageService(IOptions<AgentOptions> options, ILogger<DpapiTokenStorageService> log)
    {
        _options = options.Value;
        _log = log;
    }

    private string TokenPath => Path.Combine(_options.DataDirectory, "token.dpapi");

    public void SaveToken(string token)
    {
        Directory.CreateDirectory(_options.DataDirectory);
        var bytes = Encoding.UTF8.GetBytes(token);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
        File.WriteAllBytes(TokenPath, protectedBytes);
        try { File.SetAttributes(TokenPath, FileAttributes.Hidden); } catch { }
        _log.LogInformation("Device token saved with DPAPI at {Path}", TokenPath);
    }

    public string? LoadToken()
    {
        if (!File.Exists(TokenPath)) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(TokenPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to unprotect device token");
            return null;
        }
    }

    public void ClearToken()
    {
        if (File.Exists(TokenPath)) File.Delete(TokenPath);
    }
}

public interface IIdentityStore
{
    AgentIdentity GetOrCreate(string fingerprint);
}

public sealed class FileIdentityStore : IIdentityStore
{
    private readonly AgentOptions _options;
    private readonly ILogger<FileIdentityStore> _log;

    public FileIdentityStore(IOptions<AgentOptions> options, ILogger<FileIdentityStore> log)
    {
        _options = options.Value;
        _log = log;
    }

    private string PathFile => Path.Combine(_options.DataDirectory, "identity.json");

    public AgentIdentity GetOrCreate(string fingerprint)
    {
        Directory.CreateDirectory(_options.DataDirectory);
        if (File.Exists(PathFile))
        {
            var existing = JsonSerializer.Deserialize<AgentIdentity>(File.ReadAllText(PathFile));
            if (existing is { AgentId.Length: > 0 })
            {
                if (!string.Equals(existing.DeviceFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogWarning("Fingerprint changed; keeping agentId {Id}", existing.AgentId);
                    existing.DeviceFingerprint = fingerprint;
                    File.WriteAllText(PathFile, JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }));
                }
                return existing;
            }
        }

        var id = new AgentIdentity
        {
            AgentId = Guid.NewGuid().ToString("N"),
            DeviceFingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(PathFile, JsonSerializer.Serialize(id, new JsonSerializerOptions { WriteIndented = true }));
        _log.LogInformation("Created agent identity {Id}", id.AgentId);
        return id;
    }
}
