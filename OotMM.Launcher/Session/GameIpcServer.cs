using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using OotMM.Core;

namespace OotMM.Launcher.Session;

public enum GameIpcEventType
{
    Connected,
    Message,
    Disconnected,
    ProtocolError,
}

public sealed record GameIpcEvent(
    GameIpcEventType Type,
    string Message = "",
    string Build = "",
    int Pid = 0);

public sealed class GameIpcServer : IDisposable
{
    private const int MaxFrameSize = 4 * 1024 * 1024;

    private readonly Game _expectedGame;
    private readonly string _pipeName;
    private readonly string _token;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<GameIpcEvent> _events = new();
    private readonly object _connectionLock = new();
    private readonly Task _listenTask;
    private NamedPipeServerStream? _pipe;
    private Channel<string>? _outbound;
    private bool _connected;

    public GameIpcServer(Game expectedGame)
    {
        _expectedGame = expectedGame;
        _pipeName = $"ootmm-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        _listenTask = ListenAsync();
    }

    public string Endpoint => $@"\\.\pipe\{_pipeName}";

    public string Token => _token;

    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
            {
                return _connected;
            }
        }
    }

    public bool Send(string message)
    {
        lock (_connectionLock)
        {
            return _connected && _outbound?.Writer.TryWrite(message) == true;
        }
    }

    public bool TryDequeue(out GameIpcEvent? ipcEvent) =>
        _events.TryDequeue(out ipcEvent);

    public void Dispose()
    {
        _stop.Cancel();
        lock (_connectionLock)
        {
            _outbound?.Writer.TryComplete();
            _pipe?.Dispose();
        }

        try
        {
            _listenTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(
                   inner => inner is OperationCanceledException or IOException or ObjectDisposedException))
        {
        }

        _stop.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                lock (_connectionLock)
                {
                    _pipe = pipe;
                }

                await pipe.WaitForConnectionAsync(_stop.Token);
                var helloFrame = await ReadFrameAsync(pipe, _stop.Token);
                if (helloFrame is null)
                {
                    continue;
                }

                if (!ValidateHello(helloFrame, out var hello, out var error))
                {
                    await WriteFrameAsync(pipe, GameIpcProtocol.BuildProtocolError(error), _stop.Token);
                    _events.Enqueue(new GameIpcEvent(GameIpcEventType.ProtocolError, error));
                    continue;
                }

                var outbound = Channel.CreateUnbounded<string>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                lock (_connectionLock)
                {
                    _outbound = outbound;
                    _connected = true;
                }

                _events.Enqueue(new GameIpcEvent(
                    GameIpcEventType.Connected, Build: hello!.Build, Pid: hello.Pid));
                await ProcessConnectionAsync(pipe, outbound, _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                if (!_stop.IsCancellationRequested)
                {
                    _events.Enqueue(new GameIpcEvent(GameIpcEventType.ProtocolError, ex.Message));
                }
            }
            finally
            {
                var wasConnected = false;
                lock (_connectionLock)
                {
                    wasConnected = _connected;
                    _connected = false;
                    _outbound?.Writer.TryComplete();
                    _outbound = null;
                    _pipe = null;
                }
                if (wasConnected)
                {
                    _events.Enqueue(new GameIpcEvent(GameIpcEventType.Disconnected));
                }
            }
        }
    }

    private bool ValidateHello(
        string frame, out GameIpcHello? hello, out string error)
    {
        if (!GameIpcProtocol.TryParseHello(frame, out hello, out error))
        {
            return false;
        }
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(hello!.Token),
                Encoding.UTF8.GetBytes(_token)))
        {
            error = "hello token was rejected";
            return false;
        }
        if (hello.ProtocolMajor != GameIpcProtocol.ProtocolMajor)
        {
            error = $"unsupported protocol major {hello.ProtocolMajor}";
            return false;
        }
        if (hello.Game != _expectedGame)
        {
            error = $"expected {GameInfo.ToSeedTag(_expectedGame)} but received {GameInfo.ToSeedTag(hello.Game)}";
            return false;
        }
        if (hello.Pid <= 0)
        {
            error = "hello contains an invalid process id";
            return false;
        }
        return true;
    }

    private async Task ProcessConnectionAsync(
        NamedPipeServerStream pipe, Channel<string> outbound, CancellationToken cancellationToken)
    {
        var readTask = ReadFrameAsync(pipe, cancellationToken);
        var outboundTask = outbound.Reader.WaitToReadAsync(cancellationToken).AsTask();
        while (!cancellationToken.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(readTask, outboundTask);
            if (completed == readTask)
            {
                var message = await readTask;
                if (message is null)
                {
                    return;
                }
                _events.Enqueue(new GameIpcEvent(GameIpcEventType.Message, message));
                readTask = ReadFrameAsync(pipe, cancellationToken);
            }

            if (completed == outboundTask)
            {
                if (!await outboundTask)
                {
                    return;
                }
                while (outbound.Reader.TryRead(out var message))
                {
                    await WriteFrameAsync(pipe, message, cancellationToken);
                }
                outboundTask = outbound.Reader.WaitToReadAsync(cancellationToken).AsTask();
            }
        }
    }

    private static async Task<string?> ReadFrameAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, cancellationToken))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxFrameSize)
        {
            throw new IOException($"IPC frame size {length} is invalid");
        }

        var payload = new byte[length];
        return await ReadExactAsync(stream, payload, cancellationToken)
            ? Encoding.UTF8.GetString(payload)
            : null;
    }

    private static async Task<bool> ReadExactAsync(
        Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }
            offset += read;
        }
        return true;
    }

    private static async Task WriteFrameAsync(
        Stream stream, string message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        if (payload.Length > MaxFrameSize)
        {
            throw new IOException($"IPC frame size {payload.Length} exceeds the limit");
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
