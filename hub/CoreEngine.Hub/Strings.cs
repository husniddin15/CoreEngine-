using System.ComponentModel;
using System.Globalization;

namespace CoreEngine.Hub;

/// <summary>
/// The Hub's words in the game's three languages (docs/10 §5): English, Uzbek (Latin, with ʻ U+02BB and ʼ U+02BC)
/// and Russian. Bound in the window as {Binding T[key]}; a change of language updates every one at once.
/// </summary>
public sealed class Strings : INotifyPropertyChanged
{
    public static readonly string[] Codes = { "en", "uz", "ru" };
    public static readonly string[] Names = { "English", "Oʻzbekcha", "Русский" };

    static readonly Dictionary<string, string[]> Table = new()
    {
        ["hub"] = new[] { "CoreEngine Hub", "CoreEngine Hub", "CoreEngine Hub" },
        ["tagline"] = new[] { "Build robots, wire real parts, run real Arduino code", "Robot yasang, haqiqiy qismlarni ulang, haqiqiy Arduino kodini ishga tushiring", "Собирайте роботов, подключайте настоящие детали, запускайте настоящий код Arduino" },
        ["btn.download"] = new[] { "Download", "Yuklab olish", "Скачать" },
        ["btn.update"] = new[] { "Update", "Yangilash", "Обновить" },
        ["btn.start"] = new[] { "Start", "Boshlash", "Играть" },
        ["btn.pause"] = new[] { "Pause", "Pauza", "Пауза" },
        ["btn.resume"] = new[] { "Resume", "Davom ettirish", "Продолжить" },
        ["btn.retry"] = new[] { "Retry", "Qayta urinish", "Повторить" },
        ["btn.running"] = new[] { "Running", "Ishlamoqda", "Запущена" },
        ["btn.checking"] = new[] { "Checking…", "Tekshirilmoqda…", "Проверка…" },
        ["btn.installing"] = new[] { "Installing…", "Oʻrnatilmoqda…", "Установка…" },
        ["status.upToDate"] = new[] { "Version {0} · up to date", "{0} versiya · eng soʻnggisi", "Версия {0} · актуальна" },
        ["status.update"] = new[] { "Version {0} is ready · {1} to download", "{0} versiya tayyor · {1} yuklab olinadi", "Готова версия {0} · скачать {1}" },
        ["status.download"] = new[] { "Version {0} · {1} to download · {2} on disk", "{0} versiya · {1} yuklab olinadi · diskda {2}", "Версия {0} · скачать {1} · на диске {2}" },
        ["status.offline"] = new[] { "The download server cannot be reached. Check the internet and retry.", "Yuklab olish serveriga ulanib boʻlmadi. Internetni tekshirib, qayta urining.", "Сервер загрузки недоступен. Проверьте интернет и повторите." },
        ["status.offlineInstalled"] = new[] { "Version {0} · offline: updates cannot be checked", "{0} versiya · oflayn: yangilanishlarni tekshirib boʻlmaydi", "Версия {0} · нет сети: обновления не проверить" },
        ["status.noSource"] = new[] { "No download server is set yet (Settings)", "Yuklab olish serveri hali sozlanmagan (Sozlamalar)", "Сервер загрузки ещё не задан (Настройки)" },
        ["status.untrusted"] = new[] { "This download is not signed by CoreEngine, so it will not be installed.", "Bu yuklama CoreEngine imzosiga ega emas, shuning uchun oʻrnatilmaydi.", "Эта загрузка не подписана CoreEngine, поэтому не будет установлена." },
        ["status.preparing"] = new[] { "Preparing the Arduino compiler (first time only)…", "Arduino kompilyatori tayyorlanmoqda (faqat birinchi marta)…", "Подготовка компилятора Arduino (только в первый раз)…" },
        ["status.running"] = new[] { "CoreEngine is running", "CoreEngine ishlamoqda", "CoreEngine запущена" },
        ["status.paused"] = new[] { "Paused · {0} of {1}", "Pauza · {0} / {1}", "Приостановлено · {0} из {1}" },
        ["progress.downloading"] = new[] { "Downloading", "Yuklab olinmoqda", "Загрузка" },
        ["progress.installing"] = new[] { "Installing", "Oʻrnatilmoqda", "Установка" },
        ["progress.checking"] = new[] { "Checking files", "Fayllar tekshirilmoqda", "Проверка файлов" },
        ["progress.left"] = new[] { "{0} left", "{0} qoldi", "осталось {0}" },
        ["news"] = new[] { "What's new", "Yangiliklar", "Что нового" },
        ["news.none"] = new[] { "The notes of the newest version appear here.", "Eng yangi versiya haqidagi yozuvlar shu yerda chiqadi.", "Здесь появятся заметки о новой версии." },
        ["menu"] = new[] { "Game settings", "Oʻyin sozlamalari", "Настройки игры" },
        ["menu.repair"] = new[] { "Repair game files", "Oʻyin fayllarini tuzatish", "Восстановить файлы игры" },
        ["menu.folder"] = new[] { "Open game folder", "Oʻyin papkasini ochish", "Открыть папку игры" },
        ["menu.locate"] = new[] { "Find the game on this PC", "Kompyuterdan oʻyinni topish", "Найти игру на этом ПК" },
        ["menu.uninstall"] = new[] { "Uninstall game", "Oʻyinni oʻchirish", "Удалить игру" },
        ["install.title"] = new[] { "Install CoreEngine", "CoreEngineʼni oʻrnatish", "Установка CoreEngine" },
        ["install.folder"] = new[] { "Install to", "Oʻrnatish joyi", "Папка установки" },
        ["install.change"] = new[] { "Change…", "Oʻzgartirish…", "Изменить…" },
        ["install.space"] = new[] { "Needs {0} · {1} free on {2}", "{0} kerak · {2} diskida {1} boʻsh", "Нужно {0} · свободно {1} на {2}" },
        ["install.noSpace"] = new[] { "Not enough space: needs {0}, {1} free on {2}", "Joy yetmaydi: {0} kerak, {2} diskida {1} boʻsh", "Не хватает места: нужно {0}, свободно {1} на {2}" },
        ["install.shortcut"] = new[] { "Create a desktop shortcut", "Ish stolida yorliq yaratish", "Создать ярлык на рабочем столе" },
        ["install.go"] = new[] { "Install", "Oʻrnatish", "Установить" },
        ["cancel"] = new[] { "Cancel", "Bekor qilish", "Отмена" },
        ["ok"] = new[] { "OK", "OK", "OK" },
        ["uninstall.title"] = new[] { "Uninstall CoreEngine?", "CoreEngine oʻchirilsinmi?", "Удалить CoreEngine?" },
        ["uninstall.text"] = new[] { "The game's files are deleted from {0}. Your robots and settings stay on this PC.", "Oʻyin fayllari {0} dan oʻchiriladi. Robotlaringiz va sozlamalaringiz kompyuterda qoladi.", "Файлы игры будут удалены из {0}. Ваши роботы и настройки останутся на этом ПК." },
        ["uninstall.go"] = new[] { "Uninstall", "Oʻchirish", "Удалить" },
        ["repair.title"] = new[] { "Repair finished", "Tuzatish tugadi", "Восстановление завершено" },
        ["repair.fixed"] = new[] { "{0} damaged or missing files were downloaded again.", "{0} ta buzilgan yoki yoʻq fayl qayta yuklab olindi.", "Заново скачано повреждённых или отсутствующих файлов: {0}." },
        ["repair.fine"] = new[] { "Every file is as it should be.", "Barcha fayllar joyida.", "Все файлы в порядке." },
        ["locate.notFound"] = new[] { "There is no CoreEngine in that folder.", "Bu papkada CoreEngine yoʻq.", "В этой папке нет CoreEngine." },
        ["error.title"] = new[] { "Something went wrong", "Xatolik yuz berdi", "Что-то пошло не так" },
        ["settings"] = new[] { "Settings", "Sozlamalar", "Настройки" },
        ["settings.language"] = new[] { "Language", "Til", "Язык" },
        ["settings.speed"] = new[] { "Download speed", "Yuklab olish tezligi", "Скорость загрузки" },
        ["settings.noLimit"] = new[] { "No limit", "Cheklovsiz", "Без ограничений" },
        ["settings.afterStart"] = new[] { "When the game starts", "Oʻyin boshlanganda", "Когда игра запущена" },
        ["settings.minimize"] = new[] { "Minimise the Hub", "Hubni yigʻish", "Свернуть Hub" },
        ["settings.close"] = new[] { "Close the Hub", "Hubni yopish", "Закрыть Hub" },
        ["settings.keep"] = new[] { "Keep the Hub open", "Hubni ochiq qoldirish", "Оставить Hub открытым" },
        ["settings.source"] = new[] { "Download server", "Yuklab olish serveri", "Сервер загрузки" },
        ["settings.sourceNote"] = new[] { "A web address, or a folder with a release in it", "Veb-manzil yoki ichida reliz boʻlgan papka", "Веб-адрес или папка с релизом" },
        ["tip.settings"] = new[] { "Settings", "Sozlamalar", "Настройки" },
        ["tip.minimize"] = new[] { "Minimise", "Yigʻish", "Свернуть" },
        ["tip.close"] = new[] { "Close", "Yopish", "Закрыть" },
        ["unit.mb"] = new[] { "MB", "MB", "МБ" },
        ["unit.gb"] = new[] { "GB", "GB", "ГБ" },
        ["unit.kbs"] = new[] { "KB/s", "KB/s", "КБ/с" },
        ["unit.mbs"] = new[] { "MB/s", "MB/s", "МБ/с" },
        ["unit.min"] = new[] { "{0} min", "{0} daq", "{0} мин" },
        ["unit.s"] = new[] { "{0} s", "{0} s", "{0} с" },
    };

