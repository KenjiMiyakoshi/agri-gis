using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Npgsql;

namespace AgriGis.Api.Services;

// D'301 (WD'3): PostgreSQL LISTEN/NOTIFY を購読し、購読者に配信する singleton broker。
// API 起動時に 1 つの永続接続で LISTEN agri_gis_layer_invalidate、内部 Channel に push、
// SubscribeAsync(layerId) で IAsyncEnumerable<LayerInvalidationEvent> として 取り出す。
// 直近 5 秒分の event は ReplayRecent(layerId) で再送 (reconnect 取りこぼし対策)。

public sealed record LayerInvalidationEvent(
    [property: JsonPropertyName("layerId")] int LayerId,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("styleVersion")] int? StyleVersion,
    [property: JsonPropertyName("occurredAt")] DateTime OccurredAt);

public interface ILayerInvalidationBroker
{
    IAsyncEnumerable<LayerInvalidationEvent> SubscribeAsync(int layerId, CancellationToken ct);
    IEnumerable<LayerInvalidationEvent> ReplayRecent(int layerId, TimeSpan window);
}

public sealed class PostgresLayerInvalidationBroker
    : ILayerInvalidationBroker, IHostedService, IAsyncDisposable
{
    private readonly string _connStr;
    private readonly List<Channel<LayerInvalidationEvent>> _subscribers = new();
    private readonly object _subscribersLock = new();
    private readonly Queue<LayerInvalidationEvent> _replay = new();
    private readonly object _replayLock = new();
    private NpgsqlConnection? _conn;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public PostgresLayerInvalidationBroker(IConfiguration cfg)
    {
        _connStr = Environment.GetEnvironmentVariable("AGRI_GIS_DB")
                   ?? cfg.GetConnectionString("AgriGis")
                   ?? "Host=localhost;Port=5432;Database=agri_gis;Username=agri_user;Password=agri_pass";
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _conn = new NpgsqlConnection(_connStr);
        _conn.Notification += OnNotification;
        try
        {
            await _conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("LISTEN agri_gis_layer_invalidate", _conn);
            await cmd.ExecuteNonQueryAsync(ct);
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            // DB 未起動でも API 起動失敗にしない (開発時の柔軟性)
            Console.Error.WriteLine($"[LayerInvalidationBroker] LISTEN start failed: {ex.Message}");
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _conn is not null)
        {
            try
            {
                await _conn.WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LayerInvalidationBroker] WaitAsync error: {ex.Message}");
                await Task.Delay(1000, ct);
            }
        }
    }

    private void OnNotification(object _, NpgsqlNotificationEventArgs args)
    {
        try
        {
            var ev = JsonSerializer.Deserialize<LayerInvalidationEvent>(args.Payload);
            if (ev is null) return;
            lock (_replayLock)
            {
                _replay.Enqueue(ev);
                while (_replay.Count > 100) _replay.Dequeue();
            }
            List<Channel<LayerInvalidationEvent>> snapshot;
            lock (_subscribersLock) snapshot = new(_subscribers);
            foreach (var ch in snapshot)
            {
                ch.Writer.TryWrite(ev);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LayerInvalidationBroker] notify parse error: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<LayerInvalidationEvent> SubscribeAsync(
        int layerId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<LayerInvalidationEvent>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (_subscribersLock) _subscribers.Add(channel);
        try
        {
            await foreach (var ev in channel.Reader.ReadAllAsync(ct))
            {
                if (ev.LayerId == layerId) yield return ev;
            }
        }
        finally
        {
            lock (_subscribersLock) _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        }
    }

    public IEnumerable<LayerInvalidationEvent> ReplayRecent(int layerId, TimeSpan window)
    {
        lock (_replayLock)
        {
            var cutoff = DateTime.UtcNow - window;
            return _replay
                .Where(e => e.LayerId == layerId && e.OccurredAt >= cutoff)
                .ToList();
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        if (_loopTask is not null) await Task.WhenAny(_loopTask, Task.Delay(1000, ct));
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_conn is not null)
        {
            _conn.Notification -= OnNotification;
            await _conn.DisposeAsync();
            _conn = null;
        }
        _cts?.Dispose();
        _cts = null;
    }
}
