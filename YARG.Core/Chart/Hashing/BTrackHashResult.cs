using System;

namespace YARG.Core.Chart
{
    public readonly struct BTrackHashResult
    {
        private readonly Lazy<string> _hash;

        public string Hash => _hash.Value;

        public byte[] BTrack { get; }

        public BTrackHashResult(byte[] bTrack)
            : this(bTrack, bTrack)
        {
        }

        public BTrackHashResult(byte[] bTrack, byte[] hashInput)
        {
            BTrack = bTrack;
            _hash = new Lazy<string>(() => Encode(Blake3.Hash(hashInput)));
        }

        internal static string Encode(byte[] hash)
        {
            return Convert.ToBase64String(hash)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}