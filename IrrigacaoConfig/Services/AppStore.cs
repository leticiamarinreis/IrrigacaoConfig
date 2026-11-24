using System.Collections.Concurrent;
using System.Text.Json;
using IrrigacaoConfig.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace IrrigacaoConfig.Services;

public interface IAppStore
{
    Task EnsureIndexesAsync(CancellationToken ct);
    Task<AppUser?> FindUserByEmailAsync(string email, CancellationToken ct);
    Task<AppUser?> FindUserByIdAsync(string id, CancellationToken ct);
    /// <summary>false se o e-mail já existe.</summary>
    Task<bool> TryInsertUserAsync(AppUser user, CancellationToken ct);
    Task DeleteUserAsync(string id, CancellationToken ct);

    Task<ProjectSettings?> GetSettingsAsync(string projectId, CancellationToken ct);
    Task<List<ProjectSettings>> GetSettingsByOwnerAsync(string ownerUserId, CancellationToken ct);
    /// <summary>false se o projeto já tem dono.</summary>
    Task<bool> TryInsertSettingsAsync(ProjectSettings settings, CancellationToken ct);
    Task SaveSettingsAsync(ProjectSettings settings, CancellationToken ct);
}

/// <summary>Usa o mesmo MongoDB do EC2 (banco separado: irrigacao_config).</summary>
public sealed class MongoAppStore : IAppStore
{
    private readonly IMongoCollection<AppUser> _users;
    private readonly IMongoCollection<ProjectSettings> _settings;

    public MongoAppStore(IOptions<StorageOptions> options)
    {
        var client = new MongoClient(options.Value.ConnectionString);
        var db = client.GetDatabase(options.Value.Database);
        _users = db.GetCollection<AppUser>("users");
        _settings = db.GetCollection<ProjectSettings>("project_settings");
    }

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        await _users.Indexes.CreateOneAsync(
            new CreateIndexModel<AppUser>(Builders<AppUser>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true, Name = "ux_email" }), cancellationToken: ct);
        await _settings.Indexes.CreateOneAsync(
            new CreateIndexModel<ProjectSettings>(Builders<ProjectSettings>.IndexKeys.Ascending(s => s.OwnerUserId),
                new CreateIndexOptions { Name = "ix_owner" }), cancellationToken: ct);
    }

    public async Task<AppUser?> FindUserByEmailAsync(string email, CancellationToken ct) =>
        await _users.Find(u => u.Email == email).FirstOrDefaultAsync(ct);

    public async Task<AppUser?> FindUserByIdAsync(string id, CancellationToken ct) =>
        await _users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);

    public async Task<bool> TryInsertUserAsync(AppUser user, CancellationToken ct)
    {
        try { await _users.InsertOneAsync(user, cancellationToken: ct); return true; }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }

    public Task DeleteUserAsync(string id, CancellationToken ct) =>
        _users.DeleteOneAsync(u => u.Id == id, ct);

    public async Task<ProjectSettings?> GetSettingsAsync(string projectId, CancellationToken ct) =>
        await _settings.Find(s => s.ProjectId == projectId).FirstOrDefaultAsync(ct);

    public Task<List<ProjectSettings>> GetSettingsByOwnerAsync(string ownerUserId, CancellationToken ct) =>
        _settings.Find(s => s.OwnerUserId == ownerUserId).ToListAsync(ct);

    public async Task<bool> TryInsertSettingsAsync(ProjectSettings settings, CancellationToken ct)
    {
        try { await _settings.InsertOneAsync(settings, cancellationToken: ct); return true; }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }

    public Task SaveSettingsAsync(ProjectSettings settings, CancellationToken ct) =>
        _settings.ReplaceOneAsync(s => s.ProjectId == settings.ProjectId, settings, cancellationToken: ct);
}

/// <summary>Somente para rodar localmente sem MongoDB. Perde tudo ao reiniciar.</summary>
public sealed class InMemoryAppStore : IAppStore
{
    private readonly ConcurrentDictionary<string, AppUser> _users = new();
    private readonly ConcurrentDictionary<string, string> _emailIndex = new();
    private readonly ConcurrentDictionary<string, ProjectSettings> _settings = new();

    // Cópia profunda para imitar o comportamento de um banco (sem referências compartilhadas).
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    public Task EnsureIndexesAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<AppUser?> FindUserByEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult(_emailIndex.TryGetValue(email, out var id) && _users.TryGetValue(id, out var u) ? Clone(u) : null);

    public Task<AppUser?> FindUserByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_users.TryGetValue(id, out var u) ? Clone(u) : null);

    public Task<bool> TryInsertUserAsync(AppUser user, CancellationToken ct)
    {
        if (!_emailIndex.TryAdd(user.Email, user.Id)) return Task.FromResult(false);
        _users[user.Id] = Clone(user);
        return Task.FromResult(true);
    }

    public Task DeleteUserAsync(string id, CancellationToken ct)
    {
        if (_users.TryRemove(id, out var u)) _emailIndex.TryRemove(u.Email, out _);
        return Task.CompletedTask;
    }

    public Task<ProjectSettings?> GetSettingsAsync(string projectId, CancellationToken ct) =>
        Task.FromResult(_settings.TryGetValue(projectId, out var s) ? Clone(s) : null);

    public Task<List<ProjectSettings>> GetSettingsByOwnerAsync(string ownerUserId, CancellationToken ct) =>
        Task.FromResult(_settings.Values.Where(s => s.OwnerUserId == ownerUserId).Select(Clone).ToList());

    public Task<bool> TryInsertSettingsAsync(ProjectSettings settings, CancellationToken ct) =>
        Task.FromResult(_settings.TryAdd(settings.ProjectId, Clone(settings)));

    public Task SaveSettingsAsync(ProjectSettings settings, CancellationToken ct)
    {
        _settings[settings.ProjectId] = Clone(settings);
        return Task.CompletedTask;
    }
}