    int language;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Language
    {
        get => language;
        set
        {
            language = Math.Clamp(value, 0, Codes.Length - 1);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        }
    }

    public string Code => Codes[language];

    public string this[string key] => Table.TryGetValue(key, out var words) ? words[language] : key;

    public string Format(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, this[key], values);

    /// <summary>The language Windows is in, when the player has not chosen one: Uzbek, Russian, else English.</summary>
    public static int FromWindows()
    {
        string name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return name == "uz" ? 1 : name == "ru" ? 2 : 0;
    }

    public string Size(long bytes) => bytes >= 1024L * 1024 * 1024
        ? (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " " + this["unit.gb"]
        : Math.Max(bytes / 1048576.0, bytes > 0 ? 0.1 : 0).ToString(bytes < 10 * 1048576L ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + this["unit.mb"];

    public string Speed(double bytesPerSecond) => bytesPerSecond >= 1048576
        ? (bytesPerSecond / 1048576).ToString("0.0", CultureInfo.InvariantCulture) + " " + this["unit.mbs"]
        : (bytesPerSecond / 1024).ToString("0", CultureInfo.InvariantCulture) + " " + this["unit.kbs"];

    public string Duration(TimeSpan time) => time.TotalMinutes >= 1
        ? Format("unit.min", Math.Ceiling(time.TotalMinutes).ToString(CultureInfo.InvariantCulture))
        : Format("unit.s", Math.Max(1, Math.Ceiling(time.TotalSeconds)).ToString(CultureInfo.InvariantCulture));
}
