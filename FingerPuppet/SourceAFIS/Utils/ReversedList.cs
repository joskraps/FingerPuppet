// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System;
using System.Collections;
using System.Collections.Generic;

namespace FingerPuppet.SourceAFIS.Utils
{
    internal class ReversedList<T> : IList<T>
    {
        private readonly IList<T> Inner;

        public ReversedList(IList<T> inner)
        {
            Inner = inner;
        }

        public int IndexOf(T item)
        {
            for (var i = Count - 1; i >= 0; --i)
                if (Inner[i].Equals(item))
                    return Count - i - 1;
            return -1;
        }

        public void Insert(int position, T item)
        {
            Inner.Insert(Count - position, item);
        }

        public void RemoveAt(int position)
        {
            Inner.RemoveAt(Count - position - 1);
        }

        public T this[int index]
        {
            get => Inner[Count - index - 1];
            set => Inner[Count - index - 1] = value;
        }

        public void Add(T item)
        {
            Inner.Insert(0, item);
        }

        public void Clear()
        {
            Inner.Clear();
        }

        public bool Contains(T item)
        {
            return Inner.Contains(item);
        }

        public void CopyTo(T[] array, int at)
        {
            Inner.CopyTo(array, at);
            Array.Reverse(array, at, Count);
        }

        public bool Remove(T item)
        {
            var index = IndexOf(item);
            if (index >= 0)
            {
                Inner.RemoveAt(Count - index - 1);
                return true;
            }

            return false;
        }

        public int Count => Inner.Count;
        public bool IsReadOnly => Inner.IsReadOnly;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new Enumerator(Inner);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(Inner);
        }

        private class Enumerator : IEnumerator<T>
        {
            private readonly IList<T> Inner;
            private int Position;

            public Enumerator(IList<T> inner)
            {
                Inner = inner;
                Position = Inner.Count;
            }

            public T Current => Inner[Position];
            object IEnumerator.Current => Inner[Position];

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (Position >= 0)
                {
                    --Position;
                    return Position >= 0;
                }

                return false;
            }

            public void Reset()
            {
                Position = Inner.Count;
            }
        }
    }
}