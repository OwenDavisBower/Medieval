using UnityEngine;

/// <summary>
/// Cartoon blood hit feedback via two shared world-space particle systems.
/// Hits only enqueue positions; a single runner flushes with <see cref="ParticleSystem.Emit"/> —
/// no per-hit Instantiate/Destroy, suitable for large melees.
/// </summary>
public static class CartoonBloodHitFx
{
    const float ChestHeightOffset = 1.05f;
    const int MaxPendingBursts = 96;
    const int MaxBurstsPerFlush = 64;
    const int DropletCount = 8;
    const int SplatCount = 4;
    const int DropletMaxParticles = 1024;
    const int SplatMaxParticles = 512;

    static readonly Vector3[] s_pending = new Vector3[MaxPendingBursts];
    static readonly Vector3[] s_pendingDir = new Vector3[MaxPendingBursts];
    static int s_pendingCount;

    static ParticleSystem s_droplets;
    static ParticleSystem s_splat;
    static Transform s_dropletXf;
    static Transform s_splatXf;
    static Material s_sharedMat;
    static bool s_runnerAlive;

    /// <summary>Queues a blood burst near chest height above <paramref name="feetWorldPosition"/>.</summary>
    public static void SpawnAtNpc(Vector3 feetWorldPosition, Vector3 impactDirection = default)
    {
        Spawn(feetWorldPosition + Vector3.up * ChestHeightOffset, impactDirection);
    }

    public static void Spawn(Vector3 worldPosition, Vector3 impactDirection = default)
    {
        if (s_pendingCount >= MaxPendingBursts)
            return;

        EnsureRunner();
        s_pending[s_pendingCount] = worldPosition;
        s_pendingDir[s_pendingCount] = impactDirection;
        s_pendingCount++;
    }

    /// <summary>Emits all queued bursts. Called once per frame by the runner.</summary>
    internal static void FlushPending()
    {
        if (s_pendingCount <= 0)
            return;

        EnsureSystems();
        if (s_droplets == null || s_splat == null)
        {
            s_pendingCount = 0;
            return;
        }

        int n = s_pendingCount < MaxBurstsPerFlush ? s_pendingCount : MaxBurstsPerFlush;
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true };
        for (int i = 0; i < n; i++)
        {
            Vector3 pos = s_pending[i];
            Quaternion rot = RotationFromImpact(s_pendingDir[i]);
            emit.position = pos;

            // Shape orientation follows the transform; EmitParams pins the world origin.
            s_dropletXf.SetPositionAndRotation(pos, rot);
            s_splatXf.SetPositionAndRotation(pos, rot);
            s_droplets.Emit(emit, DropletCount);
            s_splat.Emit(emit, SplatCount);
        }

        // Keep unprocessed bursts if we hit the per-frame cap.
        int remaining = s_pendingCount - n;
        if (remaining > 0)
        {
            System.Array.Copy(s_pending, n, s_pending, 0, remaining);
            System.Array.Copy(s_pendingDir, n, s_pendingDir, 0, remaining);
        }
        s_pendingCount = remaining;
    }

    static Quaternion RotationFromImpact(Vector3 impactDirection)
    {
        if (impactDirection.sqrMagnitude > 1e-6f)
        {
            Vector3 flat = impactDirection;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
                flat = impactDirection;
            return Quaternion.LookRotation(flat.normalized, Vector3.up);
        }

        return Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
    }

    static void EnsureRunner()
    {
        if (s_runnerAlive)
            return;

        var go = new GameObject("CartoonBloodHitFx_Runner");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<CartoonBloodHitFxRunner>();
        s_runnerAlive = true;
    }

    static void EnsureSystems()
    {
        if (s_droplets != null && s_splat != null)
            return;

        s_sharedMat = CreateParticleMaterial();

        var root = new GameObject("CartoonBloodHitFx_Shared");
        Object.DontDestroyOnLoad(root);
        root.hideFlags = HideFlags.HideAndDontSave;

        s_droplets = BuildLayer(root.transform, "Droplets", core: true);
        s_splat = BuildLayer(root.transform, "Splat", core: false);
        s_dropletXf = s_droplets.transform;
        s_splatXf = s_splat.transform;
        s_droplets.Play(true);
        s_splat.Play(true);
    }

    static Material CreateParticleMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");

        if (shader != null)
        {
            var mat = new Material(shader)
            {
                name = "CartoonBloodParticle_Runtime",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", Color.white);
            return mat;
        }

        Material builtin = Resources.GetBuiltinResource<Material>("Default-Particle.mat");
        if (builtin != null)
        {
            var mat = Object.Instantiate(builtin);
            mat.name = "CartoonBloodParticle_Runtime";
            mat.hideFlags = HideFlags.HideAndDontSave;
            return mat;
        }

        return null;
    }

    static ParticleSystem BuildLayer(Transform parent, string name, bool core)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var ps = go.AddComponent<ParticleSystem>();
        // AddComponent starts playback when playOnAwake is true; stop before
        // mutating main.duration (Unity forbids that while playing).
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var psr = go.GetComponent<ParticleSystemRenderer>();

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = core
            ? new ParticleSystem.MinMaxCurve(0.28f, 0.55f)
            : new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = core
            ? new ParticleSystem.MinMaxCurve(2.4f, 4.8f)
            : new ParticleSystem.MinMaxCurve(1.1f, 2.6f);
        main.startSize = core
            ? new ParticleSystem.MinMaxCurve(0.06f, 0.14f)
            : new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f);
        main.gravityModifier = core ? 1.35f : 0.85f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = core ? DropletMaxParticles : SplatMaxParticles;
        main.startColor = core
            ? new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.22f, 0.28f, 1f),
                new Color(0.95f, 0.05f, 0.12f, 1f))
            : new ParticleSystem.MinMaxGradient(
                new Color(0.75f, 0.08f, 0.16f, 0.9f),
                new Color(0.45f, 0.02f, 0.08f, 0.75f));
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = ps.emission;
        emission.enabled = false;

        // Cone along local +Z; runner orients transform toward impact so spray fans outward.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = core ? 42f : 55f;
        shape.radius = core ? 0.04f : 0.08f;
        shape.radiusThickness = 1f;
        shape.arc = 360f;
        shape.rotation = new Vector3(-25f, 0f, 0f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildBloodGradient(core));

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(0.12f, 1.15f),
            new Keyframe(0.55f, 0.85f),
            new Keyframe(1f, 0.05f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var noise = ps.noise;
        noise.enabled = false;
        var vel = ps.velocityOverLifetime;
        vel.enabled = false;
        var collision = ps.collision;
        collision.enabled = false;
        var trails = ps.trails;
        trails.enabled = false;
        var lights = ps.lights;
        lights.enabled = false;
        var subEmitters = ps.subEmitters;
        subEmitters.enabled = false;
        var texAnim = ps.textureSheetAnimation;
        texAnim.enabled = false;

        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = s_sharedMat;
        psr.sortMode = ParticleSystemSortMode.OldestInFront;
        psr.minParticleSize = 0f;
        psr.maxParticleSize = 0.55f;
        psr.allowRoll = true;

        return ps;
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

    sealed class CartoonBloodHitFxRunner : MonoBehaviour
    {
        void LateUpdate() => FlushPending();

        void OnDestroy() => s_runnerAlive = false;
    }
}
