using System.Collections.Generic;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Single source of truth for the template-asset colour scheme.
    /// Every generated mesh references materials by these string keys, so retuning the
    /// whole art set is a matter of editing this file and re-running the generator.
    ///
    /// Palette targets the Castaway Colony reference: warm tropical mid-tones, low
    /// saturation on structural surfaces, saturation reserved for foliage and cloth
    /// accents so units stay readable against terrain from RTS camera height.
    /// </summary>
    public static class LowPolyPalette
    {
        public class Entry
        {
            public readonly string Key;
            public readonly Color Color;
            public readonly float Smoothness;
            public readonly Color Emission;
            public readonly float EmissionIntensity;

            public Entry(string key, string hex, float smoothness, string emissionHex, float emissionIntensity)
            {
                Key = key;
                Color = Parse(hex);
                Smoothness = smoothness;
                Emission = string.IsNullOrEmpty(emissionHex) ? Color.black : Parse(emissionHex);
                EmissionIntensity = emissionIntensity;
            }

            public bool HasEmission { get { return EmissionIntensity > 0f; } }
        }

        private static Color Parse(string hex)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString(hex, out c))
            {
                Debug.LogError("[LowPolyPalette] Bad hex colour: " + hex);
                return Color.magenta;
            }
            return c;
        }

        private static readonly Entry[] entries = new[]
        {
            // ---- Wood / bark -------------------------------------------------
            new Entry("PalmBark",    "#8A6A45", 0f, null, 0f),
            new Entry("TrunkBark",   "#6E4F33", 0f, null, 0f),
            new Entry("WoodPlank",   "#A97A4C", 0f, null, 0f),
            new Entry("WoodDark",    "#6B4A2E", 0f, null, 0f),
            new Entry("WoodLog",     "#7E5A38", 0f, null, 0f),
            new Entry("WoodPale",    "#C09A6B", 0f, null, 0f),

            // ---- Foliage -----------------------------------------------------
            new Entry("FrondLight",  "#7CC24E", 0f, null, 0f),
            new Entry("FrondMid",    "#57A33C", 0f, null, 0f),
            new Entry("FrondDark",   "#3D7C2E", 0f, null, 0f),
            // Tree variant canopy trios: same hue family, shifted tone, so mixed
            // forests read as "all green but differently shaded" (2026-08-26)
            new Entry("FrondOliveLight", "#A3B858", 0f, null, 0f),
            new Entry("FrondOlive",      "#7E9A42", 0f, null, 0f),
            new Entry("FrondOliveDark",  "#5C7530", 0f, null, 0f),
            new Entry("FrondDeepLight",  "#4FA36B", 0f, null, 0f),
            new Entry("FrondDeep",       "#357F4E", 0f, null, 0f),
            new Entry("FrondDeepDark",   "#245E38", 0f, null, 0f),
            new Entry("BushGreen",   "#4E9B45", 0f, null, 0f),
            new Entry("BushDark",    "#37733A", 0f, null, 0f),
            new Entry("GrassGreen",  "#86C25A", 0f, null, 0f),
            new Entry("FernGreen",   "#63AE4A", 0f, null, 0f),

            // ---- Rock / stone ------------------------------------------------
            new Entry("RockLight",   "#A2ABB0", 0f, null, 0f),
            new Entry("RockMid",     "#838D93", 0f, null, 0f),
            new Entry("RockDark",    "#616A70", 0f, null, 0f),
            new Entry("StoneBlock",  "#B4BABE", 0f, null, 0f),
            new Entry("StoneShadow", "#8D9498", 0f, null, 0f),
            new Entry("OreVein",     "#C9B07A", 0.25f, null, 0f),
            new Entry("OreRock",     "#4E5257", 0f, null, 0f),        // dark host rock of the metal node
            new Entry("OreMetal",    "#B9C6D2", 0.55f, null, 0f),     // bright metal veins / nuggets

            // ---- Thatch / sand -----------------------------------------------
            new Entry("ThatchLight", "#DCB663", 0f, null, 0f),
            new Entry("ThatchDark",  "#B58C3F", 0f, null, 0f),
            new Entry("Sand",        "#E4D2A2", 0f, null, 0f),

            // ---- Terrain bands (TerrainGrid.Surface) ---------------------------
            new Entry("SandWet",     "#C9B489", 0f, null, 0f),
            new Entry("GrassDark",   "#5E9E44", 0f, null, 0f),
            new Entry("GrassDry",    "#B9C25E", 0f, null, 0f),

            // ---- Cloth / units -----------------------------------------------
            new Entry("ClothCream",  "#E8DCC0", 0f, null, 0f),
            new Entry("ClothBlue",   "#4A7EA8", 0f, null, 0f),
            new Entry("ClothRed",    "#B5453C", 0f, null, 0f),
            new Entry("SkinTan",     "#D9A579", 0f, null, 0f),
            new Entry("LeatherBrown","#7A5236", 0f, null, 0f),
            new Entry("MetalSteel",  "#C3CAD2", 0.35f, null, 0f),
            new Entry("MetalDark",   "#6E7681", 0.25f, null, 0f),

            // ---- Enemy (deliberately off-palette so it reads as hostile) ------
            new Entry("EnemySkin",   "#7E6A8C", 0f, null, 0f),
            new Entry("EnemyCloth",  "#4A3B52", 0f, null, 0f),
            new Entry("EnemyAccent", "#C0392B", 0f, null, 0f),

            // ---- Props -------------------------------------------------------
            new Entry("BerryRed",    "#C4392F", 0f, null, 0f),
            new Entry("BarrelWood",  "#96683F", 0f, null, 0f),
            new Entry("BarrelBand",  "#6B7278", 0.3f, null, 0f),
            new Entry("Charcoal",    "#3A322E", 0f, null, 0f),

            // ---- Emissive heroes ---------------------------------------------
            // Intensity ~3 clears the Global Volume's Bloom Threshold of 1.0 without
            // needing the threshold lowered globally (see CLAUDE.md bloom gotcha).
            new Entry("FireCore",    "#FFD24A", 0f, "#FFB13B", 3f),
            new Entry("Ember",       "#E8763A", 0f, "#FF7A2E", 2f),
        };

        private static Dictionary<string, Entry> lookup;

        public static Entry Get(string key)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, Entry>(entries.Length);
                for (int i = 0; i < entries.Length; i++) lookup[entries[i].Key] = entries[i];
            }

            Entry e;
            if (lookup.TryGetValue(key, out e)) return e;

            Debug.LogError("[LowPolyPalette] Unknown palette key '" + key + "' - falling back to magenta.");
            return new Entry(key, "#FF00FF", 0f, null, 0f);
        }

        public static Entry[] All { get { return entries; } }
    }
}
