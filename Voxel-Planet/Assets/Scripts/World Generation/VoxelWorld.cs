using System.Collections.Generic;
using UnityEngine;

namespace VoxelPlanet
{
    public class VoxelWorld : MonoBehaviour
    {
        public enum BlockType
        {
            Air,
            Grass,
            Dirt,
            Stone,
            Water
        }

        // cellIndex -> layer -> block type
        private readonly Dictionary<int, Dictionary<int, BlockType>> blocksByCell =
            new Dictionary<int, Dictionary<int, BlockType>>();

        public bool HasBlock(int cellIndex, int layer)
        {
            return blocksByCell.TryGetValue(cellIndex, out var layers)
                && layers.ContainsKey(layer);
        }

        public BlockType GetBlock(int cellIndex, int layer)
        {
            if (!blocksByCell.TryGetValue(cellIndex, out var layers))
                return BlockType.Air;

            if (!layers.TryGetValue(layer, out BlockType block))
                return BlockType.Air;

            return block;
        }

        public bool HasSolidBlock(int cellIndex, int layer)
        {
            BlockType block = GetBlock(cellIndex, layer);

            return block != BlockType.Air &&
                   block != BlockType.Water;
        }

        public void SetBlock(int cellIndex, int layer, BlockType blockType)
        {
            if (blockType == BlockType.Air)
            {
                RemoveBlock(cellIndex, layer);
                return;
            }

            if (!blocksByCell.TryGetValue(cellIndex, out var layers))
            {
                layers = new Dictionary<int, BlockType>();
                blocksByCell[cellIndex] = layers;
            }

            layers[layer] = blockType;
        }

        public void RemoveBlock(int cellIndex, int layer)
        {
            if (!blocksByCell.TryGetValue(cellIndex, out var layers))
                return;

            layers.Remove(layer);

            if (layers.Count == 0)
                blocksByCell.Remove(cellIndex);
        }

        public void ClearCell(int cellIndex)
        {
            blocksByCell.Remove(cellIndex);
        }

        public void ClearAll()
        {
            blocksByCell.Clear();
        }

        public int GetHighestSolidLayer(int cellIndex)
        {
            if (!blocksByCell.TryGetValue(cellIndex, out var layers))
                return int.MinValue;

            int highest = int.MinValue;

            foreach (var pair in layers)
            {
                if (pair.Value == BlockType.Air ||
                    pair.Value == BlockType.Water)
                    continue;

                if (pair.Key > highest)
                    highest = pair.Key;
            }

            return highest;
        }

        public IReadOnlyDictionary<int, BlockType> GetBlocksForCell(int cellIndex)
        {
            if (!blocksByCell.TryGetValue(cellIndex, out var layers))
                return null;

            return layers;
        }

        public void GenerateBasicColumn(
            int cellIndex,
            int surfaceLayer,
            int oceanLevel)
        {
            ClearCell(cellIndex);

            if (surfaceLayer < oceanLevel)
                SetBlock(cellIndex, surfaceLayer, BlockType.Dirt);
            else
                SetBlock(cellIndex, surfaceLayer, BlockType.Grass);

            SetBlock(cellIndex, surfaceLayer - 1, BlockType.Dirt);
            SetBlock(cellIndex, surfaceLayer - 2, BlockType.Dirt);

            for (int layer = surfaceLayer - 3; layer >= surfaceLayer - 8; layer--)
            {
                SetBlock(cellIndex, layer, BlockType.Stone);
            }

            if (surfaceLayer < oceanLevel)
            {
                for (int layer = surfaceLayer + 1; layer <= oceanLevel; layer++)
                {
                    SetBlock(cellIndex, layer, BlockType.Water);
                }
            }
        }

        public void AddBlockAboveTop(int cellIndex, BlockType blockType = BlockType.Grass)
        {
            int highestLayer = GetHighestSolidLayer(cellIndex);

            if (highestLayer == int.MinValue)
                highestLayer = -1;

            SetBlock(cellIndex, highestLayer + 1, blockType);
        }

        public void RemoveTopSolidBlock(int cellIndex)
        {
            int highestLayer = GetHighestSolidLayer(cellIndex);

            if (highestLayer == int.MinValue)
                return;

            RemoveBlock(cellIndex, highestLayer);
        }
    }
}