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
        private const uint VERSION = 20260801;
        private const ulong CompetitiveSectionIdMax = 9;

        private enum BTrackSectionId : ulong
        {
            Resolution = 1,
            TempoMarker = 2,
            TimeSignature = 3,
            StarPower = 4,
            SoloSection = 5,
            FlexLane = 6,
            DrumFreestyle = 7,
            RangeShift = 8,
            Note = 9,
        }

        private enum BTrackNoteType : uint
        {
            Open = 1,
            Green = 2,
            Red = 3,
            Yellow = 4,
            Blue = 5,
            Orange = 6,

            Black1 = 7,
            Black2 = 8,
            Black3 = 9,
            White1 = 10,
            White2 = 11,
            White3 = 12,

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
            DiscoNoFlip = 64,
            Disco = 128,
            Flam = 256,
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

        private readonly struct BTrackRangeShift
        {
            public readonly long Tick;
            public readonly long Position;
            public readonly long Size;

            public BTrackRangeShift(long tick, long position, long size)
            {
                Tick = tick;
                Position = position;
                Size = size;
            }
        }

        private readonly struct BTrackSection
        {
            public readonly ulong Id;
            public readonly byte[] Payload;

            public BTrackSection(BTrackSectionId id, byte[] payload)
            {
                Id = (ulong) id;
                Payload = payload;
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

        public static bool IsSupported(Instrument instrument)
        {
            return instrument is
                Instrument.FiveFretGuitar or
                Instrument.FiveFretBass or
                Instrument.FiveFretRhythm or
                Instrument.FiveFretCoopGuitar or
                Instrument.Keys or
                Instrument.SixFretGuitar or
                Instrument.SixFretBass or
                Instrument.SixFretRhythm or
                Instrument.SixFretCoopGuitar or
                Instrument.FourLaneDrums or
                Instrument.ProDrums or
                Instrument.FiveLaneDrums;
        }

        public static bool TryCalculateTrackHash(SongChart chart, Instrument instrument, Difficulty difficulty, out BTrackHashResult result)
        {
            result = default;

            List<Phrase> phrases;
            List<TextEvent> textEvents;
            List<RangeShift> rangeShiftEvents;
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
                    textEvents = guitarDifficulty.TextEvents;
                    rangeShiftEvents = guitarDifficulty.RangeShiftEvents;
                    notes = NormalizeGuitarNotes(guitarDifficulty.Notes);
                    break;

                case Instrument.SixFretGuitar:
                case Instrument.SixFretBass:
                case Instrument.SixFretRhythm:
                case Instrument.SixFretCoopGuitar:
                    if (!chart.GetSixFretTrack(instrument).TryGetDifficulty(difficulty, out var sixFretDifficulty))
                    {
                        return false;
                    }
                    phrases = sixFretDifficulty.Phrases;
                    textEvents = sixFretDifficulty.TextEvents;
                    rangeShiftEvents = sixFretDifficulty.RangeShiftEvents;
                    notes = NormalizeSixFretNotes(sixFretDifficulty.Notes);
                    break;

                case Instrument.FourLaneDrums:
                case Instrument.ProDrums:
                case Instrument.FiveLaneDrums:
                    if (!GetDrumTrack(chart, instrument).TryGetDifficulty(difficulty, out var drumDifficulty))
                    {
                        return false;
                    }
                    phrases = drumDifficulty.Phrases;
                    textEvents = drumDifficulty.TextEvents;
                    rangeShiftEvents = drumDifficulty.RangeShiftEvents;
                    notes = NormalizeDrumNotes(instrument, drumDifficulty.Notes);
                    break;

                default:
                    return false;
            }

            result = WriteBTrack(
                chart.SyncTrack,
                ResolvePhraseOverlaps(PruneEmptyPhrases(GetPhrases(phrases, PhraseType.StarPower), notes)),
                ResolvePhraseOverlaps(PruneEmptyPhrases(GetPhrases(phrases, PhraseType.Solo), notes)),
                PruneEmptyFlexLanes(GetFlexLanes(phrases), notes),
                GetDrumFreestyles(phrases, textEvents),
                GetRangeShifts(rangeShiftEvents),
                notes);
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

        private static List<BTrackNote> NormalizeSixFretNotes(List<GuitarNote> notes)
        {
            var normalized = new List<BTrackNote>();
            foreach (var note in notes)
            {
                foreach (var child in note.AllNotes)
                {
                    if (TryMapSixFretNote(child, out var type))
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

        private static bool TryMapSixFretNote(GuitarNote note, out BTrackNoteType type)
        {
            type = note.Fret switch
            {
                (int) SixFretGuitarFret.Open => BTrackNoteType.Open,
                (int) SixFretGuitarFret.Black1 => BTrackNoteType.Black1,
                (int) SixFretGuitarFret.Black2 => BTrackNoteType.Black2,
                (int) SixFretGuitarFret.Black3 => BTrackNoteType.Black3,
                (int) SixFretGuitarFret.White1 => BTrackNoteType.White1,
                (int) SixFretGuitarFret.White2 => BTrackNoteType.White2,
                (int) SixFretGuitarFret.White3 => BTrackNoteType.White3,
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
            var deduped = notes
                .GroupBy(note => new { note.Tick, note.Type })
                .Select(group => new BTrackNote(
                    group.Key.Tick,
                    group.Max(note => note.Length),
                    group.Key.Type,
                    NormalizeFlags(CombineFlags(group.Select(note => note.Flags)))))
                .OrderBy(note => note.Tick)
                .ThenBy(note => note.Type)
                .ToList();

            return ResolveNoteOverlaps(deduped);
        }

        private static BTrackNoteFlags CombineFlags(IEnumerable<BTrackNoteFlags> flags)
        {
            return flags.Aggregate(BTrackNoteFlags.None, (current, flag) => current | flag);
        }

        private static BTrackNoteFlags NormalizeFlags(BTrackNoteFlags flags)
        {
            flags = NormalizeFlagGroup(flags, BTrackNoteFlags.Strum, BTrackNoteFlags.Hopo, BTrackNoteFlags.Tap);
            flags = NormalizeFlagGroup(flags, BTrackNoteFlags.DoubleKick, BTrackNoteFlags.Tom, BTrackNoteFlags.Cymbal);
            flags = NormalizeFlagGroup(flags, BTrackNoteFlags.DiscoNoFlip, BTrackNoteFlags.Disco);
            flags = NormalizeFlagGroup(flags, BTrackNoteFlags.Ghost, BTrackNoteFlags.Accent);
            return flags;
        }

        private static BTrackNoteFlags NormalizeFlagGroup(BTrackNoteFlags flags, params BTrackNoteFlags[] group)
        {
            var selected = BTrackNoteFlags.None;
            foreach (var flag in group)
            {
                if ((flags & flag) != 0)
                {
                    flags &= ~flag;
                    selected = flag;
                }
            }
            return flags | selected;
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

        private static List<BTrackDrumFreestyle> GetDrumFreestyles(List<Phrase> phrases, List<TextEvent> textEvents)
        {
            return phrases
                .Where(phrase => phrase.Type == PhraseType.DrumFill)
                .Select(phrase => new BTrackDrumFreestyle(
                    phrase.Tick,
                    phrase.TickLength,
                    HasCodaOnOrBefore(phrases, textEvents, phrase.Tick)))
                .OrderBy(phrase => phrase.Tick)
                .ToList();
        }

        private static bool HasCodaOnOrBefore(List<Phrase> phrases, List<TextEvent> textEvents, uint tick)
        {
            if (phrases.Any(phrase => phrase.Type == PhraseType.Coda && phrase.Tick <= tick))
            {
                return true;
            }

            return textEvents.Any(textEvent => textEvent.Tick <= tick && IsCodaEvent(textEvent.Text));
        }

        private static bool IsCodaEvent(string text)
        {
            var trimmed = text.Trim();
            return trimmed.Equals("coda", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("[coda]", StringComparison.OrdinalIgnoreCase);
        }

        private static List<BTrackRangeShift> GetRangeShifts(List<RangeShift> rangeShifts)
        {
            return GetLastPerTick(rangeShifts, rangeShift => rangeShift.Tick)
                .Select(rangeShift => new BTrackRangeShift(rangeShift.Tick, rangeShift.Range, rangeShift.Size))
                .ToList();
        }

        private static List<BTrackPhrase> PruneEmptyPhrases(List<BTrackPhrase> phrases, List<BTrackNote> notes)
        {
            return phrases
                .Where(phrase => notes.Any(note => note.Tick >= phrase.Tick && note.Tick < phrase.Tick + Math.Max(phrase.Length, 1)))
                .ToList();
        }

        private static List<BTrackPhrase> ResolvePhraseOverlaps(List<BTrackPhrase> phrases)
        {
            var resolved = phrases.ToList();
            for (var i = 0; i < resolved.Count - 1; i++)
            {
                var current = resolved[i];
                var next = resolved[i + 1];
                if (current.Tick >= next.Tick)
                {
                    continue;
                }

                var currentEnd = current.Tick + current.Length;
                if (currentEnd <= next.Tick)
                {
                    continue;
                }

                var nextEnd = Math.Max(currentEnd, next.Tick + next.Length);
                resolved[i] = new BTrackPhrase(current.Tick, next.Tick - current.Tick);
                resolved[i + 1] = new BTrackPhrase(next.Tick, nextEnd - next.Tick);
            }
            return resolved;
        }

        private static List<BTrackNote> ResolveNoteOverlaps(List<BTrackNote> notes)
        {
            return notes
                .GroupBy(note => note.Type)
                .SelectMany(ResolveSameTypeNoteOverlaps)
                .OrderBy(note => note.Tick)
                .ThenBy(note => note.Type)
                .ToList();
        }

        private static List<BTrackNote> ResolveSameTypeNoteOverlaps(IEnumerable<BTrackNote> notes)
        {
            var resolved = notes.OrderBy(note => note.Tick).ToList();
            for (var i = 0; i < resolved.Count - 1; i++)
            {
                var current = resolved[i];
                var next = resolved[i + 1];
                if (current.Tick >= next.Tick)
                {
                    continue;
                }

                var currentEnd = current.Tick + current.Length;
                if (currentEnd <= next.Tick)
                {
                    continue;
                }

                var nextEnd = Math.Max(currentEnd, next.Tick + next.Length);
                resolved[i] = new BTrackNote(current.Tick, next.Tick - current.Tick, current.Type, current.Flags);
                resolved[i + 1] = new BTrackNote(next.Tick, nextEnd - next.Tick, next.Type, next.Flags);
            }
            return resolved;
        }

        private static List<BTrackFlexLane> PruneEmptyFlexLanes(List<BTrackFlexLane> lanes, List<BTrackNote> notes)
        {
            return lanes
                .Where(lane => notes.Any(note => note.Tick >= lane.Tick && note.Tick <= lane.Tick + lane.Length))
                .ToList();
        }

        private static BTrackHashResult WriteBTrack(
            SyncTrack syncTrack,
            List<BTrackPhrase> starPower,
            List<BTrackPhrase> soloSections,
            List<BTrackFlexLane> flexLanes,
            List<BTrackDrumFreestyle> drumFreestyles,
            List<BTrackRangeShift> rangeShifts,
            List<BTrackNote> notes)
        {
            var sections = new List<BTrackSection>
            {
                new(BTrackSectionId.Resolution, WriteResolution(syncTrack.Resolution)),
            };

            AddListSection(sections, BTrackSectionId.TempoMarker, GetLastPerTick(syncTrack.Tempos, tempo => tempo.Tick),
                (writer, tempo) =>
                {
                    writer.Write((long) tempo.Tick);
                    writer.Write(tempo.BeatsPerMinute);
                });
            AddListSection(sections, BTrackSectionId.TimeSignature,
                GetLastPerTick(syncTrack.TimeSignatures, timeSignature => timeSignature.Tick),
                (writer, timeSignature) =>
                {
                    writer.Write((long) timeSignature.Tick);
                    writer.Write(timeSignature.Numerator);
                    writer.Write(timeSignature.Denominator);
                });
            AddListSection(sections, BTrackSectionId.StarPower, starPower,
                (writer, phrase) =>
                {
                    writer.Write(phrase.Tick);
                    writer.Write(phrase.Length);
                });
            AddListSection(sections, BTrackSectionId.SoloSection, soloSections,
                (writer, phrase) =>
                {
                    writer.Write(phrase.Tick);
                    writer.Write(phrase.Length);
                });
            AddListSection(sections, BTrackSectionId.FlexLane, flexLanes,
                (writer, lane) =>
                {
                    writer.Write(lane.Tick);
                    writer.Write(lane.Length);
                    writer.Write((byte) (lane.IsDouble ? 1 : 0));
                });
            AddListSection(sections, BTrackSectionId.DrumFreestyle, drumFreestyles,
                (writer, phrase) =>
                {
                    writer.Write(phrase.Tick);
                    writer.Write(phrase.Length);
                    writer.Write((byte) (phrase.IsCoda ? 1 : 0));
                });
            AddListSection(sections, BTrackSectionId.RangeShift, rangeShifts,
                (writer, rangeShift) =>
                {
                    writer.Write(rangeShift.Tick);
                    writer.Write(rangeShift.Position);
                    writer.Write(rangeShift.Size);
                });
            AddListSection(sections, BTrackSectionId.Note, notes,
                (writer, note) =>
                {
                    writer.Write(note.Tick);
                    writer.Write(note.Length);
                    writer.Write((uint) note.Type);
                    writer.Write((uint) note.Flags);
                });

            sections.Sort((left, right) => left.Id.CompareTo(right.Id));
            return new BTrackHashResult(WriteFile(sections), WriteHashInput(sections));
        }

        private static byte[] WriteResolution(uint resolution)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(resolution);
            return stream.ToArray();
        }

        private static void AddListSection<T>(List<BTrackSection> sections, BTrackSectionId id, List<T> items,
            Action<BinaryWriter, T> writeItem)
        {
            if (items.Count == 0)
            {
                return;
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((uint) items.Count);
            foreach (var item in items)
            {
                writeItem(writer, item);
            }

            sections.Add(new BTrackSection(id, stream.ToArray()));
        }

        private static byte[] WriteFile(List<BTrackSection> sections)
        {
            var headerSize = 8;
            var mapSize = 4 + sections.Count * 20;
            var offset = (ulong) (headerSize + mapSize);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            WriteUInt32BigEndian(writer, MAGIC);
            writer.Write(VERSION);
            writer.Write((uint) sections.Count);
            foreach (var section in sections)
            {
                writer.Write(section.Id);
                writer.Write(offset);
                writer.Write((uint) section.Payload.Length);
                offset += (ulong) section.Payload.Length;
            }

            foreach (var section in sections)
            {
                writer.Write(section.Payload);
            }

            return stream.ToArray();
        }

        private static byte[] WriteHashInput(List<BTrackSection> sections)
        {
            var competitive = sections
                .Where(section => section.Id <= CompetitiveSectionIdMax)
                .ToList();

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((uint) competitive.Count);
            foreach (var section in competitive)
            {
                writer.Write(section.Id);
            }

            foreach (var section in competitive)
            {
                writer.Write(section.Payload);
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