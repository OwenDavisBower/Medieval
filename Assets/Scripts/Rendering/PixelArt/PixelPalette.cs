using UnityEngine;

namespace Medieval.Rendering.PixelArt
{
    [CreateAssetMenu(fileName = "PixelPalette", menuName = "Medieval/Rendering/Pixel Palette", order = 0)]
    public class PixelPalette : ScriptableObject
    {
        [SerializeField] Color[] m_Colors = { Color.white, Color.black, Color.red, Color.green, Color.blue };

        public Color[] Colors => m_Colors;

        public int Count => m_Colors != null ? m_Colors.Length : 0;
    }
}
