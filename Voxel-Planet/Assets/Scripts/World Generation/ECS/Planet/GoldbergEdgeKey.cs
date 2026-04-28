using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelPlanet
{
    public readonly struct GoldbergEdgeKey : System.IEquatable<GoldbergEdgeKey>
    {
        private readonly int3 a;
        private readonly int3 b;

        private const float Precision = 100000f;

        public GoldbergEdgeKey(float3 p0, float3 p1)
        {
            int3 q0 = Quantize(p0);
            int3 q1 = Quantize(p1);

            if (Compare(q0, q1) <= 0)
            {
                a = q0;
                b = q1;
            }
            else
            {
                a = q1;
                b = q0;
            }
        }

        public bool Equals(GoldbergEdgeKey other)
        {
            return a.Equals(other.a) && b.Equals(other.b);
        }

        public override bool Equals(object obj)
        {
            return obj is GoldbergEdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + a.GetHashCode();
                hash = hash * 31 + b.GetHashCode();
                return hash;
            }
        }

        public static int3 Quantize(float3 p)
        {
            return new int3(
                (int)math.round(p.x * Precision),
                (int)math.round(p.y * Precision),
                (int)math.round(p.z * Precision)
            );
        }

        public static int Compare(int3 x, int3 y)
        {
            if (x.x != y.x) return x.x.CompareTo(y.x);
            if (x.y != y.y) return x.y.CompareTo(y.y);
            return x.z.CompareTo(y.z);
        }
    }
}