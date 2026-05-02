using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds a looping, lightweight 3D-styled cartoon fire using two stacked particle layers (billboards).
/// Run once via menu or batchmode; outputs <see cref="PrefabPath"/> and <see cref="MaterialPath"/>.
/// </summary>
public static class CartoonFirePrefabGenerator
{
    public const string PrefabPath = "Assets/Prefabs/Effects/CartoonFire.prefab";
    public const string MaterialPath = "Assets/Prefabs/Effects/CartoonFireParticle.mat";

    [MenuItem("Medieval/Effects/Generate Cartoon Fire Prefab", false, 1)]
    public static void GenerateFromMenu()
    {
        GenerateInternal();
    }

    /// <summary>Unity <c>-batchmode</c> entry (pair with <c>-quit</c>): <c>CartoonFirePrefabGenerator.GenerateCartoonFirePrefabBatch</c>.</summary>
    public static void GenerateCartoonFirePrefabBatch()
    {
        GenerateInternal();
    }

    static void GenerateInternal()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Prefabs/Effects");

        Material sharedMat = CreateOrLoadMaterial();
        var root = new GameObject("CartoonFire");
        try
        {
            BuildLayer(root.transform, "FlameCore", sharedMat, core: true);
            BuildLayer(root.transform, "FlameOuter", sharedMat, core: false);

            GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefabRoot == null)
                throw new System.InvalidOperationException("SaveAsPrefabAsset returned null.");

            Debug.Log($"[CartoonFirePrefabGenerator] Saved {PrefabPath}");
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
                name = "CartoonFireParticle",
                enableInstancing = true
            };

            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 1f);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", Color.white);
        }
        else
        {
            Material builtin = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            if (builtin == null)
                throw new System.InvalidOperationException("No particle shader or Default-Particle material found.");

            mat = Object.Instantiate(builtin);
            mat.name = "CartoonFireParticle";
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
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 2f;
        main.prewarm = true;
        main.startLifetime = core
            ? new ParticleSystem.MinMaxCurve(0.35f, 0.55f)
            : new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
        main.startSpeed = core
            ? new ParticleSystem.MinMaxCurve(0.9f, 1.7f)
            : new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
        main.startSize = core
            ? new ParticleSystem.MinMaxCurve(0.11f, 0.2f)
            : new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = core ? 48 : 64;
        main.startColor = core
            ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.55f, 1f), new Color(1f, 0.75f, 0.25f, 1f))
            : new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.85f), new Color(1f, 0.2f, 0.05f, 0.55f));

        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = core ? 28f : 22f;

        ParticleSystem.ShapeModule sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = core ? 18f : 32f;
        sh.radius = core ? 0.06f : 0.1f;
        sh.radiusThickness = 0f;
        sh.arc = 360f;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildFireGradient(core));

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.25f),
            new Keyframe(0.22f, 1f),
            new Keyframe(1f, 0.05f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.VelocityOverLifetimeModule vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        // X/Y/Z must share the same MinMaxCurve mode; Y uses two constants, so match with (0,0) on X/Z.
        vol.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.y = core ? new ParticleSystem.MinMaxCurve(0.35f, 0.9f) : new ParticleSystem.MinMaxCurve(0.25f, 0.65f);
        vol.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystem.RotationOverLifetimeModule rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.separateAxes = true;
        rol.x = new ParticleSystem.MinMaxCurve(0f);
        rol.y = new ParticleSystem.MinMaxCurve(0f);
        rol.z = new ParticleSystem.MinMaxCurve(-Mathf.PI * 0.2f, Mathf.PI * 0.2f);

        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = mat;
        psr.sortMode = ParticleSystemSortMode.OldestInFront;
        psr.minParticleSize = 0f;
        psr.maxParticleSize = 0.45f;
        psr.cameraVelocityScale = 0f;

        ParticleSystem.CollisionModule collision = ps.collision;
        collision.enabled = false;

        ParticleSystem.TriggerModule trigger = ps.trigger;
        trigger.enabled = false;

        ParticleSystem.TrailModule trails = ps.trails;
        trails.enabled = false;

        ParticleSystem.LightsModule lights = ps.lights;
        lights.enabled = false;

        ParticleSystem.SubEmittersModule subEmitters = ps.subEmitters;
        subEmitters.enabled = false;

        ParticleSystem.TextureSheetAnimationModule textureSheetAnimation = ps.textureSheetAnimation;
        textureSheetAnimation.enabled = false;

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = false;

        ps.Play();
    }

    static Gradient BuildFireGradient(bool core)
    {
        var g = new Gradient();
        if (core)
        {
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 1f, 0.85f), 0f),
                    new GradientColorKey(new Color(1f, 0.65f, 0.15f), 0.45f),
                    new GradientColorKey(new Color(1f, 0.25f, 0.05f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.9f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                });
        }
        else
        {
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.55f, 0.12f), 0f),
                    new GradientColorKey(new Color(1f, 0.22f, 0.05f), 0.55f),
                    new GradientColorKey(new Color(0.35f, 0.05f, 0.02f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.55f, 0.12f),
                    new GradientAlphaKey(0.35f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
        }

        return g;
    }
}
