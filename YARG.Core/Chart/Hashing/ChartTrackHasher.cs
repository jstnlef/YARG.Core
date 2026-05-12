using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
// ReSharper disable InconsistentNaming

namespace YARG.Core.Chart
{
    public static class ChartTrackHasher
    {
        private const uint MAGIC = 0x43484E46;
        private const uint VERSION = 20240320;

        private enum BTrackNoteType : uint
        {
            Open = 1,
            Green = 2,
            Red = 3,
            Yellow = 4,
            Blue = 5,
            Orange = 6,

            Kick = 13,
            RedDrum = 14,
            YellowDrum = 15,
            BlueDrum = 16,
            GreenDrum = 17,
        }

        [Flags]
        private enum BTrackNoteFlags : uint
        {
            None = 0,
            Strum = 1,
            Hopo = 2,
            Tap = 4,
            DoubleKick = 8,
            Tom = 16,
            Cymbal = 32,
            Ghost = 512,
            Accent = 1024,
        }

        private readonly struct BTrackPhrase
        {
            public readonly long Tick;
            public readonly long Length;

            public BTrackPhrase(long tick, long length)
            {
                Tick = tick;
                Length = length;
            }
        }

        private readonly struct BTrackFlexLane
        {
            public readonly long Tick;
            public readonly long Length;
            public readonly bool IsDouble;

            public BTrackFlexLane(long tick, long length, bool isDouble)
            {
                Tick = tick;
                Length = length;
                IsDouble = isDouble;
            }
        }

        private readonly struct BTrackDrumFreestyle
        {
            public readonly long Tick;
            public readonly long Length;
            public readonly bool IsCoda;

            public BTrackDrumFreestyle(long tick, long length, bool isCoda)
            {
                Tick = tick;
                Length = length;
                IsCoda = isCoda;
            }
        }

        private readonly struct BTrackNote
        {
            public readonly long Tick;
            public readonly long Length;
            public readonly BTrackNoteType Type;
            public readonly BTrackNoteFlags Flags;

            public BTrackNote(long tick, long length, BTrackNoteType type, BTrackNoteFlags flags)
            {
                Tick = tick;
                Length = length;
                Type = type;
                Flags = flags;
            }
        }

        public static BTrackHashResult CalculateTrackHash(SongChart chart, Instrument instrument, Difficulty difficulty)
        {
            if (!TryCalculateTrackHash(chart, instrument, difficulty, out var result))
            {
                throw new NotSupportedException($"Cannot calculate a BTrack hash for {instrument} {difficulty}.");
            }

            return result;
        }

        public static bool TryCalculateTrackHash(SongChart chart, Instrument instrument, Difficulty difficulty, out BTrackHashResult result)
        {
            result = default;

            List<Phrase> phrases;
            List<BTrackNote> notes;
            switch (instrument)
            {
                case Instrument.FiveFretGuitar:
                case Instrument.FiveFretBass:
                case Instrument.FiveFretRhythm:
                case Instrument.FiveFretCoopGuitar:
                case Instrument.Keys:
                    if (!chart.GetFiveFretTrack(instrument).TryGetDifficulty(difficulty, out var guitarDifficulty))
                    {
                        return false;
                    }
                    phrases = guitarDifficulty.Phrases;
                    notes = NormalizeGuitarNotes(guitarDifficulty.Notes);
                    break;

                case Instrument.FourLaneDrums:
                case Instrument.ProDrums:
                case Instrument.FiveLaneDrums:
                    if (!GetDrumTrack(chart, instrument).TryGetDifficulty(difficulty, out var drumDifficulty))
                    {
                        return false;
                    }
                    phrases = drumDifficulty.Phrases;
                    notes = NormalizeDrumNotes(instrument, drumDifficulty.Notes);
                    break;

                default:
                    return false;
            }

            var bTrack = WriteBTrack(
                chart.SyncTrack,
                PruneEmptyPhrases(GetPhrases(phrases, PhraseType.StarPower), notes),
                PruneEmptyPhrases(GetPhrases(phrases, PhraseType.Solo), notes),
                PruneEmptyFlexLanes(GetFlexLanes(phrases), notes),
                GetDrumFreestyles(phrases),
                notes);

            result = new BTrackHashResult(bTrack);
            return true;
        }

