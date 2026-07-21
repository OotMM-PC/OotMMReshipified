using System.IO;

namespace OotMM.Launcher.Models;

/// <summary>
/// First-run setup detection, ported from the original launcher: the ports run
/// portable, so their extracted asset archives live next to the exe (that is
/// where their extractors read and write). soh.o2r / 2ship.o2r ship with the
/// builds; oot.o2r / mm.o2r / oot-mq.o2r are produced from the player's ROMs by
/// the setup flow.
/// </summary>
public static class GameSetup
{
    /// <summary>
    /// The archives a game still needs before it can boot this seed. Empty when
    /// the exe itself is missing or unset — that is reported separately by
    /// validation, not conflated with setup.
    /// </summary>
    public static IReadOnlyList<string> MissingArchives(string exePath, bool isOot, bool needMq)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return [];
        }

        var dir = Path.GetDirectoryName(exePath)!;
        var required = isOot
            ? new List<string> { "soh.o2r", "oot.o2r" }
            : new List<string> { "2ship.o2r", "mm.o2r" };
        if (isOot && needMq)
        {
            required.Add("oot-mq.o2r");
        }

        return required.Where(name => !File.Exists(Path.Combine(dir, name))).ToList();
    }
}
