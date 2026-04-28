#nullable enable
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Medieval.Water
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PlanarReflectionController : MonoBehaviour
    {
        [Header("Targets")]
        [SerializeField] private Renderer? waterRenderer;

        [Header("Reflection")]
        [SerializeField] private LayerMask reflectionMask = ~0;
        [SerializeField, Min(16)] private int textureSize = 512;
        [SerializeField, Range(0.0f, 0.2f)] private float clipPlaneOffset = 0.05f;

        private Camera? _reflectionCamera;
        private RenderTexture? _reflectionTexture;
        private bool _isRenderingReflection;
        private bool _pendingReflectionRender;
        private int _pendingReflectionFrame = -1;
        private bool _loggedUnsupportedOnce;

        private static readonly int ReflectionTexId = Shader.PropertyToID("_ReflectionTex");

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            Cleanup();
        }

        private void OnValidate()
        {
            textureSize = Mathf.Clamp(textureSize, 16, 4096);
        }

        private void Cleanup()
        {
            if (_reflectionCamera != null)
            {
                if (Application.isPlaying) Destroy(_reflectionCamera.gameObject);
                else DestroyImmediate(_reflectionCamera.gameObject);
                _reflectionCamera = null;
            }

            if (_reflectionTexture != null)
            {
                if (Application.isPlaying) Destroy(_reflectionTexture);
                else DestroyImmediate(_reflectionTexture);
                _reflectionTexture = null;
            }
        }

        private void EnsureResources(Camera src)
        {
            if (_reflectionTexture == null || _reflectionTexture.width != textureSize || _reflectionTexture.height != textureSize)
            {
                if (_reflectionTexture != null)
                {
                    if (Application.isPlaying) Destroy(_reflectionTexture);
                    else DestroyImmediate(_reflectionTexture);
                }

                _reflectionTexture = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.ARGB32)
                {
                    name = $"PlanarReflection_{GetInstanceID()}",
                    useMipMap = false,
                    autoGenerateMips = false,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            if (_reflectionCamera == null)
            {
                var go = new GameObject("Planar Reflection Camera (Hidden)");
                go.hideFlags = HideFlags.HideAndDontSave;
                _reflectionCamera = go.AddComponent<Camera>();
                _reflectionCamera.enabled = false;
            }

            _reflectionCamera!.CopyFrom(src);
            _reflectionCamera.cameraType = CameraType.Reflection;
            _reflectionCamera.cullingMask = reflectionMask;
            _reflectionCamera.targetTexture = _reflectionTexture;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!isActiveAndEnabled) return;
            if (camera == null) return;
            if (camera.cameraType is CameraType.Reflection or CameraType.Preview) return;
            if (_reflectionCamera != null && ReferenceEquals(camera, _reflectionCamera)) return;
            if (_isRenderingReflection) return;

            var rend = waterRenderer != null ? waterRenderer : GetComponent<Renderer>();
            if (rend == null) return;

            EnsureResources(camera);

            var planePos = transform.position;
            var planeNormal = transform.up;

            // Reflect camera position across plane
            var d = -Vector3.Dot(planeNormal, planePos) - clipPlaneOffset;
            var reflectionPlane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, d);
            var reflectionMat = CalculateReflectionMatrix(reflectionPlane);

            var oldCullingMatrix = _reflectionCamera!.cullingMatrix;

            var srcPos = camera.transform.position;
            var reflPos = reflectionMat.MultiplyPoint(srcPos);

            _reflectionCamera.worldToCameraMatrix = camera.worldToCameraMatrix * reflectionMat;
            _reflectionCamera.transform.position = reflPos;
            _reflectionCamera.transform.rotation = camera.transform.rotation;

            // Oblique near plane to clip geometry above water
            var clipPlane = CameraSpacePlane(_reflectionCamera, planePos, planeNormal, 1.0f, clipPlaneOffset);
            _reflectionCamera.projectionMatrix = camera.CalculateObliqueMatrix(clipPlane);

            _reflectionCamera.cullingMatrix = _reflectionCamera.projectionMatrix * _reflectionCamera.worldToCameraMatrix;

            var prevInvertCulling = GL.invertCulling;
            GL.invertCulling = true;
            try
            {
                // URP/SRP cannot safely render a camera from within beginCameraRendering.
                // Queue a render request to be submitted later (outside the render loop).
                _pendingReflectionRender = true;
                _pendingReflectionFrame = Time.frameCount;
            }
            finally
            {
                GL.invertCulling = prevInvertCulling;
                _reflectionCamera.cullingMatrix = oldCullingMatrix;
            }

            // Feed texture to the water material/shader
            if (rend.sharedMaterial != null)
                rend.sharedMaterial.SetTexture(ReflectionTexId, _reflectionTexture);
        }

        private void LateUpdate()
        {
            if (!_pendingReflectionRender) return;
            if (!isActiveAndEnabled) return;
            if (_reflectionCamera == null) return;

            // Avoid spamming multiple submits per frame if multiple cameras trigger beginCameraRendering.
            if (_pendingReflectionFrame != Time.frameCount) return;

            _pendingReflectionRender = false;
            TrySubmitReflectionRenderRequest(_reflectionCamera);
        }

        private void TrySubmitReflectionRenderRequest(Camera reflectionCamera)
        {
            if (_isRenderingReflection) return;
            _isRenderingReflection = true;
            try
            {
                // Preferred SRP-safe path: RenderPipeline.SubmitRenderRequest (Unity 2022+).
                var rpType = typeof(RenderPipeline);
                var methods = rpType.GetMethods(BindingFlags.Public | BindingFlags.Static);

                MethodInfo? best = null;
                foreach (var m in methods)
                {
                    if (!string.Equals(m.Name, "SubmitRenderRequest", StringComparison.Ordinal)) continue;
                    var p = m.GetParameters();
                    if (p.Length != 2) continue;
                    if (p[0].ParameterType != typeof(Camera)) continue;
                    best = m;
                    break;
                }

                if (best == null)
                {
                    LogUnsupportedOnce();
                    return;
                }

                object? request = CreateDefaultRenderRequest(best);
                if (request == null)
                {
                    LogUnsupportedOnce();
                    return;
                }

                best.Invoke(null, new[] { reflectionCamera, request });
            }
            catch
            {
                LogUnsupportedOnce();
            }
            finally
            {
                _isRenderingReflection = false;
            }
        }

        private static object? CreateDefaultRenderRequest(MethodInfo submitMethod)
        {
            if (submitMethod.IsGenericMethodDefinition)
            {
                // Try RenderPipeline.StandardRequest (nested type) if present.
                var standardRequestType = typeof(RenderPipeline).GetNestedType("StandardRequest", BindingFlags.Public | BindingFlags.NonPublic);
                if (standardRequestType == null) return null;

                var closed = submitMethod.MakeGenericMethod(standardRequestType);
                // For generic SubmitRenderRequest<T>(Camera, T) we must return an instance of T.
                return Activator.CreateInstance(standardRequestType);
            }

            var parameters = submitMethod.GetParameters();
            if (parameters.Length != 2) return null;
            var requestType = parameters[1].ParameterType;
            if (requestType == typeof(object)) return new object();
            if (requestType.IsValueType) return Activator.CreateInstance(requestType);
            return Activator.CreateInstance(requestType);
        }

        private void LogUnsupportedOnce()
        {
            if (_loggedUnsupportedOnce) return;
            _loggedUnsupportedOnce = true;
            Debug.LogWarning($"{nameof(PlanarReflectionController)}: SRP render requests are not available/compatible in this Unity version/pipeline. Planar reflections will not render.", this);
        }

        private static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign, float offset)
        {
            var offsetPos = pos + normal * offset;
            var m = cam.worldToCameraMatrix;
            var cpos = m.MultiplyPoint(offsetPos);
            var cnormal = m.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }

        private static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
        {
            var reflectionMat = Matrix4x4.identity;

            reflectionMat.m00 = 1F - 2F * plane[0] * plane[0];
            reflectionMat.m01 = -2F * plane[0] * plane[1];
            reflectionMat.m02 = -2F * plane[0] * plane[2];
            reflectionMat.m03 = -2F * plane[3] * plane[0];

            reflectionMat.m10 = -2F * plane[1] * plane[0];
            reflectionMat.m11 = 1F - 2F * plane[1] * plane[1];
            reflectionMat.m12 = -2F * plane[1] * plane[2];
            reflectionMat.m13 = -2F * plane[3] * plane[1];

            reflectionMat.m20 = -2F * plane[2] * plane[0];
            reflectionMat.m21 = -2F * plane[2] * plane[1];
            reflectionMat.m22 = 1F - 2F * plane[2] * plane[2];
            reflectionMat.m23 = -2F * plane[3] * plane[2];

            reflectionMat.m30 = 0F;
            reflectionMat.m31 = 0F;
            reflectionMat.m32 = 0F;
            reflectionMat.m33 = 1F;

            return reflectionMat;
        }
    }
}

