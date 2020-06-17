// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System;
using System.Collections.Generic;
using FingerPuppet.SourceAFIS.Utils;
using Range = FingerPuppet.SourceAFIS.Utils.Range;

namespace FingerPuppet.SourceAFIS
{
    public class FingerprintMatcher
    {
        private const int MaxDistanceError = 13;
        private static readonly byte MaxAngleError = Angle.FromDegreesB(10);
        private readonly Dictionary<int, List<IndexedEdge>> EdgeHash = new Dictionary<int, List<IndexedEdge>>();
        private readonly PairInfo[] PairList;
        private readonly PriorityQueueF<EdgePair> PairQueue = new PriorityQueueF<EdgePair>();
        private readonly PairInfo[] PairsByProbe;
        private readonly FingerprintTemplate Template;
        private FingerprintTemplate Candidate;
        private int PairCount;
        private PairInfo[] PairsByCandidate;

        public FingerprintMatcher(FingerprintTemplate template)
        {
            Template = template;
            BuildEdgeHash();

            PairsByProbe = new PairInfo[Template.Minutiae.Count];
            PairList = new PairInfo[Template.Minutiae.Count];
            for (var i = 0; i < PairList.Length; ++i)
                PairList[i] = new PairInfo();
        }

        private PairInfo LastPair => PairList[PairCount - 1];

        public double Match(FingerprintTemplate candidate)
        {
            const int maxTriedRoots = 70;
            const int maxTriedTriangles = 7538;

            Candidate = candidate;

            var rootIndex = 0;
            var triangleIndex = 0;
            double bestScore = 0;
            foreach (var root in GetRoots())
            {
                var score = TryRoot(root);
                if (score > bestScore)
                    bestScore = score;
                ++rootIndex;
                if (rootIndex >= maxTriedRoots)
                    break;
                if (PairCount >= 3)
                {
                    ++triangleIndex;
                    if (triangleIndex >= maxTriedTriangles)
                        break;
                }
            }

            return bestScore;
        }

        private void BuildEdgeHash()
        {
            for (var referenceMinutia = 0; referenceMinutia < Template.Minutiae.Count; ++referenceMinutia)
            for (var neighborMinutia = 0; neighborMinutia < Template.Minutiae.Count; ++neighborMinutia)
                if (referenceMinutia != neighborMinutia)
                {
                    var edge = new IndexedEdge
                    {
                        Shape = new EdgeShape(Template, referenceMinutia, neighborMinutia),
                        Reference = referenceMinutia,
                        Neighbor = neighborMinutia
                    };
                    foreach (var hash in GetShapeCoverage(edge.Shape))
                    {
                        if (!EdgeHash.TryGetValue(hash, out var list))
                            EdgeHash[hash] = list = new List<IndexedEdge>();
                        list.Add(edge);
                    }
                }
        }

        private static IEnumerable<int> GetShapeCoverage(EdgeShape edge)
        {
            var minLengthBin = (edge.Length - MaxDistanceError) / MaxDistanceError;
            var maxLengthBin = (edge.Length + MaxDistanceError) / MaxDistanceError;
            var angleBins = MathEx.DivRoundUp(256, MaxAngleError);
            var minReferenceBin = Angle.Difference(edge.ReferenceAngle, MaxAngleError) / MaxAngleError;
            var maxReferenceBin = Angle.Add(edge.ReferenceAngle, MaxAngleError) / MaxAngleError;
            var endReferenceBin = (maxReferenceBin + 1) % angleBins;
            var minNeighborBin = Angle.Difference(edge.NeighborAngle, MaxAngleError) / MaxAngleError;
            var maxNeighborBin = Angle.Add(edge.NeighborAngle, MaxAngleError) / MaxAngleError;
            var endNeighborBin = (maxNeighborBin + 1) % angleBins;
            for (var lengthBin = minLengthBin; lengthBin <= maxLengthBin; ++lengthBin)
            for (var referenceBin = minReferenceBin;
                referenceBin != endReferenceBin;
                referenceBin = (referenceBin + 1) % angleBins)
            for (var neighborBin = minNeighborBin;
                neighborBin != endNeighborBin;
                neighborBin = (neighborBin + 1) % angleBins)
                yield return (referenceBin << 24) + (neighborBin << 16) + lengthBin;
        }

