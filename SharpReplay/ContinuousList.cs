using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SharpReplay
{
    public class ContinuousList<T> : IEnumerable<T>
    {
        private T[] Items;
        private bool HasLooped;
        private int Index;

        public T Last { get; private set; }

        public event EventHandler<T> ItemDropped = delegate { };

        public int Capacity { get; private set; }
        public int Count { get; private set; }

        public ContinuousList(int capacity)
        {
            this.Capacity = capacity;
            this.Items = new T[capacity];
        }

        public void SetCapacity(int capacity)
        {
            int previousCapacity = this.Capacity;

            this.Capacity = capacity;
            Array.Resize(ref Items, capacity);

            if (HasLooped)
            {
                int toBeMoved = previousCapacity - Index;
                Array.Copy(Items, Index, Items, Items.Length - toBeMoved, toBeMoved);
                Index += toBeMoved;
            }
        }

        public void Add(T item)
        {
            if (HasLooped)
                ItemDropped(this, Items[Index]);

            Items[Index] = Last = item;

            Index++;

            if (Index == Capacity)
            {
                Index = 0;
                HasLooped = true;
            }

            Count++;

            if (Count > Capacity)
                Count = Capacity;
        }

        public void Clear()
        {
            this.Items = new T[Capacity];
            this.HasLooped = false;
            this.Index = this.Count = 0;
            this.Last = default;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (HasLooped)
                return Items.Skip(Index).Take(Capacity).Concat(Items.Take(Index)).GetEnumerator();
            else
                return Items.Take(Index).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
