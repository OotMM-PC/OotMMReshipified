using System.Net;
using System.Net.Sockets;
using OotMM.Core;

namespace OotMM.Launcher.Session;

/// <summary>Non-blocking TCP relay client: hello then manifest on every
/// (re)connect, no room traffic before the relay reports readiness.</summary>
public sealed class RelayClient : IDisposable
{
    private const int MaxControlBytes = 256 * 1024;
    private const int MaxEphemeralBytes = 2 * 1024 * 1024;
    private const int MaxReceiveFrameBytes = 4 * 1024 * 1024;

    private readonly string _host;
    private readonly ushort _port;
    private readonly string _handshakeLine;
    private readonly string _manifestLine;
    private readonly List<IPEndPoint> _endpoints = [];
    private readonly byte[] _recvChunk = new byte[4096];

    private Socket? _socket;
    private byte[] _activeSend = [];
    private int _activeSendOffset;
    private string _handshakePending = "";
    private string _manifestPending = "";
    private string _pingPending = "";
    private readonly Queue<string> _controlQueue = [];
    private int _controlBytes;
    private bool _controlBackpressureReported;
    private readonly LinkedList<(string Key, string Wire)> _ephemeralQueue = [];
    private readonly Dictionary<string, LinkedListNode<(string Key, string Wire)>> _ephemeralByKey = [];
    private int _ephemeralBytes;
    private readonly List<byte> _recvBuffer = [];
    private TimeSpan _nextConnectAttempt;
    private TimeSpan _lastPingAt = TimeSpan.MinValue;
    private TimeSpan _lastPongAt;
    private ulong _pingNonce;
    private TimeSpan _reconnectDelay = TimeSpan.FromSeconds(1);
    private TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);

    public RelayClient(string host, ushort port, string handshakeLine, string manifestLine)
    {
        _host = host;
        _port = port;
        _handshakeLine = handshakeLine;
        _manifestLine = manifestLine;
        _lastPongAt = Now;
        ResolveEndpoints();
    }

    public bool Connected => _socket is not null;
    public bool ProtocolReady { get; private set; }
    public string Host => _host;
    public ushort Port => _port;
    public string ProtocolError { get; private set; } = "";
    public ulong ConnectionGeneration { get; private set; }

    public event Action<string>? StatusLogged;

    /// <summary>Queues a line for ordered delivery once ready; refuses the
    /// v2/v3-era reliable packet types (they must never ride a lossy queue).</summary>
    public bool QueueLine(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        var type = RelayProtocol.ExtractStringField(line, "type");
        if (type is RelayProtocol.CheckComplete or RelayProtocol.GiveItem
            or RelayProtocol.DeliveryStored or RelayProtocol.CheckClaim)
        {
            return false;
        }

        var wire = line + "\n";
        if (wire.Length > MaxControlBytes || _controlBytes > MaxControlBytes - wire.Length)
        {
            if (!_controlBackpressureReported)
            {
                StatusLogged?.Invoke("outbound control queue full; dropping ephemeral control traffic");
                _controlBackpressureReported = true;
            }

            return false;
        }

        _controlBytes += wire.Length;
        _controlQueue.Enqueue(wire);
        return true;
    }

    /// <summary>Coalesced-by-key state updates; hard-bounded, freely droppable.</summary>
    public bool QueueEphemeralLine(string key, string line)
    {
        if (key.Length == 0 || line.Length == 0)
        {
            return false;
        }

        var wire = line + "\n";
        if (wire.Length > MaxEphemeralBytes)
        {
            return false;
        }

        if (_ephemeralByKey.TryGetValue(key, out var node))
        {
            var oldSize = node.Value.Wire.Length;
            if (_ephemeralBytes - oldSize > MaxEphemeralBytes - wire.Length)
            {
                return false;
            }

            _ephemeralBytes = _ephemeralBytes - oldSize + wire.Length;
            node.Value = (key, wire);
            return true;
        }

        if (_ephemeralBytes > MaxEphemeralBytes - wire.Length)
        {
            return false;
        }

        _ephemeralBytes += wire.Length;
        _ephemeralByKey[key] = _ephemeralQueue.AddLast((key, wire));
        return true;
    }

    /// <summary>Drives the connection and queues; appends complete inbound room
    /// lines. Call once per coordinator tick.</summary>
    public void Pump(List<string> inbound)
    {
        var justConnected = false;
        if (_socket is null)
        {
            if (Now < _nextConnectAttempt)
            {
                return;
            }

            Connect();
            if (_socket is null)
            {
                return;
            }

            justConnected = true;
        }

        var socket = _socket;
        var now = Now;
        if (now - _lastPongAt >= TimeSpan.FromSeconds(15))
        {
            Disconnect("relay heartbeat timed out");
            return;
        }

        if (_lastPingAt == TimeSpan.MinValue || now - _lastPingAt >= TimeSpan.FromSeconds(5))
        {
            _lastPingAt = now;
            _pingNonce++;
            _pingPending = $"{{\"type\":\"{RelayProtocol.Ping}\",\"nonce\":\"{_pingNonce}\"}}\n";
        }

        // A fresh connection sends only its handshake this tick, so the owner sees
        // the new generation before stale traffic can leave.
        while (true)
        {
            if (_activeSendOffset >= _activeSend.Length && !ActivateNextLine(justConnected))
            {
                break;
            }

            int sent;
            try
            {
                sent = socket.Send(_activeSend, _activeSendOffset,
                                   _activeSend.Length - _activeSendOffset, SocketFlags.None);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                break;
            }
            catch (SocketException)
            {
                Disconnect("send failed");
                return;
            }

            if (sent <= 0)
            {
                break;
            }

            _activeSendOffset += sent;
            if (_activeSendOffset >= _activeSend.Length)
            {
                _activeSend = [];
                _activeSendOffset = 0;
                if (justConnected)
                {
                    break;
                }
            }
        }

        // Drain complete records after every read so a room replay can't trip the cap.
        while (true)
        {
            int received;
            try
            {
                received = socket.Receive(_recvChunk, SocketFlags.None);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                break;
            }
            catch (SocketException)
            {
                Disconnect("recv failed");
                break;
            }

            if (received == 0)
            {
                Disconnect("server closed the connection");
                break;
            }

            for (var i = 0; i < received; i++)
            {
                _recvBuffer.Add(_recvChunk[i]);
            }

            DrainReceivedLines(inbound);
            if (_recvBuffer.Count > MaxReceiveFrameBytes)
            {
                Disconnect("receive frame exceeds limit");
                return;
            }
        }
    }

    public void Dispose()
    {
        _socket?.Close();
        _socket = null;
    }

    private void DrainReceivedLines(List<string> inbound)
    {
        int newline;
        while ((newline = _recvBuffer.IndexOf((byte)'\n')) >= 0)
        {
            var line = System.Text.Encoding.UTF8.GetString(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_recvBuffer)[..newline]);
            _recvBuffer.RemoveRange(0, newline + 1);
            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }

            if (line.Length == 0)
            {
                continue;
            }

            var type = RelayProtocol.ExtractStringField(line, "type");
            if (!ProtocolReady && !RelayProtocol.FrameAllowedBeforeProtocolReady(type))
            {
                // Rejected here so pre-READY traffic never authenticates retroactively.
                ProtocolError = "relay sent room traffic before protocol readiness";
                continue;
            }

            if (type == RelayProtocol.Pong)
            {
                _lastPongAt = Now;
            }
            else if (type == RelayProtocol.Ready)
            {
                if (ProtocolError.Length > 0)
                {
                    continue;
                }

                if (RelayProtocol.ExtractIntField(line, "protocolVersion") == 4)
                {
                    ProtocolReady = true;
                    ProtocolError = "";
                    _reconnectDelay = TimeSpan.FromSeconds(1);
                }
                else
                {
                    ProtocolError = "relay accepted an incompatible protocol/epoch";
                }
            }
            else if (type == RelayProtocol.ProtocolError)
            {
                var reason = RelayProtocol.ExtractStringField(line, "reason");
                ProtocolError = reason.Length > 0 ? reason : "relay rejected the protocol";
            }
            else
            {
                inbound.Add(line);
            }
        }
    }

    private bool ActivateNextLine(bool handshakeOnly)
    {
        _activeSendOffset = 0;
        if (_handshakePending.Length > 0)
        {
            _activeSend = System.Text.Encoding.UTF8.GetBytes(_handshakePending);
            _handshakePending = "";
            return true;
        }

        if (handshakeOnly)
        {
            return false;
        }

        if (_manifestPending.Length > 0)
        {
            _activeSend = System.Text.Encoding.UTF8.GetBytes(_manifestPending);
            _manifestPending = "";
            return true;
        }

        if (!ProtocolReady)
        {
            return false;
        }

        if (_pingPending.Length > 0)
        {
            _activeSend = System.Text.Encoding.UTF8.GetBytes(_pingPending);
            _pingPending = "";
            return true;
        }

        if (_controlQueue.Count > 0)
        {
            var wire = _controlQueue.Dequeue();
            _controlBytes -= wire.Length;
            if (_controlBytes < MaxControlBytes / 2)
            {
                _controlBackpressureReported = false;
            }

            _activeSend = System.Text.Encoding.UTF8.GetBytes(wire);
            return true;
        }

        if (_ephemeralQueue.First is { } next)
        {
            _ephemeralBytes -= next.Value.Wire.Length;
            _ephemeralByKey.Remove(next.Value.Key);
            _ephemeralQueue.RemoveFirst();
            _activeSend = System.Text.Encoding.UTF8.GetBytes(next.Value.Wire);
            return true;
        }

        return false;
    }

    private void ClearOutboundQueues()
    {
        _activeSend = [];
        _activeSendOffset = 0;
        _handshakePending = "";
        _manifestPending = "";
        _pingPending = "";
        _controlQueue.Clear();
        _controlBytes = 0;
        _controlBackpressureReported = false;
        _ephemeralQueue.Clear();
        _ephemeralByKey.Clear();
        _ephemeralBytes = 0;
    }

    private void ResolveEndpoints()
    {
        try
        {
            foreach (var address in Dns.GetHostAddresses(_host))
            {
                if (address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                {
                    _endpoints.Add(new IPEndPoint(address, _port));
                }
            }
        }
        catch (SocketException)
        {
            StatusLogged?.Invoke($"cannot resolve {_host}:{_port}");
        }
    }

    private void Connect()
    {
        _nextConnectAttempt = Now + _reconnectDelay;
        _reconnectDelay = TimeSpan.FromSeconds(Math.Min(5, _reconnectDelay.TotalSeconds * 2));
        if (_endpoints.Count == 0)
        {
            return;
        }

        Socket? connected = null;
        var deadline = Now + TimeSpan.FromMilliseconds(250);
        foreach (var endpoint in _endpoints)
        {
            var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                Blocking = false,
            };
            var ok = false;
            try
            {
                socket.Connect(endpoint);
                ok = true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode
                is SocketError.WouldBlock or SocketError.InProgress or SocketError.AlreadyInProgress)
            {
                var remaining = deadline - Now;
                if (remaining > TimeSpan.Zero
                    && socket.Poll((int)(remaining.TotalMilliseconds * 1000), SelectMode.SelectWrite))
                {
                    ok = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)! == 0;
                }
            }
            catch (SocketException)
            {
            }

            if (ok)
            {
                connected = socket;
                break;
            }

            socket.Close();
            if (Now >= deadline)
            {
                break;
            }
        }

        if (connected is null)
        {
            StatusLogged?.Invoke($"connection to {_host}:{_port} failed; retrying");
            return;
        }

        _socket = connected;
        _lastPongAt = Now;
        _handshakePending = _handshakeLine + "\n";
        _manifestPending = _manifestLine + "\n";
        ProtocolReady = false;
        ProtocolError = "";
        _lastPingAt = TimeSpan.MinValue;
        ConnectionGeneration++;
        StatusLogged?.Invoke($"connected to {_host}:{_port}");
    }

    private void Disconnect(string reason)
    {
        StatusLogged?.Invoke($"disconnected ({reason}); reconnecting");
        _socket?.Close();
        _socket = null;
        _recvBuffer.Clear();
        // A retained partial send would emit a malformed line after reconnect.
        ClearOutboundQueues();
        ProtocolReady = false;
        _nextConnectAttempt = Now + _reconnectDelay;
        _reconnectDelay = TimeSpan.FromSeconds(Math.Min(5, _reconnectDelay.TotalSeconds * 2));
    }
}
