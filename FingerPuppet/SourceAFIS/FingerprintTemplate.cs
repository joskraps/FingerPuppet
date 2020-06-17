// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FingerPuppet.SourceAFIS.Utils;
using Range = FingerPuppet.SourceAFIS.Utils.Range;

namespace FingerPuppet.SourceAFIS
{
    public class FingerprintTemplate
    {
        internal NeighborEdge[][] EdgeTable;
        internal List<FingerprintMinutia> Minutiae = new List<FingerprintMinutia>();

        public FingerprintTemplate(byte[,] image, int dpi = 500)
        {
            const int blockSize = 15;

            if (dpi != 500)
                image = ScaleImage(image, dpi);
            image = InvertInput(image);
            var blocks = new BlockMap(new Point(image.GetLength(1), image.GetLength(0)), blockSize);

            var histogram = ComputeHistogram(blocks, image);
            var smoothHistogram = ComputeSmoothedHistogram(blocks, histogram);
            var mask = ComputeMask(blocks, histogram);
            var equalized = Equalize(blocks, image, smoothHistogram, mask);

            var orientation = ComputeOrientationMap(equalized, mask, blocks);
            var smoothed = SmoothByOrientation(equalized, orientation, mask, blocks, 0,
                ConstructOrientedLines(step: 1.59));
            var orthogonal = SmoothByOrientation(smoothed, orientation, mask, blocks, Angle.PIB,
                ConstructOrientedLines(11, 4, 1.11));

            var binary = Binarize(smoothed, orthogonal, mask, blocks);
            CleanupBinarized(binary);

            var pixelMask = FillBlocks(mask, blocks);
            var innerMask = ComputeInnerMask(pixelMask);

            var inverted = Invert(binary, pixelMask);

            var ridges = new FingerprintSkeleton(binary);
            var valleys = new FingerprintSkeleton(inverted);

            CollectMinutiae(ridges, FingerprintMinutiaType.Ending);
            CollectMinutiae(valleys, FingerprintMinutiaType.Bifurcation);
            ApplyMask(innerMask);
            RemoveMinutiaClouds();
            LimitTemplateSize();
            ShuffleMinutiae();

            BuildEdgeTable();
        }

        public FingerprintTemplate(XElement xml)
        {
            Minutiae = (from minutia in xml.Elements("Minutia")
                select new FingerprintMinutia
                {
                    Position = new Point((int) minutia.Attribute("X"), (int) minutia.Attribute("Y")),
                    Direction = (byte) (uint) minutia.Attribute("Direction"),
                    Type = (FingerprintMinutiaType) Enum.Parse(typeof(FingerprintMinutiaType),
                        (string) minutia.Attribute("Type"), false)
                }).ToList();
            BuildEdgeTable();
        }

        public XElement ToXml()
        {
            return new XElement("FingerprintTemplate",
                from minutia in Minutiae
                select new XElement("Minutia",
                    new XAttribute("X", minutia.Position.X),
                    new XAttribute("Y", minutia.Position.Y),
                    new XAttribute("Direction", minutia.Direction),
                    new XAttribute("Type", minutia.Type.ToString())));
        }

        public override string ToString()
        {
            return ToXml().ToString();
        }

        private static byte[,] ScaleImage(byte[,] input, int dpi)
        {
            var size = Point.SizeOf(input);
            return ScaleImage(input, Convert.ToInt32(500.0 / dpi * size.X), Convert.ToInt32(500.0 / dpi * size.Y));
        }

