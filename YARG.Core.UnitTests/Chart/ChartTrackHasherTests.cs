using NUnit.Framework;
using YARG.Core.Chart;

namespace YARG.Core.UnitTests.Chart;

public class ChartTrackHasherTests
{
    [Test]
    public void CalculateTrackHash_MinimalFiveFretGuitar_MatchesReference()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert);
        difficulty.Notes.Add(new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Strum,
            GuitarNoteFlags.None, NoteFlags.None, 0, 1, 0, 1));

        chart.FiveFretGuitar.AddDifficulty(Difficulty.Expert, difficulty);

        var result = ChartTrackHasher.CalculateTrackHash(chart, Instrument.FiveFretGuitar, Difficulty.Expert);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hash, Is.EqualTo("mAZCUM-lZIkXadmSwtJQp5LJip5yn6u-w9_dDCrDvrs="));
            Assert.That(ToHex(result.BTrack), Is.EqualTo(
                "43484e46c0d73401e001000000000000000000000000000000000000000000000000000001000000000000000000000001000000000000000200000001000000"));
        }
    }

    [Test]
    public void CalculateTrackHash_FiveFretChordTap_SortsNotesByStandardType()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert);
        var parent = new GuitarNote(FiveFretGuitarFret.Red, GuitarNoteType.Tap,
            GuitarNoteFlags.None, NoteFlags.None, 0, 1, 10, 20);
        parent.AddChildNote(new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Tap,
            GuitarNoteFlags.None, NoteFlags.None, 0, 1, 10, 20));
        difficulty.Notes.Add(parent);

        chart.FiveFretGuitar.AddDifficulty(Difficulty.Expert, difficulty);

        var result = ChartTrackHasher.CalculateTrackHash(chart, Instrument.FiveFretGuitar, Difficulty.Expert);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hash, Is.EqualTo("B3FcSYgwiDXfRr5AmF1xPxjkqImAg2d0-W_-F_1p-nM="));
            Assert.That(ToHex(result.BTrack), Is.EqualTo(
                "43484e46c0d73401e0010000000000000000000000000000000000000000000000000000020000000a00000000000000140000000000000002000000040000000a0000000000000014000000000000000300000004000000"));
        }
    }

    [Test]
    public void CalculateTrackHash_DrumsKickAndAccentCymbal_MatchesReference()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.FourLaneDrums, Difficulty.Expert);
        var kick = new DrumNote(FourLaneDrumPad.Kick, DrumNoteType.Neutral,
            DrumNoteFlags.None, NoteFlags.None, 0, 0, isDoubleKick: true);
        kick.AddChildNote(new DrumNote(FourLaneDrumPad.GreenCymbal, DrumNoteType.Accent,
            DrumNoteFlags.None, NoteFlags.None, 0, 0));
        difficulty.Notes.Add(kick);

        chart.FourLaneDrums.AddDifficulty(Difficulty.Expert, difficulty);

        var result = ChartTrackHasher.CalculateTrackHash(chart, Instrument.FourLaneDrums, Difficulty.Expert);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hash, Is.EqualTo("osWLp5RtmcLKS0NbrG6eOdx-0_zjwvCarx9JLxQOI58="));
            Assert.That(ToHex(result.BTrack), Is.EqualTo(
                "43484e46c0d73401e001000000000000000000000000000000000000000000000000000002000000000000000000000000000000000000000d00000008000000000000000000000000000000000000001100000020040000"));
        }
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}