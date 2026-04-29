using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// World XZ occupancy aligned with <see cref="TerrainGenerator"/> path distance grid: bit set = blocked (path, structures, trees, camps).
/// Path corridor and dynamic burns (structures, trees, etc.) are stored separately so dynamic occupancy can be cleared when streaming unloads content.
/// CPU sampling uses a cached byte grid; optional R8 <see cref="OccupancyTexture"/> is built lazily for GPU or debugging.
/// </summary>
public sealed class ProceduralPlacementMask : IDisposable
{
    NativeArray<uint> _pathWords;
    NativeArray<uint> _dynamicWords;
    NativeArray<byte> _cpuFreeBytes;
    Texture2D _texture;
    int _resolution;
    float _worldSize;
    Vector3 _worldOrigin;
    int _wordCount;
    bool _cpuFreeBytesStale = true;
    bool _textureGpuDirty = true;

    public int Resolution => _resolution;
    public float WorldSize => _worldSize;
    public Vector3 WorldOrigin => _worldOrigin;

    bool WordsAllocated => _pathWords.IsCreated && _dynamicWords.IsCreated;

    /// <summary>Lazily allocates and uploads from the current bit mask. Prefer <see cref="SampleFree01WorldXZ"/> for hot-path sampling.</summary>
    public Texture2D OccupancyTexture
    {
        get
        {
            if (!WordsAllocated)
                return null;
            EnsureTextureAllocated();
            if (_textureGpuDirty)
                SyncToTexture();
            return _texture;
        }
    }

    public void Allocate(TerrainGenerator gen)
    {
        Dispose();
        if (gen == null)
            throw new ArgumentNullException(nameof(gen));

        _resolution = Mathf.Max(1, gen.worldResolution);
        _worldSize = gen.worldSize;
        _worldOrigin = gen.transform.position;
        _wordCount = (_resolution * _resolution + 31) / 32;
        _pathWords = new NativeArray<uint>(_wordCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _dynamicWords = new NativeArray<uint>(_wordCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _cpuFreeBytes = new NativeArray<byte>(_resolution * _resolution, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _cpuFreeBytesStale = true;
        _textureGpuDirty = true;
    }

    /// <summary>Stamp path corridor from terrain distance field; cells with distance &lt; clearance become blocked.</summary>
    public void StampPathFromTerrain(TerrainGenerator gen, float clearanceWorldMeters)
    {
        if (!WordsAllocated || gen == null)
            return;

        if (!gen.TryStampPathOccupancyBits(_pathWords, _resolution, clearanceWorldMeters))
        {
            int n = _resolution * _resolution;
            using var blocked = new NativeArray<byte>(n, Allocator.TempJob);
            gen.WritePathBlockedBytes(blocked, clearanceWorldMeters);
            for (int i = 0; i < n; i++)
            {
                if (blocked[i] != 0)
                    SetPathBlockedByLinearIndex(i);
            }
        }

        MarkWordsChanged();
    }

    public bool IsDiskFreeWorldXZ(float worldX, float worldZ, float radiusWorld)
    {
        if (!WordsAllocated || radiusWorld < 0f)
            return false;

        float cell = _worldSize / _resolution;
        float margin = cell * 0.501f;
        float r = radiusWorld + margin;
        float rSq = r * r;

        if (!TryGetCellRangeForWorldDisk(worldX, worldZ, r, out int x0, out int x1, out int z0, out int z1))
            return false;

        for (int z = z0; z <= z1; z++)
        {
            int row = z * _resolution;
            for (int x = x0; x <= x1; x++)
            {
                if (IsCellBlockedByLinearIndex(row + x))
                {
                    float cx = CellCenterWorldX(x);
                    float cz = CellCenterWorldZ(z);
                    float dx = worldX - cx;
                    float dz = worldZ - cz;
                    if (dx * dx + dz * dz <= rSq)
                        return false;
                }
            }
        }

        return true;
    }

    public void BurnDiskWorldXZ(float worldX, float worldZ, float radiusWorld, float padding = 0f)
    {
        BurnDiskWorldXZCore(worldX, worldZ, radiusWorld, padding);
        MarkWordsChanged();
    }

    /// <summary>Burns multiple disks with a single invalidation of the CPU sampling cache.</summary>
    public void BurnDisksWorldXZ(IReadOnlyList<Vector3> positions, float radiusWorld, float padding = 0f)
    {
        if (!WordsAllocated || positions == null || positions.Count == 0)
            return;

        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 p = positions[i];
            BurnDiskWorldXZCore(p.x, p.z, radiusWorld, padding);
        }

        MarkWordsChanged();
    }

    void BurnDiskWorldXZCore(float worldX, float worldZ, float radiusWorld, float padding)
    {
        if (!WordsAllocated)
            return;

        float r = Mathf.Max(0f, radiusWorld + padding);
        float cell = _worldSize / _resolution;
        float margin = cell * 0.501f;
        float outer = r + margin;
        float outerSq = outer * outer;

        if (!TryGetCellRangeForWorldDisk(worldX, worldZ, outer, out int x0, out int x1, out int z0, out int z1))
            return;

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float cx = CellCenterWorldX(x);
                float cz = CellCenterWorldZ(z);
                float dx = worldX - cx;
                float dz = worldZ - cz;
                if (dx * dx + dz * dz <= outerSq)
                    SetDynamicCellBlocked(x, z);
            }
        }
    }

