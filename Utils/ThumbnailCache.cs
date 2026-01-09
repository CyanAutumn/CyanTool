using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace CyanTools.Utils
{
    internal sealed class ThumbnailCache
    {
        private readonly int _capacity;
        private readonly Dictionary<ThumbnailKey, LinkedListNode<CacheEntry>> _map;
        private readonly LinkedList<CacheEntry> _lru;

        public ThumbnailCache(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            _map = new Dictionary<ThumbnailKey, LinkedListNode<CacheEntry>>();
            _lru = new LinkedList<CacheEntry>();
        }

        public bool TryGet(in ThumbnailKey key, out BitmapSource? bitmap)
        {
            lock (_map)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    bitmap = node.Value.Bitmap;
                    return true;
                }
            }

            bitmap = null;
            return false;
        }

        public void Add(in ThumbnailKey key, BitmapSource bitmap)
        {
            lock (_map)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    node.Value.Bitmap = bitmap;
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    return;
                }

                var entry = new CacheEntry(key, bitmap);
                var newNode = new LinkedListNode<CacheEntry>(entry);
                _lru.AddFirst(newNode);
                _map[key] = newNode;

                while (_map.Count > _capacity)
                {
                    var last = _lru.Last;
                    if (last == null)
                    {
                        break;
                    }

                    _lru.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
            }
        }

        internal readonly struct ThumbnailKey : IEquatable<ThumbnailKey>
        {
            public ThumbnailKey(string path, int width, long lastWriteTicks)
            {
                Path = path;
                Width = width;
                LastWriteTicks = lastWriteTicks;
            }

            public string Path { get; }
            public int Width { get; }
            public long LastWriteTicks { get; }

            public bool Equals(ThumbnailKey other)
            {
                return Width == other.Width
                       && LastWriteTicks == other.LastWriteTicks
                       && StringComparer.OrdinalIgnoreCase.Equals(Path, other.Path);
            }

            public override bool Equals(object? obj)
            {
                return obj is ThumbnailKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(Path);
                hash = (hash * 397) ^ Width;
                hash = (hash * 397) ^ LastWriteTicks.GetHashCode();
                return hash;
            }
        }

        private sealed class CacheEntry
        {
            public CacheEntry(ThumbnailKey key, BitmapSource bitmap)
            {
                Key = key;
                Bitmap = bitmap;
            }

            public ThumbnailKey Key { get; }
            public BitmapSource Bitmap { get; set; }
        }
    }
}
