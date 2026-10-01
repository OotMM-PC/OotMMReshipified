using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using OotMM.Core;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.Tests;

public class GameIpcServerTests
{
    [Fact]
    public async Task Server_AuthenticatesAndExchangesFramedMessages()
    {
        using var server = new GameIpcServer(Game.Oot);
        using var client = new NamedPipeClientStream(
            ".",
            server.Endpoint.Split('\\').Last(),
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(timeout.Token);

        await WriteFrameAsync(client,
            $$"""{"type":"hello","protocolMajor":1,"protocolMinor":0,"token":"{{server.Token}}","game":"oot","pid":42,"build":"test"}""",
            timeout.Token);

        var connected = await WaitForEventAsync(server, GameIpcEventType.Connected, timeout.Token);
        Assert.Equal("test", connected.Build);
        Assert.True(server.Send(GameIpcProtocol.BuildWelcome("session", "save", 1, Game.Oot)));

        var welcome = await ReadFrameAsync(client, timeout.Token);
        Assert.Equal(GameIpcProtocol.MessageType.Welcome, GameIpcProtocol.ReadType(welcome));

        await WriteFrameAsync(client, """{"type":"settingsApplied","revision":1}""", timeout.Token);
        var message = await WaitForEventAsync(server, GameIpcEventType.Message, timeout.Token);
        Assert.Equal(GameIpcProtocol.MessageType.SettingsApplied, GameIpcProtocol.ReadType(message.Message));
    }

    [Fact]
    public async Task Server_RejectsWrongToken()
    {
        using var server = new GameIpcServer(Game.Mm);
        using var client = new NamedPipeClientStream(
            ".",
            server.Endpoint.Split('\\').Last(),
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(timeout.Token);

        await WriteFrameAsync(client,
            """{"type":"hello","protocolMajor":1,"protocolMinor":0,"token":"wrong","game":"mm","pid":42,"build":"test"}""",
            timeout.Token);

        var response = await ReadFrameAsync(client, timeout.Token);
        Assert.Equal(GameIpcProtocol.MessageType.ProtocolError, GameIpcProtocol.ReadType(response));
        var error = await WaitForEventAsync(server, GameIpcEventType.ProtocolError, timeout.Token);
        Assert.Contains("token", error.Message);
    }

    [Fact]
    public async Task Server_AcceptsNewClientAfterAnInterruptedRead()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var server = new GameIpcServer(Game.Oot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var first = new NamedPipeClientStream(".", server.Endpoint.Split('\\').Last(),
            PipeDirection.InOut, PipeOptions.Asynchronous);
        await first.ConnectAsync(timeout.Token);
        var hello = $$"""{"type":"hello","protocolMajor":1,"protocolMinor":0,"token":"{{server.Token}}","game":"oot","pid":42,"build":"reconnect"}""";
        await WriteFrameAsync(first, hello, timeout.Token);
        await WaitForEventAsync(server, GameIpcEventType.Connected, timeout.Token);
        var field = typeof(GameIpcServer).GetField("_pipe",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var pipe = Assert.IsType<NamedPipeServerStream>(field.GetValue(server));
        while (!CancelIoEx(pipe.SafePipeHandle, IntPtr.Zero))
        {
            Assert.Equal(1168, System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            await Task.Delay(10, timeout.Token);
        }
        first.Dispose();
        using var second = new NamedPipeClientStream(".", server.Endpoint.Split('\\').Last(),
            PipeDirection.InOut, PipeOptions.Asynchronous);
        await second.ConnectAsync(timeout.Token);
        await WriteFrameAsync(second, hello, timeout.Token);
        await WaitForEventAsync(server, GameIpcEventType.Connected, timeout.Token);
        Assert.True(server.Send(GameIpcProtocol.BuildWelcome("session", "save", 1, Game.Oot)));
        Assert.Equal(GameIpcProtocol.MessageType.Welcome,
            GameIpcProtocol.ReadType(await ReadFrameAsync(second, timeout.Token)));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CancelIoEx(Microsoft.Win32.SafeHandles.SafePipeHandle handle, IntPtr overlapped);

    private static async Task<GameIpcEvent> WaitForEventAsync(
        GameIpcServer server, GameIpcEventType type, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (server.TryDequeue(out var ipcEvent))
            {
                if (ipcEvent?.Type == type)
                {
                    return ipcEvent;
                }
            }
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task WriteFrameAsync(
        Stream stream, string message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadFrameAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return Encoding.UTF8.GetString(payload);
    }
}
