using Unity.Entities;
using UnityEngine;

namespace VoxelPlanet
{
    public class PlanetPlayerAuthoring : MonoBehaviour
    {
        private class Baker : Baker<PlanetPlayerAuthoring>
        {
            public override void Bake(PlanetPlayerAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<PlanetPlayerTag>(entity);
            }
        }
    }
}