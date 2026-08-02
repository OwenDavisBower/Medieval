using Medieval.NpcMovement;
using UnityEngine;

/// <summary>
/// Authoritative player objects for NPC combat and hybrid bridges; set from <see cref="PlayerController"/> on enable.
/// </summary>
public static class PlayerReference
{
    static Character s_Character;
    static PlayerExperience s_Experience;

    public static void Register(Transform transform, Rigidbody rigidbody, Character character)
    {
        var aff = transform != null ? transform.GetComponentInParent<Affiliation>() : null;
        int factionId = aff != null ? aff.FactionId : -1;
        PlayerAnchorRegistration.Register(transform, rigidbody, factionId);
        s_Character = character;
        if (transform != null)
        {
            s_Experience = transform.GetComponent<PlayerExperience>();
            if (s_Experience == null)
                s_Experience = transform.gameObject.AddComponent<PlayerExperience>();
        }
        else
            s_Experience = null;
    }

    public static void Unregister(Transform transform)
    {
        bool hadThisRoot = PlayerAnchorRegistration.HasPlayer &&
                           PlayerAnchorRegistration.Transform == transform;
        PlayerAnchorRegistration.Unregister(transform);
        if (hadThisRoot)
        {
            s_Character = null;
            s_Experience = null;
        }
    }

    public static Transform TryGetTransform() => PlayerAnchorRegistration.Transform;

    public static Rigidbody TryGetRigidbody() => PlayerAnchorRegistration.Rigidbody;

    public static Character TryGetCharacter() => s_Character;

    public static PlayerExperience TryGetExperience() => s_Experience;
}
