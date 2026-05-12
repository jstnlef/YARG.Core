using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Parsing;
using YARG.Core.UnitTests.Parsing;

namespace YARG.Core.UnitTests.Chart;

public class ScanChartCompatibilityTests
{
    [TestCase(
        "same-fret-chord-single",
        Instrument.FiveFretGuitar,
        "FzP5Ctbn5qfj_2BYcKz-4HbCo4qh3s9PiPstMiP4V3w=",
        "43484e46c0d73401e00100000100000000000000000000000000000000005e4001000000000000000000000004000000040000000000000000000000000000000000000003000000000000000000000000000000000000000200000001000000000000000000000000000000000000000300000001000000780000000000000000000000000000000200000002000000",
        new[]
        {
            "0 = N 0 0",
            "0 = N 1 0",
            "120 = N 0 0",
        })]
    [TestCase(
        "out-of-order-malformed",
        Instrument.FiveFretGuitar,
        "v4NFMZ1hekM83NMSCdAszevPkhuxN74nQymq65gg9HI=",
        "43484e46c0d73401e00100000100000000000000000000000000000000005e4001000000000000000000000004000000040000000000000000000000000000000000000003000000000000000000000000000000000000000200000001000000780000000000000000000000000000000300000002000000f00000000000000000000000000000000400000002000000",
        new[]
        {
            "240 = N 2 0",
            "malformed chart line",
            "0 = N 0 0",
            "120 = N 1 0",
        })]
    [TestCase(
        "phrases-forces-taps",
        Instrument.FiveFretGuitar,
        "8jPpTLqw-rsnS665bG8YQx7KG7oIinR7lB0S1CTqmZ0=",
        "43484e46c0d73401e00100000100000000000000000000000000000000005e400100000000000000000000000400000004000000010000000000000000000000e001000000000000010000000000000000000000e101000000000000000000000000000003000000000000000000000000000000000000000200000001000000780000000000000000000000000000000300000001000000f00000000000000000000000000000000400000004000000",
        new[]
        {
            "0 = S 2 480",
            "0 = E \"solo\"",
            "0 = N 0 0",
            "120 = N 1 0",
            "120 = N 5 0",
            "240 = N 2 0",
            "240 = N 6 0",
            "480 = E \"soloend\"",
        })]
    public void DotChartGuitarBTrack_MatchesScanChart(string name, Instrument instrument, string expectedHash,
        string expectedBTrack, string[] trackLines)
    {
        var result = CalculateDotChartHash("ExpertSingle", instrument, trackLines);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hash, Is.EqualTo(expectedHash), name);
            Assert.That(ToHex(result.BTrack), Is.EqualTo(expectedBTrack), name);
        }
    }

    [Test]
    public void DotChartDrumsBTrack_MatchesScanChart()
    {
        var result = CalculateDotChartHash("ExpertDrums", Instrument.FourLaneDrums,
            "0 = N 0 0",
            "0 = N 4 0",
            "0 = N 66 0",
            "0 = N 34 0");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hash, Is.EqualTo("JKsrFdgF-dPQWwGedjUPNaE6AcTQox2hypEEbogpfT8="));
            Assert.That(ToHex(result.BTrack), Is.EqualTo(
                "43484e46c0d73401e00100000100000000000000000000000000000000005e4001000000000000000000000004000000040000000000000000000000000000000000000002000000000000000000000000000000000000000d00000000000000000000000000000000000000000000001100000010000000"));
        }
    }

    private static BTrackHashResult CalculateDotChartHash(string trackName, Instrument instrument, params string[] trackLines)
    {
        var chartText = ChartText.Chart(
            ChartText.SongSection(480),
            ChartText.SyncSection(),
            ChartText.Section("Events"),
            ChartText.Section(trackName, trackLines));

        var chart = SongChart.FromDotChart(ParseSettings.Default_Chart, chartText.AsSpan());
        return ChartTrackHasher.CalculateTrackHash(chart, instrument, Difficulty.Expert);
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
