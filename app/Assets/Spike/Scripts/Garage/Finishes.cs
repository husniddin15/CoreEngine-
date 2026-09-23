using System.Collections.Generic;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    public enum FinishTarget { Body, Wheels }

    /// <summary>
    /// A visual finish (docs/03 §10): colour and surface only; finishes never change physics.
    /// <see cref="Pack"/> is null for free finishes, otherwise the key of the customization pack
    /// that sells it (ADR-0007). Names are EN / UZ / RU (AI drafts for the owner to review).
    /// </summary>
    public sealed class Finish
    {
        public Finish(string id, FinishTarget target, Color color, float smoothness, float metallic, string? pack, string en, string uz, string ru)
        {
            Id = id;
            Target = target;
            Color = color;
            Smoothness = smoothness;
            Metallic = metallic;
            Pack = pack;
            names = new[] { en, uz, ru };
        }

        readonly string[] names;

        public string Id { get; }
        public FinishTarget Target { get; }
        public Color Color { get; }
        public float Smoothness { get; }
        public float Metallic { get; }
        public string? Pack { get; }
        public bool IsFree => Pack == null;
        public string Name => names[UI.SpikeStrings.Language];
    }

    /// <summary>The prototype's finish catalogue: free finishes plus the two launch customization packs (D15).</summary>
    public static class Finishes
    {
        public const string CarbonMetalPack = "pack.carbonMetal";
        public const string NeonPack = "pack.neon";

        public static readonly IReadOnlyList<Finish> All = new List<Finish>
        {
            new Finish("blue-acrylic", FinishTarget.Body, new Color(0.13f, 0.33f, 0.78f), 0.80f, 0f, null, "Blue acrylic", "Koʻk akril", "Синий акрил"),
            new Finish("smoked-acrylic", FinishTarget.Body, new Color(0.22f, 0.24f, 0.27f), 0.85f, 0f, null, "Smoked acrylic", "Tutunli akril", "Дымчатый акрил"),
            new Finish("white-pla", FinishTarget.Body, new Color(0.90f, 0.90f, 0.87f), 0.30f, 0f, null, "White PLA", "Oq PLA", "Белый PLA"),
            new Finish("orange-pla", FinishTarget.Body, new Color(0.95f, 0.45f, 0.10f), 0.35f, 0f, null, "Orange PLA", "Toʻq sariq PLA", "Оранжевый PLA"),
            new Finish("black-pla", FinishTarget.Body, new Color(0.07f, 0.07f, 0.08f), 0.35f, 0f, null, "Black PLA", "Qora PLA", "Чёрный PLA"),
            new Finish("carbon-fibre", FinishTarget.Body, new Color(0.05f, 0.05f, 0.06f), 0.90f, 0.35f, CarbonMetalPack, "Carbon fibre", "Karbon tola", "Карбон"),
            new Finish("aluminium", FinishTarget.Body, new Color(0.78f, 0.79f, 0.81f), 0.60f, 1f, CarbonMetalPack, "Aluminium", "Alyuminiy", "Алюминий"),
            new Finish("anodized-red", FinishTarget.Body, new Color(0.62f, 0.05f, 0.08f), 0.75f, 1f, CarbonMetalPack, "Anodized red", "Anodlangan qizil", "Красный анодированный"),
            new Finish("neon-green", FinishTarget.Body, new Color(0.35f, 1.00f, 0.25f), 0.55f, 0f, NeonPack, "Neon green", "Neon yashil", "Неоновый зелёный"),
            new Finish("neon-pink", FinishTarget.Body, new Color(1.00f, 0.22f, 0.65f), 0.55f, 0f, NeonPack, "Neon pink", "Neon pushti", "Неоновый розовый"),

            new Finish("yellow-hubs", FinishTarget.Wheels, new Color(0.98f, 0.80f, 0.10f), 0.30f, 0f, null, "Yellow hubs", "Sariq gupchaklar", "Жёлтые диски"),
            new Finish("black-hubs", FinishTarget.Wheels, new Color(0.08f, 0.08f, 0.09f), 0.30f, 0f, null, "Black hubs", "Qora gupchaklar", "Чёрные диски"),
            new Finish("white-hubs", FinishTarget.Wheels, new Color(0.92f, 0.92f, 0.90f), 0.30f, 0f, null, "White hubs", "Oq gupchaklar", "Белые диски"),
            new Finish("chrome-hubs", FinishTarget.Wheels, new Color(0.85f, 0.86f, 0.88f), 0.95f, 1f, CarbonMetalPack, "Chrome hubs", "Xrom gupchaklar", "Хромированные диски"),
            new Finish("neon-hubs", FinishTarget.Wheels, new Color(0.20f, 0.95f, 1.00f), 0.55f, 0f, NeonPack, "Neon cyan hubs", "Neon moviy gupchaklar", "Неоновые голубые диски"),
        };

        public static Finish Get(string id, FinishTarget target)
        {
            foreach (var finish in All) if (finish.Id == id && finish.Target == target) return finish;
            foreach (var finish in All) if (finish.Target == target) return finish;
            return All[0];
        }

        /// <summary>No Steam in the prototype, so no pack is owned; a player can still try every finish.</summary>
        public static bool Owns(string? pack) => pack == null;
    }
}
