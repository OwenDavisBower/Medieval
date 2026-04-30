#nullable enable
using System;
using UnityEngine;

namespace Medieval.VAT
{
    [Serializable]
    public struct VatClipInfo
    {
        public string name;
        public int startFrame;
        public int frameCount;
        public float length;
        public bool loop;
    }

    public sealed class VatAtlasAsset : ScriptableObject
    {
        public GameObject? sourcePrefab;
        public Mesh? sourceMesh;
        public int vertexCount;
        public int fps;
        public int totalFrames;
        public Texture2D? posTex;
        public Texture2D? nrmTex;
        public Bounds bounds;
        public VatClipInfo[] clips = Array.Empty<VatClipInfo>();
    }
}

