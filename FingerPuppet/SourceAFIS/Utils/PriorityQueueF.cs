// Part of SourceAFIS: https://sourceafis.machinezoo.com

using System;

namespace FingerPuppet.SourceAFIS.Utils
{
    internal class PriorityQueueF<V> where V : new()
    {
        private Item[] Heap;

        public PriorityQueueF()
        {
            Heap = new Item[1];
        }

        public int Count { get; private set; }

        public void Clear()
        {
            Count = 0;
        }

        private void Enlarge()
        {
            var larger = new Item[2 * Heap.Length];
            Array.Copy(Heap, larger, Heap.Length);
            Heap = larger;
        }

        private void BubbleUp(int bottom)
        {
            for (var bubble = bottom; bubble > 1; bubble = bubble >> 1)
            {
                var parent = bubble >> 1;
                if (Heap[parent].Key < Heap[bubble].Key)
                    break;
                var tmp = Heap[bubble];
                Heap[bubble] = Heap[parent];
                Heap[parent] = tmp;
            }
        }

        public void Enqueue(double key, V value)
        {
            if (Count + 1 >= Heap.Length)
                Enlarge();
            ++Count;
            Heap[Count].Key = key;
            Heap[Count].Value = value;
            BubbleUp(Count);
        }

        private void BubbleDown()
        {
            var bubble = 1;
            while (true)
            {
                var left = bubble << 1;
                var right = (bubble << 1) + 1;
                if (left > Count)
                    break;
                int child;
                if (right > Count || Heap[left].Key < Heap[right].Key)
                    child = left;
                else
                    child = right;
                if (Heap[bubble].Key < Heap[child].Key)
                    break;
                var tmp = Heap[bubble];
                Heap[bubble] = Heap[child];
                Heap[child] = tmp;
                bubble = child;
            }
        }

        public V Peek()
        {
            if (Count <= 0)
                throw new InvalidOperationException();
            return Heap[1].Value;
        }

        public V Dequeue()
        {
            if (Count == 0)
                throw new InvalidOperationException();
            var result = Heap[1].Value;
            Heap[1] = Heap[Count];
            --Count;
            BubbleDown();
            return result;
        }

        private struct Item
        {
            public double Key;
            public V Value;
        }
    }
}