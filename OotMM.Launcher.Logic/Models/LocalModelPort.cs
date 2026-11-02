using System.IO;
using OotMM.Core.Models;

namespace OotMM.Launcher.Models;

public sealed record ModelPortStatus(bool Active, int Entries, string Error = "");

public static class LocalModelPort
{
    public const string AdultArchive = "zz_localmodel_oot_adult.o2r";
    public const string ChildArchive = "zz_localmodel_oot_child.o2r";
    private const string ChildMarkerEntry = "objects/ot_obj_link_child/ootmm_localmodel_marker";
    private const string PortFormat = "portv3|";

    public static ModelPortStatus EnsureAdult(
        string ootExePath, string mmExePath, IReadOnlyList<string> selectedOotModels, bool enabled)
    {
        var ootMods = ModsDir(ootExePath);
        var mmMods = ModsDir(mmExePath);
        if (ootMods is null || mmMods is null)
        {
            return new ModelPortStatus(false, 0);
        }

        var output = Path.Combine(mmMods, AdultArchive);
        var sidecar = output + ".sources";
        var sources = selectedOotModels
            .Select(name => Path.Combine(ootMods, name))
            .Where(File.Exists)
            .Reverse()
            .ToList();

        try
        {
            if (!enabled || sources.Count == 0)
            {
                File.Delete(output);
                File.Delete(sidecar);
                return new ModelPortStatus(false, 0);
            }

            var signature = PortFormat + string.Join("\n", sources.Select(path =>
            {
                var info = new FileInfo(path);
                return $"{Path.GetFileName(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            }));
            if (File.Exists(output) && File.Exists(sidecar) && File.ReadAllText(sidecar) == signature)
            {
                return new ModelPortStatus(true, -1);
            }

            var result = ModelRepack.PortDirectory(sources, output, "object_link_boy", "ot_obj_link_boy");
            if (!result.Ok)
            {
                return new ModelPortStatus(false, 0, result.Error);
            }

            if (result.Kept == 0)
            {
                File.Delete(sidecar);
                return new ModelPortStatus(false, 0);
            }

            File.WriteAllText(sidecar, signature);
            return new ModelPortStatus(true, result.Kept);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ModelPortStatus(false, 0, ex.Message);
        }
    }

    public static ModelPortStatus EnsureChild(
        string ootExePath, string mmExePath, IReadOnlyList<string> selectedOotModels, bool enabled)
    {
        var ootMods = ModsDir(ootExePath);
        var mmMods = ModsDir(mmExePath);
        if (ootMods is null || mmMods is null)
        {
            return new ModelPortStatus(false, 0);
        }

        var output = Path.Combine(mmMods, ChildArchive);
        var sidecar = output + ".sources";
        var sources = selectedOotModels
            .Select(name => Path.Combine(ootMods, name))
            .Where(File.Exists)
            .Reverse()
            .ToList();

        try
        {
            if (!enabled || sources.Count == 0)
            {
                File.Delete(output);
                File.Delete(sidecar);
                return new ModelPortStatus(false, 0);
            }

            var signature = PortFormat + string.Join("\n", sources.Select(path =>
            {
                var info = new FileInfo(path);
                return $"{Path.GetFileName(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            }));
            if (File.Exists(output) && File.Exists(sidecar) && File.ReadAllText(sidecar) == signature)
            {
                return new ModelPortStatus(true, -1);
            }

            var result = ModelRepack.PortDirectory(sources, output, "object_link_child", "ot_obj_link_child");
            if (!result.Ok)
            {
                return new ModelPortStatus(false, 0, result.Error);
            }

            if (result.Kept == 0)
            {
                File.Delete(sidecar);
                return new ModelPortStatus(false, 0);
            }

            using (var zip = System.IO.Compression.ZipFile.Open(
                       output, System.IO.Compression.ZipArchiveMode.Update))
            {
                using var marker = zip.CreateEntry(ChildMarkerEntry).Open();
                marker.WriteByte(1);
            }

            File.WriteAllText(sidecar, signature);
            return new ModelPortStatus(true, result.Kept);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ModelPortStatus(false, 0, ex.Message);
        }
    }

    private static string? ModsDir(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "mods");
    }
}