        private static byte[,] ScaleImage(byte[,] input, int newX, int newY)
        {
            var oldSize = Point.SizeOf(input);
            var output = new byte[newY, newX];
            var scaleX = newX / (double) oldSize.X;
            var scaleY = newY / (double) oldSize.Y;
            var descaleX = 1 / scaleX;
            var descaleY = 1 / scaleY;
            for (var y = 0; y < newY; ++y)
            {
                var y1 = y * descaleY;
                var y2 = y1 + descaleY;
                var y1i = (int) y1;
                var y2i = (int) Math.Ceiling(y2);
                for (var x = 0; x < newX; ++x)
                {
                    var x1 = x * descaleX;
                    var x2 = x1 + descaleX;
                    var x1i = (int) x1;
                    var x2i = (int) Math.Ceiling(x2);
                    double sum = 0;
                    for (var oy = y1i; oy < y2i; ++oy)
                    {
                        var ry = Math.Min(oy + 1, y2) - Math.Max(oy, y1);
                        for (var ox = x1i; ox < x2i; ++ox)
                        {
                            var rx = Math.Min(ox + 1, x2) - Math.Max(ox, x1);
                            sum += rx * ry * input[oy, ox];
                        }
                    }

                    output[y, x] = Convert.ToByte(sum * (scaleX * scaleY));
                }
            }

            return output;
        }

        private static byte[,] InvertInput(byte[,] image)
        {
            var result = new byte[image.GetLength(0), image.GetLength(1)];
            for (var y = 0; y < image.GetLength(0); ++y)
            for (var x = 0; x < image.GetLength(1); ++x)
                result[y, x] = (byte) (255 - image[image.GetLength(0) - y - 1, x]);
            return result;
        }

        private static int[,,] ComputeHistogram(BlockMap blocks, byte[,] image)
        {
            var histogram = new int[blocks.BlockCount.Y, blocks.BlockCount.X, 256];
            foreach (var block in blocks.AllBlocks)
            {
                var area = blocks.BlockAreas[block];
                for (var y = area.Bottom; y < area.Top; ++y)
                for (var x = area.Left; x < area.Right; ++x)
                    ++histogram[block.Y, block.X, image[y, x]];
            }

            return histogram;
        }

        private static int[,,] ComputeSmoothedHistogram(BlockMap blocks, int[,,] input)
        {
            var blocksAround = new[] {new Point(0, 0), new Point(-1, 0), new Point(0, -1), new Point(-1, -1)};
            var output = new int[blocks.CornerCount.Y, blocks.CornerCount.X, 256];
            foreach (var corner in blocks.AllCorners)
            foreach (var relative in blocksAround)
            {
                var block = corner + relative;

                if (!blocks.AllBlocks.Contains(block)) continue;

                for (var i = 0; i < 256; ++i)
                    output[corner.Y, corner.X, i] += input[block.Y, block.X, i];
            }

            return output;
        }

        private static void CleanupBinarized(bool[,] binary)
        {
            var size = Point.SizeOf(binary);
            var islands = FilterBinarized(InvertMask(binary));
            var holes = FilterBinarized(binary);
            for (var y = 0; y < size.Y; ++y)
            for (var x = 0; x < size.X; ++x)
                binary[y, x] = binary[y, x] && !islands[y, x] || holes[y, x];
            RemoveCrosses(binary);
        }

        private static bool[,] ComputeMask(BlockMap blocks, int[,,] histogram)
        {
            var contrast = ComputeClippedContrast(blocks, histogram);
            var mask = ComputeAbsoluteContrast(contrast);
            MergeMask(mask, ComputeRelativeContrast(contrast, blocks));
            MergeMask(mask, ApplyVotingFilter(mask, 9, 0.86, 7));
            MergeMask(mask, FilterBlockErrors(mask));
            mask = InvertMask(mask);
            MergeMask(mask, FilterBlockErrors(mask));
            MergeMask(mask, FilterBlockErrors(mask));
            MergeMask(mask, ApplyVotingFilter(mask, 7, borderDist: 4));
            return mask;
        }

        private static bool[,] FilterBlockErrors(bool[,] input)
        {
            return ApplyVotingFilter(input, majority: 0.7, borderDist: 4);
        }

        private static bool[,] FilterBinarized(bool[,] input)
        {
            return ApplyVotingFilter(input, 2, 0.61, 17);
        }

