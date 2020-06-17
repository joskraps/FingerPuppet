// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System.Collections;
using System.Collections.Generic;

namespace FingerPuppet.SourceAFIS.Utils
{
    internal struct Rectangle : IEnumerable<Point>
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public int Left
        {
            get => X;
            set
            {
                Width += X - value;
                X = value;
            }
        }

        public int Bottom
        {
            get => Y;
            set
            {
                Height += Y - value;
                Y = value;
            }
        }

        public int Right
        {
            get => X + Width;
            set => Width = value - X;
        }

        public int Top
        {
            get => Y + Height;
            set => Height = value - Y;
        }

        public Point Point
        {
            get => new Point(Left, Bottom);
            set
            {
                X = value.X;
                Y = value.Y;
            }
        }

        public Point Size
        {
            get => new Point(Width, Height);
            set
            {
                Width = value.X;
                Height = value.Y;
            }
        }

        public Range RangeX => new Range(Left, Right);
        public Range RangeY => new Range(Bottom, Top);
        public Point Center => new Point((Right + Left) / 2, (Bottom + Top) / 2);
        public int TotalArea => Width * Height;

        public Rectangle(Rectangle other)
        {
            X = other.X;
            Y = other.Y;
            Width = other.Width;
            Height = other.Height;
        }

        public Rectangle(Point at, Point size)
        {
            X = at.X;
            Y = at.Y;
            Width = size.X;
            Height = size.Y;
        }

        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public static Rectangle Between(Point begin, Point end)
        {
            return new Rectangle(begin.X, begin.Y, end.X - begin.X, end.Y - begin.Y);
        }

        public Rectangle(Point size)
        {
            X = 0;
            Y = 0;
            Width = size.X;
            Height = size.Y;
        }

        public Rectangle(int width, int height)
        {
            X = 0;
            Y = 0;
            Width = width;
            Height = height;
        }

        public readonly bool Contains(Point point)
        {
            return point.X >= Left && point.Y >= Bottom && point.X < Right && point.Y < Top;
        }

        public Point GetRelative(Point absolute)
        {
            return new Point(absolute.X - X, absolute.Y - Y);
        }

        public PointF GetFraction(Point absolute)
        {
            var relative = GetRelative(absolute);
            return new PointF(relative.X / (double) Width, relative.Y / (double) Height);
        }

        public void Shift(Point relative)
        {
            Point += relative;
        }

        public Rectangle GetShifted(Point relative)
        {
            var result = new Rectangle(this);
            result.Shift(relative);
            return result;
        }

        public void Clip(Rectangle other)
        {
            if (Left < other.Left)
                Left = other.Left;
            if (Right > other.Right)
                Right = other.Right;
            if (Bottom < other.Bottom)
                Bottom = other.Bottom;
            if (Top > other.Top)
                Top = other.Top;
        }

        public void Include(Point point)
        {
            if (Left > point.X)
                Left = point.X;
            if (Right <= point.X)
                Right = point.X + 1;
            if (Bottom > point.Y)
                Bottom = point.Y;
            if (Top <= point.Y)
                Top = point.Y + 1;
        }

        IEnumerator<Point> IEnumerable<Point>.GetEnumerator()
        {
            var point = new Point();
            for (point.Y = Bottom; point.Y < Top; ++point.Y)
            for (point.X = Left; point.X < Right; ++point.X)
                yield return point;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<Point>) this).GetEnumerator();
        }
    }
}