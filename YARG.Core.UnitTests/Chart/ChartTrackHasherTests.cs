using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;

namespace YARG.Core.UnitTests.Chart;

public class ChartTrackHasherTests
{
    // CHNF + version 20260801, resolution 480, one green strum at tick 0.
    // Empty list sections are omitted. Offsets: map starts at 8, two entries (44-byte map),
    // resolution payload at 52, notes payload at 56.
    private const string MinimalGuitarBTrackHex =
        "43484e46c1273501020000000100000000000000340000000000000004000000" +
        "090000000000000038000000000000001c000000e00100000100000000000000" +
        "0000000001000000000000000200000001000000";

    private const string MinimalGuitarHashInputHex =
        "0200000001000000000000000900000000000000e00100000100000000000000" +
        "0000000001000000000000000200000001000000";

    [TestCase(Instrument.ProGuitar_17Fret)]
    [TestCase(Instrument.Vocals)]
    public void TryCalculateTrackHash_UnsupportedInstrument_ReturnsFalse(Instrument instrument)
    {
        Assert.That(ChartTrackHasher.TryCalculateTrackHash(CreateMinimalGuitarChart(), instrument, Difficulty.Expert, out _), Is.False);
    }

    [Test]
    public void CalculateTrackHash_MinimalFiveFretGuitar_WritesSectionalFileAndHashesStrippedInput()
    {
        var chart = CreateMinimalGuitarChart();

        var result = Hash(chart, Instrument.FiveFretGuitar, Difficulty.Expert);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ToHex(result.BTrack), Is.EqualTo(MinimalGuitarBTrackHex));
            Assert.That(result.Hash, Is.EqualTo(HashHex(MinimalGuitarHashInputHex)));
            Assert.That(result.Hash, Does.Not.EndWith("="));
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

        var notes = Parse(Hash(chart, Instrument.FiveFretGuitar, Difficulty.Expert).BTrack).Notes;

