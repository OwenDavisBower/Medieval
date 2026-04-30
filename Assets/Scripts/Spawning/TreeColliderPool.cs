using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Positions a fixed pool of <see cref="CapsuleCollider"/> GameObjects to match instanced trees (hybrid physics).
/// </summary>
[DisallowMultipleComponent]
public class TreeColliderPool : MonoBehaviour
{
    [SerializeField] Transform poolRoot;
    [SerializeField] int poolSize = 2048;

    Transform[] _entries;
    CapsuleCollider[] _capsules;
    CapsuleCollider[] _navMeshCarveCapsules;

    void Awake()
    {
        if (poolRoot == null)
            poolRoot = transform;

        poolSize = Mathf.Max(0, poolSize);
        _entries = new Transform[poolSize];
        _capsules = new CapsuleCollider[poolSize];
        _navMeshCarveCapsules = new CapsuleCollider[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject($"TreeColliderPool_{i}");
            go.transform.SetParent(poolRoot, false);
            go.hideFlags = HideFlags.HideInHierarchy;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.enabled = false;
            var navCap = go.AddComponent<CapsuleCollider>();
            navCap.enabled = false;
            navCap.excludeLayers = Physics.AllLayers;
            _entries[i] = go.transform;
            _capsules[i] = cap;
            _navMeshCarveCapsules[i] = navCap;
        }
    }

    /// <summary>
    /// Activates up to <see cref="poolSize"/> colliders for <paramref name="instances"/>; remaining pool entries are disabled.
    /// </summary>
    public void Sync(TreeSpawnConfig config, IReadOnlyList<TreeInstanceData> instances)
    {
        if (_capsules == null || config == null || instances == null)
            return;

        float carveExtra = Mathf.Max(0f, config.NavMeshCarveRadiusExtra);
        int n = Mathf.Min(instances.Count, _capsules.Length);
        for (int i = 0; i < n; i++)
        {
            TreeInstanceData d = instances[i];
            if (!config.TryGetVariantInstancing(
                    d.VariantId,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out float capsuleHeight,
                    out float capsuleRadius,
                    out _))
            {
                _capsules[i].enabled = false;
                if (_navMeshCarveCapsules != null)
                    _navMeshCarveCapsules[i].enabled = false;
                continue;
            }

            var cap = _capsules[i];
            cap.height = Mathf.Max(0.1f, capsuleHeight * d.Scale);
            cap.radius = Mathf.Max(0.05f, capsuleRadius * d.Scale);
            cap.direction = 1;
            cap.center = new Vector3(0f, cap.height * 0.5f, 0f);

            if (_navMeshCarveCapsules != null && carveExtra > 0f)
            {
                var navCap = _navMeshCarveCapsules[i];
                float navR = cap.radius + carveExtra;
                navCap.radius = navR;
                navCap.height = Mathf.Max(cap.height, 2f * navR);
                navCap.direction = 1;
                navCap.center = new Vector3(0f, navCap.height * 0.5f, 0f);
                navCap.enabled = true;
            }
            else if (_navMeshCarveCapsules != null)
                _navMeshCarveCapsules[i].enabled = false;

            Transform tr = _entries[i];
            tr.SetPositionAndRotation((Vector3)d.Position, (Quaternion)d.Rotation);
            cap.enabled = true;
        }

        for (int i = n; i < _capsules.Length; i++)
        {
            _capsules[i].enabled = false;
            if (_navMeshCarveCapsules != null)
                _navMeshCarveCapsules[i].enabled = false;
        }
    }

    public void ClearAll()
    {
        if (_capsules == null)
            return;

        for (int i = 0; i < _capsules.Length; i++)
        {
            _capsules[i].enabled = false;
            if (_navMeshCarveCapsules != null)
                _navMeshCarveCapsules[i].enabled = false;
        }
    }
}
