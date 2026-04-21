using UnityEngine;

/// <summary>Assigns a physics layer to a transform and all descendants.</summary>
public static class HierarchyLayers
{
    public static void SetRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetRecursive(root.GetChild(i), layer);
    }

    public static void SetRecursiveByLayerName(Transform root, string layerName)
    {
        if (root == null || string.IsNullOrEmpty(layerName))
            return;
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
            return;
        SetRecursive(root, layer);
    }
}