        private static InstrumentTrack<DrumNote> GetDrumTrack(SongChart chart, Instrument instrument)
        {
            return instrument switch
            {
                Instrument.FourLaneDrums => chart.FourLaneDrums,
                Instrument.ProDrums => chart.ProDrums,
                Instrument.FiveLaneDrums => chart.FiveLaneDrums,
                _ => throw new ArgumentException($"Instrument {instrument} is not a drums instrument.", nameof(instrument)),
            };
        }

        private static List<BTrackNote> NormalizeGuitarNotes(List<GuitarNote> notes)
        {
            var normalized = new List<BTrackNote>();
            foreach (var note in notes)
            {
                foreach (var child in note.AllNotes)
                {
                    if (TryMapGuitarNote(child, out var type))
                    {
                        normalized.Add(new BTrackNote(child.Tick, child.TickLength, type, MapGuitarFlags(child)));
                    }
                }
            }
            return NormalizeNotes(normalized);
        }

        private static bool TryMapGuitarNote(GuitarNote note, out BTrackNoteType type)
        {
            type = note.Fret switch
            {
                (int) FiveFretGuitarFret.Open => BTrackNoteType.Open,
                (int) FiveFretGuitarFret.Green => BTrackNoteType.Green,
                (int) FiveFretGuitarFret.Red => BTrackNoteType.Red,
                (int) FiveFretGuitarFret.Yellow => BTrackNoteType.Yellow,
                (int) FiveFretGuitarFret.Blue => BTrackNoteType.Blue,
                (int) FiveFretGuitarFret.Orange => BTrackNoteType.Orange,
                _ => default,
            };
            return type != default;
        }

        private static BTrackNoteFlags MapGuitarFlags(GuitarNote note)
        {
            return note.Type switch
            {
                GuitarNoteType.Hopo => BTrackNoteFlags.Hopo,
                GuitarNoteType.Tap => BTrackNoteFlags.Tap,
                _ => BTrackNoteFlags.Strum,
            };
        }

        private static List<BTrackNote> NormalizeDrumNotes(Instrument instrument, List<DrumNote> notes)
        {
            var normalized = new List<BTrackNote>();
            foreach (var note in notes)
            {
                foreach (var child in note.AllNotes)
                {
                    if (TryMapDrumNote(instrument, child, out var type, out var flags))
                    {
                        normalized.Add(new BTrackNote(child.Tick, child.TickLength, type, flags));
                    }
                }
            }
            return NormalizeNotes(normalized);
        }

        private static bool TryMapDrumNote(Instrument instrument, DrumNote note, out BTrackNoteType type, out BTrackNoteFlags flags)
        {
            flags = BTrackNoteFlags.None;
            if (note.IsDoubleKick)
            {
                flags |= BTrackNoteFlags.DoubleKick;
            }

            if (note.IsGhost)
            {
                flags |= BTrackNoteFlags.Ghost;
            }
            else if (note.IsAccent)
            {
                flags |= BTrackNoteFlags.Accent;
            }

            if (instrument == Instrument.FiveLaneDrums)
            {
                return TryMapFiveLaneDrumNote(note, ref flags, out type);
            }

            return TryMapFourLaneDrumNote(note, ref flags, out type);
        }