        private static void MergeMask(bool[,] mask, bool[,] merged)
        {
            var size = Point.SizeOf(mask);
            for (var y = 0; y < size.Y; ++y)
            for (var x = 0; x < size.X; ++x)
                mask[y, x] |= merged[y, x];
        }

        private static bool[,] InvertMask(bool[,] mask)
        {
            var size = Point.SizeOf(mask);
            var inverted = size.Allocate<bool>();
            for (var y = 0; y < size.Y; ++y)
            for (var x = 0; x < size.X; ++x)
                inverted[y, x] = !mask[y, x];
            return inverted;
        }

        private static bool[,] Invert(bool[,] binary, bool[,] mask)
        {
            var size = Point.SizeOf(binary);
            var inverted = size.Allocate<bool>();
            for (var y = 0; y < size.Y; ++y)
            for (var x = 0; x < size.X; ++x)
                inverted[y, x] = !binary[y, x] && mask[y, x];
            return inverted;
        }

        private static bool[,] FillBlocks(bool[,] mask, BlockMap blocks)
        {
            var pixelized = blocks.PixelCount.Allocate<bool>();
            foreach (var block in blocks.AllBlocks)
                if (block.Get(mask))
                    foreach (var pixel in blocks.BlockAreas[block])
                        pixel.Set(pixelized, true);
            return pixelized;
        }

        private static byte[,] ComputeClippedContrast(BlockMap blocks, int[,,] histogram)
        {
            const double clipFraction = 0.08;

            var result = new byte[blocks.BlockCount.Y, blocks.BlockCount.X];
            foreach (var block in blocks.AllBlocks)
            {
                var area = 0;
                for (var i = 0; i < 256; ++i)
                    area += histogram[block.Y, block.X, i];
                var clipLimit = Convert.ToInt32(area * clipFraction);

                var accumulator = 0;
                var lowerBound = 255;
                for (var i = 0; i < 256; ++i)
                {
                    accumulator += histogram[block.Y, block.X, i];
                    if (accumulator > clipLimit)
                    {
                        lowerBound = i;
                        break;
                    }
                }

                accumulator = 0;
                var upperBound = 0;
                for (var i = 255; i >= 0; --i)
                {
                    accumulator += histogram[block.Y, block.X, i];
                    if (accumulator > clipLimit)
                    {
                        upperBound = i;
                        break;
                    }
                }

                result[block.Y, block.X] = (byte) (upperBound - lowerBound);
            }

            return result;
        }

        private static bool[,] ComputeAbsoluteContrast(byte[,] contrast)
        {
            const int limit = 17;
            var size = Point.SizeOf(contrast);
            var result = size.Allocate<bool>();
            for (var y = 0; y < size.Y; ++y)
            for (var x = 0; x < size.X; ++x)
                if (contrast[y, x] < limit)
                    result[y, x] = true;
            return result;
        }

        private static bool[,] ComputeRelativeContrast(byte[,] contrast, BlockMap blocks)
        {
            const int sampleSize = 168568;
            const double sampleFraction = 0.49;
            const double relativeLimit = 0.34;

            var sortedContrast = new List<byte>();
            foreach (var contrastItem in contrast)
                sortedContrast.Add(contrastItem);
            sortedContrast.Sort();
            sortedContrast.Reverse();

            var pixelsPerBlock = blocks.PixelCount.Area / blocks.AllBlocks.TotalArea;
            var sampleCount = Math.Min(sortedContrast.Count, sampleSize / pixelsPerBlock);
            var consideredBlocks = Math.Max(Convert.ToInt32(sampleCount * sampleFraction), 1);

            var averageContrast = 0;
            for (var i = 0; i < consideredBlocks; ++i)
                averageContrast += sortedContrast[i];
            averageContrast /= consideredBlocks;
            var limit = Convert.ToByte(averageContrast * relativeLimit);

            var result = blocks.BlockCount.Allocate<bool>();
            for (var y = 0; y < blocks.BlockCount.Y; ++y)
            for (var x = 0; x < blocks.BlockCount.X; ++x)
                if (contrast[y, x] < limit)
                    result[y, x] = true;
            return result;
        }

