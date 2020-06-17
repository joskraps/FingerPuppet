// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System.Collections.Generic;
using System.Linq;
using FingerPuppet.SourceAFIS.Utils;

namespace FingerPuppet.SourceAFIS
{
    internal class FingerprintSkeleton
    {
        public List<SkeletonMinutia> Minutiae = new List<SkeletonMinutia>();
        public Point Size;

        public FingerprintSkeleton(bool[,] binary)
        {
            Size = Point.SizeOf(binary);
            var thinned = Thin(binary);
            var minutiaPoints = FindMinutiae(thinned);
            var linking = LinkNeighboringMinutiae(minutiaPoints);
            var minutiaMap = ComputeMinutiaCenters(linking);
            TraceRidges(thinned, minutiaMap);
            FixLinkingGaps();
            Filter();
        }

        private List<Point> FindMinutiae(bool[,] thinned)
        {
            var result = new List<Point>();
            foreach (var at in Size)
                if (at.Get(thinned))
                {
                    var count = Point.CornerNeighbors.Count(relative => (at + relative).Get(thinned, false));
                    if (count == 1 || count > 2)
                        result.Add(at);
                }

            return result;
        }

        private static Dictionary<Point, List<Point>> LinkNeighboringMinutiae(List<Point> minutiae)
        {
            var linking = new Dictionary<Point, List<Point>>();
            foreach (var minutiaPos in minutiae)
            {
                List<Point> ownLinks = null;
                foreach (var neighborRelative in Point.CornerNeighbors)
                {
                    var neighborPos = minutiaPos + neighborRelative;
                    if (linking.ContainsKey(neighborPos))
                    {
                        var neighborLinks = linking[neighborPos];
                        if (neighborLinks != ownLinks)
                        {
                            if (ownLinks != null)
                            {
                                neighborLinks.AddRange(ownLinks);
                                foreach (var mergedPos in ownLinks)
                                    linking[mergedPos] = neighborLinks;
                            }

                            ownLinks = neighborLinks;
                        }
                    }
                }

                if (ownLinks == null)
                    ownLinks = new List<Point>();
                ownLinks.Add(minutiaPos);
                linking[minutiaPos] = ownLinks;
            }

            return linking;
        }

        private Dictionary<Point, SkeletonMinutia> ComputeMinutiaCenters(Dictionary<Point, List<Point>> linking)
        {
            var centers = new Dictionary<Point, SkeletonMinutia>();
            foreach (var currentPos in linking.Keys)
            {
                var linkedMinutiae = linking[currentPos];
                var primaryPos = linkedMinutiae[0];
                if (!centers.ContainsKey(primaryPos))
                {
                    var sum = new Point();
                    foreach (var linkedPos in linkedMinutiae)
                        sum += linkedPos;
                    var center = new Point(sum.X / linkedMinutiae.Count, sum.Y / linkedMinutiae.Count);
                    var minutia = new SkeletonMinutia(center);
                    AddMinutia(minutia);
                    centers[primaryPos] = minutia;
                }

                centers[currentPos] = centers[primaryPos];
            }

            return centers;
        }

        private static void TraceRidges(bool[,] thinned, Dictionary<Point, SkeletonMinutia> minutiaePoints)
        {
            var leads = new Dictionary<Point, SkeletonRidge>();
            foreach (var minutiaPoint in minutiaePoints.Keys)
            foreach (var startRelative in Point.CornerNeighbors)
            {
                var start = minutiaPoint + startRelative;
                if (start.Get(thinned, false) && !minutiaePoints.ContainsKey(start) && !leads.ContainsKey(start))
                {
                    var ridge = new SkeletonRidge();
                    ridge.Points.Add(minutiaPoint);
                    ridge.Points.Add(start);
                    var previous = minutiaPoint;
                    var current = start;
                    do
                    {
                        var next = new Point();
                        foreach (var nextRelative in Point.CornerNeighbors)
                        {
                            next = current + nextRelative;
                            if (next.Get(thinned, false) && next != previous)
                                break;
                        }

                        previous = current;
                        current = next;
                        ridge.Points.Add(current);
                    } while (!minutiaePoints.ContainsKey(current));

                    var end = current;

                    ridge.Start = minutiaePoints[minutiaPoint];
                    ridge.End = minutiaePoints[end];
                    leads[ridge.Points[1]] = ridge;
                    leads[ridge.Reversed.Points[1]] = ridge;
                }
            }
        }

