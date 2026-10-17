using Microsoft.Data.Sqlite;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Web.Demo;

public sealed class DemoWorkspace
{
    public required string Id { get; init; }
    public required string ConnectionString { get; init; }
    public required SqliteConnection Keeper { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; set; }
    public required Association Association { get; init; }
    internal int Readers { get; set; }
}

// Each visitor owns a separate disposable database. No production data is copied into it.
public sealed class DemoWorkspaceStore : IDisposable
{
    private readonly Dictionary<string, DemoWorkspace> _workspaces = [];
    private readonly object _sync = new();
    private readonly SemaphoreSlim _creation = new(1, 1);
    private readonly TimeProvider _clock;
    private readonly ITimer _cleanup;
    private readonly int _capacity;

    public DemoWorkspaceStore(TimeProvider clock, IConfiguration configuration)
    {
        _clock = clock;
        _capacity = Math.Clamp(configuration.GetValue("PublicDemo:MaxSessions", 40), 1, 100);
        _cleanup = clock.CreateTimer(_ => CleanExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public DemoLease? Acquire(string? id)
    {
        lock (_sync)
        {
            if (id is null || !_workspaces.TryGetValue(id, out var workspace) || workspace.ExpiresAtUtc <= _clock.GetUtcNow()) return null;
            workspace.Readers++;
            return new DemoLease(workspace, () => Release(workspace));
        }
    }

    public async Task<DemoLease?> CreateAsync(CancellationToken cancellationToken)
    {
        await _creation.WaitAsync(cancellationToken);
        try
        {
            CleanExpired();
            lock (_sync) if (_workspaces.Count >= _capacity) return null;
            var id = Guid.NewGuid().ToString("N");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = "dachahub-demo-" + id, Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared, Pooling = false, DefaultTimeout = 10
            }.ToString();
            var keeper = new SqliteConnection(connectionString);
            try
            {
                await keeper.OpenAsync(cancellationToken);
                var association = await DemoData.SeedAsync(connectionString, _clock.GetUtcNow(), cancellationToken);
                var workspace = new DemoWorkspace
                {
                    Id = id, ConnectionString = connectionString, Keeper = keeper, Association = association,
                    ExpiresAtUtc = _clock.GetUtcNow().AddMinutes(PublicDemo.DurationMinutes), Readers = 1
                };
                lock (_sync) _workspaces.Add(id, workspace);
                return new DemoLease(workspace, () => Release(workspace));
            }
            catch { await keeper.DisposeAsync(); throw; }
        }
        finally { _creation.Release(); }
    }

    public void Expire(string? id)
    {
        lock (_sync)
            if (id is not null && _workspaces.TryGetValue(id, out var workspace))
                workspace.ExpiresAtUtc = _clock.GetUtcNow();
        CleanExpired();
    }

    private void Release(DemoWorkspace workspace)
    {
        lock (_sync) workspace.Readers--;
        CleanExpired();
    }

    private void CleanExpired()
    {
        lock (_sync)
            foreach (var item in _workspaces.Values.Where(x => x.Readers == 0 && x.ExpiresAtUtc <= _clock.GetUtcNow()).ToArray())
            {
                _workspaces.Remove(item.Id);
                item.Keeper.Dispose();
            }
    }

    public void Dispose()
    {
        _cleanup.Dispose();
        lock (_sync)
        {
            foreach (var workspace in _workspaces.Values) workspace.Keeper.Dispose();
            _workspaces.Clear();
        }
        _creation.Dispose();
    }
}

public sealed class DemoLease(DemoWorkspace workspace, Action release) : IDisposable
{
    public DemoWorkspace Workspace { get; } = workspace;
    private Action? _release = release;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