        private static bool[,] ApplyVotingFilter(bool[,] input, int radius = 1, double majority = 0.51,
            int borderDist = 0)
        {
            var size = Point.SizeOf(input);
            var rect = new Rectangle(new Point(borderDist, borderDist),
                new Point(size.X - 2 * borderDist, size.Y - 2 * borderDist));
            var output = size.Allocate<bool>();
            for (var y = rect.RangeY.Begin; y < rect.RangeY.End; ++y)
            for (var x = rect.Left; x < rect.Right; ++x)
            {
                var neighborhood = Rectangle.Between(
                    new Point(Math.Max(x - radius, 0), Math.Max(y - radius, 0)),
                    new Point(Math.Min(x + radius + 1, size.X), Math.Min(y + radius + 1, size.Y)));

                var ones = 0;
                for (var ny = neighborhood.Bottom; ny < neighborhood.Top; ++ny)
                for (var nx = neighborhood.Left; nx < neighborhood.Right; ++nx)
                    if (input[ny, nx])
                        ++ones;

                var voteWeight = 1.0 / neighborhood.TotalArea;
                if (ones * voteWeight >= majority)
                    output[y, x] = true;
            }

            return output;
        }

        private static double[,] Equalize(BlockMap blocks, byte[,] image, int[,,] histogram, bool[,] blockMask)
        {
            const double maxScaling = 3.99;
            const double minScaling = 0.25;
            const double rangeMin = -1;
            const double rangeMax = 1;
            const double rangeSize = rangeMax - rangeMin;
            const double widthMax = rangeSize / 256 * maxScaling;
            const double widthMin = rangeSize / 256 * minScaling;

            var limitedMin = new double[256];
            var limitedMax = new double[256];
            var toFloatTable = new double[256];

            for (var i = 0; i < 256; ++i)
            {
                limitedMin[i] = Math.Max(i * widthMin + rangeMin, rangeMax - (255 - i) * widthMax);
                limitedMax[i] = Math.Min(i * widthMax + rangeMin, rangeMax - (255 - i) * widthMin);
                toFloatTable[i] = i / 255;
            }

            var cornerMapping = new double[blocks.CornerCount.Y, blocks.CornerCount.X, 256];

            foreach (var corner in blocks.AllCorners)
                if (corner.Get(blockMask, false)
                    || new Point(corner.X - 1, corner.Y).Get(blockMask, false)
                    || new Point(corner.X, corner.Y - 1).Get(blockMask, false)
                    || new Point(corner.X - 1, corner.Y - 1).Get(blockMask, false))
                {
                    var area = 0;

                    for (var i = 0; i < 256; ++i)
                        area += histogram[corner.Y, corner.X, i];

                    var widthWeigth = rangeSize / area;

                    var top = rangeMin;

                    for (var i = 0; i < 256; ++i)
                    {
                        var width = histogram[corner.Y, corner.X, i] * widthWeigth;
                        var equalized = top + toFloatTable[i] * width;

                        top += width;

                        var limited = equalized;

                        if (limited < limitedMin[i])
                            limited = limitedMin[i];
                        if (limited > limitedMax[i])
                            limited = limitedMax[i];
                        cornerMapping[corner.Y, corner.X, i] = limited;
                    }
                }

            var result = new double[blocks.PixelCount.Y, blocks.PixelCount.X];
            foreach (var block in blocks.AllBlocks)
                if (block.Get(blockMask))
                {
                    var area = blocks.BlockAreas[block];
                    for (var y = area.Bottom; y < area.Top; ++y)
                    for (var x = area.Left; x < area.Right; ++x)
                    {
                        var pixel = image[y, x];

                        var bottomLeft = cornerMapping[block.Y, block.X, pixel];
                        var bottomRight = cornerMapping[block.Y, block.X + 1, pixel];
                        var topLeft = cornerMapping[block.Y + 1, block.X, pixel];
                        var topRight = cornerMapping[block.Y + 1, block.X + 1, pixel];

                        var fraction = area.GetFraction(new Point(x, y));
                        result[y, x] = MathEx.Interpolate(topLeft, topRight, bottomLeft, bottomRight, fraction);
                    }
                }

            return result;
        }

