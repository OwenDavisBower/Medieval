using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Periodically scans a sphere for colliders on <see cref="targetLayerMask"/> and returns the closest
/// entity whose <see cref="Affiliation"/> is <see cref="Relationship.Enemy"/> relative to this object.
/// </summary>
[DisallowMultipleComponent]
public sealed class TargetFinder : MonoBehaviour
{
    [FormerlySerializedAs("targetLayer")]
    [Tooltip("Physics layers scanned for enemy colliders (defaults to Character + Building).")]
    [SerializeField] LayerMask targetLayerMask;
    [SerializeField] float scanRadius = 12f;
    [SerializeField, Min(0.02f)] float scanInterval = 0.2f;
    [SerializeField, Min(1)] int overlapBufferSize = 32;
    [Tooltip("When false, only ScanNow() runs scans (e.g. combat FixedUpdate or tower fire tick).")]
    [SerializeField] bool periodicScanInUpdate = true;

    Affiliation _selfAffiliation;
    Collider[] _overlapBuffer;
    float _nextScanTime;
    Transform _cachedTransform;

    /// <summary>Last enemy found by the most recent scan (may be null).</summary>
    public Transform CurrentEnemyTarget { get; private set; }

    void Awake()
    {
        _cachedTransform = transform;
        _selfAffiliation = GetComponent<Affiliation>();
        _overlapBuffer = new Collider[Mathf.Max(1, overlapBufferSize)];
        EnsureTargetLayerMask();
    }

#if UNITY_EDITOR
    void OnValidate() => EnsureTargetLayerMask();
#endif

    void EnsureTargetLayerMask()
    {
        int v = targetLayerMask.value;
        if (v != 0 && v != -1)
            return;
        int m = LayerMask.GetMask("Character", "Building");
        if (m != 0)
            targetLayerMask = m;
    }

    void Update()
    {
        if (!periodicScanInUpdate)
            return;

        if (Time.time < _nextScanTime)
            return;

        _nextScanTime = Time.time + scanInterval;
        RunScan();
    }

    /// <summary>Forces an immediate scan regardless of <see cref="scanInterval"/>.</summary>
    public void ScanNow()
    {
        RunScan();
    }

    void RunScan()
    {
        CurrentEnemyTarget = null;

        FactionManager fm = FactionManager.Instance;
        if (fm == null || _selfAffiliation == null || _selfAffiliation.Faction == null)
            return;

        int myId = _selfAffiliation.FactionId;
        if (myId < 0)
            return;

        Vector3 origin = _cachedTransform.position;
        int hitCount = Physics.OverlapSphereNonAlloc(origin, scanRadius, _overlapBuffer, targetLayerMask,
            QueryTriggerInteraction.Ignore);

        float bestSq = float.MaxValue;
        Transform best = null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _overlapBuffer[i];
            if (col == null)
                continue;

            if (!Affiliation.TryGetForCollider(col, out Affiliation other) || other.Faction == null)
                continue;

            int otherId = other.FactionId;
            if (otherId < 0)
                continue;

            if (fm.GetRelationship(myId, otherId) != Relationship.Enemy)
                continue;

            float sq = (other.transform.position - origin).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = other.transform;
            }
        }

        CurrentEnemyTarget = best;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, scanRadius);
    }
}
