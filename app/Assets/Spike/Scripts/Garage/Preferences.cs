using CoreEngine.Spike.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The player's own settings, kept between sessions in Unity's player preferences: the language and the size of
    /// the interface (research R5: a UI-scale option, and languages chosen once and then kept in Settings rather
    /// than in the top bar). Benchmark runs start from the defaults and save nothing.
    /// </summary>
    public static class Preferences
    {
        const string LanguageKey = "CoreEngine.Language", ChosenKey = "CoreEngine.LanguageChosen", ScaleKey = "CoreEngine.UiScale";

        /// <summary>The interface sizes offered, as the panel's scale over the 1280 × 720 layout's own growth with the screen.</summary>
        public static readonly float[] Scales = { 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };

        static bool loaded, saving;

        public static float UiScale { get; private set; } = 1f;

        /// <summary>False until the player has picked a language once (the first launch asks).</summary>
        public static bool LanguageChosen => !saving || PlayerPrefs.GetInt(ChosenKey, 0) == 1;

        public static void Load(bool benchmark)
        {
            if (loaded) return;
            loaded = true;
            saving = !benchmark;
            if (!saving) return;
            // Russian when Windows is in Russian; Unity knows no Uzbek, so English otherwise until the player chooses.
            SpikeStrings.SetLanguage(PlayerPrefs.GetInt(LanguageKey, Application.systemLanguage == SystemLanguage.Russian ? 2 : 0));
            UiScale = Mathf.Clamp(PlayerPrefs.GetFloat(ScaleKey, 1f), Scales[0], Scales[Scales.Length - 1]);
        }

        public static void SetLanguage(int language)
        {
            SpikeStrings.SetLanguage(language);
            if (!saving) return;
            PlayerPrefs.SetInt(LanguageKey, SpikeStrings.Language);
            PlayerPrefs.SetInt(ChosenKey, 1);
            PlayerPrefs.Save();
        }

        public static void SetUiScale(float scale, PanelSettings? panel)
        {
            UiScale = scale;
            Apply(panel);
            if (!saving) return;
            PlayerPrefs.SetFloat(ScaleKey, scale);
            PlayerPrefs.Save();
        }

        /// <summary>Gives a scene's panel the chosen size (the arena and the Garage share the panel settings).</summary>
        public static void Apply(PanelSettings? panel)
        {
            if (panel != null) panel.scale = UiScale;
        }
    }
}