        private static bool TryMapFourLaneDrumNote(DrumNote note, ref BTrackNoteFlags flags, out BTrackNoteType type)
        {
            switch ((FourLaneDrumPad) note.Pad)
            {
                case FourLaneDrumPad.Kick:
                    type = BTrackNoteType.Kick;
                    return true;
                case FourLaneDrumPad.RedDrum:
                    flags |= BTrackNoteFlags.Tom;
                    type = BTrackNoteType.RedDrum;
                    return true;
                case FourLaneDrumPad.YellowDrum:
                    flags |= BTrackNoteFlags.Tom;
                    type = BTrackNoteType.YellowDrum;
                    return true;
                case FourLaneDrumPad.BlueDrum:
                    flags |= BTrackNoteFlags.Tom;
                    type = BTrackNoteType.BlueDrum;
                    return true;
                case FourLaneDrumPad.GreenDrum:
                    flags |= BTrackNoteFlags.Tom;
                    type = BTrackNoteType.GreenDrum;
                    return true;
                case FourLaneDrumPad.YellowCymbal:
                    flags |= BTrackNoteFlags.Cymbal;
                    type = BTrackNoteType.YellowDrum;
                    return true;
                case FourLaneDrumPad.BlueCymbal:
                    flags |= BTrackNoteFlags.Cymbal;
                    type = BTrackNoteType.BlueDrum;
                    return true;
                case FourLaneDrumPad.GreenCymbal:
                    flags |= BTrackNoteFlags.Cymbal;
                    type = BTrackNoteType.GreenDrum;
                    return true;
                default:
                    type = default;
                    return false;
            }
        }

        private static bool TryMapFiveLaneDrumNote(DrumNote note, ref BTrackNoteFlags flags, out BTrackNoteType type)
        {
            switch ((FiveLaneDrumPad) note.Pad)
            {
                case FiveLaneDrumPad.Kick:
                    type = BTrackNoteType.Kick;
                    return true;
                case FiveLaneDrumPad.Red:
                    flags |= BTrackNoteFlags.Tom;
                    type = BTrackNoteType.RedDrum;
                    return true;
                case FiveLaneDrumPad.Yellow:
                    flags |= BTrackNoteFlags.Cymbal;
                    type = BTrackNoteType.YellowDrum;
                    return true;
                case FiveLaneDrumPad.Blue:
                    flags |= BTrackNoteFlags.Tom;
                    type = BTrackNoteType.BlueDrum;
                    return true;
                case FiveLaneDrumPad.Orange:
                    flags |= BTrackNoteFlags.Cymbal;
                    type = BTrackNoteType.GreenDrum;
                    return true;
                case FiveLaneDrumPad.Green:
                    flags |= BTrackNoteFlags.Tom;
                    type = BTrackNoteType.GreenDrum;
                    return true;
                default:
                    type = default;
                    return false;
            }
        }

        private static List<BTrackNote> NormalizeNotes(List<BTrackNote> notes)
        {
            return notes
                .GroupBy(note => new { note.Tick, note.Type })
                .Select(group => new BTrackNote(
                    group.Key.Tick,
                    group.Max(note => note.Length),
                    group.Key.Type,
                    CombineFlags(group.Select(note => note.Flags))))
                .OrderBy(note => note.Tick)
                .ThenBy(note => note.Type)
                .ToList();
        }

        private static BTrackNoteFlags CombineFlags(IEnumerable<BTrackNoteFlags> flags)
        {
            return flags.Aggregate(BTrackNoteFlags.None, (current, flag) => current | flag);
        }

        private static List<BTrackPhrase> GetPhrases(List<Phrase> phrases, PhraseType type)
        {
            return phrases
                .Where(phrase => phrase.Type == type)
                .GroupBy(phrase => phrase.Tick)
                .Select(group => new BTrackPhrase(group.Key, group.Max(phrase =>
                    phrase.TickLength + (type == PhraseType.Solo ? 1 : 0))))
                .OrderBy(phrase => phrase.Tick)
                .ToList();
        }

        private static List<BTrackFlexLane> GetFlexLanes(List<Phrase> phrases)
        {
            return phrases
                .Where(phrase => phrase.Type is PhraseType.TremoloLane or PhraseType.TrillLane)
                .GroupBy(phrase => new { phrase.Tick, IsDouble = phrase.Type == PhraseType.TrillLane })
                .Select(group => new BTrackFlexLane(group.Key.Tick, group.Max(phrase => phrase.TickLength), group.Key.IsDouble))
                .OrderBy(lane => lane.Tick)
                .ThenBy(lane => lane.IsDouble)
                .ToList();
        }