    public void BurnAxisAlignedRectWorldXZ(float minX, float minZ, float maxX, float maxZ, float padding = 0f)
    {
        if (!WordsAllocated)
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
                SetDynamicCellBlocked(x, z);
        }

        MarkWordsChanged();
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

    /// <summary>Clears dynamic (non-path) occupancy inside the axis-aligned XZ bounds. Path corridor bits are unchanged.</summary>
    public void UnburnAxisAlignedRectWorldXZ(float minX, float minZ, float maxX, float maxZ, float padding = 0f)
    {
        if (!WordsAllocated)
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
                ClearDynamicCellBlocked(x, z);
        }

        MarkWordsChanged();
    }

    public void UnburnFromRendererBoundsXZ(Bounds worldBounds, float padding)
    {
        UnburnAxisAlignedRectWorldXZ(
            worldBounds.min.x,
            worldBounds.min.z,
            worldBounds.max.x,
            worldBounds.max.z,
            padding);
    }

    /// <summary>Bilinear sample of free channel in [0,1]: 1 = free, 0 = blocked.</summary>
    public float SampleFree01WorldXZ(float worldX, float worldZ)
    {
        if (!WordsAllocated)
            return 0f;

        EnsureCpuFreeBytesCurrent();
        float half = _worldSize * 0.5f;
        float u = (worldX - _worldOrigin.x + half) / _worldSize;
        float v = (worldZ - _worldOrigin.z + half) / _worldSize;
        return SampleBilinearFree01(_cpuFreeBytes, _resolution, u, v);
    }

    /// <summary>Uploads the cached CPU free grid to <see cref="OccupancyTexture"/> (allocates texture if needed).</summary>
    public void SyncToTexture()
    {
        if (!WordsAllocated)
            return;

        EnsureTextureAllocated();
        EnsureCpuFreeBytesCurrent();
        _texture.SetPixelData(_cpuFreeBytes, 0);
        _texture.Apply(false, false);
        _textureGpuDirty = false;
    }

    void MarkWordsChanged()
    {
        _cpuFreeBytesStale = true;
        _textureGpuDirty = true;
    }

    public void Dispose()
    {
        if (_pathWords.IsCreated)
            _pathWords.Dispose();
        if (_dynamicWords.IsCreated)
            _dynamicWords.Dispose();

        if (_cpuFreeBytes.IsCreated)
            _cpuFreeBytes.Dispose();

        if (_texture != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_texture);
            else
                UnityEngine.Object.DestroyImmediate(_texture);
            _texture = null;
        }

        _cpuFreeBytesStale = true;
        _textureGpuDirty = true;
    }

    void EnsureTextureAllocated()
    {
        if (_texture != null)
            return;

        _texture = new Texture2D(_resolution, _resolution, TextureFormat.R8, false, true)
        {
            name = "ProceduralPlacementOccupancy",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
    }

    void EnsureCpuFreeBytesCurrent()
    {
        if (!_cpuFreeBytesStale)
            return;

        int n = _resolution * _resolution;
        for (int i = 0; i < n; i++)
            _cpuFreeBytes[i] = IsCellBlockedByLinearIndex(i) ? (byte)0 : (byte)255;

        _cpuFreeBytesStale = false;
    }

    /// <summary>Matches <see cref="Texture2D.GetPixelBilinear"/> on an R8 grid laid out row-major with the same indexing as occupancy bits.</summary>
    static float SampleBilinearFree01(NativeArray<byte> grid, int resolution, float u, float v)
    {
        if (!grid.IsCreated || resolution < 1)
            return 0f;

        int w = resolution;
        int h = resolution;
        u = Mathf.Clamp01(u) * w - 0.5f;
        v = Mathf.Clamp01(v) * h - 0.5f;
        int x0 = Mathf.FloorToInt(u);
        int y0 = Mathf.FloorToInt(v);
        float tx = u - x0;
        float ty = v - y0;
        int x1 = Mathf.Min(x0 + 1, w - 1);
        int y1 = Mathf.Min(y0 + 1, h - 1);
        x0 = Mathf.Clamp(x0, 0, w - 1);
        y0 = Mathf.Clamp(y0, 0, h - 1);

        float c00 = grid[y0 * w + x0] * (1f / 255f);
        float c10 = grid[y0 * w + x1] * (1f / 255f);
        float c01 = grid[y1 * w + x0] * (1f / 255f);
        float c11 = grid[y1 * w + x1] * (1f / 255f);
        float a = Mathf.Lerp(c00, c10, tx);
        float b = Mathf.Lerp(c01, c11, tx);
        return Mathf.Lerp(a, b, ty);
    }

    float CellCenterWorldX(int ix) =>
        _worldOrigin.x + ((ix + 0.5f) / _resolution - 0.5f) * _worldSize;

    float CellCenterWorldZ(int iz) =>
        _worldOrigin.z + ((iz + 0.5f) / _resolution - 0.5f) * _worldSize;

    bool TryGetCellRangeForWorldDisk(float wx, float wz, float radius, out int x0, out int x1, out int z0, out int z1)
    {
        x0 = x1 = z0 = z1 = 0;
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

    bool IsCellBlockedByLinearIndex(int linear)
    {
        int word = linear >> 5;
        int bit = linear & 31;
        if ((uint)word >= (uint)_wordCount)
            return true;
        uint mask = 1u << bit;
        return ((_pathWords[word] | _dynamicWords[word]) & mask) != 0u;
    }

    void SetDynamicCellBlocked(int ix, int iz)
    {
        if ((uint)ix >= (uint)_resolution || (uint)iz >= (uint)_resolution)
            return;
        SetDynamicBlockedByLinearIndex(iz * _resolution + ix);
    }

    void ClearDynamicCellBlocked(int ix, int iz)
    {
        if ((uint)ix >= (uint)_resolution || (uint)iz >= (uint)_resolution)
            return;
        ClearDynamicBlockedByLinearIndex(iz * _resolution + ix);
    }

    void SetPathBlockedByLinearIndex(int linear)
    {
        int word = linear >> 5;
        int bit = linear & 31;
        if ((uint)word >= (uint)_wordCount)
            return;
        _pathWords[word] |= 1u << bit;
    }

    void SetDynamicBlockedByLinearIndex(int linear)
    {
        int word = linear >> 5;
        int bit = linear & 31;
        if ((uint)word >= (uint)_wordCount)
            return;
        _dynamicWords[word] |= 1u << bit;
    }

    void ClearDynamicBlockedByLinearIndex(int linear)
    {
        int word = linear >> 5;
        int bit = linear & 31;
        if ((uint)word >= (uint)_wordCount)
            return;
        _dynamicWords[word] &= ~(1u << bit);
    }
}