        private static byte[,] ComputeOrientationMap(double[,] image, bool[,] mask, BlockMap blocks)
        {
            var accumulated = ComputePixelwiseOrientation(image, mask, blocks);
            var byBlock = AverageBlockOrientations(accumulated, blocks, mask);
            var smooth = SmoothOutOrientationMap(byBlock, mask);
            return ConvertOrientationVectorsToAngles(smooth, mask);
        }

        private static PointF[,] ComputePixelwiseOrientation(double[,] input, bool[,] mask, BlockMap blocks)
        {
            var neighbors = GetTestedOrientations();

            var orientation = new PointF[input.GetLength(0), input.GetLength(1)];
            for (var blockY = 0; blockY < blocks.BlockCount.Y; ++blockY)
            {
                var validMaskRange = GetMaskLineRange(mask, blockY);
                if (validMaskRange.Length > 0)
                {
                    var validXRange = new Range(blocks.BlockAreas[blockY, validMaskRange.Begin].Left,
                        blocks.BlockAreas[blockY, validMaskRange.End - 1].Right);
                    for (var y = blocks.BlockAreas[blockY, 0].Bottom; y < blocks.BlockAreas[blockY, 0].Top; ++y)
                        foreach (var neighbor in neighbors[y % neighbors.Count])
                        {
                            var radius = Math.Max((sbyte) Math.Abs(neighbor.CheckLocation.X),
                                (sbyte) Math.Abs(neighbor.CheckLocation.Y));
                            if (y - radius < 0 || y + radius >= input.GetLength(0)) continue;
                            var xRange = new Range(Math.Max(radius, validXRange.Begin),
                                Math.Min(input.GetLength(1) - radius, validXRange.End));
                            for (var x = xRange.Begin; x < xRange.End; ++x)
                            {
                                var before = input[y - neighbor.CheckLocation.Y, x - neighbor.CheckLocation.X];
                                var at = input[y, x];
                                var after = input[y + neighbor.CheckLocation.Y, x + neighbor.CheckLocation.X];
                                var strength = at - Math.Max(before, after);
                                if (strength > 0)
                                    orientation[y, x] = orientation[y, x] + strength * neighbor.OrientationVector;
                            }
                        }
                }
            }

            return orientation;
        }

        private static List<List<ConsideredOrientation>> GetTestedOrientations()
        {
            const double minHalfDistance = 2;
            const double maxHalfDistance = 6;
            const int orientationListSplit = 50;
            const int orientationsChecked = 20;

            var random = new Random(0);
            var allSplits = new List<List<ConsideredOrientation>>();
            for (var i = 0; i < orientationListSplit; ++i)
            {
                var orientations = new List<ConsideredOrientation>();
                for (var j = 0; j < orientationsChecked; ++j)
                {
                    var orientation = new ConsideredOrientation();
                    do
                    {
                        var angle = Angle.FromFraction(random.NextDouble() * 0.5);
                        var distance =
                            MathEx.InterpolateExponential(minHalfDistance, maxHalfDistance, random.NextDouble());
                        orientation.CheckLocation = (distance * Angle.ToVector(angle)).Round();
                    } while (orientation.CheckLocation == new Point() || orientation.CheckLocation.Y < 0);

                    orientation.OrientationVector =
                        Angle.ToVector(Angle.Add(Angle.ToOrientation(Angle.Atan(orientation.CheckLocation)), Math.PI));
                    if (orientations.All(info => info.CheckLocation != orientation.CheckLocation))
                        orientations.Add(orientation);
                }

                orientations.Sort((left, right) => MathEx.CompareYX(left.CheckLocation, right.CheckLocation));
                allSplits.Add(orientations);
            }

            return allSplits;
        }