        private IEnumerable<MinutiaPair> GetRoots()
        {
            const int minEdgeLength = 58;
            const int maxEdgeLookups = 1633;

            var counter = 0;
            var filters = new Predicate<EdgeShape>[]
            {
                shape => shape.Length >= minEdgeLength,
                shape => shape.Length < minEdgeLength
            };
            foreach (var shapeFilter in filters)
                for (var step = 1; step < Candidate.Minutiae.Count; ++step)
                for (var pass = 0; pass < step + 1; ++pass)
                for (var candidateReference = pass;
                    candidateReference < Candidate.Minutiae.Count;
                    candidateReference += step + 1)
                {
                    var candidateNeighbor = (candidateReference + step) % Candidate.Minutiae.Count;
                    var candidateEdge = new EdgeShape(Candidate, candidateReference, candidateNeighbor);

                    if (!shapeFilter(candidateEdge)) continue;

                    if (!EdgeHash.TryGetValue(HashShape(candidateEdge), out var matches)) continue;

                    foreach (var match in matches)
                    {
                        if (!MatchingShapes(match.Shape, candidateEdge)) continue;

                        var pair = new MinutiaPair
                        {
                            Probe = match.Reference,
                            Candidate = candidateReference
                        };
                        yield return pair;
                        ++counter;
                        if (counter >= maxEdgeLookups)
                            yield break;
                    }
                }
        }

        private static int HashShape(EdgeShape edge)
        {
            return ((edge.ReferenceAngle / MaxAngleError) << 24) + ((edge.NeighborAngle / MaxAngleError) << 16) +
                   edge.Length / MaxDistanceError;
        }

        private static bool MatchingShapes(EdgeShape probe, EdgeShape candidate)
        {
            var lengthDelta = probe.Length - candidate.Length;
            if (lengthDelta >= -MaxDistanceError && lengthDelta <= MaxDistanceError)
            {
                var complementaryAngleError = Angle.Complementary(MaxAngleError);
                var referenceDelta = Angle.Difference(probe.ReferenceAngle, candidate.ReferenceAngle);
                if (referenceDelta <= MaxAngleError || referenceDelta >= complementaryAngleError)
                {
                    var neighborDelta = Angle.Difference(probe.NeighborAngle, candidate.NeighborAngle);
                    if (neighborDelta <= MaxAngleError || neighborDelta >= complementaryAngleError)
                        return true;
                }
            }

            return false;
        }

        private double TryRoot(MinutiaPair root)
        {
            CreateRootPairing(root);
            BuildPairing();
            return ComputeScore();
        }

        private void CreateRootPairing(MinutiaPair root)
        {
            if (PairsByCandidate == null || PairsByCandidate.Length < Candidate.Minutiae.Count)
                PairsByCandidate = new PairInfo[Candidate.Minutiae.Count];
            for (var i = 0; i < PairCount; ++i)
            {
                PairList[i].SupportingEdges = 0;
                PairsByProbe[PairList[i].Pair.Probe] = null;
                PairsByCandidate[PairList[i].Pair.Candidate] = null;
            }

            PairsByCandidate[root.Candidate] = PairsByProbe[root.Probe] = PairList[0];
            PairList[0].Pair = root;
            PairCount = 1;
        }

        private void BuildPairing()
        {
            while (true)
            {
                CollectEdges();
                SkipPaired();
                if (PairQueue.Count == 0)
                    break;
                AddPair(PairQueue.Dequeue());
            }
        }

        private void AddPair(EdgePair edge)
        {
            PairsByCandidate[edge.Neighbor.Candidate] = PairsByProbe[edge.Neighbor.Probe] = PairList[PairCount];
            PairList[PairCount].Pair = edge.Neighbor;
            PairList[PairCount].Reference = edge.Reference;
            ++PairCount;
        }

        private void SkipPaired()
        {
            while (PairQueue.Count > 0 && (PairsByProbe[PairQueue.Peek().Neighbor.Probe] != null
                                           || PairsByCandidate[PairQueue.Peek().Neighbor.Candidate] != null))
            {
                var edge = PairQueue.Dequeue();
                if (PairsByProbe[edge.Neighbor.Probe] != null &&
                    PairsByProbe[edge.Neighbor.Probe].Pair.Candidate == edge.Neighbor.Candidate)
                {
                    ++PairsByProbe[edge.Reference.Probe].SupportingEdges;
                    ++PairsByProbe[edge.Neighbor.Probe].SupportingEdges;
                }
            }
        }

        private void CollectEdges()
        {
            var reference = LastPair.Pair;
            var probeNeighbors = Template.EdgeTable[reference.Probe];
            var candidateNeigbors = Candidate.EdgeTable[reference.Candidate];
            var matches = FindMatchingPairs(probeNeighbors, candidateNeigbors);
            foreach (var match in matches)
            {
                var neighbor = match.Pair;
                if (PairsByCandidate[neighbor.Candidate] == null && PairsByProbe[neighbor.Probe] == null)
                {
                    PairQueue.Enqueue(match.Distance, new EdgePair {Reference = reference, Neighbor = neighbor});
                }
                else if (PairsByProbe[neighbor.Probe] != null &&
                         PairsByProbe[neighbor.Probe].Pair.Candidate == neighbor.Candidate)
                {
                    ++PairsByProbe[reference.Probe].SupportingEdges;
                    ++PairsByProbe[neighbor.Probe].SupportingEdges;
                }
            }
        }

