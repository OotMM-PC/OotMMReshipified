using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OotMM.Core;

/// <summary>128-bit reward identity (SHA-256 prefix); dedups item deliveries
/// across the wire, so values must match the C++ side bit for bit.</summary>
public readonly record struct RewardIdentity(ulong High, ulong Low) : IComparable<RewardIdentity>
{
    public int CompareTo(RewardIdentity other) =>
        High != other.High ? High.CompareTo(other.High) : Low.CompareTo(other.Low);

    public string ToHex() => $"{High:x16}{Low:x16}";

    public static RewardIdentity? FromHex(string hex)
    {
        if (hex.Length != 32
            || !ulong.TryParse(hex[..16], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high)
            || !ulong.TryParse(hex[16..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low))
        {
            return null;
        }

        return new RewardIdentity(high, low);
    }
}

/// <summary>Identity crypto ported from AnchorBridge.cpp; every string
/// construction here is a hash input and must not change.</summary>
public static class Identity
{
    public static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string CheckKey(Game game, string checkId) =>
        $"{GameInfo.ToSeedTag(game)}:{checkId}";

    public static string RewardNamespace(string teamId, string seedId) =>
        $"{teamId}|{seedId}";

    public static RewardIdentity BuildRewardIdentity(
        string roomId, ushort sourcePlayer, Game checkGame, string checkId,
        ushort rewardIndex, ushort targetPlayer, Game destinationGame)
    {
        var digest = Sha256Hex(
            $"{roomId}|p{sourcePlayer}|{CheckKey(checkGame, checkId)}|r{rewardIndex}"
            + $"|p{targetPlayer}|{GameInfo.ToSeedTag(destinationGame)}");
        return new RewardIdentity(
            ulong.Parse(digest[..16], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            ulong.Parse(digest[16..32], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static string SeedManifestHash(Seed seed) => Sha256Hex(CanonicalManifestBody(seed));

    private static string CanonicalManifestBody(Seed seed)
    {
        var sorted = seed.Placements
            .OrderBy(p => (int)p.Check.Game)
            .ThenBy(p => p.Check.Id, StringComparer.Ordinal)
            .ThenBy(p => p.OwnerPlayer)
            .ThenBy(p => (int)p.Item.Game)
            .ThenBy(p => p.Item.Id, StringComparer.Ordinal);

        var body = new StringBuilder();
        body.Append(seed.Format).Append('\n')
            .Append(seed.GeneratorVersion).Append('\n')
            .Append(seed.SeedId).Append('\n')
            .Append(seed.SettingsHash).Append('\n')
            .Append(seed.TeamId).Append('\n')
            .Append(seed.PlayerId).Append('\n')
            .Append(seed.PlayerCount).Append('\n');
        foreach (var placement in sorted)
        {
            body.Append(GameInfo.ToSeedTag(placement.Check.Game)).Append('\t')
                .Append(placement.Check.Id).Append('\t')
                .Append(GameInfo.ToSeedTag(placement.Item.Game)).Append('\t')
                .Append(placement.Item.Id).Append('\t')
                .Append(placement.Item.Name).Append('\t')
                .Append(placement.OwnerPlayer).Append('\n');
        }

        return body.ToString();
    }
}
