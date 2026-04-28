using UnityEngine;

namespace Medieval.Rendering.PixelArt
{
    [CreateAssetMenu(fileName = "PixelArtColorSettings", menuName = "Medieval/Rendering/Pixel Art Color Settings", order = 0)]
    public sealed class PixelArtColorSettings : ScriptableObject
    {
        [Tooltip("Per-channel posterization steps. 0 disables posterization.")]
        [SerializeField] [Range(0f, 256f)] float m_ColorDepth;

        [Tooltip("Ordered 4×4 Bayer dither before quantization. 0 = off.")]
        [SerializeField] [Range(0f, 1f)] float m_PosterizeDitherStrength;

        public float ColorDepth => m_ColorDepth;
        public float PosterizeDitherStrength => m_PosterizeDitherStrength;
    }
}