        private static Range GetMaskLineRange(bool[,] mask, int y)
        {
            var first = -1;
            var last = -1;
            for (var x = 0; x < mask.GetLength(1); ++x)
                if (mask[y, x])
                {
                    last = x;
                    if (first < 0)
                        first = x;
                }

            if (first >= 0)
                return new Range(first, last + 1);
            return new Range();
        }

        private static PointF[,] AverageBlockOrientations(PointF[,] orientation, BlockMap blocks, bool[,] mask)
        {
            var sums = new PointF[blocks.BlockCount.Y, blocks.BlockCount.X];
            foreach (var block in blocks.AllBlocks)
                if (block.Get(mask))
                {
                    var sum = new PointF();
                    var area = blocks.BlockAreas[block];
                    for (var y = area.Bottom; y < area.Top; ++y)
                    for (var x = area.Left; x < area.Right; ++x)
                        sum += orientation[y, x];
                    sums[block.Y, block.X] = sum;
                }

            return sums;
        }

        private static PointF[,] SmoothOutOrientationMap(PointF[,] orientation, bool[,] mask)
        {
            const int radius = 1;
            var size = Point.SizeOf(mask);
            var smoothed = size.Allocate<PointF>();
            for (var y = 0; y < size.Y; ++y)
            for (var x = 0; x < size.X; ++x)
                if (mask[y, x])
                {
                    var neighbors = Rectangle.Between(
                        new Point(Math.Max(0, x - radius), Math.Max(0, y - radius)),
                        new Point(Math.Min(size.X, x + radius + 1), Math.Min(size.Y, y + radius + 1)));
                    var sum = new PointF();
                    for (var ny = neighbors.Bottom; ny < neighbors.Top; ++ny)
                    for (var nx = neighbors.Left; nx < neighbors.Right; ++nx)
                        if (mask[ny, nx])
                            sum += orientation[ny, nx];
                    smoothed[y, x] = sum;
                }

            return smoothed;
        }

        private static byte[,] ConvertOrientationVectorsToAngles(PointF[,] vectors, bool[,] mask)
        {
            var size = Point.SizeOf(mask);
            var angles = size.Allocate<byte>();
            for (var y = 0; y < size.Y; ++y)
            for (var x = 0; x < size.X; ++x)
                if (mask[y, x])
                    angles[y, x] = Angle.ToByte(Angle.Atan(vectors[y, x]));
            return angles;
        }

        private static Point[][] ConstructOrientedLines(int resolution = 32, int radius = 7, double step = 1.5)
        {
            var result = new Point[resolution][];
            for (var orientationIndex = 0; orientationIndex < resolution; ++orientationIndex)
            {
                var line = new List<Point> {new Point()};
                var direction = Angle.ToVector(Angle.ByBucketCenter(orientationIndex, 2 * resolution));
                for (double r = radius; r >= 0.5; r /= step)
                {
                    var point = (r * direction).Round();
                    if (line.Contains(point)) continue;

                    line.Add(point);
                    line.Add(-point);
                }

                line.Sort(MathEx.CompareYX);
                result[orientationIndex] = line.ToArray();
            }

            return result;
        }

