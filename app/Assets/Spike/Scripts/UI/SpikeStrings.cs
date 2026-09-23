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
            // ---- Garage (ADR-0009) ----
            ["nav.garage"] = new[] { "Garage", "Garaj", "Гараж" },
            ["nav.notebook"] = new[] { "Notebook", "Daftar", "Блокнот" },
            ["nav.shop"] = new[] { "Shop", "Doʻkon", "Магазин" },
            ["nav.workshop"] = new[] { "Workshop", "Ustaxona", "Мастерская" },
            ["nav.back"] = new[] { "◀ Garage", "◀ Garaj", "◀ Гараж" },
            ["start"] = new[] { "START", "BOSHLASH", "СТАРТ" },
            ["arena"] = new[] { "Arena", "Arena", "Арена" },
            ["arena.obstacles"] = new[] { "Obstacle field", "Toʻsiqlar maydoni", "Поле препятствий" },
            ["arena.line"] = new[] { "Line track (Phase 2)", "Chiziq yoʻli (2-bosqich)", "Трасса по линии (фаза 2)" },
            ["arena.maze"] = new[] { "Maze (Phase 2)", "Labirint (2-bosqich)", "Лабиринт (фаза 2)" },
            ["arena.sumo"] = new[] { "Sumo ring (Phase 2)", "Sumo ringi (2-bosqich)", "Ринг для сумо (фаза 2)" },
            ["arena.later"] = new[] { "This arena comes in Phase 2. The obstacle field is ready.", "Bu arena 2-bosqichda qoʻshiladi. Toʻsiqlar maydoni tayyor.", "Эта арена появится в фазе 2. Поле препятствий уже готово." },
            ["garage.hint"] = new[] { "Drag to rotate · mouse wheel to zoom", "Aylantirish uchun torting · yaqinlashtirish uchun sichqoncha gʻildiragi", "Тяните, чтобы вращать · колесо мыши — масштаб" },
            ["act.build"] = new[] { "Build", "Yigʻish", "Сборка" },
            ["act.wire"] = new[] { "Wire", "Simlash", "Провода" },
            ["act.code"] = new[] { "Code", "Kod", "Код" },
            ["act.body"] = new[] { "Body", "Korpus", "Корпус" },
            ["act.customize"] = new[] { "Customize", "Bezash", "Оформление" },
            ["act.repair"] = new[] { "Check & repair", "Tekshirish va taʼmirlash", "Проверка и ремонт" },
            ["act.buildInfo"] = new[] { "Place real parts from the Parts Bin on the chassis and the breadboard. Comes in Phase 1.", "Qismlar qutisidan haqiqiy qismlarni shassi va maketlash platasiga joylashtiring. 1-bosqichda keladi.", "Размещайте настоящие детали из ящика деталей на шасси и макетной плате. Появится в фазе 1." },
            ["act.wireInfo"] = new[] { "Connect pins with jumper wires, like on a real breadboard. Comes in Phase 1.", "Pinlarni haqiqiy maketlash platasidagidek simlar bilan ulang. 1-bosqichda keladi.", "Соединяйте выводы проводами, как на настоящей макетной плате. Появится в фазе 1." },
            ["act.bodyInfo"] = new[] { "Design the chassis from shapes and holes, then export it for a 3D printer. The holed deck of the second robot was made with this kernel (Manifold). Comes in Phase 2.", "Shassini shakllar va teshiklardan loyihalang, soʻng 3D printer uchun eksport qiling. Ikkinchi robotning teshikli paneli shu yadro (Manifold) bilan yasalgan. 2-bosqichda keladi.", "Проектируйте шасси из фигур и отверстий и экспортируйте его для 3D-принтера. Дырчатая панель второго робота сделана этим ядром (Manifold). Появится в фазе 2." },
            ["card.board"] = new[] { "Board", "Plata", "Плата" },
            ["card.noBoard"] = new[] { "none", "yoʻq", "нет" },
            ["card.sketch"] = new[] { "Sketch", "Sketch", "Скетч" },
            ["card.compiled"] = new[] { "compiled, {0} bytes", "kompilyatsiya qilingan, {0} bayt", "скомпилирован, {0} байт" },
            ["card.noSketch"] = new[] { "no sketch yet", "hali sketch yoʻq", "скетча пока нет" },
            ["card.parts"] = new[] { "Parts", "Qismlar", "Детали" },
            ["card.mass"] = new[] { "Mass", "Massa", "Масса" },
            ["card.battery"] = new[] { "Battery", "Batareya", "Батарея" },
            ["card.ready"] = new[] { "✓ Ready to start", "✓ Boshlashga tayyor", "✓ Готов к старту" },
            ["warn.noBoard"] = new[] { "⚠ No board yet: add one in Build", "⚠ Plata yoʻq: Yigʻish boʻlimida qoʻshing", "⚠ Платы нет: добавьте её в сборке" },
            ["warn.batteryLow"] = new[] { "⚠ Battery low: {0:F0} %", "⚠ Batareya kam: {0:F0} %", "⚠ Батарея почти разряжена: {0:F0} %" },
            ["warn.batteryEmpty"] = new[] { "⚠ Battery empty", "⚠ Batareya tugagan", "⚠ Батарея разряжена" },
            ["warn.motorBurnt"] = new[] { "⚠ {0} motor burnt out", "⚠ {0} motor kuyib qolgan", "⚠ {0} мотор сгорел" },
            ["warn.notUploaded"] = new[] { "⚠ Code changed, not uploaded yet", "⚠ Kod oʻzgargan, hali yuklanmagan", "⚠ Код изменён, но не загружен" },
            ["warn.trying"] = new[] { "★ Trying a pack finish", "★ Toʻplam bezagi sinab koʻrilmoqda", "★ Примерка оформления из набора" },
            ["side.left"] = new[] { "Left", "Chap", "Левый" },
            ["side.right"] = new[] { "Right", "Oʻng", "Правый" },
            ["bar.new"] = new[] { "+ New robot", "+ Yangi robot", "+ Новый робот" },
            ["bar.empty"] = new[] { "empty chassis", "boʻsh shassi", "пустое шасси" },
            ["start.needParts"] = new[] { "This robot has no board or motors yet. Add them in Build (Phase 1).", "Bu robotda hali plata va motorlar yoʻq. Ularni Yigʻish boʻlimida qoʻshing (1-bosqich).", "У этого робота пока нет платы и моторов. Добавьте их в сборке (фаза 1)." },
            ["cust.body"] = new[] { "Body finish", "Korpus bezagi", "Отделка корпуса" },
            ["cust.wheels"] = new[] { "Wheels", "Gʻildiraklar", "Колёса" },
            ["cust.free"] = new[] { "Free", "Bepul", "Бесплатно" },
            ["cust.trying"] = new[] { "Trying {0} from the {1}. You can test it everywhere; only owned finishes are saved.", "{1} dan {0} sinab koʻrilmoqda. Uni hamma joyda sinash mumkin; faqat sotib olingan bezaklar saqlanadi.", "Примерка: {0} из набора «{1}». Её можно проверить везде; сохраняется только купленное оформление." },
            ["cust.stop"] = new[] { "Stop trying", "Sinashni toʻxtatish", "Отменить примерку" },
            ["pack.carbonMetal"] = new[] { "Carbon & Metal pack", "Karbon va metall toʻplami", "Карбон и металл" },
            ["pack.neon"] = new[] { "Neon pack", "Neon toʻplami", "Неон" },
            ["rep.readiness"] = new[] { "Readiness", "Tayyorlik", "Готовность" },
            ["rep.parts"] = new[] { "Parts", "Qismlar", "Детали" },
            ["rep.power"] = new[] { "Power: 4×AA, {0:F2} V", "Quvvat: 4×AA, {0:F2} V", "Питание: 4×AA, {0:F2} В" },
            ["rep.sketch"] = new[] { "Sketch compiled for Arduino Uno, {0} bytes", "Sketch Arduino Uno uchun kompilyatsiya qilingan, {0} bayt", "Скетч скомпилирован для Arduino Uno, {0} байт" },
            ["rep.ok"] = new[] { "OK", "Soz", "Исправен" },
            ["rep.burnt"] = new[] { "Burnt out", "Kuygan", "Сгорел" },
            ["rep.motor"] = new[] { "{0} TT motor: {1:F0} °C now, peak {2:F0} °C", "{0} TT motor: hozir {1:F0} °C, eng yuqori {2:F0} °C", "{0} мотор TT: сейчас {1:F0} °C, максимум {2:F0} °C" },
            ["rep.battery"] = new[] { "Batteries 4×AA: {0:F1} % charge", "Batareyalar 4×AA: {0:F1} % zaryad", "Батарейки 4×AA: заряд {0:F1} %" },
            ["rep.replace"] = new[] { "Replace (free)", "Almashtirish (bepul)", "Заменить (бесплатно)" },
            ["rep.replaceBatteries"] = new[] { "Replace batteries (free)", "Batareyalarni almashtirish (bepul)", "Заменить батарейки (бесплатно)" },
            ["rep.why"] = new[] { "Why it broke", "Nega buzildi", "Почему сломалось" },
            ["rep.whyF18"] = new[] { "The motor was stalled. At 1.05 A its 4 Ω winding turns 4.4 W into heat, and above 120 °C the insulation melts and the winding breaks. Real robots avoid this by noticing when they are stuck and stopping the motors.", "Motor tiqilib qolgan edi. 1,05 A da uning 4 Ω chulgʻami 4,4 W issiqlik chiqaradi, 120 °C dan yuqorida izolyatsiya erib, chulgʻam uziladi. Haqiqiy robotlar tiqilib qolganini sezib, motorlarni toʻxtatadi.", "Мотор был заблокирован. При 1,05 А его обмотка 4 Ом выделяет 4,4 Вт тепла, а выше 120 °C изоляция плавится и обмотка обрывается. Настоящие роботы замечают, что застряли, и останавливают моторы." },
            ["rep.noParts"] = new[] { "Only the chassis plate so far.", "Hozircha faqat shassi paneli.", "Пока только пластина шасси." },
            ["code.upload"] = new[] { "Upload", "Yuklash", "Загрузить" },
            ["code.close"] = new[] { "Close", "Yopish", "Закрыть" },
            ["code.compiling"] = new[] { "Compiling with arduino-cli…", "arduino-cli bilan kompilyatsiya…", "Компиляция в arduino-cli…" },
            ["code.ok"] = new[] { "Uploaded: {0} bytes, compiled in {1:F1} s. START runs the new code.", "Yuklandi: {0} bayt, {1:F1} s da kompilyatsiya qilindi. BOSHLASH yangi kodni ishga tushiradi.", "Загружено: {0} байт, компиляция {1:F1} с. СТАРТ запустит новый код." },
            ["code.failed"] = new[] { "Not uploaded: {0} errors. Click an error to jump to its line.", "Yuklanmadi: {0} ta xato. Qatoriga oʻtish uchun xatoni bosing.", "Не загружено: ошибок {0}. Нажмите на ошибку, чтобы перейти к строке." },
            ["code.noToolchain"] = new[] { "Arduino toolchain not found: run tools/fetch-toolchain.ps1 once.", "Arduino vositalari topilmadi: tools/fetch-toolchain.ps1 ni bir marta ishga tushiring.", "Инструменты Arduino не найдены: один раз запустите tools/fetch-toolchain.ps1." },
            ["page.notebookInfo"] = new[] { "Datasheet cards for every part, error help and \"why it broke\" cards, in English, Uzbek and Russian. Comes in Phase 1.", "Har bir qism uchun maʼlumot kartalari, xato yordami va \"nega buzildi\" kartalari: ingliz, oʻzbek va rus tillarida. 1-bosqichda keladi.", "Карточки с даташитами всех деталей, помощь по ошибкам и карточки «почему сломалось» на английском, узбекском и русском. Появится в фазе 1." },
            ["page.workshopInfo"] = new[] { "Share your robots, bodies and arenas on the Steam Workshop and try other players' robots. Comes in Phase 3.", "Robotlaringiz, korpuslaringiz va arenalaringizni Steam Workshop da ulashing va boshqa oʻyinchilarning robotlarini sinab koʻring. 3-bosqichda keladi.", "Делитесь роботами, корпусами и аренами в Мастерской Steam и пробуйте роботов других игроков. Появится в фазе 3." },
            ["page.shopInfo"] = new[] { "Paid packs planned for release (ADR-0007). Not on sale yet:", "Chiqishga rejalashtirilgan pullik toʻplamlar (ADR-0007). Hali sotuvda emas:", "Платные наборы, запланированные к выходу (ADR-0007). Пока не продаются:" },
            ["shop.mega"] = new[] { "Mega 2560 Pack: a real Arduino Mega board with its own emulated ATmega2560", "Mega 2560 toʻplami: oʻz emulyatsiya qilingan ATmega2560 si bilan haqiqiy Arduino Mega platasi", "Набор Mega 2560: настоящая плата Arduino Mega с эмуляцией ATmega2560" },
            ["shop.carbon"] = new[] { "Carbon & Metal pack: carbon fibre, aluminium and anodized finishes, chrome hubs", "Karbon va metall toʻplami: karbon tola, alyuminiy va anodlangan bezaklar, xrom gupchaklar", "Набор «Карбон и металл»: карбон, алюминий, анодированные отделки, хромированные диски" },
            ["shop.neon"] = new[] { "Neon pack: neon finishes and hubs", "Neon toʻplami: neon bezaklar va gupchaklar", "Набор «Неон»: неоновые отделки и диски" },
            ["shop.bundle"] = new[] { "Supporter bundle: all three packs, about 30 % off", "Qoʻllab-quvvatlovchi toʻplami: uchala toʻplam, taxminan 30 % chegirma", "Набор поддержки: все три набора со скидкой около 30 %" },
            ["shop.try"] = new[] { "Every pack finish can be tried in Customize.", "Har bir toʻplam bezagini Bezash boʻlimida sinab koʻrish mumkin.", "Любую отделку из наборов можно примерить в оформлении." },
            ["page.close"] = new[] { "Close", "Yopish", "Закрыть" },
            ["settings.info"] = new[] { "Settings (graphics, UI scale, audio) come in Phase 1. The language buttons are next to this one.", "Sozlamalar (grafika, interfeys oʻlchami, ovoz) 1-bosqichda keladi. Til tugmalari shu yerda.", "Настройки (графика, масштаб интерфейса, звук) появятся в фазе 1. Кнопки языка рядом." },
            ["event.burnt"] = new[] { "{0} motor burnt out (F18): {1:F0} °C", "{0} motor kuyib qoldi (F18): {1:F0} °C", "{0} мотор сгорел (F18): {1:F0} °C" },
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
