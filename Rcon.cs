using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace ReforgerLauncher;

// RCON for the dedicated server: the BattlEye RCon protocol (UDP), which Reforger's server speaks when its config has an
// "rcon" section. Every packet is 'B' 'E', a CRC32 of the rest, 0xFF, then: 0x00 + password (login; the reply's byte is
// 1 when accepted), 0x01 + sequence + command (the reply carries the same sequence, possibly in parts: 0x00, count,
// index), or 0x02 + sequence + text (a message from the server, which must be acknowledged with 0x02 + sequence). An
// empty command every 30 s keeps the login alive (the server drops a client after 45 s of silence).
// Reforger (1.8) answers a command with an empty reply right away and sends its result as messages: "Processing
// Command: #players", then the result ("Players on server: [Player#] ; [Player UID] ; [Player Name]" ...). Commands
// without a result (#shutdown, #restart) send only the first. Checked against a live server, 2026-10-22.
public sealed class RconClient : IDisposable
{
    readonly UdpClient udp;
    readonly ConcurrentDictionary<byte, TaskCompletionSource<string>> pending = new();
    readonly ConcurrentDictionary<byte, string[]> parts = new();
    readonly object sendLock = new();
    readonly CancellationTokenSource stop = new();
    readonly System.Threading.Timer keepAlive;
    TaskCompletionSource<bool> login;
    byte seq;
    // Commands waiting for their result message; "processing" is the one the server just said it's running.
    sealed class Waiter { public string Command; public TaskCompletionSource<string> Result = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    readonly List<Waiter> waiters = new();
    Waiter processing;
    DateTime lastSent = DateTime.Now, lastHeard = DateTime.Now;

    public string Host { get; }
    public int Port { get; }
    public bool LoggedIn { get; private set; }
    public bool Closed { get; private set; }

    // A message the server sent on its own (chat, connections, ...). Raised on a worker thread.
    public event Action<string> Message;
    // The connection stopped answering. Raised on a worker thread.
    public event Action Lost;
    // Every packet received, when set (for diagnosing).
    public ConcurrentQueue<(DateTime At, byte[] Packet)> RawLog;

    RconClient(string host, int port)
    {
        Host = host; Port = port;
        udp = new UdpClient();
        udp.Connect(host, port);
        _ = Task.Run(ReadLoop);
        keepAlive = new System.Threading.Timer(_ => KeepAlive(), null, 5000, 5000);
    }

    // Connects and logs in; throws with the reason when it can't.
    public static async Task<RconClient> ConnectAsync(string host, int port, string password, int timeoutMs = 4000)
    {
        var c = new RconClient(host, port);
        try
        {
            c.login = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            c.SendPacket(new byte[] { 0x00 }.Concat(Encoding.ASCII.GetBytes(password ?? "")).ToArray());
            var done = await Task.WhenAny(c.login.Task, Task.Delay(timeoutMs));
            if (done != c.login.Task) throw new TimeoutException($"No RCON answer from {host}:{port}.");
            if (!c.login.Task.Result) throw new UnauthorizedAccessException("RCON refused the password.");
            c.LoggedIn = true;
            return c;
        }
        catch { c.Dispose(); throw; }
    }

    // Sends a command (e.g. "#players") and returns its result: the reply, or the message the server sends after
    // "Processing Command: ..." ("" when there's none within resultMs). Throws when the server doesn't even acknowledge it.
    public async Task<string> SendAsync(string command, int ackMs = 4000, int resultMs = 3000)
    {
        if (Closed) throw new InvalidOperationException("RCON isn't connected.");
        command = command.Trim();
        var ack = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var w = new Waiter { Command = command };
        lock (waiters) waiters.Add(w);
        try
        {
            byte s;
            lock (sendLock)
            {
                s = seq++;
                pending[s] = ack;
                parts.TryRemove(s, out _);
                SendPacket(new byte[] { 0x01, s }.Concat(Encoding.UTF8.GetBytes(command)).ToArray());
            }
            var done = await Task.WhenAny(ack.Task, Task.Delay(ackMs));
            pending.TryRemove(s, out _);
            if (done != ack.Task) throw new TimeoutException($"The server didn't answer '{command}'.");
            if (ack.Task.Result.Length > 0) return ack.Task.Result;
            done = await Task.WhenAny(w.Result.Task, Task.Delay(resultMs));
            return done == w.Result.Task ? w.Result.Task.Result : "";
        }
        finally
        {
            lock (waiters) { waiters.Remove(w); if (processing == w) processing = null; }
        }
    }

