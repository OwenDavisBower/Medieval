using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Procedural heightmap via fractal Brownian motion (layered Perlin), with power shaping and a
/// height floor so most of the terrain stays flat with occasional rolling hills.
/// </summary>
[RequireComponent(typeof(Terrain))]
public class TerrainGenerator : MonoBehaviour
{
        [Header("Noise (FBM)")]
        [SerializeField, Min(0.0001f)]
        [Tooltip("Larger values stretch noise in world space (broader, smoother features).")]
        private float scale = 32f;

        [SerializeField, Range(1, 12)]
        private int octaves = 5;

        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("Amplitude falloff per octave.")]
        private float persistence = 0.45f;

        [SerializeField, Range(1.05f, 4f)]
        [Tooltip("Frequency multiplier per octave.")]
        private float lacunarity = 2.1f;

        [Header("Shaping")]
        [SerializeField, Range(0.1f, 8f)]
        [Tooltip("Higher values push low noise toward zero (flatter plains, sharper hill tops).")]
        private float hillSharpness = 2f;

        [SerializeField, Range(0f, 0.99f)]
        [Tooltip("Raw FBM (before hill shaping) below this becomes flat ground. Higher = more plain, occasional hills only.")]
        private float heightThreshold = 0.58f;

        [Header("Variation")]
        [SerializeField]
        private int seed = 42;

        [SerializeField]
        [Tooltip("Pans sampling in noise space (scroll the pattern).")]
        private Vector2 noiseOffset = Vector2.zero;

        [ContextMenu("Generate Terrain")]
        public void GenerateTerrain()
        {
            var terrain = GetComponent<Terrain>();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogError("TerrainGenerator: requires a Terrain with TerrainData assigned.", this);
                return;
            }

            TerrainData data = terrain.terrainData;
            int w = data.heightmapResolution;
            int h = data.heightmapResolution;
            float[,] heights = new float[h, w];

            float invX = w > 1 ? 1f / (w - 1) : 0f;
            float invZ = h > 1 ? 1f / (h - 1) : 0f;

            // Stable pseudo-offset from seed (reproducible)
            float seedX = Hash01(seed, 1);
            float seedZ = Hash01(seed, 3);

            // Threshold raw FBM first, then pow only the hill fraction — pow-before-threshold zeros almost all height.
            for (int z = 0; z < h; z++)
            {
                float nz = z * invZ;
                for (int x = 0; x < w; x++)
                {
                    float nx = x * invX;

                    float sampleX = (nx + noiseOffset.x + seedX) / scale;
                    float sampleZ = (nz + noiseOffset.y + seedZ) / scale;

                    float raw = Mathf.Clamp01(SampleFbm01(sampleX, sampleZ));

                    float n;
                    if (raw <= heightThreshold)
                    {
                        n = 0f;
                    }
                    else
                    {
                        float denom = 1f - heightThreshold;
                        float t = denom > 1e-6f ? (raw - heightThreshold) / denom : 0f;
                        n = Mathf.Pow(Mathf.Clamp01(t), hillSharpness);
                    }

                    heights[z, x] = Mathf.Clamp01(n);
                }
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.RegisterCompleteObjectUndo(data, "Generate Terrain");
                Undo.RegisterCompleteObjectUndo(terrain, "Generate Terrain");
            }
#endif

            data.SetHeights(0, 0, heights);

#if UNITY_EDITOR
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(terrain);
            if (!Application.isPlaying && terrain.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            SceneView.RepaintAll();
#endif
        }

        /// <summary>Layered Perlin noise normalized to approximately 0–1.</summary>
        private float SampleFbm01(float x, float z)
        {
            float sum = 0f;
            float weight = 1f;
            float freq = 1f;
            float norm = 0f;

            for (int o = 0; o < octaves; o++)
            {
                float px = x * freq;
                float pz = z * freq;
                float sample = Mathf.PerlinNoise(px, pz);
                sum += sample * weight;
                norm += weight;
                weight *= persistence;
                freq *= lacunarity;
            }

            return norm > 1e-6f ? sum / norm : 0f;
        }

        private static float Hash01(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093 ^ salt * 19349663);
                h ^= h >> 16;
                h *= 0x85ebca6bu;
                h ^= h >> 13;
                h *= 0xc2b2ae35u;
                h ^= h >> 16;
                return (h & 0xFFFFu) / 65535f;
            }
        }
}
