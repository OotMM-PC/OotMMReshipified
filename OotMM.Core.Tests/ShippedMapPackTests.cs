using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public sealed class ShippedMapPackTests
{
    private static EntranceMapPack ShippedPack()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OotMMReshipified.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var pack = EntranceMapPack.Load(Path.Combine(directory.FullName, "OotMM.Launcher", "Assets", "Maps"));
        Assert.NotNull(pack);
        return pack;
    }

    [Fact]
    public void NoAreaPinsTheSameEntrancesTwice()
    {
        foreach (var area in ShippedPack().Areas)
        {
            var doors = area.Pins.Where(pin => pin.Entrances.Count > 0)
                .Select(pin => string.Join(",", pin.Entrances))
                .ToList();
            Assert.Equal(doors.Distinct().Order(), doors.Order());
        }
    }

    [Fact]
    public void EveryPictureIsShipped()
    {
        var pack = ShippedPack();
        foreach (var area in pack.Areas)
        {
            Assert.True(File.Exists(pack.ImagePath(area.Image)), area.Image);
        }
    }
}
