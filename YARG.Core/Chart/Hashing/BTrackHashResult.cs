namespace YARG.Core.Chart
{
    public sealed class BTrackHashResult
    {
        public string Hash { get; }
        public byte[] BTrack { get; }

        public BTrackHashResult(string hash, byte[] bTrack)
        {
            Hash = hash;
            BTrack = bTrack;
        }
    }
}
