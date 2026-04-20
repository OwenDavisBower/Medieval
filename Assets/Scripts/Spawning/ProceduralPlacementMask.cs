using System;
using Unity.Collections;
using UnityEngine;
/// <summary>
/// World XZ occupancy aligned with <see cref="TerrainGenerator"/> path distance grid: bit set = blocked (path, structures, trees, camps).
/// Mirrors words to a <see cref="ComputeBuffer"/> and an R8 texture (white = free, black = blocked) for sampling.
/// </summary>
public sealed class ProceduralPlacementMask : IDisposable
{
    NativeArray<uint> _words;
    ComputeBuffer _computeBuffer;
    Texture2D _texture;
    int _resolution;
    float _worldSize;
    Vector3 _worldOrigin;
    int _wordCount;
    bool _textureDirty = true;

    public int Resolution => _resolution;
    public float WorldSize => _worldSize;
    public Vector3 WorldOrigin => _worldOrigin;
    public Texture2D OccupancyTexture => _texture;

    public void Allocate(TerrainGenerator gen)
    {
        Dispose();
        if (gen == null)
            throw new ArgumentNullException(nameof(gen));

        _resolution = Mathf.Max(1, gen.worldResolution);
        _worldSize = gen.worldSize;
        _worldOrigin = gen.transform.position;
        _wordCount = (_resolution * _resolution + 31) / 32;
        _words = new NativeArray<uint>(_wordCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        _computeBuffer = new ComputeBuffer(_wordCount, sizeof(uint));
        _texture = new Texture2D(_resolution, _resolution, TextureFormat.R8, false, true)
        {
            name = "ProceduralPlacementOccupancy",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _textureDirty = true;
        SyncComputeBuffer();
    }

    /// <summary>Stamp path corridor from terrain distance field; cells with distance &lt; clearance become blocked.</summary>
    public void StampPathFromTerrain(TerrainGenerator gen, float clearanceWorldMeters)
    {
        if (!_words.IsCreated || gen == null)
            return;

        int n = _resolution * _resolution;
        using var blocked = new NativeArray<byte>(n, Allocator.TempJob);
        gen.WritePathBlockedBytes(blocked, clearanceWorldMeters);
        for (int i = 0; i < n; i++)
        {
            if (blocked[i] != 0)
                SetCellBlockedByLinearIndex(i);
        }

        _textureDirty = true;
        SyncComputeBuffer();
    }

    public bool IsDiskFreeWorldXZ(float worldX, float worldZ, float radiusWorld)
    {
        if (!_words.IsCreated || radiusWorld < 0f)
            return false;

        float cell = _worldSize / _resolution;
        float margin = cell * 0.501f;
        float r = radiusWorld + margin;

        if (!TryGetCellRangeForWorldDisk(worldX, worldZ, r, out int x0, out int x1, out int z0, out int z1))
            return false;

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (IsCellBlocked(x, z))
                {
                    float cx = CellCenterWorldX(x);
                    float cz = CellCenterWorldZ(z);
                    float dx = worldX - cx;
                    float dz = worldZ - cz;
                    if (dx * dx + dz * dz <= r * r)
                        return false;
                }
            }
        }

        return true;
    }

