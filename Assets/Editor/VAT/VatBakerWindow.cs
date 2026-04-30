#nullable enable
using System;
using System.Collections.Generic;
using Medieval.VAT;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Medieval.Editor.VAT
{
    public sealed class VatBakerWindow : EditorWindow
    {
        [Serializable]
        private sealed class ClipEntry
        {
            public AnimationClip? clip;
            public bool loop = true;
        }

        [SerializeField] private GameObject? _sourcePrefab;
        [SerializeField] private int _skinnedMeshRendererIndex = 0;
        [SerializeField] private List<ClipEntry> _clips = new();

        [Header("Bake Settings")]
        [SerializeField] private int _fps = 20;
        [SerializeField] private bool _bakeNormals = true;
        [SerializeField] private bool _loopDefault = true;
        [SerializeField] private float _sampleStartOffset = 0f;
        [SerializeField] private bool _includeLastFrame = false;

        [SerializeField] private DefaultAsset? _outputFolder;

        private ReorderableList? _clipList;
        private string[] _smrOptions = Array.Empty<string>();

        [MenuItem("Tools/Medieval/VAT Baker")]
        private static void Open()
        {
            GetWindow<VatBakerWindow>("VAT Baker");
        }

        private void OnEnable()
        {
            if (_clips.Count == 0)
                _clips.Add(new ClipEntry { loop = _loopDefault });

            EnsureList();
            RefreshSmrOptions();
        }

        private void OnGUI()
        {
            using var check = new EditorGUI.ChangeCheckScope();

            EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel);
            _sourcePrefab = (GameObject?)EditorGUILayout.ObjectField("Prefab", _sourcePrefab, typeof(GameObject), false);
            if (check.changed)
            {
                RefreshSmrOptions();
                if (_outputFolder == null && _sourcePrefab != null)
                    _outputFolder = EnsureAndGetDefaultOutputFolder(_sourcePrefab);
            }

            using (new EditorGUI.DisabledScope(_sourcePrefab == null || _smrOptions.Length == 0))
            {
                _skinnedMeshRendererIndex = EditorGUILayout.Popup("SkinnedMeshRenderer", _skinnedMeshRendererIndex, _smrOptions);
            }

            EnsureList();
            _clipList!.DoLayoutList();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bake Settings", EditorStyles.boldLabel);
            _fps = EditorGUILayout.IntField(new GUIContent("FPS"), _fps);
            _bakeNormals = EditorGUILayout.Toggle(new GUIContent("Bake Normals"), _bakeNormals);
            _loopDefault = EditorGUILayout.Toggle(new GUIContent("Loop Default"), _loopDefault);
            _sampleStartOffset = EditorGUILayout.FloatField(new GUIContent("Sample Start Offset (s)"), _sampleStartOffset);
            _includeLastFrame = EditorGUILayout.Toggle(new GUIContent("Include Last Frame"), _includeLastFrame);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Outputs", EditorStyles.boldLabel);
            _outputFolder = (DefaultAsset?)EditorGUILayout.ObjectField("Output Folder", _outputFolder, typeof(DefaultAsset), false);

            using (new EditorGUI.DisabledScope(!CanBake()))
            {
                if (GUILayout.Button("Bake VAT Atlas"))
                    Bake();
            }

            if (_sourcePrefab != null && _outputFolder == null)
            {
                EditorGUILayout.HelpBox("Pick an output folder under Assets/ (or it will auto-create Assets/VAT/<PrefabName>/).", MessageType.Info);
            }
        }

        private void EnsureList()
        {
            if (_clipList != null)
                return;

            _clipList = new ReorderableList(_clips, typeof(ClipEntry), true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Clips (atlas order)"),
                onAddCallback = _ =>
                {
                    _clips.Add(new ClipEntry { loop = _loopDefault });
                },
                drawElementCallback = (rect, index, _, __) =>
                {
                    var entry = _clips[index];
                    var line = rect;
                    line.height = EditorGUIUtility.singleLineHeight;

                    var clipRect = new Rect(line.x, line.y, line.width * 0.75f, line.height);
                    var loopRect = new Rect(line.x + line.width * 0.78f, line.y, line.width * 0.22f, line.height);

                    entry.clip = (AnimationClip?)EditorGUI.ObjectField(clipRect, entry.clip, typeof(AnimationClip), false);
                    entry.loop = EditorGUI.ToggleLeft(loopRect, "Loop", entry.loop);
                }
            };
        }

        private bool CanBake()
        {
            if (_sourcePrefab == null)
                return false;
            if (_fps <= 0)
                return false;

            var anyClip = false;
            foreach (var c in _clips)
            {
                if (c.clip != null)
                    anyClip = true;
            }
            return anyClip;
        }

        private void RefreshSmrOptions()
        {
            _smrOptions = Array.Empty<string>();
            _skinnedMeshRendererIndex = 0;

            if (_sourcePrefab == null)
                return;

            var preview = (GameObject?)PrefabUtility.InstantiatePrefab(_sourcePrefab);
            if (preview == null)
                return;

            preview.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var smrs = preview.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0)
                    return;

                _smrOptions = new string[smrs.Length];
                for (var i = 0; i < smrs.Length; i++)
                {
                    var mesh = smrs[i].sharedMesh;
                    _smrOptions[i] = $"{smrs[i].name}  ({(mesh != null ? mesh.vertexCount : 0)} vtx)";
                }
            }
            finally
            {
                DestroyImmediate(preview);
            }
        }

        private void Bake()
        {
            if (_sourcePrefab == null)
                throw new InvalidOperationException("No prefab selected.");

            var outputFolderPath = GetOrCreateOutputFolderPath(_sourcePrefab);
            var prefabName = _sourcePrefab.name;

            var go = (GameObject?)PrefabUtility.InstantiatePrefab(_sourcePrefab);
            if (go == null)
                throw new InvalidOperationException("Failed to instantiate prefab for baking.");

            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            try
            {
                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0)
                    throw new InvalidOperationException("Prefab contains no SkinnedMeshRenderer.");

                var smrIndex = Mathf.Clamp(_skinnedMeshRendererIndex, 0, smrs.Length - 1);
                var smr = smrs[smrIndex];
                if (smr.sharedMesh == null)
                    throw new InvalidOperationException("Target SkinnedMeshRenderer has no sharedMesh.");

                var sourceMesh = smr.sharedMesh;
                var vertexCount = sourceMesh.vertexCount;

                var clipsOrdered = new List<(AnimationClip clip, bool loop)>();
                foreach (var entry in _clips)
                {
                    if (entry.clip == null)
                        continue;
                    clipsOrdered.Add((entry.clip, entry.loop));
                }
                if (clipsOrdered.Count == 0)
                    throw new InvalidOperationException("No animation clips provided.");

                var totalFrames = 0;
                var clipInfos = new List<VatClipInfo>(clipsOrdered.Count);
                foreach (var (clip, loop) in clipsOrdered)
                    totalFrames += ComputeFrameCount(clip.length, _fps, _includeLastFrame);

                if (totalFrames <= 0)
                    throw new InvalidOperationException("Total frames is 0. Check FPS and clip lengths.");

                var posPixels = new Color[vertexCount * totalFrames];
                var nrmPixels = _bakeNormals ? new Color[vertexCount * totalFrames] : null;

                var tempMesh = new Mesh { name = "__VAT_BakeMesh" };
                tempMesh.MarkDynamic();

                var overallBounds = new Bounds(Vector3.zero, Vector3.zero);
                var boundsInit = false;

                AnimationMode.StartAnimationMode();
                try
                {
                    var frameCursor = 0;
                    foreach (var (clip, loop) in clipsOrdered)
                    {
                        var clipFrames = ComputeFrameCount(clip.length, _fps, _includeLastFrame);
                        clipInfos.Add(new VatClipInfo
                        {
                            name = clip.name,
                            startFrame = frameCursor,
                            frameCount = clipFrames,
                            length = clip.length,
                            loop = loop
                        });

                        for (var f = 0; f < clipFrames; f++)
                        {
                            var t = _sampleStartOffset + (f / (float)_fps);

                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(go, clip, t);
                            AnimationMode.EndSampling();

                            smr.BakeMesh(tempMesh, true);

                            if (tempMesh.vertexCount != vertexCount)
                            {
                                throw new InvalidOperationException(
                                    $"Vertex count mismatch while baking '{clip.name}' at frame {f}: expected {vertexCount}, got {tempMesh.vertexCount}.");
                            }

                            var b = tempMesh.bounds;
                            if (!boundsInit)
                            {
                                overallBounds = b;
                                boundsInit = true;
                            }
                            else
                            {
                                overallBounds.Encapsulate(b);
                            }

                            var verts = tempMesh.vertices;
                            var norms = _bakeNormals ? tempMesh.normals : null;
                            var row = frameCursor + f;
                            var rowBase = row * vertexCount;

                            for (var i = 0; i < vertexCount; i++)
                            {
                                var p = verts[i];
                                posPixels[rowBase + i] = new Color(p.x, p.y, p.z, 1f);

                                if (_bakeNormals && norms != null)
                                {
                                    var n = norms[i];
                                    nrmPixels![rowBase + i] = new Color(n.x, n.y, n.z, 0f);
                                }
                            }
                        }

                        frameCursor += clipFrames;
                    }
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                }

                var posTexPath = $"{outputFolderPath}/{prefabName}_VAT_PosTex.asset";
                var nrmTexPath = $"{outputFolderPath}/{prefabName}_VAT_NrmTex.asset";
                var metaPath = $"{outputFolderPath}/{prefabName}_VAT.asset";

                DeleteIfExists(posTexPath);
                DeleteIfExists(nrmTexPath);
                DeleteIfExists(metaPath);

                var posTex = new Texture2D(vertexCount, totalFrames, TextureFormat.RGBAHalf, mipChain: false, linear: true)
                {
                    name = $"{prefabName}_VAT_PosTex",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };
                posTex.SetPixelData(posPixels, mipLevel: 0);
                posTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                AssetDatabase.CreateAsset(posTex, posTexPath);

                Texture2D? nrmTex = null;
                if (_bakeNormals && nrmPixels != null)
                {
                    nrmTex = new Texture2D(vertexCount, totalFrames, TextureFormat.RGBAHalf, mipChain: false, linear: true)
                    {
                        name = $"{prefabName}_VAT_NrmTex",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        anisoLevel = 0
                    };
                    nrmTex.SetPixelData(nrmPixels, mipLevel: 0);
                    nrmTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                    AssetDatabase.CreateAsset(nrmTex, nrmTexPath);
                }

                var meta = CreateInstance<VatAtlasAsset>();
                meta.name = $"{prefabName}_VAT";
                meta.sourcePrefab = _sourcePrefab;
                meta.sourceMesh = sourceMesh;
                meta.vertexCount = vertexCount;
                meta.fps = _fps;
                meta.totalFrames = totalFrames;
                meta.posTex = posTex;
                meta.nrmTex = nrmTex;
                meta.bounds = overallBounds;
                meta.clips = clipInfos.ToArray();
                AssetDatabase.CreateAsset(meta, metaPath);

                EditorUtility.SetDirty(meta);
                EditorUtility.SetDirty(posTex);
                if (nrmTex != null) EditorUtility.SetDirty(nrmTex);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Selection.activeObject = meta;
                EditorGUIUtility.PingObject(meta);
            }
            finally
            {
                DestroyImmediate(go);
            }
        }

        private string GetOrCreateOutputFolderPath(GameObject prefab)
        {
            if (_outputFolder == null)
                _outputFolder = EnsureAndGetDefaultOutputFolder(prefab);

            var path = AssetDatabase.GetAssetPath(_outputFolder);
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets", StringComparison.Ordinal))
                throw new InvalidOperationException("Output folder must be under Assets/.");

            EnsureFolderExists(path);
            return path.TrimEnd('/');
        }

        private static DefaultAsset EnsureAndGetDefaultOutputFolder(GameObject prefab)
        {
            const string basePath = "Assets/VAT";
            EnsureFolderExists(basePath);

            var childPath = $"{basePath}/{prefab.name}";
            EnsureFolderExists(childPath);

            var folderObj = AssetDatabase.LoadAssetAtPath<DefaultAsset>(childPath);
            if (folderObj == null)
                throw new InvalidOperationException($"Failed to load output folder object at '{childPath}'.");

            return folderObj;
        }

        private static void EnsureFolderExists(string assetPath)
        {
            assetPath = assetPath.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            if (!assetPath.StartsWith("Assets", StringComparison.Ordinal))
                throw new InvalidOperationException("Folder path must start with Assets/.");

            var parts = assetPath.Split('/');
            var current = parts[0]; // Assets
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static int ComputeFrameCount(float clipLengthSeconds, int fps, bool includeLastFrame)
        {
            if (fps <= 0)
                return 0;

            var raw = clipLengthSeconds * fps;
            var frames = includeLastFrame ? Mathf.CeilToInt(raw) + 1 : Mathf.FloorToInt(raw);
            return Mathf.Max(1, frames);
        }

        private static void DeleteIfExists(string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (existing != null)
                AssetDatabase.DeleteAsset(assetPath);
        }
    }
}

