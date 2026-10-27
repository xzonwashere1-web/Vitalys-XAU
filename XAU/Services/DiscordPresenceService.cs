using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XAU.Services;

/// <summary>Discord Rich Presence client using the local Discord IPC protocol.</summary>
public sealed class DiscordPresenceService : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private PresenceUpdate? _pending;
    private Task? _worker;
    private bool _handshaken;
    private string? _applicationId;
    private string? _connectedApplicationId;
    private bool _disposed;

    public void SetGame(string? gameName, int gamerscore, int totalGamerscore, DateTime sessionStarted)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _pending = new PresenceUpdate(gameName, gamerscore, totalGamerscore,
                new DateTimeOffset(sessionStarted).ToUnixTimeSeconds(), false, false);
            _worker ??= Task.Run(ProcessUpdatesAsync);
        }
    }

    public void SetAppActive(DateTime sessionStarted)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _pending = new PresenceUpdate(null, 0, 0,
                new DateTimeOffset(sessionStarted).ToUnixTimeSeconds(), true, false);
            _worker ??= Task.Run(ProcessUpdatesAsync);
        }
    }

    public void SetApplicationId(string? applicationId)
    {
        lock (_sync)
            _applicationId = applicationId?.Trim();
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _pending = new PresenceUpdate(null, 0, 0, 0, false, true);
            _worker ??= Task.Run(ProcessUpdatesAsync);
        }
    }

    private async Task ProcessUpdatesAsync()
    {
        while (true)
        {
            PresenceUpdate update;
            lock (_sync)
            {
                if (_pending == null)
                {
                    _worker = null;
                    return;
                }
                update = _pending;
                _pending = null;
            }

            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string? applicationId;
                lock (_sync) applicationId = _applicationId;
                if (!ulong.TryParse(applicationId, out _))
                {
                    Disconnect();
                    continue;
                }
                if (_handshaken && _connectedApplicationId != applicationId)
                    Disconnect();
                if (_pipe?.IsConnected != true && !await ConnectAsync().ConfigureAwait(false))
                    continue;

                if (!_handshaken)
                {
                    // Discord IPC opcodes: HANDSHAKE=0, FRAME=1. The prior code used
                    // FRAME for the handshake and CLOSE for SET_ACTIVITY, so Discord ignored it.
                    await SendFrameAsync(0, new { v = 1, client_id = applicationId }).ConfigureAwait(false);
                    var handshake = await ReadFrameAsync().ConfigureAwait(false);
                    if (handshake.Opcode != 1 || (string?)handshake.Body["evt"] != "READY")
                        throw new IOException($"Discord IPC handshake rejected: {handshake.Body}");
                    _handshaken = true;
                    _connectedApplicationId = applicationId;
                }

                object? activity = update.Clear
                    ? null
                    : update.AppActive
                        ? new
                        {
                            details = "Using Vitalys XAU",
                            state = "Achievement progress",
                            timestamps = new { start = update.StartedAt }
                        }
                        : new
                        {
                            details = $"Unlocking achievements: {update.GameName}",
                            state = $"Gamerscore: {update.Gamerscore}/{update.TotalGamerscore}",
                            timestamps = new { start = update.StartedAt }
                        };
                await SendFrameAsync(1, new
                {
                    cmd = "SET_ACTIVITY",
                    args = new { pid = Environment.ProcessId, activity },
                    nonce = Guid.NewGuid().ToString("N")
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Discord Rich Presence unavailable: {ex.Message}");
                Disconnect();
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }

    private async Task<bool> ConnectAsync()
    {
        Disconnect();
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
                _pipe = pipe;
                return true;
            }
            catch { pipe.Dispose(); }
        }
        return false;
    }

    private async Task SendFrameAsync(int opcode, object payload)
    {
        var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
        var header = new byte[8];
        BitConverter.GetBytes(opcode).CopyTo(header, 0);
        BitConverter.GetBytes(json.Length).CopyTo(header, 4);
        await _pipe!.WriteAsync(header).ConfigureAwait(false);
        await _pipe.WriteAsync(json).ConfigureAwait(false);
        await _pipe.FlushAsync().ConfigureAwait(false);
    }

    private async Task<(int Opcode, JObject Body)> ReadFrameAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var header = new byte[8];
        await ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
        var length = BitConverter.ToInt32(header, 4);
        if (length < 0 || length > 1024 * 1024) throw new InvalidDataException("Invalid Discord IPC frame length.");
        var body = new byte[length];
        await ReadExactlyAsync(body, timeout.Token).ConfigureAwait(false);
        return (BitConverter.ToInt32(header, 0), JObject.Parse(Encoding.UTF8.GetString(body)));
    }

    private async Task ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _pipe!.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Discord closed the IPC connection.");
            offset += read;
        }
    }

    private void Disconnect()
    {
        _pipe?.Dispose();
        _pipe = null;
        _handshaken = false;
        _connectedApplicationId = null;
    }

    public async ValueTask DisposeAsync()
    {
        Task worker;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = new PresenceUpdate(null, 0, 0, 0, false, true);
            _worker ??= Task.Run(ProcessUpdatesAsync);
            worker = _worker;
        }
        await worker.ConfigureAwait(false);
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Disconnect();
        }
        finally
        {
            _sendLock.Release();
            _sendLock.Dispose();
        }
    }

    private sealed record PresenceUpdate(string? GameName, int Gamerscore, int TotalGamerscore, long StartedAt, bool AppActive, bool Clear);
}
