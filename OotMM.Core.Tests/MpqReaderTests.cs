using System.IO.Compression;
using System.Text;
using OotMM.Core.Models;
using Xunit;

namespace OotMM.Core.Tests;

/// <summary>Fixture: a synthetic archive in the OTR-exporter MPQ subset, covering a
/// sectored-zlib file, a single-unit-zlib file, an uncompressed file, and a (listfile).</summary>
public class MpqReaderTests
{
    private const string FixtureBase64 =
        "TVBRGiAAAADTAgAAAAAAABMCAACTAgAACAAAAAQAAAAQAAAARQAAAFUAAABlAAAAAnicC/Z29clPykpNLinWh9DxOZl52fHJGZk5"
        + "KfrpPpl52R6luYl5IakVDKOAYdgBAJ1XEH4CeJxjYBgFDCMYAAACAAABAnicY2AYvoCdTAAALLgBiQJ4nGNgZGJmYWVj5+Dk4ubh"
        + "5eMXEBQSFhEVE5eQlJKWkZWTV1BUUlZRVVPX0NTS1tHV0zcwNDI2MTUzt7C0sraxtbN3cHRydnF1c/fw9PL28fXzDwgMCg4JDQuP"
        + "iIyKjomNi09ITEpOSU1Lz8jMys7JzcsvKCwqLiktK6+orKquqa2rb2hsam5pbWvv6Ozq7unt658wcdLkKVOnTZ8xc9bsOXPnzV+w"
        + "cNHiJUuXLV+xctXqNWvXrd+wcdPmLVu3bd+xc9fuPXv37T9w8NDhI0ePHT9x8tTpM2fPnb9w8dLlK1evXb9x89btO3fv3X/w8NHj"
        + "J0+fPX/x8tXrN2/fvf/w8dPnL1+/ff/x89fvP3///WcY4f4HAONs/wFoZWxsb29iamVjdHMvb2JqZWN0X2xpbmtfY2hpbGQvZ0xp"
        + "bmtIdW1hblNrZWwNCmFsdC9vYmplY3RzL29iamVjdF9saW5rX2NoaWxkL2dMaW5rSHVtYW5UZXgNCm5vdGVzLnR4dA0KKGxpc3Rm"
        + "aWxlKQ0KMzDDeSjZMpi8c2+fsohO6fgknobroD18dXQ0YO7HnJAWfIg/H3kuC7vaKxZu1oAV+A2bOXrxOqqCMQhAsUiXFZi/H5xJ"
        + "x9eoN1TvK+2fs9OuRDW+oPNUsMVqULMyH4TjkWvcyfoYkNvbGvBVbY9qPzKAGSU/4m+OPpBA0kfTePCrZ0g9xNMIyiy+NfjigTPo"
        + "nOqksqFL5e0EaJePAkQwjbqskk35C5R58XIegXXuDxyhhRaDyZ+Sb6M5QJzdLfMd";

    private static byte[] Fixture => Convert.FromBase64String(FixtureBase64);

    [Fact]
    public void ReadEntries_ListsAndExtractsEverySupportedStorageMode()
    {
        var entries = MpqReader.ReadEntries(Fixture);

        Assert.NotNull(entries);
        Assert.Equal(
            ["alt/objects/object_link_child/gLinkHumanTex", "notes.txt",
             "objects/object_link_child/gLinkHumanSkel"],
            entries!.Select(e => e.Path).OrderBy(p => p).ToList());

        var skel = entries.Single(e => e.Path.EndsWith("gLinkHumanSkel")).Data;
        Assert.Equal(1300, skel.Length);
        Assert.Equal("SKEL", Encoding.ASCII.GetString(skel, 0, 4));

        var tex = entries.Single(e => e.Path.EndsWith("gLinkHumanTex")).Data;
        Assert.Equal(Enumerable.Range(0, 256).Concat(Enumerable.Range(0, 256))
            .Select(v => (byte)v).ToArray(), tex);

        Assert.Equal("hello"u8.ToArray(), entries.Single(e => e.Path == "notes.txt").Data);
    }

    [Fact]
    public void ReadEntries_RefusesNonMpqData()
    {
        Assert.Null(MpqReader.ReadEntries([1, 2, 3, 4]));
        Assert.Null(MpqReader.ReadEntries("PKjunk"u8.ToArray()));
    }

    [Fact]
    public void Repack_ReadsMpqInputsFlattensAltAndRewritesReferences()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var input = Path.Combine(dir, "model.otr");
            File.WriteAllBytes(input, Fixture);
            var output = Path.Combine(dir, "coop.o2r");

            var result = ModelRepack.Repack([input], output, 1);

            Assert.True(result.Ok, result.Error);
            using var zip = ZipFile.OpenRead(output);
            Assert.Equal(
                ["p01objs/object_link_child/gLinkHumanSkel", "p01objs/object_link_child/gLinkHumanTex"],
                zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToList());

            using var stream = zip.GetEntry("p01objs/object_link_child/gLinkHumanSkel")!.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var text = Encoding.ASCII.GetString(buffer.ToArray());
            Assert.Contains("p01objs/object_link_child/gLinkHumanTex", text);
            Assert.DoesNotContain("objects/object_link_child/gLinkHumanTex", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
