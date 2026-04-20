using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Puts <see cref="Character"/> world-space health bars (layer <c>HealthBar</c>) on a URP overlay
/// so they are drawn after the base pass post-processing at native resolution (on top of render-scale upscaling).
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-40)]
sealed class HealthBarOverlayRig : MonoBehaviour
{
    [SerializeField] [Tooltip("Layer set on health bar canvases; must exist in Tag Manager.")] string _healthBarLayer = "HealthBar";
    [SerializeField] [Tooltip("Match base camera FOV/clip/ortho every frame (e.g. animated FOV).")] bool _syncProjectionEachFrame = true;

    const string ChildName = "HealthBarOverlay";
    const string MissingLayerError =
        "HealthBarOverlayRig: project layer 'HealthBar' is missing. Add a user layer named HealthBar in Edit → Project Settings → Tags and Layers.";

    Camera _baseCamera;
    UniversalAdditionalCameraData _baseUrp;
    Camera _overlay;
    int _cullingLayer = -1;

    void Awake()
    {
        _baseCamera = GetComponent<Camera>();
        _baseUrp = _baseCamera != null ? _baseCamera.GetUniversalAdditionalCameraData() : null;
        if (_baseUrp == null || _baseUrp.renderType != CameraRenderType.Base)
        {
            enabled = false;
            return;
        }

        if (_baseUrp.cameraStack == null)
        {
            Debug.LogError(
                "HealthBarOverlayRig: this URP renderer does not support camera stacking; health bars stay on the base pass.",
                this);
            enabled = false;
            return;
        }

        int layer = LayerMask.NameToLayer(_healthBarLayer);
        if (layer < 0)
        {
            Debug.LogError(MissingLayerError, this);
            enabled = false;
            return;
        }

        _cullingLayer = layer;
        _baseCamera.cullingMask = _baseCamera.cullingMask & ~(1 << layer);

        Transform existing = transform.Find(ChildName);
        if (existing != null)
            Destroy(existing.gameObject);

        var childGo = new GameObject(ChildName);
        childGo.transform.SetParent(transform, false);
        childGo.transform.localPosition = Vector3.zero;
        childGo.transform.localRotation = Quaternion.identity;
        childGo.transform.localScale = Vector3.one;

        _overlay = childGo.AddComponent<Camera>();
        _overlay.name = ChildName;
        _overlay.cullingMask = 1 << layer;
        _overlay.clearFlags = CameraClearFlags.Nothing;
        _overlay.allowMSAA = _baseCamera.allowMSAA;
        _overlay.allowDynamicResolution = _baseCamera.allowDynamicResolution;
        _overlay.depth = _baseCamera.depth + 1f;
        _overlay.nearClipPlane = _baseCamera.nearClipPlane;
        _overlay.farClipPlane = _baseCamera.farClipPlane;
        _overlay.orthographic = _baseCamera.orthographic;
        if (_baseCamera.orthographic)
        {
            _overlay.orthographic = true;
            _overlay.orthographicSize = _baseCamera.orthographicSize;
        }
        else
            _overlay.fieldOfView = _baseCamera.fieldOfView;

        var oUrp = _overlay.GetUniversalAdditionalCameraData();
        oUrp.renderType = CameraRenderType.Overlay;
        oUrp.renderPostProcessing = false;
        oUrp.antialiasing = AntialiasingMode.None;
        oUrp.dithering = false;
        oUrp.stopNaN = false;
        oUrp.volumeLayerMask = 0;

        if (!ContainsOverlay(_baseUrp.cameraStack, _overlay))
            _baseUrp.cameraStack.Add(_overlay);
    }

    void LateUpdate()
    {
        if (_overlay == null)
            return;
        if (_syncProjectionEachFrame)
        {
            _overlay.nearClipPlane = _baseCamera.nearClipPlane;
            _overlay.farClipPlane = _baseCamera.farClipPlane;
            if (_baseCamera.orthographic)
            {
                _overlay.orthographic = true;
                _overlay.orthographicSize = _baseCamera.orthographicSize;
            }
            else
            {
                _overlay.orthographic = false;
                _overlay.fieldOfView = _baseCamera.fieldOfView;
            }
        }
    }

    void OnDestroy()
    {
        if (_baseUrp != null && _baseUrp.cameraStack != null && _overlay != null)
            _baseUrp.cameraStack.Remove(_overlay);
        if (_cullingLayer >= 0 && _baseCamera != null)
            _baseCamera.cullingMask |= 1 << _cullingLayer;
    }

    static bool ContainsOverlay(System.Collections.Generic.IReadOnlyList<Camera> stack, Camera cam)
    {
        for (int i = 0; i < stack.Count; i++)
        {
            if (stack[i] == cam)
                return true;
        }

        return false;
    }

}
