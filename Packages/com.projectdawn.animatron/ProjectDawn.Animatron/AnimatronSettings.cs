using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Settings asset of Animatron package.
    /// </summary>
    [CreateAssetMenu(fileName = "Animatron Settings", menuName = "Rendering/Animatron Settings", order = 1000)]
    [HelpURL("https://lukaschod.github.io/agents-navigation-docs/manual/settings.html")]
    public class AnimatronSettings : ScriptableObject
    {
        static AnimatronSettings s_Instance;

        /// <summary>
        /// Currently used animatron settings asset.
        /// </summary>
        public static AnimatronSettings Instance
        {
            get
            {
#if UNITY_EDITOR
                // Settings asset is stored in preloaded assets
                // Here we attempt to find it
                foreach (var asset in UnityEditor.PlayerSettings.GetPreloadedAssets())
                {
                    if (asset is AnimatronSettings settings)
                    {
                        return settings;
                    }
                }
                return null;
#else
                return s_Instance;
#endif
            }

            set
            {
                if (Application.isPlaying)
                    throw new InvalidOperationException("Can not change agents navigation settings at runtime!");

#if UNITY_EDITOR
                var assets = new List<UnityEngine.Object>(UnityEditor.PlayerSettings.GetPreloadedAssets());

                // Remove all AnimatronSettings
                if (value == null)
                {
                    for (int i = 0; i < assets.Count; i++)
                    {
                        if (assets[i] is AnimatronSettings)
                        {
                            assets.RemoveAt(i);
                            i--;
                        }
                    }
                    UnityEditor.PlayerSettings.SetPreloadedAssets(assets.ToArray());
                    return;
                }

                // Change existing AnimatronSettings to new value
                for (int i = 0; i < assets.Count; i++)
                {
                    if (assets[i] is AnimatronSettings)
                    {
                        assets[i] = value;

                        // Force to contain only single AnimatronSettings
                        for (int j = i + 1; j < assets.Count; j++)
                        {
                            if (assets[j] is AnimatronSettings)
                            {
                                assets.RemoveAt(j);
                                j--;
                            }
                        }

                        UnityEditor.PlayerSettings.SetPreloadedAssets(assets.ToArray());
                        return;
                    }
                }

                // Simply add at the end AgentsNavigationSettings
                assets.Add(value);
                UnityEditor.PlayerSettings.SetPreloadedAssets(assets.ToArray());
#endif
            }
        }

        [Tooltip("All Animatron joints are stored in a single resizable GPU buffer. This property controls the initial joint capacity at the start of the game. It’s useful to set this value to either the average or maximum expected capacity to avoid expensive reallocations during gameplay. You can use the statistics below to monitor real-time usage.")]
        [SerializeField]
        int m_ReserveTotalJoints = 2 << 16;

        /// <summary>
        /// All Animatron joints are stored in a single resizable GPU buffer.
        /// This property controls the initial joint capacity at the start of the game.
        /// It’s useful to set this value to either the average or maximum expected capacity to avoid expensive reallocations during gameplay.
        /// </summary>
        public static int ReserveTotalJoints => Instance?.m_ReserveTotalJoints ?? Activator.CreateInstance<AnimatronSettings>().m_ReserveTotalJoints;

        public AnimatronSettings()
        {
            s_Instance = this;
        }
    }
}