        private static List<MatchingPair> FindMatchingPairs(NeighborEdge[] probeStar, NeighborEdge[] candidateStar)
        {
            var complementaryAngleError = Angle.Complementary(MaxAngleError);
            var results = new List<MatchingPair>();
            var range = new Range();

            foreach (var candidateEdge in candidateStar)
            {
                while (range.Begin < probeStar.Length &&
                       probeStar[range.Begin].Edge.Length < candidateEdge.Edge.Length - MaxDistanceError)
                    ++range.Begin;
                if (range.End < range.Begin)
                    range.End = range.Begin;
                while (range.End < probeStar.Length &&
                       probeStar[range.End].Edge.Length <= candidateEdge.Edge.Length + MaxDistanceError)
                    ++range.End;

                for (var probeIndex = range.Begin; probeIndex < range.End; ++probeIndex)
                {
                    var probeEdge = probeStar[probeIndex];
                    var referenceDiff =
                        Angle.Difference(probeEdge.Edge.ReferenceAngle, candidateEdge.Edge.ReferenceAngle);
                    if (referenceDiff <= MaxAngleError || referenceDiff >= complementaryAngleError)
                    {
                        var neighborDiff = Angle.Difference(probeEdge.Edge.NeighborAngle,
                            candidateEdge.Edge.NeighborAngle);
                        if (neighborDiff <= MaxAngleError || neighborDiff >= complementaryAngleError)
                            results.Add(new MatchingPair
                            {
                                Pair = new MinutiaPair
                                {
                                    Probe = probeEdge.Neighbor,
                                    Candidate = candidateEdge.Neighbor
                                },
                                Distance = candidateEdge.Edge.Length
                            });
                    }
                }
            }

            return results;
        }

        private double ComputeScore()
        {
            const int minSupportingEdges = 1;
            const double distanceErrorFlatness = 0.69;
            const double angleErrorFlatness = 0.27;

            const double pairCountFactor = 0.032;
            const double pairFractionFactor = 8.98;
            const double correctTypeFactor = 0.629;
            const double supportedCountFactor = 0.193;
            const double edgeCountFactor = 0.265;
            const double distanceAccuracyFactor = 9.9;
            const double angleAccuracyFactor = 2.79;

            var score = pairCountFactor * PairCount;
            score += pairFractionFactor * (PairCount / (double) Template.Minutiae.Count +
                                           PairCount / (double) Candidate.Minutiae.Count) / 2;

            for (var i = 0; i < PairCount; ++i)
            {
                var pair = PairList[i];
                if (pair.SupportingEdges >= minSupportingEdges)
                    score += supportedCountFactor;
                score += edgeCountFactor * (pair.SupportingEdges + 1);
                if (Template.Minutiae[pair.Pair.Probe].Type == Candidate.Minutiae[pair.Pair.Candidate].Type)
                    score += correctTypeFactor;
            }

            var innerDistanceRadius = Convert.ToInt32(distanceErrorFlatness * MaxDistanceError);
            var innerAngleRadius = Convert.ToInt32(angleErrorFlatness * MaxAngleError);

            var distanceErrorSum = 0;
            var angleErrorSum = 0;

            for (var i = 1; i < PairCount; ++i)
            {
                var pair = PairList[i];
                var probeEdge = new EdgeShape(Template, pair.Reference.Probe, pair.Pair.Probe);
                var candidateEdge = new EdgeShape(Candidate, pair.Reference.Candidate, pair.Pair.Candidate);
                distanceErrorSum += Math.Abs(probeEdge.Length - candidateEdge.Length);
                angleErrorSum += Math.Max(innerDistanceRadius,
                    Angle.Distance(probeEdge.ReferenceAngle, candidateEdge.ReferenceAngle));
                angleErrorSum += Math.Max(innerAngleRadius,
                    Angle.Distance(probeEdge.NeighborAngle, candidateEdge.NeighborAngle));
            }

            if (PairCount < 2) return score;

            var maxDistanceError = MaxDistanceError * (PairCount - 1);
            score += distanceAccuracyFactor * (maxDistanceError - distanceErrorSum) / maxDistanceError;
            var maxAngleError = MaxAngleError * (PairCount - 1) * 2;
            score += angleAccuracyFactor * (maxAngleError - angleErrorSum) / maxAngleError;

            return score;
        }

        private struct MinutiaPair
        {
            public int Probe;
            public int Candidate;
        }

        private class EdgePair
        {
            public MinutiaPair Neighbor;
            public MinutiaPair Reference;
        }

        private class PairInfo
        {
            public MinutiaPair Pair;
            public MinutiaPair Reference;
            public int SupportingEdges;
        }

        private class IndexedEdge
        {
            public int Neighbor;
            public int Reference;
            public EdgeShape Shape;
        }

        private struct MatchingPair
        {
            public MinutiaPair Pair;
            public int Distance;
        }
    }
}