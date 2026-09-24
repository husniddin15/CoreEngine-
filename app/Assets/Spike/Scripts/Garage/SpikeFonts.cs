using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// Fonts shared by every spike screen, created once from Windows (docs/10 §5): Segoe UI with Segoe UI
    /// Symbol as fallback for ▶ ✓ ⚙ and similar signs, and Consolas for code. Every needed glyph is
    /// loaded at creation, because a glyph drawn for the first time mid-scroll causes a visible hitch.
    /// Static references keep the font assets alive across scene loads.
    /// </summary>
    public static class SpikeFonts
    {
        static bool created;
        static FontAsset? ui, code, symbols;

        public static FontAsset? Ui { get { Create(); return ui; } }
        public static FontAsset? Code { get { Create(); return code; } }
        public static double PreloadMs { get; private set; }
        public static double SymbolFontMs { get; private set; }

        /// <summary>ASCII, Russian, Uzbek Latin marks, and the symbols the screens use.</summary>
        public static string Charset()
        {
            var sb = new StringBuilder();
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            for (char c = 'А'; c <= 'я'; c++) sb.Append(c);
            sb.Append("Ёёʻʼ‘’«»—–…№°±×→←≥≤µΩ•≈³−");
            return sb.ToString();
        }

        const string Symbols = "▶◀✓✗✕⚙●▾⚠★⚡";

        static void Create()
        {
            if (created) return;
            created = true;
            var watch = Stopwatch.StartNew();
            ui = CreateOsFont("Segoe UI");
            code = CreateOsFont("Consolas") ?? CreateOsFont("Cascadia Mono");
            double mainFonts = watch.Elapsed.TotalMilliseconds;
            symbols = CreateOsFont("Segoe UI Symbol");
            SymbolFontMs = watch.Elapsed.TotalMilliseconds - mainFonts;
            if (symbols != null)
            {
                if (ui != null) ui.fallbackFontAssetTable = new List<FontAsset> { symbols };
                if (code != null) code.fallbackFontAssetTable = new List<FontAsset> { symbols };
            }
            string charset = Charset();
            ui?.TryAddCharacters(charset, false);
            code?.TryAddCharacters(charset, false);
            symbols?.TryAddCharacters(Symbols, false);
            PreloadMs = watch.Elapsed.TotalMilliseconds;
        }

        public static FontAsset? CreateOsFont(string family)
        {
            try
            {
                var font = FontAsset.CreateFontAsset(family, "Regular", 90);
                if (font != null)
                {
                    font.name = family;
                    font.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                return font;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"SpikeFonts: {family} is not available: {e.Message}");
                return null;
            }
        }
    }
}
