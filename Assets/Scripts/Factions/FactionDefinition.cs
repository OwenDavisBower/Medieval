using UnityEngine;

/// <summary>
/// Authorable identity for a faction. <see cref="FactionID"/> must be unique and non-negative;
/// it indexes the runtime relationship matrix in <see cref="FactionManager"/>.
/// </summary>
[CreateAssetMenu(fileName = "FactionDefinition", menuName = "Medieval/Factions/Faction Definition")]
public class FactionDefinition : ScriptableObject
{
    [SerializeField] int factionID;
    [SerializeField] string factionName = "Faction";
    [Tooltip("Multiplied onto neutral NPC clothing (albedo alpha mask on tintable materials).")]
    [SerializeField] Color clothingColor = Color.white;

    /// <summary>Stable numeric id used for O(1) relationship lookups.</summary>
    public int FactionID => factionID;

    /// <summary>Display name for UI, debugging, and tools.</summary>
    public string FactionName => factionName;

    /// <summary>Tint applied to masked clothing regions on tintable NPC materials.</summary>
    public Color ClothingColor => clothingColor;

#if UNITY_EDITOR
    void OnValidate()
    {
        factionID = Mathf.Max(0, factionID);
        if (string.IsNullOrWhiteSpace(factionName))
            factionName = "Faction";
    }
#endif
}
