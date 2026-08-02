using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds a one-shot cartoon blood hit burst (two particle layers) for optional scene placement / preview.
/// Runtime hits use <see cref="CartoonBloodHitFx"/>; this menu regenerates a matching prefab asset.
/// </summary>
public static class CartoonBloodPrefabGenerator
{
    public const string PrefabPath = "Assets/Prefabs/Effects/CartoonBlood.prefab";
    public const string MaterialPath = "Assets/Prefabs/Effects/CartoonBloodParticle.mat";

    [MenuItem("Medieval/Effects/Generate Cartoon Blood Prefab", false, 2)]
    public static void GenerateFromMenu()
    {
        GenerateInternal();
    }

    /// <summary>Unity <c>-batchmode</c> entry: <c>CartoonBloodPrefabGenerator.GenerateCartoonBloodPrefabBatch</c>.</summary>
    public static void GenerateCartoonBloodPrefabBatch()
    {
        GenerateInternal();
    }

    static void GenerateInternal()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Prefabs/Effects");

        Material sharedMat = CreateOrLoadMaterial();
        var root = new GameObject("CartoonBlood");
        try
        {
            BuildLayer(root.transform, "Droplets", sharedMat, core: true);
            BuildLayer(root.transform, "Splat", sharedMat, core: false);

            GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefabRoot == null)
                throw new System.InvalidOperationException("SaveAsPrefabAsset returned null.");

            Debug.Log($"[CartoonBloodPrefabGenerator] Saved {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
            return;

        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        if (!string.IsNullOrEmpty(parent))
            AssetDatabase.CreateFolder(parent, name);
    }

    static Material CreateOrLoadMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null)
            return existing;

        Shader shader = ResolveParticleShader();
        Material mat;
        if (shader != null)
        {
            mat = new Material(shader)
            {
                name = "CartoonBloodParticle",
                enableInstancing = true
            };

            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", Color.white);
        }
        else
        {
            Material builtin = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            if (builtin == null)
                throw new System.InvalidOperationException("No particle shader or Default-Particle material found.");

            mat = Object.Instantiate(builtin);
            mat.name = "CartoonBloodParticle";
        }

        AssetDatabase.CreateAsset(mat, MaterialPath);
        return mat;
    }

    static Shader ResolveParticleShader()
    {
        string[] candidates =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Particles/Simple Lit",
            "Particles/Standard Unlit"
        };

        foreach (string n in candidates)
        {
            Shader s = Shader.Find(n);
            if (s != null)
                return s;
        }

        return null;
    }

    static void BuildLayer(Transform parent, string name, Material mat, bool core)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var ps = go.AddComponent<ParticleSystem>();
        var psr = go.GetComponent<ParticleSystemRenderer>();

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = true;
        main.duration = 0.25f;
        main.startLifetime = core
            ? new ParticleSystem.MinMaxCurve(0.28f, 0.55f)
            : new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = core
            ? new ParticleSystem.MinMaxCurve(2.4f, 4.8f)
            : new ParticleSystem.MinMaxCurve(1.1f, 2.6f);
        main.startSize = core
            ? new ParticleSystem.MinMaxCurve(0.06f, 0.14f)
            : new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = core ? 1.35f : 0.85f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = core ? 36 : 24;
        main.startColor = core
            ? new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.22f, 0.28f, 1f),
                new Color(0.95f, 0.05f, 0.12f, 1f))
            : new ParticleSystem.MinMaxGradient(
                new Color(0.75f, 0.08f, 0.16f, 0.9f),
                new Color(0.45f, 0.02f, 0.08f, 0.75f));

        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)(core ? 14 : 8), (short)(core ? 22 : 14))
        });

        ParticleSystem.ShapeModule sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = core ? 42f : 55f;
        sh.radius = core ? 0.04f : 0.08f;
        sh.radiusThickness = 1f;
        sh.arc = 360f;
        sh.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildBloodGradient(core));

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(0.12f, 1.15f),
            new Keyframe(0.55f, 0.85f),
            new Keyframe(1f, 0.05f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.VelocityOverLifetimeModule vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        vol.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        vol.y = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
        vol.z = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = core ? 0.35f : 0.55f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.4f;
        noise.octaveCount = 1;

        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = mat;
        psr.sortMode = ParticleSystemSortMode.OldestInFront;
        psr.minParticleSize = 0f;
        psr.maxParticleSize = 0.55f;
    }

    static Gradient BuildBloodGradient(bool core)
    {
        var g = new Gradient();
        if (core)
        {
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.45f, 0.55f), 0f),
                    new GradientColorKey(new Color(1f, 0.12f, 0.18f), 0.35f),
                    new GradientColorKey(new Color(0.55f, 0.02f, 0.08f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.06f),
                    new GradientAlphaKey(0.95f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
        }
        else
        {
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.9f, 0.15f, 0.22f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.05f, 0.1f), 0.5f),
                    new GradientColorKey(new Color(0.25f, 0.02f, 0.05f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.75f, 0.1f),
                    new GradientAlphaKey(0.45f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
        }

        return g;
    }
}
