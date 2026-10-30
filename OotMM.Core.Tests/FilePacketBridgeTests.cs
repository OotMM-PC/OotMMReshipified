using System.Text;
using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public sealed class FilePacketBridgeTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ootmm-bridge-tests", Guid.NewGuid().ToString("N"));

    public FilePacketBridgeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void AppendThenRead_RoundTrips()
    {
        var path = PathFor("a.jsonl");
        var packets = new[] { """{"a":1}""", """{"b":2}""", """{"c":3}""" };

        Assert.Equal(3, FilePacketBridge.AppendPacketLines(path, packets));

        var result = FilePacketBridge.ReadPacketLines(path, 0);
        Assert.True(result.Success);
        Assert.Equal(packets, result.Packets);
        Assert.Equal(new FileInfo(path).Length, result.NextOffset);
        Assert.Equal(3, result.PacketEndOffsets.Count);
        Assert.True(result.PacketEndOffsets.SequenceEqual(result.PacketEndOffsets.OrderBy(o => o)));

        var again = FilePacketBridge.ReadPacketLines(path, result.NextOffset);
        Assert.True(again.Success);
        Assert.Empty(again.Packets);
        Assert.Equal(result.NextOffset, again.NextOffset);
    }

    [Fact]
    public void PacketEndOffsets_ResumeOnePacketAtATime()
    {
        var path = PathFor("resume.jsonl");
        var packets = new[] { "one", "two", "three" };
        FilePacketBridge.AppendPacketLines(path, packets);

        var first = FilePacketBridge.ReadPacketLines(path, 0);
        for (var i = 0; i < first.Packets.Count; i++)
        {
            var rest = FilePacketBridge.ReadPacketLines(path, first.PacketEndOffsets[i]);
            Assert.Equal(packets.Skip(i + 1), rest.Packets);
        }
    }

    [Fact]
    public void Append_RepairsTornTail()
    {
        var path = PathFor("torn.jsonl");
        File.WriteAllText(path, "{\"kept\":1}\n{\"torn");

        Assert.Equal(1, FilePacketBridge.AppendPacketLines(path, ["{\"new\":2}"]));

        var result = FilePacketBridge.ReadPacketLines(path, 0);
        Assert.Equal(["{\"kept\":1}", "{\"new\":2}"], result.Packets);
    }

    [Fact]
    public void Append_TruncatesFileThatIsAllTornTail()
    {
        var path = PathFor("alltorn.jsonl");
        File.WriteAllText(path, "no newline at all");

        Assert.Equal(1, FilePacketBridge.AppendPacketLines(path, ["only"]));
        Assert.Equal(["only"], FilePacketBridge.ReadPacketLines(path, 0).Packets);
    }

    [Fact]
    public void Append_SkipsEmptyPackets()
    {
        var path = PathFor("empties.jsonl");
        Assert.Equal(1, FilePacketBridge.AppendPacketLines(path, ["", "x", ""]));
        Assert.Equal(["x"], FilePacketBridge.ReadPacketLines(path, 0).Packets);
    }

    [Fact]
    public void Read_RetainsUnterminatedTail()
    {
        var path = PathFor("tail.jsonl");
        File.WriteAllText(path, "{\"a\":1}\npartial");

        var result = FilePacketBridge.ReadPacketLines(path, 0);
        Assert.True(result.Success);
        Assert.Equal(["{\"a\":1}"], result.Packets);
        Assert.Equal(8, result.NextOffset);

        File.AppendAllText(path, " done\n");
        var finished = FilePacketBridge.ReadPacketLines(path, result.NextOffset);
        Assert.Equal(["partial done"], finished.Packets);
    }

    [Fact]
    public void Read_OnlyUnterminatedContent_SucceedsWithoutAdvancing()
    {
        var path = PathFor("onlytail.jsonl");
        File.WriteAllText(path, "still-writing");

        var result = FilePacketBridge.ReadPacketLines(path, 0);
        Assert.True(result.Success);
        Assert.Empty(result.Packets);
        Assert.Equal(0, result.NextOffset);
    }

    [Fact]
    public void Read_OffsetPastEnd_RewindsToStart()
    {
        var path = PathFor("rewind.jsonl");
        FilePacketBridge.AppendPacketLines(path, ["a", "b"]);

        var result = FilePacketBridge.ReadPacketLines(path, 9999);
        Assert.True(result.Success);
        Assert.Equal(["a", "b"], result.Packets);
    }

    [Fact]
    public void Read_MissingFile_IsSuccessfulEmptyStream()
    {
        var result = FilePacketBridge.ReadPacketLines(PathFor("nope.jsonl"), 42);
        Assert.True(result.Success);
        Assert.Empty(result.Packets);
        Assert.Equal(0, result.NextOffset);
    }

    [Fact]
    public void Read_SkipsEmptyLinesAndStripsCarriageReturns()
    {
        var path = PathFor("crlf.jsonl");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("a\r\n\r\n\nb\n"));

        var result = FilePacketBridge.ReadPacketLines(path, 0);
        Assert.Equal(["a", "b"], result.Packets);
        Assert.Equal([3L, 8L], result.PacketEndOffsets);
        Assert.Equal(8, result.NextOffset);
    }

    [Fact]
    public void Read_OffsetsAreByteOffsets_NotCharOffsets()
    {
        var path = PathFor("utf8.jsonl");
        var packet = """{"name":"Müller"}""";
        FilePacketBridge.AppendPacketLines(path, [packet, "next"]);

        var result = FilePacketBridge.ReadPacketLines(path, 0);
        Assert.Equal(packet, result.Packets[0]);
        Assert.Equal(Encoding.UTF8.GetByteCount(packet) + 1, result.PacketEndOffsets[0]);

        var rest = FilePacketBridge.ReadPacketLines(path, result.PacketEndOffsets[0]);
        Assert.Equal(["next"], rest.Packets);
    }

    [Fact]
    public void Cursor_RoundTrips()
    {
        var packetPath = PathFor("inbox.jsonl");
        FilePacketBridge.AppendPacketLines(packetPath, ["0123456789"]);
        var cursorPath = PathFor("inbox.cursor");

        Assert.True(FilePacketBridge.WritePacketCursor(cursorPath, 7));
        Assert.Equal(7L, FilePacketBridge.ReadPacketCursor(cursorPath, packetPath));
    }

    [Fact]
    public void Cursor_PastPacketFile_Rewinds()
    {
        var packetPath = PathFor("short.jsonl");
        FilePacketBridge.AppendPacketLines(packetPath, ["ab"]);
        var cursorPath = PathFor("short.cursor");
        FilePacketBridge.WritePacketCursor(cursorPath, 9999);

        Assert.Null(FilePacketBridge.ReadPacketCursor(cursorPath, packetPath));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(" 42")]
    [InlineData("42x")]
    [InlineData("+42")]
    [InlineData("")]
    public void Cursor_Malformed_Rewinds(string content)
    {
        var packetPath = PathFor("m.jsonl");
        FilePacketBridge.AppendPacketLines(packetPath, ["0123456789"]);
        var cursorPath = PathFor("m.cursor");
        File.WriteAllText(cursorPath, content + "\n");

        Assert.Null(FilePacketBridge.ReadPacketCursor(cursorPath, packetPath));
    }

    [Fact]
    public void Cursor_MissingCursorOrPacketFile_Rewinds()
    {
        var packetPath = PathFor("p.jsonl");
        FilePacketBridge.AppendPacketLines(packetPath, ["x"]);

        Assert.Null(FilePacketBridge.ReadPacketCursor(PathFor("absent.cursor"), packetPath));

        var cursorPath = PathFor("c.cursor");
        FilePacketBridge.WritePacketCursor(cursorPath, 0);
        Assert.Null(FilePacketBridge.ReadPacketCursor(cursorPath, PathFor("absent.jsonl")));
    }
}
