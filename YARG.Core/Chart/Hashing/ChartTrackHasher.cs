using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ReSharper disable InconsistentNaming

namespace YARG.Core.Chart.Hashing
{
    public static class ChartTrackHasher
    {
        private const uint MAGIC = 0x43484E46;
        private const uint VERSION = 20260801;

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

        public static bool TryCalculateTrackHash(SongChart chart, Instrument instrument, Difficulty difficulty, out BTrackHashResult result)
        {
            result = default;

            List<Phrase> phrases;
            List<TextEvent> textEvents;
            List<RangeShift> rangeShiftEvents;
            List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)> notes;
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
                    notes = NormalizeGuitarNotes(guitarDifficulty.Notes, TryMapFiveFretNote);
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
                    notes = NormalizeGuitarNotes(sixFretDifficulty.Notes, TryMapSixFretNote);
                    break;

                case Instrument.FourLaneDrums:
                case Instrument.ProDrums:
                case Instrument.FiveLaneDrums:
                    if (!chart.GetDrumsTrack(instrument).TryGetDifficulty(difficulty, out var drumDifficulty))
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
                ResolveOverlaps(PruneEmptyPhrases(GetPhrases(phrases, PhraseType.StarPower), notes),
                    phrase => phrase.Tick, phrase => phrase.Length, (_, tick, length) => (tick, length)),
                ResolveOverlaps(PruneEmptyPhrases(GetPhrases(phrases, PhraseType.Solo), notes),
                    phrase => phrase.Tick, phrase => phrase.Length, (_, tick, length) => (tick, length)),
                PruneEmptyFlexLanes(GetFlexLanes(phrases), notes),
                GetDrumFreestyles(phrases, textEvents),
                GetRangeShifts(rangeShiftEvents),
                notes);
            return true;
        }

        private static List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)> NormalizeGuitarNotes(
            List<GuitarNote> notes, TryMapFret tryMap)
        {
            var normalized = new List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)>();
            foreach (var note in notes)
            {
                foreach (var child in note.AllNotes)
                {
                    if (tryMap(child, out var type))
                    {
                        normalized.Add((child.Tick, child.TickLength, type, MapGuitarFlags(child)));
                    }
                }
            }
            return NormalizeNotes(normalized);
        }

        private delegate bool TryMapFret(GuitarNote note, out BTrackNoteType type);

        private static bool TryMapFiveFretNote(GuitarNote note, out BTrackNoteType type)
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

        private static List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)> NormalizeDrumNotes(
            Instrument instrument, List<DrumNote> notes)
        {
            var normalized = new List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)>();
            foreach (var note in notes)
            {
                foreach (var child in note.AllNotes)
                {
                    if (TryMapDrumNote(instrument, child, out var type, out var flags))
                    {
                        normalized.Add((child.Tick, child.TickLength, type, flags));
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

        private static List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)> NormalizeNotes(
            List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)> notes)
        {
            var deduped = notes
                .GroupBy(note => new { note.Tick, note.Type })
                .Select(group => (
                    Tick: group.Key.Tick,
                    Length: group.Max(note => note.Length),
                    Type: group.Key.Type,
                    Flags: NormalizeFlags(CombineFlags(group.Select(note => note.Flags)))))
                .OrderBy(note => note.Tick)
                .ThenBy(note => note.Type)
                .ToList();

            return deduped
                .GroupBy(note => note.Type)
                .SelectMany(group => ResolveOverlaps(group.OrderBy(note => note.Tick).ToList(),
                    note => note.Tick, note => note.Length,
                    (note, tick, length) => (tick, length, note.Type, note.Flags)))
                .OrderBy(note => note.Tick)
                .ThenBy(note => note.Type)
                .ToList();
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

        private static List<(long Tick, long Length)> GetPhrases(List<Phrase> phrases, PhraseType type)
        {
            return phrases
                .Where(phrase => phrase.Type == type)
                .GroupBy(phrase => phrase.Tick)
                .Select(group => (Tick: (long) group.Key, Length: (long) group.Max(phrase =>
                    phrase.TickLength + (type == PhraseType.Solo ? 1 : 0))))
                .OrderBy(phrase => phrase.Tick)
                .ToList();
        }

        private static List<(long Tick, long Length, bool IsDouble)> GetFlexLanes(List<Phrase> phrases)
        {
            return phrases
                .Where(phrase => phrase.Type is PhraseType.TremoloLane or PhraseType.TrillLane)
                .GroupBy(phrase => new { phrase.Tick, IsDouble = phrase.Type == PhraseType.TrillLane })
                .Select(group => (Tick: (long) group.Key.Tick, Length: (long) group.Max(phrase => phrase.TickLength), IsDouble: group.Key.IsDouble))
                .OrderBy(lane => lane.Tick)
                .ThenBy(lane => lane.IsDouble)
                .ToList();
        }

        private static List<(long Tick, long Length, bool IsCoda)> GetDrumFreestyles(List<Phrase> phrases, List<TextEvent> textEvents)
        {
            return phrases
                .Where(phrase => phrase.Type == PhraseType.DrumFill)
                .Select(phrase => (
                    Tick: (long) phrase.Tick,
                    Length: (long) phrase.TickLength,
                    IsCoda: HasCodaOnOrBefore(phrases, textEvents, phrase.Tick)))
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

        private static List<(long Tick, long Position, long Size)> GetRangeShifts(List<RangeShift> rangeShifts)
        {
            return GetLastPerTick(rangeShifts, rangeShift => rangeShift.Tick)
                .Select(rangeShift => (Tick: (long) rangeShift.Tick, Position: (long) rangeShift.Range, Size: (long) rangeShift.Size))
                .ToList();
        }

        private static List<(long Tick, long Length)> PruneEmptyPhrases(
            List<(long Tick, long Length)> phrases,
            List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)> notes)
        {
            return phrases
                .Where(phrase => notes.Any(note => note.Tick >= phrase.Tick && note.Tick < phrase.Tick + Math.Max(phrase.Length, 1)))
                .ToList();
        }

        private static List<(long Tick, long Length, bool IsDouble)> PruneEmptyFlexLanes(
            List<(long Tick, long Length, bool IsDouble)> lanes,
            List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)> notes)
        {
            return lanes
                .Where(lane => notes.Any(note => note.Tick >= lane.Tick && note.Tick <= lane.Tick + lane.Length))
                .ToList();
        }

        private static List<T> ResolveOverlaps<T>(
            List<T> items,
            Func<T, long> tick,
            Func<T, long> length,
            Func<T, long, long, T> withRange)
        {
            var resolved = items.ToList();
            for (var i = 0; i < resolved.Count - 1; i++)
            {
                var currentTick = tick(resolved[i]);
                var nextTick = tick(resolved[i + 1]);
                if (currentTick >= nextTick)
                {
                    continue;
                }

                var currentEnd = currentTick + length(resolved[i]);
                if (currentEnd <= nextTick)
                {
                    continue;
                }

                var nextEnd = Math.Max(currentEnd, nextTick + length(resolved[i + 1]));
                resolved[i] = withRange(resolved[i], currentTick, nextTick - currentTick);
                resolved[i + 1] = withRange(resolved[i + 1], nextTick, nextEnd - nextTick);
            }
            return resolved;
        }

        private static BTrackHashResult WriteBTrack(
            SyncTrack syncTrack,
            List<(long Tick, long Length)> starPower,
            List<(long Tick, long Length)> soloSections,
            List<(long Tick, long Length, bool IsDouble)> flexLanes,
            List<(long Tick, long Length, bool IsCoda)> drumFreestyles,
            List<(long Tick, long Position, long Size)> rangeShifts,
            List<(long Tick, long Length, BTrackNoteType Type, BTrackNoteFlags Flags)> notes)
        {
            var resolution = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(resolution, syncTrack.Resolution);
            var sections = new List<(ulong Id, byte[] Payload)>
            {
                ((ulong) BTrackSectionId.Resolution, resolution),
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

            return new BTrackHashResult(WriteFile(sections), WriteHashInput(sections));
        }

        private static void AddListSection<T>(List<(ulong Id, byte[] Payload)> sections, BTrackSectionId id, List<T> items,
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

            sections.Add(((ulong) id, stream.ToArray()));
        }

        private static byte[] WriteFile(List<(ulong Id, byte[] Payload)> sections)
        {
            var headerSize = 8;
            var mapSize = 4 + sections.Count * 20;
            var offset = (ulong) (headerSize + mapSize);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            var magic = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(magic, MAGIC);
            writer.Write(magic);
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

        private static byte[] WriteHashInput(List<(ulong Id, byte[] Payload)> sections)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((uint) sections.Count);
            foreach (var section in sections)
            {
                writer.Write(section.Id);
            }

            foreach (var section in sections)
            {
                writer.Write(section.Payload);
            }

            return stream.ToArray();
        }

        private static List<T> GetLastPerTick<T>(List<T> events, Func<T, uint> getTick)
        {
            return events
                .GroupBy(getTick)
                .Select(group => group.Last())
                .OrderBy(getTick)
                .ToList();
        }
    }
}