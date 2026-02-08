using System;

namespace CyanTools.Utils
{




    public sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public int Size => _parent.Length;

        public DisjointSet(int size)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "size must be > 0.");
            }

            _parent = new int[size];
            _rank = new int[size];
            for (int i = 0; i < size; i++)
            {
                _parent[i] = i;
                _rank[i] = 0;
            }
        }

        public int Find(int x)
        {
            if ((uint)x >= (uint)_parent.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            int root = x;
            while (_parent[root] != root)
            {
                root = _parent[root];
            }


            while (_parent[x] != x)
            {
                int p = _parent[x];
                _parent[x] = root;
                x = p;
            }

            return root;
        }

        public bool Union(int a, int b)
        {
            int ra = Find(a);
            int rb = Find(b);
            if (ra == rb)
            {
                return false;
            }

            int rankA = _rank[ra];
            int rankB = _rank[rb];

            if (rankA < rankB)
            {
                _parent[ra] = rb;
                return true;
            }

            if (rankA > rankB)
            {
                _parent[rb] = ra;
                return true;
            }


            if (ra <= rb)
            {
                _parent[rb] = ra;
                _rank[ra]++;
            }
            else
            {
                _parent[ra] = rb;
                _rank[rb]++;
            }

            return true;
        }
    }
}

