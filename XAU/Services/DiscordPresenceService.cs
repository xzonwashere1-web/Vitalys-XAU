using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;

namespace XAU.Services;

/// <summary>Small Discord IPC Rich Presence client; silently stays inactive when Discord is closed.</summary>
public sealed class DiscordPresenceService : IDisposable
{
    // This is the app's Discord application ID. Rich presence assets are configured in that app's portal.
    private const string ApplicationId = "1374776817669460038";
    private readonly object _sync = new();
    private NamedPipeClientStream? _pipe;
    private long _startedAt;
    private bool _disposed;
    private bool _handshaken;

    public void SetGame(string? gameName, int gamerscore, int totalGamerscore, DateTime sessionStarted)
    {
        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                if (_pipe?.IsConnected != true && !Connect()) return;
                _startedAt = new DateTimeOffset(sessionStarted).ToUnixTimeSeconds();
                if (!_handshaken)
                {
                    SendFrame(1, new { v = 1, client_id = ApplicationId });
                    // Receive READY before sending presence to satisfy the IPC handshake.
                    if (!ReadFrame()) { Disconnect(); return; }
                    _handshaken = true;
                }
                if (string.IsNullOrWhiteSpace(gameName))
                    SendFrame(2, new { cmd = "SET_ACTIVITY", args = new { pid = Environment.ProcessId, activity = (object?)null }, nonce = Guid.NewGuid().ToString("N") });
                else
                    SendFrame(2, new
                    {
                        cmd = "SET_ACTIVITY",
                        args = new
                        {
                            pid = Environment.ProcessId,
                            activity = new
                            {
                                details = $"Unlocking achievements: {gameName}",
                                state = $"Gamerscore: {gamerscore}/{totalGamerscore}",
                                timestamps = new { start = _startedAt },
                                assets = new { large_image = "vitalysxau", large_text = "Vitalys XAU" }
                            }
                        },
                        nonce = Guid.NewGuid().ToString("N")
                    });
            }
            catch
            {
                Disconnect();
            }
        }
    }

    public void Clear() => SetGame(null, 0, 0, DateTime.Now);

    private bool Connect()
    {
        Disconnect();
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(150);
                _pipe = pipe;
                return true;
            }
            catch { pipe.Dispose(); }
        }
        return false;
    }

    private void SendFrame(int opcode, object payload)
    {
        var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
        var header = new byte[8];
        BitConverter.GetBytes(opcode).CopyTo(header, 0);
        BitConverter.GetBytes(json.Length).CopyTo(header, 4);
        _pipe!.Write(header, 0, header.Length);
        _pipe.Write(json, 0, json.Length);
        _pipe.Flush();
    }

    private bool ReadFrame()
    {
        var header = new byte[8];
        if (!ReadExactly(header)) return false;
        var length = BitConverter.ToInt32(header, 4);
        if (length < 0 || length > 1024 * 1024) return false;
        var body = new byte[length];
        return ReadExactly(body);
    }

    private bool ReadExactly(byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = _pipe!.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    private void Disconnect()
    {
        _pipe?.Dispose();
        _pipe = null;
        _handshaken = false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                if (_pipe?.IsConnected == true)
                {
                    SendFrame(2, new { cmd = "SET_ACTIVITY", args = new { pid = Environment.ProcessId, activity = (object?)null }, nonce = Guid.NewGuid().ToString("N") });
                }
            }
            catch { }
            _disposed = true;
            Disconnect();
        }
    }
}