        private static double[,] SmoothByOrientation(double[,] input, byte[,] orientation, bool[,] mask,
            BlockMap blocks, byte angle, Point[][] lines)
        {
            var output = new double[input.GetLength(0), input.GetLength(1)];
            foreach (var block in blocks.AllBlocks)
                if (block.Get(mask))
                {
                    var line = lines[Angle.Quantize(Angle.Add(orientation[block.Y, block.X], angle), lines.Length)];
                    foreach (var linePoint in line)
                    {
                        var target = blocks.BlockAreas[block];
                        var source = target.GetShifted(linePoint);
                        source.Clip(new Rectangle(blocks.PixelCount));
                        target = source.GetShifted(-linePoint);
                        for (var y = target.Bottom; y < target.Top; ++y)
                        for (var x = target.Left; x < target.Right; ++x)
                            output[y, x] += input[y + linePoint.Y, x + linePoint.X];
                    }

                    var blockArea = blocks.BlockAreas[block];
                    for (var y = blockArea.Bottom; y < blockArea.Top; ++y)
                    for (var x = blockArea.Left; x < blockArea.Right; ++x)
                        output[y, x] *= 1.0 / line.Length;
                }

            return output;
        }

        private static bool[,] Binarize(double[,] input, double[,] baseline, bool[,] mask, BlockMap blocks)
        {
            var size = Point.SizeOf(input);
            var binarized = size.Allocate<bool>();
            for (var blockY = 0; blockY < blocks.AllBlocks.Height; ++blockY)
            for (var blockX = 0; blockX < blocks.AllBlocks.Width; ++blockX)
                if (mask[blockY, blockX])
                {
                    var rect = blocks.BlockAreas[blockY, blockX];
                    for (var y = rect.Bottom; y < rect.Top; ++y)
                    for (var x = rect.Left; x < rect.Right; ++x)
                        if (input[y, x] - baseline[y, x] > 0)
                            binarized[y, x] = true;
                }

            return binarized;
        }

        private static void RemoveCrosses(bool[,] input)
        {
            var size = Point.SizeOf(input);
            var any = true;
            while (any)
            {
                any = false;
                for (var y = 0; y < size.Y - 1; ++y)
                for (var x = 0; x < size.X - 1; ++x)
                    if (input[y, x] && input[y + 1, x + 1] && !input[y + 1, x] && !input[y, x + 1] ||
                        input[y + 1, x] && input[y, x + 1] && !input[y, x] && !input[y + 1, x + 1])
                    {
                        input[y, x] = false;
                        input[y + 1, x] = false;
                        input[y, x + 1] = false;
                        input[y + 1, x + 1] = false;
                        any = true;
                    }
            }
        }

        private static bool[,] ComputeInnerMask(bool[,] outer)
        {
            const int minBorderDistance = 14;
            var size = Point.SizeOf(outer);
            var inner = size.Allocate<bool>();
            for (var y = 1; y < size.Y - 1; ++y)
            for (var x = 1; x < size.X - 1; ++x)
                inner[y, x] = outer[y, x];
            inner = ShrinkMask(inner, 1);
            var total = 1;
            for (var step = 1; total + step <= minBorderDistance; step *= 2)
            {
                inner = ShrinkMask(inner, step);
                total += step;
            }

            if (total < minBorderDistance)
                inner = ShrinkMask(inner, minBorderDistance - total);
            return inner;
        }

        private static bool[,] ShrinkMask(bool[,] mask, int amount)
        {
            var size = Point.SizeOf(mask);
            var shrunk = size.Allocate<bool>();
            for (var y = amount; y < size.Y - amount; ++y)
            for (var x = amount; x < size.X - amount; ++x)
                shrunk[y, x] = mask[y - amount, x] && mask[y + amount, x] && mask[y, x - amount] && mask[y, x + amount];
            return shrunk;
        }

        private void CollectMinutiae(FingerprintSkeleton skeleton, FingerprintMinutiaType type)
        {
            foreach (var skeletonMinutia in skeleton.Minutiae)
                if (skeletonMinutia.IsConsidered && skeletonMinutia.Ridges.Count == 1)
                {
                    var templateMinutia = new FingerprintMinutia
                    {
                        Type = type,
                        Position = skeletonMinutia.Position,
                        Direction = skeletonMinutia.Ridges[0].ComputeDirection()
                    };
                    Minutiae.Add(templateMinutia);
                }
        }

