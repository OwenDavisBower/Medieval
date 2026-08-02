using UnityEngine;

/// <summary>
/// World pickup spawned when an enemy NPC dies. Collects into <see cref="PlayerWallet"/> when the player is nearby.
/// </summary>
public sealed class GoldDrop : MonoBehaviour
{
    public const int DefaultMinAmount = 3;
    public const int DefaultMaxAmountInclusive = 8;

    const float CollectRadius = 1.85f;
    const float LifetimeSeconds = 45f;
    const float BobAmplitude = 0.12f;
    const float BobSpeed = 3.2f;
    const float SpinSpeedDegrees = 90f;
    const float MagnetSpeed = 9f;
    const float MagnetStartRadius = 3.2f;
    const float GroundLift = 0.55f;

    static readonly Color CollectLabelColor = new Color(1f, 0.88f, 0.28f, 1f);

    int _amount;
    float _age;
    float _baseY;
    bool _collected;
    MeshRenderer _renderer;
    static Material s_sharedMat;

    /// <summary>Spawns a gold pickup at <paramref name="worldPosition"/> with a random small amount.</summary>
    public static void SpawnRandom(Vector3 worldPosition, int minAmount = DefaultMinAmount,
        int maxAmountInclusive = DefaultMaxAmountInclusive)
    {
        int lo = Mathf.Min(minAmount, maxAmountInclusive);
        int hi = Mathf.Max(minAmount, maxAmountInclusive);
        int amount = UnityEngine.Random.Range(lo, hi + 1);
        Spawn(worldPosition, amount);
    }

    public static void Spawn(Vector3 worldPosition, int amount)
    {
        if (amount <= 0)
            return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "GoldDrop";
        Object.Destroy(go.GetComponent<Collider>());

        Vector3 pos = worldPosition;
        pos.y += GroundLift;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.28f;

        var drop = go.AddComponent<GoldDrop>();
        drop._amount = amount;
        drop._baseY = pos.y;
        drop._renderer = go.GetComponent<MeshRenderer>();
        if (drop._renderer != null)
            drop._renderer.sharedMaterial = SharedGoldMaterial();
    }

    static Material SharedGoldMaterial()
    {
        if (s_sharedMat != null)
            return s_sharedMat;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        s_sharedMat = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        s_sharedMat.name = "GoldDropMat";
        s_sharedMat.color = new Color(1f, 0.82f, 0.2f, 1f);
        if (s_sharedMat.HasProperty("_Metallic"))
            s_sharedMat.SetFloat("_Metallic", 0.85f);
        if (s_sharedMat.HasProperty("_Smoothness"))
            s_sharedMat.SetFloat("_Smoothness", 0.75f);
        return s_sharedMat;
    }

    void Update()
    {
        if (_collected)
            return;

        _age += Time.deltaTime;
        if (_age >= LifetimeSeconds)
        {
            Destroy(gameObject);
            return;
        }

        float bob = Mathf.Sin(_age * BobSpeed) * BobAmplitude;
        transform.position = new Vector3(transform.position.x, _baseY + bob, transform.position.z);
        transform.Rotate(Vector3.up, SpinSpeedDegrees * Time.deltaTime, Space.World);

        Transform player = PlayerReference.TryGetTransform();
        if (player == null)
            return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;

        if (dist <= CollectRadius)
        {
            Collect();
            return;
        }

        if (dist <= MagnetStartRadius && dist > 0.01f)
        {
            Vector3 step = toPlayer.normalized * (MagnetSpeed * Time.deltaTime);
            if (step.sqrMagnitude > toPlayer.sqrMagnitude)
                step = toPlayer;
            Vector3 next = transform.position + step;
            next.y = _baseY + bob;
            transform.position = next;
            _baseY = next.y - bob;
        }
    }

    void Collect()
    {
        if (_collected)
            return;
        _collected = true;

        var wallet = PlayerWallet.Instance;
        if (wallet != null)
            wallet.Add(_amount);

        // +0.95 offsets Spawn's built-in start height so the label rises from just above the coin.
        FloatingWorldText.Spawn(
            transform.position + Vector3.up * 0.95f,
            $"+{_amount} Gold",
            CollectLabelColor);
        Destroy(gameObject);
    }
}