    // A server message: a command's progress or result, else something the server says by itself.
    void OnServerMessage(string text)
    {
        const string marker = "Processing Command:";
        lock (waiters)
        {
            if (text.StartsWith(marker))
            {
                var cmd = text[marker.Length..].Trim();
                processing = waiters.FirstOrDefault(x => x.Command == cmd && !x.Result.Task.IsCompleted) ?? waiters.FirstOrDefault(x => !x.Result.Task.IsCompleted);
                if (processing != null) return;
            }
            else if (processing != null)
            {
                processing.Result.TrySetResult(text);
                processing = null;
                return;
            }
        }
        try { Message?.Invoke(text); } catch { }
    }

    void KeepAlive()
    {
        if (Closed || !LoggedIn) return;
        if ((DateTime.Now - lastHeard).TotalSeconds > 75) { Close(); try { Lost?.Invoke(); } catch { } return; }
        if ((DateTime.Now - lastSent).TotalSeconds < 30) return;
        lock (sendLock) { SendPacket(new byte[] { 0x01, seq++ }); }
    }

    void SendPacket(byte[] payload)
    {
        var body = new byte[payload.Length + 1];
        body[0] = 0xFF;
        Buffer.BlockCopy(payload, 0, body, 1, payload.Length);
        uint crc = Crc32(body);
        var packet = new byte[6 + body.Length];
        packet[0] = (byte)'B'; packet[1] = (byte)'E';
        BitConverter.GetBytes(crc).CopyTo(packet, 2);   // little-endian, as BattlEye wants
        Buffer.BlockCopy(body, 0, packet, 6, body.Length);
        try { udp.Send(packet, packet.Length); lastSent = DateTime.Now; } catch (Exception) when (Closed) { }
    }

    async Task ReadLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(stop.Token); }
            catch (OperationCanceledException) { return; }
            catch (SocketException) { if (Closed) return; await Task.Delay(200); continue; }   // e.g. port closed (server not up)
            catch (ObjectDisposedException) { return; }
            var b = r.Buffer;
            RawLog?.Enqueue((DateTime.Now, b));
            if (b.Length < 8 || b[0] != 'B' || b[1] != 'E' || b[6] != 0xFF) continue;
            lastHeard = DateTime.Now;
            switch (b[7])
            {
                case 0x00:
                    login?.TrySetResult(b.Length > 8 && b[8] == 1);
                    break;
                case 0x01 when b.Length >= 9:
                {
                    byte s = b[8];
                    if (b.Length >= 12 && b[9] == 0x00)
                    {
                        // One part of a long answer: 0x00, number of parts, this part's index.
                        int count = b[10], index = b[11];
                        var arr = parts.GetOrAdd(s, _ => new string[Math.Max(1, count)]);
                        if (index < arr.Length) arr[index] = Encoding.UTF8.GetString(b, 12, b.Length - 12);
                        if (arr.All(x => x != null) && pending.TryGetValue(s, out var t1)) { parts.TryRemove(s, out _); t1.TrySetResult(string.Concat(arr)); }
                    }
                    else if (pending.TryGetValue(s, out var t2)) t2.TrySetResult(Encoding.UTF8.GetString(b, 9, b.Length - 9));
                    break;
                }
                case 0x02 when b.Length >= 9:
                {
                    byte s = b[8];
                    lock (sendLock) { SendPacket(new byte[] { 0x02, s }); }
                    var text = Encoding.UTF8.GetString(b, 9, b.Length - 9);
                    if (text.Length > 0) OnServerMessage(text);
                    break;
                }
            }
        }
    }

    public void Close()
    {
        if (Closed) return;
        Closed = true;
        LoggedIn = false;
        try { stop.Cancel(); } catch { }
        keepAlive.Dispose();
        udp.Dispose();
        foreach (var t in pending.Values) t.TrySetCanceled();
    }

    public void Dispose() => Close();

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        uint c = (uint)i;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var x in data) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
