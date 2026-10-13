using System;
using System.Globalization;

namespace AmbientLightCalibrate
{
    /// <summary>UI text: Ukrainian when Windows UI language is Ukrainian, English otherwise.</summary>
    public static class S
    {
        static readonly bool Uk = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "uk";
        static string T(string en, string uk) => Uk ? uk : en;

        public static string AppTitle => T("Auto brightness", "Автояскравість");
        public static string Measure => T("Measure", "Заміряти");
        public static string Monitors => T("Monitors", "Монітори");
        public static string AllMonitors => T("All monitors", "Усі разом");
        public static string Offset => T("Offset", "Поправка");
        public static string OffsetSub => T("to the curve", "до кривої");
        public static string StartWithWindows => T("Start with Windows", "Запуск з Windows");
        public static string Settings => T("Settings", "Налаштування");
        public static string Log => T("Log", "Журнал");
        public static string Exit => T("Exit", "Вийти");
        public static string Back => T("Back", "Назад");
        public static string IntervalMin => T("Measure every, s", "Інтервал заміру, с");
        public static string Hysteresis => T("Ignore changes smaller than, %", "Не чіпати, якщо різниця менша, %");
        public static string MaxStep => T("Largest step at once, nits (0 = no limit)", "Найбільший крок за раз, ніт (0 = без обмеження)");
        public static string CurveTitle => T("Curve: room light → screen brightness", "Крива: освітлення кімнати → яскравість");
        public static string CurveHint => T("Empty row removes a point. Brightness changes smoothly between points.",
                                            "Порожній рядок прибирає точку. Між точками яскравість змінюється плавно.");
        public static string Lux => T("lux", "люкси");
        public static string Nits => T("nits", "ніти");
        public static string AddPoint => T("+ point", "+ точка");
        public static string Instrument => T("Colorimeter (ArgyllCMS)", "Колориметр (ArgyllCMS)");
        public static string ArgyllFolder => T("ArgyllCMS bin folder", "Папка ArgyllCMS (bin)");
        public static string Cancel => T("Cancel", "Скасувати");
        public static string Save => T("Save", "Зберегти");
        public static string BrightnessMaps => T("Brightness → nits per monitor", "Яскравість → ніти для моніторів");
        public static string BrightnessMapsHint => T("One-time, ~1 min per monitor: put the sensor on that screen with the diffuser OPEN.",
                                                     "Один раз, ~1 хв на монітор: датчик на цей екран, розсіювач ВІДКРИТИЙ.");
        public static string Calibrate => T("Calibrate", "Калібрувати");
        public static string Start => T("Start", "Почати");
        public static string NotCalibrated => T("not calibrated", "не відкалібровано");
        public static string NoDdc => T("no DDC/CI", "немає DDC/CI");
        public static string PointsRange(int n, double lo, double hi) => T($"{n} points, {lo:F0}–{hi:F0} nits", $"{n} точок, {lo:F0}–{hi:F0} ніт");
        public static string PlaceSensor(string mon) => T($"Hang the sensor in the centre of {mon}, diffuser open, then press Start.",
                                                          $"Повісьте датчик по центру {mon}, розсіювач відкритий, і натисніть «Почати».");
        public static string Calibrating(string mon, int set) => T($"Calibrating {mon}: Brightness {set}…", $"Калібрую {mon}: Brightness {set}…");
        public static string CalibDone(string mon) => T($"{mon} calibrated.", $"{mon} відкалібровано.");
        public static string CalibFailed(string why) => T($"Calibration failed: {why}", $"Калібровка не вдалась: {why}");

        public static string NoMonitors => T("No monitor with DDC/CI and a brightness map yet.\nOpen Settings → Calibrate.",
                                             "Ще немає монітора з DDC/CI і картою яскравості.\nНалаштування → Калібрувати.");
        public static string NitsUnit => T("nits", "ніт");
        public static string Hdr => "HDR";
        public static string HdrTip => T("HDR is on: brightness is left alone", "Увімкнено HDR: яскравість не чіпаю");
        public static string PresetTip(int n) => T($"All monitors {n} nits", $"Усі монітори {n} ніт");

        public static string WaitingFirst => T("Waiting for the first reading…", "Чекаю на перший замір…");
        public static string AutoOff => T("Automation is off", "Автоматика вимкнена");
        public static string Manual(string until) => T($"Manual · automation resumes at {until}", $"Вручну · автоматика з {until}");
        public static string Skipped(string why, string at) => T($"Skipped: {why} · {at}", $"Пропуск: {why} · {at}");
        public static string Room(double lux, double nits, string at) => T($"Room {lux:F0} lx → {nits:F0} nits · {at}", $"Кімната {lux:F0} лк → {nits:F0} ніт · {at}");
        public static string BusyWith(string what) => T($"{what} is running", $"працює {what}");
        public static string MeasureFailed => T("measurement failed", "замір не вдався");
        public static string NoSensor => T("sensor not responding", "датчик не відповідає");
        public static string NoArgyll => T("ArgyllCMS not found (Settings)", "не знайдено ArgyllCMS (Налаштування)");

        public static string RecalTitle => T("Time to recalibrate", "Час перекалібрувати");
        public static string RecalItem(string name, bool hdr, int days) => T($"{name} ({(hdr ? "HDR" : "SDR")}) — {days} days since calibration", $"{name} ({(hdr ? "HDR" : "SDR")}) — {days} дн. від калібрування");
        public static string Snooze => T("Remind me in a week", "Нагадати через тиждень");
        public static string ReminderSetting => T("Calibration reminder", "Нагадування про калібрування");
        public static string ReminderHint => T("The date comes from each monitor's active colour profile (SDR and HDR).", "Дата береться з активного колірного профілю кожного монітора (SDR і HDR).");
        public static string ReminderOff => T("Off", "Вимкнено");
        public static string EveryMonths(int n) => T(n == 1 ? "Every month" : $"Every {n} months", n == 1 ? "Щомісяця" : $"Кожні {n} місяці");
        public static string ProfileLine(string name, bool hdr, DateTime d, int days) => T($"{name} {(hdr ? "HDR" : "SDR")}: {d:dd.MM.yyyy} ({days} d)", $"{name} {(hdr ? "HDR" : "SDR")}: {d:dd.MM.yyyy} ({days} дн.)");
        public static string Balloon(int n) => T($"{n} colour profile(s) are due for recalibration.", $"Час перекалібрувати: {n} профіль(і).");

        public static string ErrInterval => T("Interval ≥ 10 s", "Інтервал ≥ 10 с");
        public static string ErrNumber => T("Enter a number", "Введіть число");
        public static string ErrCurve => T("Curve: positive numbers", "Крива: додатні числа");
        public static string ErrCurve2 => T("Curve: at least 2 points", "Крива: щонайменше 2 точки");
    }
}
