namespace OotMM.Core.Models;

public sealed class RemoteModelGroup
{
    public List<ModelAnnounce> Announces { get; } = [];
    public bool Installed { get; set; }
    public bool Refused { get; set; }
    public long TotalBytes => Announces.Sum(a => a.Size);
}

public sealed class ModelDownload(string hash, long total)
{
    public string Hash { get; } = hash;
    public long Total { get; } = total;
    public Dictionary<long, byte[]> Chunks { get; } = [];
    public bool Complete => Chunks.Count == Total;

    public bool Accept(ModelChunk chunk)
    {
        if (chunk.Total != Total || chunk.Seq < 0 || chunk.Seq >= Total || Chunks.ContainsKey(chunk.Seq))
        {
            return false;
        }

        Chunks[chunk.Seq] = chunk.Data;
        return true;
    }

    public byte[] Assemble()
    {
        var result = new byte[Chunks.Values.Sum(c => c.Length)];
        var offset = 0;
        foreach (var (_, chunk) in Chunks.OrderBy(pair => pair.Key))
        {
            chunk.CopyTo(result, offset);
            offset += chunk.Length;
        }

        return result;
    }
}

public static class ModelSyncPolicy
{
    public const int DefaultMaxMegabytes = 100;

    public static long CapBytes(int megabytes) => (long)Math.Max(1, megabytes) * 1024 * 1024;

    public static bool WithinCap(long totalBytes, int megabytes) => totalBytes <= CapBytes(megabytes);

    public static string DescribeRefusal(long totalBytes, int megabytes) =>
        $"{totalBytes / (1024.0 * 1024.0):0.#} MB exceeds the {megabytes} MB model sync limit";
}
