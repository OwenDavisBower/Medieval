using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Injects <see cref="PixelatePass"/> into the URP renderer. Pair with a <see cref="PixelateVolume"/> on your
/// scene Volume Profile and enable the pipeline opaque texture so <c>_CameraOpaqueTexture</c> is populated.
/// </summary>
[DisallowMultipleRendererFeature]
public sealed class PixelateRendererFeature : ScriptableRendererFeature
{
    [SerializeField] Shader m_Shader;
    [SerializeField] RenderPassEvent m_InjectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

    Material m_Material;
    PixelatePass m_Pass;

    public override void Create()
    {
        Shader shader = m_Shader != null ? m_Shader : Shader.Find("Hidden/URP/PixelateScreen");
        if (shader == null)
        {
            Debug.LogError("PixelateRendererFeature: assign the PixelateScreen shader or ensure it exists at Hidden/URP/PixelateScreen.");
            return;
        }

        m_Material = CoreUtils.CreateEngineMaterial(shader);
        m_Pass = new PixelatePass(m_Material, m_InjectionPoint);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Pass == null)
            return;

        CameraData cameraData = renderingData.cameraData;
        if (!ShouldRun(cameraData.cameraType))
            return;

        if (IsOverlayCamera(cameraData))
            return;

        if (!PixelateGrid.TryGetActiveVolume(out PixelateVolume volume))
            return;

        if (!cameraData.requiresOpaqueTexture)
            return;

        // Requests the opaque-only color snapshot consumed by the shader as _CameraOpaqueTexture.
        m_Pass.ConfigureInput(ScriptableRenderPassInput.Color);

        renderer.EnqueuePass(m_Pass);
    }

    static bool ShouldRun(CameraType cameraType) =>
        cameraType is CameraType.Game or CameraType.SceneView;

    static bool IsOverlayCamera(CameraData cameraData)
    {
        Camera c = cameraData.camera;
        if (c == null)
            return false;
        if (!c.TryGetComponent<UniversalAdditionalCameraData>(out var u))
            return false;
        return u.renderType == CameraRenderType.Overlay;
    }

    protected override void Dispose(bool disposing)
    {
        if (m_Material != null)
            CoreUtils.Destroy(m_Material);

        m_Material = null;
        m_Pass = null;
    }
}