        private void FixLinkingGaps()
        {
            foreach (var minutia in Minutiae)
            foreach (var ridge in minutia.Ridges)
                if (ridge.Points[0] != minutia.Position)
                {
                    var filling = MathEx.ConstructLine(ridge.Points[0], minutia.Position);
                    for (var i = 1; i < filling.Length; ++i)
                        ridge.Reversed.Points.Add(filling[i]);
                }
        }

        private bool[,] Thin(bool[,] input)
        {
            const int maxIterations = 26;

            var neighborhoodTypes = GetNeighborhoodTypes();
            var partial = Size.Allocate<bool>();
            for (var y = 1; y < Size.Y - 1; ++y)
            for (var x = 1; x < Size.X - 1; ++x)
                partial[y, x] = input[y, x];

            var thinned = Size.Allocate<bool>();
            var removedAnything = true;
            for (var i = 0; i < maxIterations && removedAnything; ++i)
            {
                removedAnything = false;
                for (var evenY = 0; evenY < 2; ++evenY)
                for (var evenX = 0; evenX < 2; ++evenX)
                for (var y = 1 + evenY; y < Size.Y - 1; y += 2)
                for (var x = 1 + evenX; x < Size.X - 1; x += 2)
                    if (partial[y, x] && !thinned[y, x] &&
                        !(partial[y - 1, x] && partial[y + 1, x] && partial[y, x - 1] && partial[y, x + 1]))
                    {
                        var neighbors = (partial[y + 1, x + 1] ? 128u : 0u)
                                        | (partial[y + 1, x] ? 64u : 0u)
                                        | (partial[y + 1, x - 1] ? 32u : 0u)
                                        | (partial[y, x + 1] ? 16u : 0u)
                                        | (partial[y, x - 1] ? 8u : 0u)
                                        | (partial[y - 1, x + 1] ? 4u : 0u)
                                        | (partial[y - 1, x] ? 2u : 0u)
                                        | (partial[y - 1, x - 1] ? 1u : 0u);
                        if (neighborhoodTypes[neighbors] == NeighborhoodType.Removable
                            || neighborhoodTypes[neighbors] == NeighborhoodType.Ending
                            && IsFalseEnding(partial, new Point(x, y)))
                        {
                            removedAnything = true;
                            partial[y, x] = false;
                        }
                        else
                        {
                            thinned[y, x] = true;
                        }
                    }
            }

            return thinned;
        }

        private static NeighborhoodType[] GetNeighborhoodTypes()
        {
            var types = new NeighborhoodType[256];
            for (uint mask = 0; mask < 256; ++mask)
            {
                var TL = (mask & 1) != 0;
                var TC = (mask & 2) != 0;
                var TR = (mask & 4) != 0;
                var CL = (mask & 8) != 0;
                var CR = (mask & 16) != 0;
                var BL = (mask & 32) != 0;
                var BC = (mask & 64) != 0;
                var BR = (mask & 128) != 0;

                var count = MathEx.CountBits(mask);

                var diagonal = !TC && !CL && TL || !CL && !BC && BL || !BC && !CR && BR || !CR && !TC && TR;
                var horizontal = !TC && !BC && (TR || CR || BR) && (TL || CL || BL);
                var vertical = !CL && !CR && (TL || TC || TR) && (BL || BC || BR);
                var end = count == 1;

                if (end)
                    types[mask] = NeighborhoodType.Ending;
                else if (!diagonal && !horizontal && !vertical)
                    types[mask] = NeighborhoodType.Removable;
            }

            return types;
        }

