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

        private readonly int Capacity;

        public ContinuousList(int capacity)
        {
            this.Capacity = capacity;
            this.Items = new T[capacity];
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
        }

        public void Clear()
        {
            this.Items = new T[Capacity];
            this.HasLooped = false;
            this.Index = 0;
            this.Last = default;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return Items.Skip(Index).Take(HasLooped ? Capacity : Capacity - Index).Concat(Items.Take(Index)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