        private static List<BTrackDrumFreestyle> GetDrumFreestyles(List<Phrase> phrases)
        {
            var codas = phrases.Where(phrase => phrase.Type == PhraseType.Coda).ToList();
            return phrases
                .Where(phrase => phrase.Type == PhraseType.DrumFill)
                .Select(phrase => new BTrackDrumFreestyle(
                    phrase.Tick,
                    phrase.TickLength,
                    codas.Any(coda => phrase.Tick >= coda.Tick && phrase.Tick < coda.Tick + Math.Max(coda.TickLength, 1))))
                .OrderBy(phrase => phrase.Tick)
                .ToList();
        }

        private static List<BTrackPhrase> PruneEmptyPhrases(List<BTrackPhrase> phrases, List<BTrackNote> notes)
        {
            return phrases
                .Where(phrase => notes.Any(note => note.Tick >= phrase.Tick && note.Tick < phrase.Tick + Math.Max(phrase.Length, 1)))
                .ToList();
        }

        private static List<BTrackFlexLane> PruneEmptyFlexLanes(List<BTrackFlexLane> lanes, List<BTrackNote> notes)
        {
            return lanes
                .Where(lane => notes.Any(note => note.Tick >= lane.Tick && note.Tick < lane.Tick + Math.Max(lane.Length, 1)))
                .ToList();
        }

        private static byte[] WriteBTrack(
            SyncTrack syncTrack,
            List<BTrackPhrase> starPower,
            List<BTrackPhrase> soloSections,
            List<BTrackFlexLane> flexLanes,
            List<BTrackDrumFreestyle> drumFreestyles,
            List<BTrackNote> notes)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            WriteUInt32BigEndian(writer, MAGIC);
            writer.Write(VERSION);
            writer.Write(syncTrack.Resolution);

            var tempos = GetLastPerTick(syncTrack.Tempos, tempo => tempo.Tick);
            writer.Write(tempos.Count);
            foreach (var tempo in tempos)
            {
                writer.Write((long) tempo.Tick);
                writer.Write(tempo.BeatsPerMinute);
            }

            var timeSignatures = GetLastPerTick(syncTrack.TimeSignatures, timeSignature => timeSignature.Tick);
            writer.Write(timeSignatures.Count);
            foreach (var timeSignature in timeSignatures)
            {
                writer.Write((long) timeSignature.Tick);
                writer.Write(timeSignature.Numerator);
                writer.Write(timeSignature.Denominator);
            }

            writer.Write(starPower.Count);
            foreach (var phrase in starPower)
            {
                writer.Write(phrase.Tick);
                writer.Write(phrase.Length);
            }

            writer.Write(soloSections.Count);
            foreach (var phrase in soloSections)
            {
                writer.Write(phrase.Tick);
                writer.Write(phrase.Length);
            }

            writer.Write(flexLanes.Count);
            foreach (var lane in flexLanes)
            {
                writer.Write(lane.Tick);
                writer.Write(lane.Length);
                writer.Write((byte) (lane.IsDouble ? 1 : 0));
            }

            writer.Write(drumFreestyles.Count);
            foreach (var phrase in drumFreestyles)
            {
                writer.Write(phrase.Tick);
                writer.Write(phrase.Length);
                writer.Write((byte) (phrase.IsCoda ? 1 : 0));
            }

            writer.Write(notes.Count);
            foreach (var note in notes)
            {
                writer.Write(note.Tick);
                writer.Write(note.Length);
                writer.Write((uint) note.Type);
                writer.Write((uint) note.Flags);
            }

            return stream.ToArray();
        }

        private static List<T> GetLastPerTick<T>(List<T> events, Func<T, uint> getTick)
        {
            return events
                .Select((value, index) => new { value, index })
                .GroupBy(item => getTick(item.value))
                .Select(group => group.OrderByDescending(item => item.index).First().value)
                .OrderBy(getTick)
                .ToList();
        }

        private static void WriteUInt32BigEndian(BinaryWriter writer, uint value)
        {
            writer.Write((byte) (value >> 24));
            writer.Write((byte) (value >> 16));
            writer.Write((byte) (value >> 8));
            writer.Write((byte) value);
        }

    }
}