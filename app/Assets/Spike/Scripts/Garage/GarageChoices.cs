using CoreEngine.Sim.Design;
using CoreEngine.Spike.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// Choosing where to drive and how the game looks (research R5):
    /// <list type="bullet">
    /// <item>the arena as a picture chip beside START, its destination, instead of a dropdown;</item>
    /// <item>the arena picker: a card per arena with a picture of it, a one-line goal, what the chosen robot needs
    /// for it and how hard it is, easiest first; the arenas still to come share one card;</item>
    /// <item>Settings: the language, each written in its own language, and the size of the interface; the first
    /// launch asks for the language once.</item>
    /// </list>
    /// </summary>
    public sealed partial class GarageSpike
    {
        Button arenaChip = null!;
        Image arenaChipImage = null!;
        Label arenaChipName = null!;

        static readonly string[] LanguageNames = { "English", "Oʻzbekcha", "Русский" };

        VisualElement BuildArenaChip()
        {
            arenaChip = new Button(OpenArenaPicker) { focusable = false };
            arenaChip.AddToClassList("arena-chip");
            arenaChipImage = new Image { scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            arenaChipImage.AddToClassList("arena-chip-image");
            arenaChip.Add(arenaChipImage);
            var words = Layout("arena-chip-text");
            words.pickingMode = PickingMode.Ignore;
            arenaChipName = Classed(new Label { pickingMode = PickingMode.Ignore }, "arena-chip-name");
            words.Add(arenaChipName);
            words.Add(Classed(Localized(new Label { pickingMode = PickingMode.Ignore }, "arena.change"), "arena-chip-change"));
            arenaChip.Add(words);
            ShowArenaChip();
            return arenaChip;
        }

        void ShowArenaChip()
        {
            if (arenaChip == null) return;
            arenaChipName.text = Tr(ArenaKeys[0]);
            arenaChipImage.image = arenaPicture;
            arenaChipImage.style.display = arenaPicture != null ? DisplayStyle.Flex : DisplayStyle.None;
            arenaChip.tooltip = Tr("arena.pick");
        }

        void OpenArenaPicker() => ShowOverlay(true, () =>
        {
            var header = OverlayHeader(Tr("arena.pick"));
            header.Add(SmallButton("page.close", CloseOverlay));
            overlayPanel.Add(header);
            var cards = Layout("arena-cards");

            var field = new Button(() =>
            {
                GarageState.Arena = 0;
                CloseOverlay();
                ShowArenaChip();
            }) { focusable = false };
            field.AddToClassList("arena-card");
            field.AddToClassList("arena-card--chosen");
            var picture = new Image { image = arenaPicture, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            picture.AddToClassList("arena-card-image");
            field.Add(picture);
            var text = Layout("arena-card-text");
            text.pickingMode = PickingMode.Ignore;
            Label Line(string words, string className)
            {
                var label = Classed(new Label(words) { pickingMode = PickingMode.Ignore }, className);
                text.Add(label);
                return label;
            }
            Line(Tr("arena.obstacles"), "arena-card-title");
            Line(Tr("arena.obstacles.goal"), "arena-card-goal");
            Line(Tr("arena.obstacles.size"), "arena-card-fact");
            // What the chosen robot needs here, checked against it: a symbol and words, never colour alone.
            bool sonar = Robot.Design.Count(PartCatalog.HcSr04) > 0;
            Line((sonar ? "✓ " : "✗ ") + Tr("arena.needsSonar"), sonar ? "arena-card-ok" : "arena-card-missing");
            Line(Tr("arena.easy") + "  ●○○", "arena-card-level");
            field.Add(text);
            cards.Add(field);

            var later = Layout("arena-card");
            later.AddToClassList("arena-card--later");
            var soon = Layout("arena-card-soon");
            soon.Add(Classed(new IconView(Icon.ViewArena), "arena-soon-icon"));
            later.Add(soon);
            var laterText = Layout("arena-card-text");
            laterText.Add(Classed(new Label(Tr("arena.soon")), "arena-card-title"));
            laterText.Add(Classed(new Label(Tr("arena.soon.list")), "arena-card-goal"));
            later.Add(laterText);
            cards.Add(later);
            overlayPanel.Add(cards);
        });

        /// <summary>Settings: the language and the size of the interface.</summary>
        void OpenSettings() => ShowOverlay(true, () =>
        {
            var header = OverlayHeader(Tr("settings.title"));
            header.Add(SmallButton("page.close", CloseOverlay));
            overlayPanel.Add(header);
            overlayPanel.Add(Classed(new Label(Tr("settings.language")), "settings-heading"));
            overlayPanel.Add(LanguageRow(closeAfter: false));
            overlayPanel.Add(Classed(new Label(Tr("settings.size")), "settings-heading"));
            var sizes = Layout("settings-row");
            var panel = GetComponent<UIDocument>().panelSettings;
            foreach (float scale in Preferences.Scales)
            {
                float chosen = scale;
                var button = new Button(() =>
                {
                    Preferences.SetUiScale(chosen, panel);
                    renderOverlay?.Invoke();
                }) { text = $"{scale * 100:F0} %", focusable = false };
                button.AddToClassList("settings-choice");
                button.EnableInClassList("settings-choice--active", Mathf.Approximately(scale, Preferences.UiScale));
                sizes.Add(button);
            }
            overlayPanel.Add(sizes);
            overlayPanel.Add(Classed(new Label(Tr("settings.sizeNote")), "info-text"));
        });

        /// <summary>The three languages, each in its own words; the chosen one lit.</summary>
        VisualElement LanguageRow(bool closeAfter)
        {
            var row = Layout("settings-row");
            for (int i = 0; i < LanguageNames.Length; i++)
            {
                int language = i;
                var button = new Button(() =>
                {
                    Preferences.SetLanguage(language);
                    if (closeAfter) CloseOverlay();
                    else renderOverlay?.Invoke();
                }) { text = LanguageNames[i], focusable = false };
                button.AddToClassList("settings-choice");
                button.AddToClassList("settings-choice--language");
                button.EnableInClassList("settings-choice--active", i == SpikeStrings.Language);
                row.Add(button);
            }
            return row;
        }

        /// <summary>The first launch: which language, once, in all three at once.</summary>
        void AskLanguage() => ShowOverlay(true, () =>
        {
            var header = OverlayHeader("Language · Til · Язык");
            overlayPanel.Add(header);
            overlayPanel.Add(LanguageRow(closeAfter: true));
            overlayPanel.Add(Classed(new Label("You can change it later in Settings · Keyinroq Sozlamalarda oʻzgartirishingiz mumkin · Позже его можно сменить в Настройках"), "info-text"));
        });
    }
}
