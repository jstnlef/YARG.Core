using System;

namespace YARG.Core.Chart
{
    public readonly struct BTrackHashResult
    {
        private readonly Lazy<string> _hash;

        public string Hash => _hash.Value;

        public byte[] BTrack { get; }

        public BTrackHashResult(byte[] bTrack)
        {
            BTrack = bTrack;
            _hash = new Lazy<string>(() => ComputeHash(bTrack));
        }

        private static string ComputeHash(byte[] bTrack)
        {
            var hash = Blake3.Hash(bTrack);
            return Convert.ToBase64String(hash)
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}