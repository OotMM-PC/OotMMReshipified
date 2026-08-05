namespace OotMM.Core.Models;

/// <summary>A remote player's model set for one game, and how much of it has arrived.</summary>
public sealed class RemoteModelGroup
{
    public List<ModelAnnounce> Announces { get; } = [];
    public bool Installed { get; set; }
    /// <summary>Rejected for exceeding the size cap; the puppet keeps its vanilla fill.</summary>
    public bool Refused { get; set; }
    public long TotalBytes => Announces.Sum(a => a.Size);
}

/// <summary>A transfer in flight, assembled in order so a dropped chunk cannot be papered over.
/// </summary>
public sealed class ModelDownload(string hash, long total)
{
    public string Hash { get; } = hash;
    public long Total { get; } = total;
    public List<byte[]> Chunks { get; } = [];
    public long NextSeq => Chunks.Count;
    public bool Complete => Chunks.Count == Total;

    public bool Accept(ModelChunk chunk)
    {
        if (chunk.Seq != NextSeq || chunk.Total != Total)
        {
            return false;
        }

        Chunks.Add(chunk.Data);
        return true;
    }

    public byte[] Assemble()
    {
        var result = new byte[Chunks.Sum(c => c.Length)];
        var offset = 0;
        foreach (var chunk in Chunks)
        {
            chunk.CopyTo(result, offset);
            offset += chunk.Length;
        }

        return result;
    }
}

/// <summary>Decides what a session does with each player's announced model set.</summary>
public static class ModelSyncPolicy
{
    public const int DefaultMaxMegabytes = 100;

    public static long CapBytes(int megabytes) => (long)Math.Max(1, megabytes) * 1024 * 1024;

    /// <summary>Whether a set is small enough to ship. Applied to the whole set for a game, not
    /// per archive, so a model split across files cannot slip past the cap.</summary>
    public static bool WithinCap(long totalBytes, int megabytes) => totalBytes <= CapBytes(megabytes);

    public static string DescribeRefusal(long totalBytes, int megabytes) =>
        $"{totalBytes / (1024.0 * 1024.0):0.#} MB exceeds the {megabytes} MB model sync limit";
}
