using Unity.Mathematics;

namespace VoxelPlanet
{
    public readonly struct VertexKey : System.IEquatable<VertexKey>
    {
        private readonly int x;
        private readonly int y;
        private readonly int z;

        public VertexKey(float3 p)
        {
            float3 n = math.normalize(p);

            x = (int)math.round(n.x * 100000);
            y = (int)math.round(n.y * 100000);
            z = (int)math.round(n.z * 100000);
        }

        public bool Equals(VertexKey other)
        {
            return x == other.x &&
                   y == other.y &&
                   z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is VertexKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = x;
                hash = hash * 397 ^ y;
                hash = hash * 397 ^ z;
                return hash;
            }
        }
    }
}