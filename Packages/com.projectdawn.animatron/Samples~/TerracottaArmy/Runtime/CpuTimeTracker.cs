using System.Collections.Generic;
using UnityEngine;

namespace ProjectDawn.Sample
{
    public class CpuTimeTracker : MonoBehaviour
    {
        [Header("Settings")]
        public float sampleIntervalSeconds = 5f;
        public Vector2 guiPosition = new Vector2(10, 10);

        private readonly List<float> frameTimes = new List<float>();
        private float elapsedTime = 0f;

        private float averageMs = 0f;
        private GUIStyle guiStyle;

        void Start()
        {
            guiStyle = new GUIStyle
            {
                fontSize = 16,
                normal = { textColor = Color.white }
            };
        }

        void Update()
        {
            float deltaMs = Time.deltaTime * 1000f;
            frameTimes.Add(deltaMs);
            elapsedTime += Time.deltaTime;

            if (elapsedTime >= sampleIntervalSeconds)
            {
                float total = 0f;
                foreach (float ms in frameTimes)
                    total += ms;

                averageMs = frameTimes.Count > 0 ? total / frameTimes.Count : 0f;

                frameTimes.Clear();
                elapsedTime = 0f;
            }
        }

        void OnGUI()
        {
            string text = $"CPU Frame Time: {Time.deltaTime * 1000f:F2} ms\n" +
                          $"Avg over {sampleIntervalSeconds}s: {averageMs:F2} ms";

            GUI.Label(new Rect(guiPosition.x, guiPosition.y, 300, 50), text, guiStyle);
        }
    }
}