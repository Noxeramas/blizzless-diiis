using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.Net.Sockets;
using DiIiS_NA.GameServer.ClientSystem;
using DiIiS_NA.GameServer.ClientSystem.Base;
using DiIiS_NA.GameServer.MessageSystem;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Connection;

DiIiS_NA.Core.Logging.LogManager.Enabled = true;
DiIiS_NA.Core.Logging.LogManager.AttachLogTarget(new TestLogTarget());

var failures = 0;
if (args.Contains("--gem-tooltips"))
{
    LegendaryGemChecks.Tooltips(Check);
    return failures == 0 ? 0 : 1;
}
Check("coalesced packets retain order", () =>
{
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    var client = new RecordingClient(new Connection(socket));
    client.Receive(Packet(1).Concat(Packet(2)).Concat(Packet(3)).ToArray());
    client.Expect(1, 2, 3);
});
Check("fragmented headers and payloads survive across receives", () =>
{
    var packet = Packet(2);
    for (var split = 1; split < packet.Length; split++)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var client = new RecordingClient(new Connection(socket));
        client.Receive(Packet(1).Concat(packet.Take(split)).ToArray());
        client.WaitForCount(1);
        client.Receive(packet.Skip(split).Concat(Packet(3)).ToArray());
        client.Expect(1, 2, 3);
    }
});
Check("receive buffer is owned before the socket reuses it", () =>
{
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var client = new RecordingClient(new Connection(socket), entered, release);
    client.Receive(Packet(0));
    if (!entered.Wait(TimeSpan.FromSeconds(3))) throw new Exception("Parser did not start");
    try
    {
        var reused = Packet(1);
        client.Receive(Enumerate(reused));
        Packet(2).CopyTo(reused, 0);
        client.Receive(Enumerate(reused));
    }
    finally { release.Set(); }
    client.Expect(0, 1, 2);
});
Check("a burst behind a blocked handler stays in receive order", () =>
{
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var client = new RecordingClient(new Connection(socket), entered, release);
    client.Receive(Packet(0));
    if (!entered.Wait(TimeSpan.FromSeconds(3))) throw new Exception("Parser did not start");
    try
    {
        for (var i = 1; i <= 200; i++) client.Receive(Packet(i));
    }
    finally { release.Set(); }
    client.Expect(Enumerable.Range(0, 201).ToArray());
});
Check("empty packets do not hide the next message", () =>
{
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    var client = new RecordingClient(new Connection(socket));
    var header = new GameBitBuffer(4);
    header.WriteInt(32, 4);
    client.Receive(header.Data.Take(4).Concat(Packet(7)).ToArray());
    client.Expect(7);
});
Check("invalid packet lengths close the connection", () =>
{
    foreach (var size in new[] { 0, 3, -1, int.MaxValue })
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connection = new Connection(socket);
        var client = new RecordingClient(connection);
        var header = new GameBitBuffer(4);
        header.WriteInt(32, size);
        client.Receive(header.Data.Take(4).ToArray());
        if (!SpinWait.SpinUntil(() => !connection.IsOpen(), TimeSpan.FromSeconds(3)))
            throw new Exception($"Invalid size {size} was not rejected");
    }
});
Check("messages cannot consume bytes from the next packet", () =>
{
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    var connection = new Connection(socket);
    var client = new RecordingClient(connection);
    var malformed = Packet(1);
    var header = new GameBitBuffer(4);
    header.WriteInt(32, 6); // Not enough payload for the ping, even though more bytes follow.
    Array.Copy(header.Data, malformed, 4);
    client.Receive(malformed.Concat(Packet(2)).ToArray());
    if (!SpinWait.SpinUntil(() => !connection.IsOpen(), TimeSpan.FromSeconds(3)))
        throw new Exception("A message crossed its declared packet boundary");
});
TargetingChecks.Run(Check);
ClockChecks.Run(Check);
MovementChecks.Run(Check);
EtchedSigilChecks.Run(Check);
TwistedSwordChecks.Run(Check);
TwisterCubeChecks.Run(Check);
WizardChannelingChecks.Run(Check);
EquippedLegendaryChecks.Run(Check);
if (args.Contains("--assets"))
{
    LegendaryGemChecks.Run(Check);
    TwisterCubeChecks.RunInstalled(Check);
    EquippedLegendaryChecks.RunInstalled(Check);
}
CombatMeterChecks.Run(Check, args.Contains("--assets"));
return failures == 0 ? 0 : 1;

void Check(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
}

static byte[] Packet(int counter)
{
    var buffer = new GameBitBuffer(32);
    buffer.WriteInt(32, 0);
    buffer.EncodeMessage(new PingMessage { Id = (int)Opcodes.PingMessage, PingCounter = counter });
    return buffer.GetPacketAndReset();
}

static IEnumerable<byte> Enumerate(byte[] bytes)
{
    foreach (var value in bytes) yield return value;
}

sealed class RecordingClient : GameClient
{
    private readonly ConcurrentQueue<int> _received = new();
    private readonly ManualResetEventSlim _entered, _release;

    public RecordingClient(IConnection connection, ManualResetEventSlim entered = null,
        ManualResetEventSlim release = null) : base(connection)
    {
        _entered = entered;
        _release = release;
    }

    public void Receive(IEnumerable<byte> bytes) => Parse(new ConnectionDataEventArgs(Connection, bytes));

    public override void SendMessage(GameMessage message)
    {
        var counter = ((PongMessage)message).Counter;
        if (counter == 0 && _entered != null)
        {
            _entered.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Blocked test handler");
        }
        _received.Enqueue(counter);
    }

    public void WaitForCount(int count)
    {
        if (!SpinWait.SpinUntil(() => _received.Count >= count, TimeSpan.FromSeconds(3)))
            throw new Exception($"Expected {count} replies, received {_received.Count}");
    }

    public void Expect(params int[] counters)
    {
        WaitForCount(counters.Length);
        var actual = _received.ToArray();
        if (!actual.SequenceEqual(counters))
            throw new Exception($"Reply order/content differs: {string.Join(", ", actual.Take(12))}");
    }
}

sealed class TestLogTarget : DiIiS_NA.Core.Logging.LogTarget
{
    public TestLogTarget()
    {
        MinimumLevel = DiIiS_NA.Core.Logging.Logger.Level.Warn;
        MaximumLevel = DiIiS_NA.Core.Logging.Logger.Level.Fatal;
    }
    public override void LogMessage(DiIiS_NA.Core.Logging.Logger.Level level, string logger, string message)
        => Console.WriteLine(logger + ": " + message);
    public override void LogException(DiIiS_NA.Core.Logging.Logger.Level level, string logger, string message, Exception exception)
        => Console.WriteLine(logger + ": " + message + exception);
}
