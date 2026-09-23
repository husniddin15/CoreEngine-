using System;
using System.Collections.Generic;
using System.Text;

namespace CoreEngine.Spike.UI
{
    /// <summary>
    /// String table for the Phase 0.6 localisation check (docs/10 §5): English, Uzbek (Latin script,
    /// with oʻ/gʻ written with U+02BB and the tutuq belgisi ʼ with U+02BC) and Russian.
    /// The Uzbek and Russian texts are AI drafts: the owner reviews them (docs/13 R22, R23).
    /// Compiler output and code are never translated.
    /// </summary>
    public static class SpikeStrings
    {
        public static readonly string[] LanguageCodes = { "en", "uz", "ru" };
        public static readonly string[] LanguageButtons = { "EN", "OʻZ", "RU" };

        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            ["lang.name"] = new[] { "English", "Oʻzbekcha", "Русский" },
            ["mode.build"] = new[] { "Build", "Yigʻish", "Сборка" },
            ["mode.wire"] = new[] { "Wire", "Simlash", "Провода" },
            ["mode.code"] = new[] { "Code", "Kod", "Код" },
            ["mode.test"] = new[] { "Test", "Sinov", "Тест" },
            ["panel.code"] = new[] { "Code", "Kod", "Код" },
            ["panel.inspector"] = new[] { "Inspector", "Xususiyatlar", "Свойства" },
            ["panel.console"] = new[] { "Console", "Konsol", "Консоль" },
            ["panel.serial"] = new[] { "Serial Monitor", "Port monitori", "Монитор порта" },
            ["panel.events"] = new[] { "Event log", "Hodisalar jurnali", "Журнал событий" },
            ["dock.empty"] = new[] { "Drag a panel tab here", "Panel yorligʻini shu yerga torting", "Перетащите сюда вкладку панели" },
            ["code.status"] = new[] { "Ln {0}, Col {1} · {2} lines", "{0}-qator, {1}-ustun · {2} qator", "Стр. {0}, стлб. {1} · строк: {2}" },
            ["insp.sensor"] = new[] { "HC-SR04 ultrasonic sensor", "HC-SR04 ultratovush sensori", "Ультразвуковой датчик HC-SR04" },
            ["insp.distance"] = new[] { "Distance", "Masofa", "Расстояние" },
            ["insp.measurements"] = new[] { "Measurements", "Oʻlchovlar soni", "Число измерений" },
            ["insp.noEcho"] = new[] { "no echo", "aks-sado yoʻq", "нет эха" },
            ["insp.driver"] = new[] { "L298N motor driver", "L298N motor drayveri", "Драйвер моторов L298N" },
            ["insp.supply"] = new[] { "Supply voltage", "Taʼminot kuchlanishi", "Напряжение питания" },
            ["insp.leftMotor"] = new[] { "Left motor", "Chap motor", "Левый мотор" },
            ["insp.rightMotor"] = new[] { "Right motor", "Oʻng motor", "Правый мотор" },
            ["insp.wheelSpeed"] = new[] { "Wheel speed", "Gʻildirak tezligi", "Скорость колёс" },
            ["insp.board"] = new[] { "Arduino Uno (emulated)", "Arduino Uno (emulyatsiya)", "Arduino Uno (эмуляция)" },
            ["insp.emulatedTime"] = new[] { "Emulated time", "Emulyatsiya vaqti", "Время эмуляции" },
            ["insp.emulatorLoad"] = new[] { "Emulator load", "Emulyator yuklamasi", "Нагрузка эмулятора" },
            ["insp.perStep"] = new[] { "{0:F2} ms per 10 ms step", "10 ms qadamga {0:F2} ms", "{0:F2} мс на шаг 10 мс" },
            ["unit.cm"] = new[] { "cm", "sm", "см" },
            ["unit.V"] = new[] { "V", "V", "В" },
            ["unit.A"] = new[] { "A", "A", "А" },
            ["unit.s"] = new[] { "s", "s", "с" },
            ["unit.rads"] = new[] { "rad/s", "rad/s", "рад/с" },
            ["serial.autoscroll"] = new[] { "Autoscroll", "Avtoaylantirish", "Автопрокрутка" },
            ["serial.baud"] = new[] { "{0} baud", "{0} bod", "{0} бод" },
            ["console.example"] = new[] { "Example of an error card:", "Xato kartasi namunasi:", "Пример карточки ошибки:" },
            ["console.helpTitle"] = new[] { "What this error means", "Bu xato nimani anglatadi", "Что означает эта ошибка" },
            ["console.helpText"] = new[]
            {
                "A statement is missing its semicolon (;). Add ; at the end of the line before the marked one.",
                "Buyruq oxirida nuqtali vergul (;) yetishmayapti. Belgilangan qatordan oldingi qator oxiriga ; qoʻying.",
                "В конце команды не хватает точки с запятой (;). Поставьте ; в конце строки перед отмеченной.",
            },
            ["event.stall"] = new[] { "{0} motor stalled: {1:F2} A", "{0} motor toʻxtab qoldi: {1:F2} A", "{0} мотор остановлен нагрузкой: {1:F2} А" },
            ["event.left"] = new[] { "Left", "Chap", "Левый" },
            ["event.right"] = new[] { "Right", "Oʻng", "Правый" },
            ["ui.hint"] = new[]
            {
                "F1: hide panels · C: camera · drag a tab to move its panel",
                "F1: panellarni yashirish · C: kamera · panelni koʻchirish uchun yorligʻini torting",
                "F1: скрыть панели · C: камера · перетащите вкладку, чтобы переместить панель",
            },
        };

        public static int Language { get; private set; }
        public static event Action? LanguageChanged;

        public static void SetLanguage(int index)
        {
            Language = Math.Max(0, Math.Min(index, LanguageCodes.Length - 1));
            LanguageChanged?.Invoke();
        }

        public static string Get(string key) =>
            Table.TryGetValue(key, out var texts) ? texts[Language] : key;

        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        /// <summary>Every string of one language, for the font coverage check.</summary>
        public static string AllText(int language)
        {
            var sb = new StringBuilder();
            foreach (var texts in Table.Values) sb.Append(texts[language]);
            return sb.ToString();
        }
    }
}
