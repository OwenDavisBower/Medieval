using UnityEngine;

public static class CombatVisuals
{
    const string RangedHatName = "RangedHat";

    /// <summary>Small cube on the unit's head when ranged; hidden for melee.</summary>
    public static void SetRangedHatVisible(Transform unitRoot, bool visible)
    {
        if (unitRoot == null)
            return;

        if (!visible)
        {
            Transform existing = unitRoot.Find(RangedHatName);
            if (existing != null)
                existing.gameObject.SetActive(false);
            return;
        }

        Transform hat = unitRoot.Find(RangedHatName);
        if (hat == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = RangedHatName;
            Object.Destroy(go.GetComponent<Collider>());
            hat = go.transform;
            hat.SetParent(unitRoot, false);
            hat.localPosition = new Vector3(0f, 2.05f, 0f);
            hat.localScale = new Vector3(0.22f, 0.14f, 0.22f);
        }

        hat.gameObject.SetActive(true);
    }
}