    public void BurnDiskWorldXZ(float worldX, float worldZ, float radiusWorld, float padding = 0f)
    {
        if (!_words.IsCreated)
            return;

        float r = Mathf.Max(0f, radiusWorld + padding);
        float cell = _worldSize / _resolution;
        float margin = cell * 0.501f;

        if (!TryGetCellRangeForWorldDisk(worldX, worldZ, r + margin, out int x0, out int x1, out int z0, out int z1))
            return;

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float cx = CellCenterWorldX(x);
                float cz = CellCenterWorldZ(z);
                float dx = worldX - cx;
                float dz = worldZ - cz;
                if (dx * dx + dz * dz <= (r + margin) * (r + margin))
                    SetCellBlocked(x, z);
            }
        }

        _textureDirty = true;
        SyncComputeBuffer();
    }

    public void BurnAxisAlignedRectWorldXZ(float minX, float minZ, float maxX, float maxZ, float padding = 0f)
    {
        if (!_words.IsCreated)
            return;

        float loX = Mathf.Min(minX, maxX) - padding;
        float hiX = Mathf.Max(minX, maxX) + padding;
        float loZ = Mathf.Min(minZ, maxZ) - padding;
        float hiZ = Mathf.Max(minZ, maxZ) + padding;

        if (!WorldToCellInclusive(loX, loZ, hiX, hiZ, out int x0, out int z0, out int x1, out int z1))
            return;

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
                SetCellBlocked(x, z);
        }

        _textureDirty = true;
        SyncComputeBuffer();
    }

    public void BurnFromRendererBoundsXZ(Bounds worldBounds, float padding)
    {
        BurnAxisAlignedRectWorldXZ(
            worldBounds.min.x,
            worldBounds.min.z,
            worldBounds.max.x,
            worldBounds.max.z,
            padding);
    }

    /// <summary>Bilinear sample of free channel in [0,1]: 1 = free, 0 = blocked.</summary>
    public float SampleFree01WorldXZ(float worldX, float worldZ)
    {
        if (_texture == null)
            return 0f;

        if (_textureDirty)
            SyncToTexture();

        float half = _worldSize * 0.5f;
        float u = (worldX - _worldOrigin.x + half) / _worldSize;
        float v = (worldZ - _worldOrigin.z + half) / _worldSize;
        return _texture.GetPixelBilinear(u, v).r;
    }

    public void SyncToTexture()
    {
        if (!_words.IsCreated || _texture == null)
            return;

        int n = _resolution * _resolution;
        var pixels = new NativeArray<byte>(n, Allocator.TempJob);
        try
        {
            for (int i = 0; i < n; i++)
            {
                bool blocked = IsCellBlockedByLinearIndex(i);
                pixels[i] = blocked ? (byte)0 : (byte)255;
            }

            _texture.SetPixelData(pixels, 0);
            _texture.Apply(false, false);
            _textureDirty = false;
        }
        finally
        {
            if (pixels.IsCreated)
                pixels.Dispose();
        }
    }

    public void SyncComputeBuffer()
    {
        if (_computeBuffer != null && _words.IsCreated)
            _computeBuffer.SetData(_words);
    }

    public void Dispose()
    {
        if (_words.IsCreated)
            _words.Dispose();

        if (_computeBuffer != null)
        {
            _computeBuffer.Release();
            _computeBuffer = null;
        }

        if (_texture != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_texture);
            else
                UnityEngine.Object.DestroyImmediate(_texture);
            _texture = null;
        }

        _textureDirty = true;
    }

    float CellCenterWorldX(int ix) =>
        _worldOrigin.x + ((ix + 0.5f) / _resolution - 0.5f) * _worldSize;

    float CellCenterWorldZ(int iz) =>
        _worldOrigin.z + ((iz + 0.5f) / _resolution - 0.5f) * _worldSize;

    bool TryGetCellRangeForWorldDisk(float wx, float wz, float radius, out int x0, out int x1, out int z0, out int z1)
    {
        x0 = x1 = z0 = z1 = 0;
        float half = _worldSize * 0.5f;
        float minX = wx - radius;
        float maxX = wx + radius;
        float minZ = wz - radius;
        float maxZ = wz + radius;
        return WorldToCellInclusive(minX, minZ, maxX, maxZ, out x0, out z0, out x1, out z1);
    }

    bool WorldToCellInclusive(float minX, float minZ, float maxX, float maxZ, out int x0, out int z0, out int x1, out int z1)
    {
        float half = _worldSize * 0.5f;
        float ox = _worldOrigin.x;
        float oz = _worldOrigin.z;

        float fx0 = (minX - ox + half) / _worldSize * _resolution - 0.5f;
        float fx1 = (maxX - ox + half) / _worldSize * _resolution - 0.5f;
        float fz0 = (minZ - oz + half) / _worldSize * _resolution - 0.5f;
        float fz1 = (maxZ - oz + half) / _worldSize * _resolution - 0.5f;

        x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(fx0, fx1)), 0, _resolution - 1);
        x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(fx0, fx1)) - 1, 0, _resolution - 1);
        z0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(fz0, fz1)), 0, _resolution - 1);
        z1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(fz0, fz1)) - 1, 0, _resolution - 1);
        return true;
    }

    bool IsCellBlocked(int ix, int iz)
    {
        if ((uint)ix >= (uint)_resolution || (uint)iz >= (uint)_resolution)
            return true;
        return IsCellBlockedByLinearIndex(iz * _resolution + ix);
    }

    bool IsCellBlockedByLinearIndex(int linear)
    {
        int word = linear >> 5;
        int bit = linear & 31;
        if ((uint)word >= (uint)_wordCount)
            return true;
        return (_words[word] & (1u << bit)) != 0u;
    }

    void SetCellBlocked(int ix, int iz)
    {
        if ((uint)ix >= (uint)_resolution || (uint)iz >= (uint)_resolution)
            return;
        SetCellBlockedByLinearIndex(iz * _resolution + ix);
    }

    void SetCellBlockedByLinearIndex(int linear)
    {
        int word = linear >> 5;
        int bit = linear & 31;
        if ((uint)word >= (uint)_wordCount)
            return;
        _words[word] |= 1u << bit;
    }
}
