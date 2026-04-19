using UnityEngine;

public class EnableMeshRendererOnAwake : MonoBehaviour
{
    void Awake()
    {
        var meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.enabled = true;
    }
}