        Assert.That(notes, Is.EqualTo(new List<(long Tick, long Length, uint Type, uint Flags)>
        {
            (10, 20, 2, 4),
            (10, 20, 3, 4),
        }));
    }

    [Test]
    public void CalculateTrackHash_SixFretNote_UsesSixFretNoteTypes()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<GuitarNote>(Instrument.SixFretGuitar, Difficulty.Expert);
        difficulty.Notes.Add(new GuitarNote(SixFretGuitarFret.Black1, GuitarNoteType.Strum,
            GuitarNoteFlags.None, NoteFlags.None, 0, 1, 0, 1));
        chart.SixFretGuitar.AddDifficulty(Difficulty.Expert, difficulty);

        var notes = Parse(Hash(chart, Instrument.SixFretGuitar, Difficulty.Expert).BTrack).Notes;

        Assert.That(notes, Is.EqualTo(new List<(long Tick, long Length, uint Type, uint Flags)>
        {
            (0, 1, 7, 1),
        }));
    }

    [Test]
    public void CalculateTrackHash_DrumsKickAndAccentCymbal_MapsFlags()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.FourLaneDrums, Difficulty.Expert);
        var kick = new DrumNote(FourLaneDrumPad.Kick, DrumNoteType.Neutral,
            DrumNoteFlags.None, NoteFlags.None, 0, 0, isDoubleKick: true);
        kick.AddChildNote(new DrumNote(FourLaneDrumPad.GreenCymbal, DrumNoteType.Accent,
            DrumNoteFlags.None, NoteFlags.None, 0, 0));
        difficulty.Notes.Add(kick);

        chart.FourLaneDrums.AddDifficulty(Difficulty.Expert, difficulty);

        var notes = Parse(Hash(chart, Instrument.FourLaneDrums, Difficulty.Expert).BTrack).Notes;

        Assert.That(notes, Is.EqualTo(new List<(long Tick, long Length, uint Type, uint Flags)>
        {
            (0, 0, 13, 8),
            (0, 0, 17, 1056),
        }));
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

        var notes = Parse(Hash(chart, Instrument.FiveFretGuitar, Difficulty.Expert).BTrack).Notes;

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

        var starPower = Parse(Hash(chart, Instrument.FiveFretGuitar, Difficulty.Expert).BTrack).StarPower;

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

        var notes = Parse(Hash(chart, Instrument.FourLaneDrums, Difficulty.Expert).BTrack).Notes;

        Assert.That(notes, Is.EqualTo(new List<(long Tick, long Length, uint Type, uint Flags)>
        {
            (0, 0, 15, 1056),
        }));
    }

    [Test]
    public void CalculateTrackHash_RangeShift_WritesRangeShiftSection()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert);
        difficulty.Notes.Add(new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Strum,
            GuitarNoteFlags.None, NoteFlags.None, 0, 1, 0, 1));
        difficulty.RangeShiftEvents.Add(new RangeShift(0, 1, 120, 1, 3, 5));
        chart.FiveFretGuitar.AddDifficulty(Difficulty.Expert, difficulty);

        var rangeShifts = Parse(Hash(chart, Instrument.FiveFretGuitar, Difficulty.Expert).BTrack).RangeShifts;

        Assert.That(rangeShifts, Is.EqualTo(new List<(long Tick, long Position, long Size)>
        {
            (120, 3, 5),
        }));
    }

    [Test]
    public void CalculateTrackHash_OmitsEmptyListSections()
    {
        var result = Hash(CreateMinimalGuitarChart(), Instrument.FiveFretGuitar, Difficulty.Expert);
        var parsed = Parse(result.BTrack);

        Assert.That(parsed.SectionIds, Is.EqualTo(new ulong[] { 1, 9 }));
    }

    [Test]
    public void CalculateTrackHash_HashMatchesIndependentlyStrippedFile()
    {
        var result = Hash(CreateMinimalGuitarChart(), Instrument.FiveFretGuitar, Difficulty.Expert);

        Assert.That(result.Hash, Is.EqualTo(HashBytes(StripForHash(result.BTrack))));
    }

    private static BTrackHashResult Hash(SongChart chart, Instrument instrument, Difficulty difficulty = Difficulty.Expert)
    {
        Assert.That(ChartTrackHasher.TryCalculateTrackHash(chart, instrument, difficulty, out var result), Is.True);
        return result;
    }

    private static SongChart CreateMinimalGuitarChart()
    {
        var chart = new SongChart(480);
        var difficulty = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert);
        difficulty.Notes.Add(new GuitarNote(FiveFretGuitarFret.Green, GuitarNoteType.Strum,
            GuitarNoteFlags.None, NoteFlags.None, 0, 1, 0, 1));
        chart.FiveFretGuitar.AddDifficulty(Difficulty.Expert, difficulty);
        return chart;
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static byte[] FromHex(string hex)
    {
        return Convert.FromHexString(hex);
    }

    private static string HashHex(string hex)
    {
        return HashBytes(FromHex(hex));
    }

    private static string HashBytes(byte[] bytes)
    {
        return BTrackHashResult.Encode(Blake3.Hash(bytes));
    }

    private static byte[] StripForHash(byte[] bTrack)
    {
        using var stream = new MemoryStream(bTrack);
        using var reader = new BinaryReader(stream);
        stream.Position = 8;
        var count = reader.ReadUInt32();
        var ids = new List<ulong>();
        var payloads = new List<byte[]>();
        for (var i = 0; i < count; i++)
        {
            ids.Add(reader.ReadUInt64());
            var offset = reader.ReadUInt64();
            var length = reader.ReadUInt32();
            var restore = stream.Position;
            stream.Position = (long) offset;
            payloads.Add(reader.ReadBytes((int) length));
            stream.Position = restore;
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((uint) ids.Count);
        foreach (var id in ids)
        {
            writer.Write(id);
        }
        foreach (var payload in payloads)
        {
            writer.Write(payload);
        }

        return output.ToArray();
    }

    private static ParsedBTrack Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        var magic = (uint) ((reader.ReadByte() << 24) | (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte());
        var version = reader.ReadUInt32();
        var count = reader.ReadUInt32();
        var map = new List<(ulong Id, ulong Offset, uint Length)>();
        for (var i = 0; i < count; i++)
        {
            map.Add((reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt32()));
        }

        uint resolution = 0;
        var starPower = new List<(long Tick, long Length)>();
        var notes = new List<(long Tick, long Length, uint Type, uint Flags)>();
        var rangeShifts = new List<(long Tick, long Position, long Size)>();
        var sectionIds = new List<ulong>();

        foreach (var entry in map)
        {
            sectionIds.Add(entry.Id);
            stream.Position = (long) entry.Offset;
            switch (entry.Id)
            {
                case 1:
                    resolution = reader.ReadUInt32();
                    break;
                case 4:
                    starPower = ReadPhrases(reader);
                    break;
                case 8:
                    rangeShifts = ReadRangeShifts(reader);
                    break;
                case 9:
                    notes = ReadNotes(reader);
                    break;
            }
        }

        return new ParsedBTrack(magic, version, resolution, sectionIds, starPower, notes, rangeShifts);
    }

    private static List<(long Tick, long Length)> ReadPhrases(BinaryReader reader)
    {
        var phrases = new List<(long Tick, long Length)>();
        var count = reader.ReadUInt32();
        for (var i = 0; i < count; i++)
        {
            phrases.Add((reader.ReadInt64(), reader.ReadInt64()));
        }
        return phrases;
    }

    private static List<(long Tick, long Position, long Size)> ReadRangeShifts(BinaryReader reader)
    {
        var rangeShifts = new List<(long Tick, long Position, long Size)>();
        var count = reader.ReadUInt32();
        for (var i = 0; i < count; i++)
        {
            rangeShifts.Add((reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64()));
        }
        return rangeShifts;
    }

    private static List<(long Tick, long Length, uint Type, uint Flags)> ReadNotes(BinaryReader reader)
    {
        var notes = new List<(long Tick, long Length, uint Type, uint Flags)>();
        var count = reader.ReadUInt32();
        for (var i = 0; i < count; i++)
        {
            notes.Add((reader.ReadInt64(), reader.ReadInt64(), reader.ReadUInt32(), reader.ReadUInt32()));
        }
        return notes;
    }

    private readonly record struct ParsedBTrack(
        uint Magic,
        uint Version,
        uint Resolution,
        List<ulong> SectionIds,
        List<(long Tick, long Length)> StarPower,
        List<(long Tick, long Length, uint Type, uint Flags)> Notes,
        List<(long Tick, long Position, long Size)> RangeShifts);
}
