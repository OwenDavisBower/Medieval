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

    void Awake()
    {
        if (poolRoot == null)
            poolRoot = transform;

        poolSize = Mathf.Max(0, poolSize);
        _entries = new Transform[poolSize];
        _capsules = new CapsuleCollider[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject($"TreeColliderPool_{i}");
            go.transform.SetParent(poolRoot, false);
            go.hideFlags = HideFlags.HideInHierarchy;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.enabled = false;
            _entries[i] = go.transform;
            _capsules[i] = cap;
        }
    }

    /// <summary>
    /// Activates up to <see cref="poolSize"/> colliders for <paramref name="instances"/>; remaining pool entries are disabled.
    /// </summary>
    public void Sync(TreeSpawnConfig config, IReadOnlyList<TreeInstanceData> instances)
    {
        if (_capsules == null || config == null || instances == null)
            return;

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
                    out float capsuleRadius))
            {
                _capsules[i].enabled = false;
                continue;
            }

            var cap = _capsules[i];
            cap.height = Mathf.Max(0.1f, capsuleHeight * d.Scale);
            cap.radius = Mathf.Max(0.05f, capsuleRadius * d.Scale);
            cap.direction = 1;
            cap.center = new Vector3(0f, cap.height * 0.5f, 0f);

            Transform tr = _entries[i];
            tr.SetPositionAndRotation(
                (Vector3)d.Position,
                (Quaternion)d.Rotation * config.InstanceMeshRotationOffset);
            cap.enabled = true;
        }

        for (int i = n; i < _capsules.Length; i++)
            _capsules[i].enabled = false;
    }

    public void ClearAll()
    {
        if (_capsules == null)
            return;

        for (int i = 0; i < _capsules.Length; i++)
            _capsules[i].enabled = false;
    }
}
