using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelPlanet
{
    [UpdateAfter(typeof(GoldbergPlanetBuildSystem))]
    public partial class GoldbergVoxelNeighbourSystem : SystemBase
    {
        private struct EdgeOwner
        {
            public int CellIndex;
            public int EdgeIndex;
        }

        protected override void OnCreate()
        {
            RequireForUpdate<GoldbergPlanetSettings>();
            RequireForUpdate<GoldbergCell>();
        }

        protected override void OnUpdate()
        {
            EntityManager entityManager = EntityManager;

            EntityQuery query = GetEntityQuery(
                typeof(GoldbergPlanetSettings),
                typeof(GoldbergCell),
                typeof(GoldbergCellVertex)
            );

            using NativeArray<Entity> planets =
                query.ToEntityArray(Allocator.Temp);

            for (int p = 0; p < planets.Length; p++)
            {
                Entity planetEntity = planets[p];

                if (entityManager.HasComponent<GoldbergNeighboursBuilt>(planetEntity))
                    continue;

                if (!entityManager.HasBuffer<GoldbergCellNeighbour>(planetEntity))
                {
                    entityManager.AddBuffer<GoldbergCellNeighbour>(planetEntity);
                }

                // Re-fetch buffers AFTER any structural changes.
                DynamicBuffer<GoldbergCell> cells =
                    entityManager.GetBuffer<GoldbergCell>(planetEntity);

                DynamicBuffer<GoldbergCellVertex> cellVertices =
                    entityManager.GetBuffer<GoldbergCellVertex>(planetEntity);

                DynamicBuffer<GoldbergCellNeighbour> neighbours =
                    entityManager.GetBuffer<GoldbergCellNeighbour>(planetEntity);

                neighbours.Clear();

                Dictionary<GoldbergEdgeKey, EdgeOwner> edgeOwners =
                    new Dictionary<GoldbergEdgeKey, EdgeOwner>();

                int sharedEdges = 0;

                for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
                {
                    GoldbergCell cell = cells[cellIndex];

                    for (int edgeIndex = 0; edgeIndex < cell.VertexCount; edgeIndex++)
                    {
                        int nextEdgeIndex = (edgeIndex + 1) % cell.VertexCount;

                        float3 a =
                            cellVertices[cell.FirstVertexIndex + edgeIndex].Position;

                        float3 b =
                            cellVertices[cell.FirstVertexIndex + nextEdgeIndex].Position;

                        GoldbergEdgeKey key = new GoldbergEdgeKey(a, b);

                        if (edgeOwners.TryGetValue(key, out EdgeOwner other))
                        {
                            neighbours.Add(new GoldbergCellNeighbour
                            {
                                CellIndex = cellIndex,
                                NeighbourCellIndex = other.CellIndex,
                                EdgeIndex = edgeIndex
                            });

                            neighbours.Add(new GoldbergCellNeighbour
                            {
                                CellIndex = other.CellIndex,
                                NeighbourCellIndex = cellIndex,
                                EdgeIndex = other.EdgeIndex
                            });

                            sharedEdges++;
                        }
                        else
                        {
                            edgeOwners.Add(key, new EdgeOwner
                            {
                                CellIndex = cellIndex,
                                EdgeIndex = edgeIndex
                            });
                        }
                    }
                }

                int cellCount = cells.Length;
                int neighbourCount = neighbours.Length;
                int boundaryEdges = edgeOwners.Count - sharedEdges;

                // Do this LAST, after reading buffer lengths.
                entityManager.AddComponent<GoldbergNeighboursBuilt>(planetEntity);

                Debug.Log(
                    $"Goldberg neighbours built. Cells: {cellCount}, Shared edges: {sharedEdges}, Boundary edges: {boundaryEdges}, Neighbour entries: {neighbourCount}"
                );
            }
        }
    }
}