using System;

namespace CyanTools.Utils
{




    public sealed class NumpyRandom
    {

        private const int StateSize = 624;
        private const int M = 397;
        private const uint MatrixA = 0x9908B0DFU;
        private const uint UpperMask = 0x80000000U;
        private const uint LowerMask = 0x7FFFFFFFU;

        private readonly uint[] _mt = new uint[StateSize];
        private int _index = StateSize;

        public NumpyRandom(int seed)
        {
            Initialize((uint)seed);
        }

        private void Initialize(uint seed)
        {
            _mt[0] = seed;
            for (int i = 1; i < StateSize; i++)
            {
                uint prev = _mt[i - 1];
                _mt[i] = unchecked(1812433253U * (prev ^ (prev >> 30)) + (uint)i);
            }
            _index = StateSize;
        }

        private void Twist()
        {
            for (int i = 0; i < StateSize; i++)
            {
                uint y = (_mt[i] & UpperMask) | (_mt[(i + 1) % StateSize] & LowerMask);
                uint mag = (y & 1U) == 0U ? 0U : MatrixA;
                _mt[i] = _mt[(i + M) % StateSize] ^ (y >> 1) ^ mag;
            }
            _index = 0;
        }

        public uint NextUInt32()
        {
            if (_index >= StateSize)
            {
                Twist();
            }

            uint y = _mt[_index++];

            y ^= (y >> 11);
            y ^= (y << 7) & 0x9D2C5680U;
            y ^= (y << 15) & 0xEFC60000U;
            y ^= (y >> 18);
            return y;
        }




        public double NextDouble()
        {

            ulong a = NextUInt32() >> 5;
            ulong b = NextUInt32() >> 6;
            return (a * 67108864.0 + b) / 9007199254740992.0;
        }

        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax), "exclusiveMax must be > 0.");
            }


            uint bound = (uint)exclusiveMax;
            uint threshold = (uint)(unchecked((0U - bound)) % bound);
            while (true)
            {
                uint r = NextUInt32();
                if (r >= threshold)
                {
                    return (int)(r % bound);
                }
            }
        }

        public int NextInt(int inclusiveMin, int exclusiveMax)
        {
            if (exclusiveMax <= inclusiveMin)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax), "exclusiveMax must be > inclusiveMin.");
            }

            return inclusiveMin + NextInt(exclusiveMax - inclusiveMin);
        }
    }
}

