using System;
using Data.HashFunction.Blake3;

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
            var blake3 = Blake3Factory.Instance.Create(new Blake3Config { HashSizeInBits = 256 });
            var hash = blake3.ComputeHash(bTrack).Hash;
            return Convert.ToBase64String(hash)
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}