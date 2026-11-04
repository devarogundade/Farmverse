#nullable enable
using System;
using System.Collections.Generic;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// What the farmer carries on their back. Goods stack in the order they were picked up and come
    /// off the top first (the last thing you picked up is the first thing you hand over), up to a
    /// capacity in units.
    /// </summary>
    public sealed class Basket
    {
        private readonly List<Stack> _stacks = new List<Stack>();

        public Basket(int capacity) => Capacity = capacity;

        /// <summary>One run of the same good in the basket.</summary>
        public sealed class Stack
        {
            public Stack(string item, int count)
            {
                Item = item;
                Count = count;
            }

            public string Item { get; }
            public int Count { get; internal set; }
        }

        public int Capacity { get; internal set; }
        public int Count { get; private set; }
        public int Free => Math.Max(0, Capacity - Count);
        public bool IsFull => Count >= Capacity;
        public bool IsEmpty => Count == 0;
        /// <summary>Bottom first.</summary>
        public IReadOnlyList<Stack> Stacks => _stacks;

        /// <summary>Put up to <paramref name="count"/> on; returns how many fit.</summary>
        public int Add(string item, int count)
        {
            var take = Math.Min(Math.Max(0, count), Free);
            if (take == 0) return 0;
            if (_stacks.Count > 0 && _stacks[_stacks.Count - 1].Item == item) _stacks[_stacks.Count - 1].Count += take;
            else _stacks.Add(new Stack(item, take));
            Count += take;
            return take;
        }

        /// <summary>The good on top, without taking it.</summary>
        public string? Top => _stacks.Count == 0 ? null : _stacks[_stacks.Count - 1].Item;

        /// <summary>Take one unit off the top.</summary>
        public string? TakeOne()
        {
            if (_stacks.Count == 0) return null;
            var top = _stacks[_stacks.Count - 1];
            top.Count--;
            Count--;
            if (top.Count == 0) _stacks.RemoveAt(_stacks.Count - 1);
            return top.Item;
        }

        /// <summary>Take one unit of a given good from wherever it sits in the basket (the topmost one).</summary>
        public bool TakeItem(string item)
        {
            for (var i = _stacks.Count - 1; i >= 0; i--)
            {
                if (_stacks[i].Item != item) continue;
                _stacks[i].Count--;
                Count--;
                if (_stacks[i].Count == 0)
                {
                    _stacks.RemoveAt(i);
                    // two stacks of the same good that now touch become one
                    if (i > 0 && i < _stacks.Count && _stacks[i - 1].Item == _stacks[i].Item)
                    {
                        _stacks[i - 1].Count += _stacks[i].Count;
                        _stacks.RemoveAt(i);
                    }
                }
                return true;
            }
            return false;
        }

        /// <summary>Put one unit back on top (an unload that the building could not take).</summary>
        internal void PutBack(string item) => Add(item, 1);

        public int CountOf(string item)
        {
            var n = 0;
            foreach (var s in _stacks) if (s.Item == item) n += s.Count;
            return n;
        }

        internal void Clear()
        {
            _stacks.Clear();
            Count = 0;
        }
    }
}