        private void ApplyMask(bool[,] mask)
        {
            const double directedExtension = 10.06;
            Minutiae.RemoveAll(minutia =>
            {
                var arrow = (-directedExtension * Angle.ToVector(minutia.Direction)).Round();
                return !(minutia.Position + arrow).Get(mask, false);
            });
        }

        private void RemoveMinutiaClouds()
        {
            const int radius = 20;
            const int maxNeighbors = 4;
            var radiusSq = MathEx.Sq(radius);
            Minutiae = Minutiae.Except(
                (from minutia in Minutiae
                    where Minutiae.Count(neighbor => (neighbor.Position - minutia.Position).SqLength <= radiusSq) - 1 >
                          maxNeighbors
                    select minutia).ToList()).ToList();
        }

        private void LimitTemplateSize()
        {
            const int maxMinutiae = 100;
            const int neighborhoodSize = 5;
            if (Minutiae.Count > maxMinutiae)
                Minutiae =
                    (from minutia in Minutiae
                        let radiusSq = (from neighbor in Minutiae
                            let distanceSq = (minutia.Position - neighbor.Position).SqLength
                            orderby distanceSq
                            select distanceSq).Skip(neighborhoodSize).First()
                        orderby radiusSq descending
                        select minutia).Take(maxMinutiae).ToList();
        }

        private void ShuffleMinutiae()
        {
            var seed = 0;
            foreach (var minutia in Minutiae)
                seed += minutia.Direction + minutia.Position.X + minutia.Position.Y + (int) minutia.Type;
            Minutiae = MathEx.Shuffle(Minutiae, new Random(seed)).ToList();
        }

        private void BuildEdgeTable()
        {
            const int maxDistance = 490;
            const int maxNeighbors = 9;

            EdgeTable = new NeighborEdge[Minutiae.Count][];
            var edges = new List<NeighborEdge>();
            var allSqDistances = new int[Minutiae.Count];

            for (var reference = 0; reference < EdgeTable.Length; ++reference)
            {
                var referencePosition = Minutiae[reference].Position;
                var sqMaxDistance = MathEx.Sq(maxDistance);
                if (Minutiae.Count - 1 > maxNeighbors)
                {
                    for (var neighbor = 0; neighbor < Minutiae.Count; ++neighbor)
                        allSqDistances[neighbor] = (referencePosition - Minutiae[neighbor].Position).SqLength;
                    Array.Sort(allSqDistances);
                    sqMaxDistance = allSqDistances[maxNeighbors];
                }

                for (var neighbor = 0; neighbor < Minutiae.Count; ++neighbor)
                {
                    if (neighbor == reference ||
                        (referencePosition - Minutiae[neighbor].Position).SqLength > sqMaxDistance) continue;
                    var record = new NeighborEdge
                    {
                        Edge = new EdgeShape(this, reference, neighbor), Neighbor = neighbor
                    };
                    edges.Add(record);
                }

                edges.Sort(NeighborEdgeComparer.Instance);
                if (edges.Count > maxNeighbors)
                    edges.RemoveRange(maxNeighbors, edges.Count - maxNeighbors);
                EdgeTable[reference] = edges.ToArray();
                edges.Clear();
            }
        }

        private class ConsideredOrientation
        {
            public Point CheckLocation;
            public PointF OrientationVector;
        }

        private class NeighborEdgeComparer : IComparer<NeighborEdge>
        {
            public static readonly NeighborEdgeComparer Instance = new NeighborEdgeComparer();

            public int Compare(NeighborEdge left, NeighborEdge right)
            {
                var result = MathEx.Compare(left.Edge.Length, right.Edge.Length);
                return result != 0 ? result : MathEx.Compare(left.Neighbor, right.Neighbor);
            }
        }
    }
}