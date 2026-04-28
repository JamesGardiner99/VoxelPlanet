using Unity.Mathematics;

namespace VoxelPlanet
{
    public readonly struct EdgeKey : System.IEquatable<EdgeKey>
    {
        private readonly int ax;
        private readonly int ay;
        private readonly int az;
        private readonly int bx;
        private readonly int by;
        private readonly int bz;

        public EdgeKey(float3 a, float3 b)
        {
            float3 an = math.normalize(a);
            float3 bn = math.normalize(b);

            int qax = (int)math.round(an.x * 100000);
            int qay = (int)math.round(an.y * 100000);
            int qaz = (int)math.round(an.z * 100000);

            int qbx = (int)math.round(bn.x * 100000);
            int qby = (int)math.round(bn.y * 100000);
            int qbz = (int)math.round(bn.z * 100000);

            bool swap =
                qax > qbx ||
                (qax == qbx && qay > qby) ||
                (qax == qbx && qay == qby && qaz > qbz);

            if (!swap)
            {
                ax = qax;
                ay = qay;
                az = qaz;

                bx = qbx;
                by = qby;
                bz = qbz;
            }
            else
            {
                ax = qbx;
                ay = qby;
                az = qbz;

                bx = qax;
                by = qay;
                bz = qaz;
            }
        }

        public bool Equals(EdgeKey other)
        {
            return ax == other.ax &&
                   ay == other.ay &&
                   az == other.az &&
                   bx == other.bx &&
                   by == other.by &&
                   bz == other.bz;
        }

        public override bool Equals(object obj)
        {
            return obj is EdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ax;
                hash = hash * 397 ^ ay;
                hash = hash * 397 ^ az;
                hash = hash * 397 ^ bx;
                hash = hash * 397 ^ by;
                hash = hash * 397 ^ bz;
                return hash;
            }
        }
    }
}