// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System;
using FingerPuppet.SourceAFIS.Utils;

namespace FingerPuppet.SourceAFIS
{
    internal struct EdgeShape
    {
        public readonly int Length;
        public readonly byte ReferenceAngle;
        public readonly byte NeighborAngle;
        private const int PolarCacheBits = 8;
        private const uint PolarCacheRadius = 1u << PolarCacheBits;
        private const uint PolarCacheMask = PolarCacheRadius - 1;
        private static readonly int[,] PolarDistance = new int[PolarCacheRadius, PolarCacheRadius];
        private static readonly byte[,] PolarAngle = new byte[PolarCacheRadius, PolarCacheRadius];

        static EdgeShape()
        {
            for (var y = 0; y < PolarCacheRadius; ++y)
            for (var x = 0; x < PolarCacheRadius; ++x)
            {
                PolarDistance[y, x] = Convert.ToInt16(Math.Round(Math.Sqrt(MathEx.Sq(x) + MathEx.Sq(y))));
                if (y > 0 || x > 0)
                    PolarAngle[y, x] = Angle.AtanB(new Point(x, y));
                else
                    PolarAngle[y, x] = 0;
            }
        }

        public EdgeShape(FingerprintTemplate template, int reference, int neighbor)
        {
            var vector = template.Minutiae[neighbor].Position - template.Minutiae[reference].Position;
            var quadrant = 0;
            var x = vector.X;
            var y = vector.Y;

            if (y < 0)
            {
                x = -x;
                y = -y;
                quadrant = 128;
            }

            if (x < 0)
            {
                var tmp = -x;
                x = y;
                y = tmp;
                quadrant += 64;
            }

            var shift = MathEx.HighestBit((uint) (x | y) >> PolarCacheBits);

            Length = PolarDistance[y >> shift, x >> shift] << shift;

            var angle = (byte) (PolarAngle[y >> shift, x >> shift] + quadrant);
            ReferenceAngle = Angle.Difference(template.Minutiae[reference].Direction, angle);
            NeighborAngle = Angle.Difference(template.Minutiae[neighbor].Direction, Angle.Opposite(angle));
        }
    }
}