        private static bool IsFalseEnding(bool[,] binary, Point ending)
        {
            foreach (var relativeNeighbor in Point.CornerNeighbors)
            {
                var neighbor = ending + relativeNeighbor;
                if (neighbor.Get(binary))
                {
                    var count = 0;
                    foreach (var relative2 in Point.CornerNeighbors)
                        if ((neighbor + relative2).Get(binary, false))
                            ++count;
                    return count > 2;
                }
            }

            return false;
        }

        private void Filter()
        {
            RemoveDots();
            RemovePores();
            RemoveGaps();
            RemoveTails();
            RemoveFragments();
            DisableBranchMinutiae();
        }

        private void RemovePores()
        {
            const int maxArmLength = 41;
            foreach (var minutia in Minutiae)
                if (minutia.Ridges.Count == 3)
                    for (var exit = 0; exit < 3; ++exit)
                    {
                        var exitRidge = minutia.Ridges[exit];
                        var arm1 = minutia.Ridges[(exit + 1) % 3];
                        var arm2 = minutia.Ridges[(exit + 2) % 3];
                        if (arm1.End == arm2.End && exitRidge.End != arm1.End && arm1.End != minutia &&
                            exitRidge.End != minutia)
                        {
                            var end = arm1.End;
                            if (end.Ridges.Count == 3 && arm1.Points.Count <= maxArmLength &&
                                arm2.Points.Count <= maxArmLength)
                            {
                                arm1.Detach();
                                arm2.Detach();
                                var merged = new SkeletonRidge();
                                merged.Start = minutia;
                                merged.End = end;
                                foreach (var point in MathEx.ConstructLine(minutia.Position, end.Position))
                                    merged.Points.Add(point);
                            }

                            break;
                        }
                    }

            RemoveKnots();
        }

        private void RemoveGaps()
        {
            const int minEndingLength = 7;
            var queue = new PriorityQueueF<Gap>();
            foreach (var end1 in Minutiae)
                if (end1.Ridges.Count == 1 && end1.Ridges[0].Points.Count >= minEndingLength)
                    foreach (var end2 in Minutiae)
                        if (end2 != end1 && end2.Ridges.Count == 1 && end1.Ridges[0].End != end2
                            && end2.Ridges[0].Points.Count >= minEndingLength && IsWithinGapLimits(end1, end2))
                        {
                            Gap gap;
                            gap.End1 = end1;
                            gap.End2 = end2;
                            queue.Enqueue((end1.Position - end2.Position).SqLength, gap);
                        }

            var shadow = GetShadow();
            while (queue.Count > 0)
            {
                var gap = queue.Dequeue();
                if (gap.End1.Ridges.Count == 1 && gap.End2.Ridges.Count == 1)
                {
                    var line = MathEx.ConstructLine(gap.End1.Position, gap.End2.Position);
                    if (!IsRidgeOverlapping(line, shadow))
                        AddGapRidge(shadow, gap, line);
                }
            }

            RemoveKnots();
        }

        private static bool IsWithinGapLimits(SkeletonMinutia end1, SkeletonMinutia end2)
        {
            const int ruptureSize = 5;
            const int gapSize = 20;
            const byte gapAngle = 32;

            var distanceSq = (end1.Position - end2.Position).SqLength;
            if (distanceSq <= MathEx.Sq(ruptureSize))
                return true;
            if (distanceSq > MathEx.Sq(gapSize))
                return false;

            var gapDirection = Angle.AtanB(end1.Position, end2.Position);
            var direction1 = Angle.AtanB(end1.Position, GetAngleSampleForGapRemoval(end1));
            if (Angle.Distance(direction1, Angle.Opposite(gapDirection)) > gapAngle)
                return false;
            var direction2 = Angle.AtanB(end2.Position, GetAngleSampleForGapRemoval(end2));
            if (Angle.Distance(direction2, gapDirection) > gapAngle)
                return false;
            return true;
        }

