using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;

namespace YARG.Core.UnitTests.Chart;

public class ChartTrackHasherTests
{
    [TestCase(Instrument.FiveFretGuitar, true)]
    [TestCase(Instrument.FiveFretBass, true)]
    [TestCase(Instrument.Keys, true)]
    [TestCase(Instrument.FourLaneDrums, true)]
    [TestCase(Instrument.ProDrums, true)]
    [TestCase(Instrument.FiveLaneDrums, true)]
    [TestCase(Instrument.SixFretGuitar, false)]
    [TestCase(Instrument.ProGuitar_17Fret, false)]
    [TestCase(Instrument.Vocals, false)]
    public void IsSupported_ReturnsExpectedSupport(Instrument instrument, bool expected)
    {
        Assert.That(ChartTrackHasher.IsSupported(instrument), Is.EqualTo(expected));
    }

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

    [Test]
    public void CalculateTrackHash_OverlappingNotesOfSameType_ResolvesOverlap()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert);
        difficulty.Notes.Add(new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Strum,
            GuitarNoteFlags.None, NoteFlags.None, 0, 240, 0, 240));
        difficulty.Notes.Add(new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Hopo,
            GuitarNoteFlags.None, NoteFlags.None, 0, 60, 120, 60));

        chart.FiveFretGuitar.AddDifficulty(Difficulty.Expert, difficulty);

        var notes = ReadBTrack(ChartTrackHasher.CalculateTrackHash(chart, Instrument.FiveFretGuitar, Difficulty.Expert).BTrack).Notes;

        Assert.That(notes, Is.EqualTo(new List<(long Tick, long Length, uint Type, uint Flags)>
        {
            (0, 120, 2, 1),
            (120, 120, 2, 2),
        }));
    }

    [Test]
    public void CalculateTrackHash_OverlappingPhrases_ResolvesOverlap()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert);
        difficulty.Notes.Add(new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Strum,
            GuitarNoteFlags.None, NoteFlags.None, 0, 1, 0, 1));
        difficulty.Notes.Add(new GuitarNote(FiveFretGuitarFret.Red, GuitarNoteType.Strum,
            GuitarNoteFlags.None, NoteFlags.None, 0, 1, 120, 1));
        difficulty.Phrases.Add(new Phrase(PhraseType.StarPower, 0, 240, 0, 240));
        difficulty.Phrases.Add(new Phrase(PhraseType.StarPower, 0, 60, 120, 60));

        chart.FiveFretGuitar.AddDifficulty(Difficulty.Expert, difficulty);

        var starPower = ReadBTrack(ChartTrackHasher.CalculateTrackHash(chart, Instrument.FiveFretGuitar, Difficulty.Expert).BTrack).StarPower;

        Assert.That(starPower, Is.EqualTo(new List<(long Tick, long Length)>
        {
            (0, 120),
            (120, 120),
        }));
    }

    [Test]
    public void CalculateTrackHash_DuplicateDrumLane_KeepsLargestMutuallyExclusiveFlags()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.FourLaneDrums, Difficulty.Expert);
        difficulty.Notes.Add(new DrumNote(FourLaneDrumPad.YellowDrum, DrumNoteType.Ghost,
            DrumNoteFlags.None, NoteFlags.None, 0, 0));
        difficulty.Notes.Add(new DrumNote(FourLaneDrumPad.YellowCymbal, DrumNoteType.Accent,
            DrumNoteFlags.None, NoteFlags.None, 0, 0));

        chart.FourLaneDrums.AddDifficulty(Difficulty.Expert, difficulty);

        var notes = ReadBTrack(ChartTrackHasher.CalculateTrackHash(chart, Instrument.FourLaneDrums, Difficulty.Expert).BTrack).Notes;

        Assert.That(notes, Is.EqualTo(new List<(long Tick, long Length, uint Type, uint Flags)>
        {
            (0, 0, 15, 1056),
        }));
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static (List<(long Tick, long Length)> StarPower, List<(long Tick, long Length, uint Type, uint Flags)> Notes)
        ReadBTrack(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        stream.Position = 12;
        SkipTempos(reader);
        SkipTimeSignatures(reader);
        var starPower = ReadPhrases(reader);
        SkipPhrases(reader);
        SkipFlexLanes(reader);
        SkipDrumFreestyles(reader);
        var notes = ReadNotes(reader);
        return (starPower, notes);
    }

    private static void SkipTempos(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            reader.ReadInt64();
            reader.ReadDouble();
        }
    }

    private static void SkipTimeSignatures(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            reader.ReadInt64();
            reader.ReadUInt32();
            reader.ReadUInt32();
        }
    }

    private static List<(long Tick, long Length)> ReadPhrases(BinaryReader reader)
    {
        var phrases = new List<(long Tick, long Length)>();
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            phrases.Add((reader.ReadInt64(), reader.ReadInt64()));
        }
        return phrases;
    }

    private static void SkipPhrases(BinaryReader reader)
    {
        ReadPhrases(reader);
    }

    private static void SkipFlexLanes(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            reader.ReadInt64();
            reader.ReadInt64();
            reader.ReadByte();
        }
    }

    private static void SkipDrumFreestyles(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            reader.ReadInt64();
            reader.ReadInt64();
            reader.ReadByte();
        }
    }

    private static List<(long Tick, long Length, uint Type, uint Flags)> ReadNotes(BinaryReader reader)
    {
        var notes = new List<(long Tick, long Length, uint Type, uint Flags)>();
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            notes.Add((reader.ReadInt64(), reader.ReadInt64(), reader.ReadUInt32(), reader.ReadUInt32()));
        }
        return notes;
    }
}