// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System.Collections.Generic;
using FingerPuppet.SourceAFIS.Utils;

namespace FingerPuppet.SourceAFIS
{
    internal class SkeletonRidge
    {
        public readonly IList<Point> Points;
        public readonly SkeletonRidge Reversed;
        private SkeletonMinutia EndMinutia;
        private SkeletonMinutia StartMinutia;

        public SkeletonRidge()
        {
            Points = new CircularArray<Point>();
            Reversed = new SkeletonRidge(this);
        }

        private SkeletonRidge(SkeletonRidge reversed)
        {
            Reversed = reversed;
            Points = new ReversedList<Point>(reversed.Points);
        }

        public SkeletonMinutia Start
        {
            get => StartMinutia;
            set
            {
                if (StartMinutia != value)
                {
                    if (StartMinutia != null)
                    {
                        var detachFrom = StartMinutia;
                        StartMinutia = null;
                        detachFrom.DetachStart(this);
                    }

                    StartMinutia = value;
                    if (StartMinutia != null)
                        StartMinutia.AttachStart(this);
                    Reversed.EndMinutia = value;
                }
            }
        }

        public SkeletonMinutia End
        {
            get => EndMinutia;
            set
            {
                if (EndMinutia != value)
                {
                    EndMinutia = value;
                    Reversed.Start = value;
                }
            }
        }

        public void Detach()
        {
            Start = null;
            End = null;
        }

        public byte ComputeDirection()
        {
            const int segmentLength = 21;
            const int segmentSkip = 1;

            var first = segmentSkip;
            var last = segmentSkip + segmentLength - 1;

            if (last >= Points.Count)
            {
                var shift = last - Points.Count + 1;
                last -= shift;
                first -= shift;
            }

            if (first < 0)
                first = 0;

            return Angle.AtanB(Points[first], Points[last]);
        }
    }
}