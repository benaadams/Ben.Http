using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace System
{
    public static class ConcurrentRandom
    {
        [ThreadStatic]
        private static Random? t_random;

        private static Random Random => t_random ??= CreateRandom();

        private static Random CreateRandom() => new Random((int)Stopwatch.GetTimestamp());

        public static int Next(int maxValue) => Random.Next(maxValue);
    }
}
