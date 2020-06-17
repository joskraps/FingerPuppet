// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System;
using System.Collections;
using System.Collections.Generic;

namespace FingerPuppet.SourceAFIS.Utils
{
    internal class CircularArray<T> : IList<T>
    {
        private int First;
        private T[] Inner;

        public CircularArray()
        {
            Inner = new T[16];
        }

        private int HeadCount => Math.Min(Count, Inner.Length - First);
        private int TailCount => Math.Max(0, First + Count - Inner.Length);

        public int IndexOf(T item)
        {
            var index = Array.IndexOf(Inner, item, First, Math.Min(Count, Inner.Length - First));
            if (index >= 0)
                return index - First;
            if (First + Count > Inner.Length)
                return Array.IndexOf(Inner, item, 0, First + Count - Inner.Length);
            return -1;
        }

        public void Insert(int index, T item)
        {
            CheckIndex(index);
            if (index > 0)
            {
                InsertSpace(index);
                Inner[GetRealIndex(index)] = item;
            }
            else
            {
                if (Count >= Inner.Length)
                    Enlarge();
                DecFirst();
                ++Count;
                Inner[GetRealIndex(0)] = item;
            }
        }

        public void RemoveAt(int index)
        {
            if (index == 0)
            {
                IncFirst();
                --Count;
            }
            else if (index == Count - 1)
            {
                --Count;
            }
            else
            {
                CheckIndex(index);
                RemoveSpace(index);
            }
        }

        public T this[int index]
        {
            get
            {
                CheckIndex(index);
                return Inner[GetRealIndex(index)];
            }
            set
            {
                CheckIndex(index);
                Inner[GetRealIndex(index)] = value;
            }
        }

        public void Add(T item)
        {
            if (Count >= Inner.Length)
                Enlarge();
            ++Count;
            Inner[GetRealIndex(Count - 1)] = item;
        }

        public void Clear()
        {
            First = 0;
            Count = 0;
        }

        public bool Contains(T item)
        {
            return Array.IndexOf(Inner, item, First, HeadCount) >= 0
                   || Array.IndexOf(Inner, item, 0, TailCount) >= 0;
        }

        public void CopyTo(T[] array, int at)
        {
            Array.Copy(Inner, First, array, at, HeadCount);
            Array.Copy(Inner, 0, array, at + HeadCount, TailCount);
        }

        public bool Remove(T item)
        {
            var index = IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }

            return false;
        }

        public int Count { get; private set; }
        public bool IsReadOnly => false;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(this);
        }

        private void CheckIndex(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException();
        }

        private int GetRealIndex(int index)
        {
            return index < HeadCount ? First + index : index - HeadCount;
        }

        private void IncFirst()
        {
            ++First;
            if (First >= Inner.Length)
                First -= Inner.Length;
        }

        private void DecFirst()
        {
            --First;
            if (First < 0)
                First += Inner.Length;
        }

        private void Enlarge()
        {
            var enlarged = new T[2 * Inner.Length];
            for (var i = 0; i < Count; ++i)
                enlarged[i] = Inner[GetRealIndex(i)];
            Inner = enlarged;
            First = 0;
        }

        private void Move(int from, int to, int length)
        {
            if (from < to)
                for (var i = length - 1; i >= 0; --i)
                    Inner[GetRealIndex(to + i)] = Inner[GetRealIndex(from + i)];
            else
                for (var i = 0; i < length; ++i)
                    Inner[GetRealIndex(to + i)] = Inner[GetRealIndex(from + i)];
        }

        private void MoveForward(int from, int length)
        {
            Move(from, from + 1, length);
        }

        private void MoveBackward(int from, int length)
        {
            Move(from, from - 1, length);
        }

        private void InsertSpaceForward(int index)
        {
            ++Count;
            MoveForward(index, Count - index - 1);
        }

        private void InsertSpaceBackward(int index)
        {
            DecFirst();
            ++Count;
            MoveBackward(1, index + 1);
        }

        private void InsertSpace(int index)
        {
            if (Count >= Inner.Length)
                Enlarge();
            if (index >= Count / 2)
                InsertSpaceForward(index);
            else
                InsertSpaceBackward(index);
        }

        private void RemoveSpaceForward(int index)
        {
            MoveBackward(index + 1, Count - index - 1);
            --Count;
        }

        private void RemoveSpaceBackward(int index)
        {
            MoveForward(0, index);
            IncFirst();
            --Count;
        }

        private void RemoveSpace(int index)
        {
            if (index >= Count / 2)
                RemoveSpaceForward(index);
            else
                RemoveSpaceBackward(index);
        }

        private class Enumerator : IEnumerator<T>
        {
            private readonly CircularArray<T> Array;
            private int Index;

            public Enumerator(CircularArray<T> array)
            {
                Array = array;
                Index = -1;
            }

            public T Current => Array[Index];
            object IEnumerator.Current => Array[Index];

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (Index < Array.Count)
                {
                    ++Index;
                    return Index < Array.Count;
                }

                return false;
            }

            public void Reset()
            {
                Index = -1;
            }
        }
    }
}