        private static Point GetAngleSampleForGapRemoval(SkeletonMinutia minutia)
        {
            const int angleSampleOffset = 22;
            var ridge = minutia.Ridges[0];
            if (angleSampleOffset < ridge.Points.Count)
                return ridge.Points[angleSampleOffset];
            return ridge.End.Position;
        }

        private static bool IsRidgeOverlapping(Point[] line, bool[,] shadow)
        {
            const int toleratedOverlapLength = 2;
            for (var i = toleratedOverlapLength; i < line.Length - toleratedOverlapLength; ++i)
                if (line[i].Get(shadow))
                    return true;
            return false;
        }

        private static void AddGapRidge(bool[,] shadow, Gap gap, Point[] line)
        {
            var ridge = new SkeletonRidge();
            foreach (var point in line)
                ridge.Points.Add(point);
            ridge.Start = gap.End1;
            ridge.End = gap.End2;
            foreach (var point in line)
                point.Set(shadow, true);
        }

        private void RemoveTails()
        {
            const int minTailLength = 21;
            foreach (var minutia in Minutiae)
                if (minutia.Ridges.Count == 1 && minutia.Ridges[0].End.Ridges.Count >= 3)
                    if (minutia.Ridges[0].Points.Count < minTailLength)
                        minutia.Ridges[0].Detach();
            RemoveDots();
            RemoveKnots();
        }

        private void RemoveFragments()
        {
            const int minFragmentLength = 22;
            foreach (var minutia in Minutiae)
                if (minutia.Ridges.Count == 1)
                {
                    var ridge = minutia.Ridges[0];
                    if (ridge.End.Ridges.Count == 1 && ridge.Points.Count < minFragmentLength)
                        ridge.Detach();
                }

            RemoveDots();
        }

        private void RemoveKnots()
        {
            foreach (var minutia in Minutiae)
                if (minutia.Ridges.Count == 2 && minutia.Ridges[0].Reversed != minutia.Ridges[1])
                {
                    var extended = minutia.Ridges[0].Reversed;
                    var removed = minutia.Ridges[1];
                    if (extended.Points.Count < removed.Points.Count)
                    {
                        MathEx.Swap(ref extended, ref removed);
                        extended = extended.Reversed;
                        removed = removed.Reversed;
                    }

                    extended.Points.RemoveAt(extended.Points.Count - 1);
                    foreach (var point in removed.Points)
                        extended.Points.Add(point);

                    extended.End = removed.End;
                    removed.Detach();
                }

            RemoveDots();
        }

        private void RemoveDots()
        {
            var removed = new List<SkeletonMinutia>();
            foreach (var minutia in Minutiae)
                if (minutia.Ridges.Count == 0)
                    removed.Add(minutia);
            foreach (var minutia in removed)
                RemoveMinutia(minutia);
        }

        private void DisableBranchMinutiae()
        {
            foreach (var minutia in Minutiae)
                if (minutia.Ridges.Count > 2)
                    minutia.IsConsidered = false;
        }

        public void AddMinutia(SkeletonMinutia minutia)
        {
            Minutiae.Add(minutia);
        }

        public void RemoveMinutia(SkeletonMinutia minutia)
        {
            Minutiae.Remove(minutia);
        }

        private bool[,] GetShadow()
        {
            var shadow = Size.Allocate<bool>();
            foreach (var minutia in Minutiae)
            {
                minutia.Position.Set(shadow, true);
                foreach (var ridge in minutia.Ridges)
                    if (ridge.Start.Position.Y <= ridge.End.Position.Y)
                        foreach (var point in ridge.Points)
                            point.Set(shadow, true);
            }

            return shadow;
        }

        private enum NeighborhoodType
        {
            Skeleton,
            Ending,
            Removable
        }

        private struct Gap
        {
            public SkeletonMinutia End1;
            public SkeletonMinutia End2;
        }
    }
}