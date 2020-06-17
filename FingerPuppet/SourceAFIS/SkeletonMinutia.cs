// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System.Collections.Generic;
using FingerPuppet.SourceAFIS.Utils;

namespace FingerPuppet.SourceAFIS
{
    internal class SkeletonMinutia
    {
        public readonly Point Position;
        public readonly List<SkeletonRidge> Ridges = new List<SkeletonRidge>();
        public bool IsConsidered = true;

        public SkeletonMinutia(Point position)
        {
            Position = position;
        }

        public void AttachStart(SkeletonRidge ridge)
        {
            if (!Ridges.Contains(ridge))
            {
                Ridges.Add(ridge);
                ridge.Start = this;
            }
        }

        public void DetachStart(SkeletonRidge ridge)
        {
            if (Ridges.Contains(ridge))
            {
                Ridges.Remove(ridge);
                if (ridge.Start == this)
                    ridge.Start = null;
            }
        }
    }
}