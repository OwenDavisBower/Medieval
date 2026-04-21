using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Declares which faction this entity belongs to. Registers child <see cref="Collider"/>s so
/// systems like <see cref="TargetFinder"/> can resolve faction without per-hit <c>GetComponent</c>.
/// </summary>
[DisallowMultipleComponent]
public sealed class Affiliation : MonoBehaviour
{
    static readonly Dictionary<int, Affiliation> s_colliderInstanceIdToAffiliation = new Dictionary<int, Affiliation>(256);

    [SerializeField] FactionDefinition faction;

    /// <summary>Authoring reference; can be swapped at runtime.</summary>
    public FactionDefinition Faction
    {
        get => faction;
        set => faction = value;
    }

    public int FactionId => faction != null ? faction.FactionID : -1;

    readonly List<Collider> _registeredColliders = new List<Collider>(4);

    void OnEnable()
    {
        RegisterCollidersUnderThisHierarchy();
    }

    void OnDisable()
    {
        UnregisterAllColliders();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying && isActiveAndEnabled)
        {
            UnregisterAllColliders();
            RegisterCollidersUnderThisHierarchy();
        }
    }
#endif

    void RegisterCollidersUnderThisHierarchy()
    {
        GetComponentsInChildren<Collider>(true, _registeredColliders);
        for (int i = 0; i < _registeredColliders.Count; i++)
        {
            Collider c = _registeredColliders[i];
            if (c == null)
                continue;
            s_colliderInstanceIdToAffiliation[c.GetInstanceID()] = this;
        }
    }

    void UnregisterAllColliders()
    {
        for (int i = 0; i < _registeredColliders.Count; i++)
        {
            Collider c = _registeredColliders[i];
            if (c == null)
                continue;
            int id = c.GetInstanceID();
            if (s_colliderInstanceIdToAffiliation.TryGetValue(id, out Affiliation owner) && owner == this)
                s_colliderInstanceIdToAffiliation.Remove(id);
        }

        _registeredColliders.Clear();
    }

    /// <summary>O(1) lookup for overlap hits; avoids <c>GetComponent</c> in tight loops.</summary>
    public static bool TryGetForCollider(Collider collider, out Affiliation affiliation)
    {
        affiliation = null;
        if (collider == null)
            return false;
        return s_colliderInstanceIdToAffiliation.TryGetValue(collider.GetInstanceID(), out affiliation);
    }
}
