using System;
using System.Collections.Generic;
using System.Globalization;

namespace IslamicNashat.Core;

/// <summary>
/// The widget's text in Arabic, Kurdish (Sorani) and English. Prayer names and the phrasing of
/// reminders follow the Android app's strings so the two read the same.
/// </summary>
public static class Strings
{
    public static string Language { get; set; } = "ar";
    public static bool IsRightToLeft => Language != "en";

    public static string T(string key)
    {
        if (!Table.TryGetValue(key, out var row)) return key;
        return Language switch { "ckb" => row.Ckb, "en" => row.En, _ => row.Ar };
    }

    public static string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);

    public static string PrayerName(Prayer p, DateTime? on = null, bool ramadan = false)
    {
        if (p == Prayer.Dhuhr && on?.DayOfWeek == DayOfWeek.Friday) return T("jumuah");
        if (ramadan && p == Prayer.Maghrib) return T("iftar");
        return T(p.ToString().ToLowerInvariant());
    }

    static readonly string[] ArabicMonths =
        ["محرم", "صفر", "ربيع الأول", "ربيع الآخر", "جمادى الأولى", "جمادى الآخرة", "رجب", "شعبان", "رمضان", "شوال", "ذو القعدة", "ذو الحجة"];
    static readonly string[] KurdishMonths =
        ["موحەڕڕەم", "سەفەر", "ڕەبیعولئەووەڵ", "ڕەبیعولئاخیر", "جومادەلئوولا", "جومادەلئاخیرە", "ڕەجەب", "شەعبان", "ڕەمەزان", "شەووال", "ذولقەعدە", "ذولحیججە"];
    static readonly string[] EnglishMonths =
        ["Muharram", "Safar", "Rabi' al-Awwal", "Rabi' al-Akhir", "Jumada al-Ula", "Jumada al-Akhirah", "Rajab", "Sha'ban", "Ramadan", "Shawwal", "Dhu al-Qa'dah", "Dhu al-Hijjah"];

    public static string HijriText(HijriDate h)
    {
        var months = Language switch { "ckb" => KurdishMonths, "en" => EnglishMonths, _ => ArabicMonths };
        return Language == "en" ? $"{h.Day} {months[h.Month - 1]} {h.Year} AH" : $"{Digits(h.Day)} {months[h.Month - 1]} {Digits(h.Year)}";
    }

    static readonly string[] ArabicDays = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];
    static readonly string[] KurdishDays = ["یەکشەممە", "دووشەممە", "سێشەممە", "چوارشەممە", "پێنجشەممە", "هەینی", "شەممە"];

    public static string DayName(DateTime d) => Language switch
    {
        "ckb" => KurdishDays[(int)d.DayOfWeek],
        "en" => d.DayOfWeek.ToString(),
        _ => ArabicDays[(int)d.DayOfWeek],
    };

    /// <summary>Clock time like the phone shows it: 12-hour with Eastern Arabic digits in Arabic and Kurdish.</summary>
    public static string Clock(DateTime t)
    {
        var hour = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        if (Language == "en") return $"{hour}:{t.Minute:00} {(t.Hour < 12 ? "AM" : "PM")}";
        var suffix = Language == "ckb" ? (t.Hour < 12 ? "پ.ن" : "د.ن") : (t.Hour < 12 ? "ص" : "م");
        return $"{Digits(hour)}:{Digits(t.Minute, 2)} {suffix}";
    }

    /// <summary>A countdown that never changes width: always hh:mm:ss, so the bar doesn't jump each tick.</summary>
    public static string Countdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        var total = (long)Math.Ceiling(span.TotalSeconds);
        var text = $"{total / 3600:00}:{total % 3600 / 60:00}:{total % 60:00}";
        return Language == "en" ? text : Digits(text);
    }

    public static string Minutes(int minutes) => F("minutes_n", Language == "en" ? minutes.ToString(CultureInfo.InvariantCulture) : Digits(minutes));

    public static string Digits(int n, int pad = 1) => Digits(n.ToString(new string('0', pad), CultureInfo.InvariantCulture));

    public static string Digits(string s)
    {
        if (Language == "en") return s;
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is >= '0' and <= '9') chars[i] = (char)('٠' + (chars[i] - '0'));
        return new string(chars);
    }

    sealed record Row(string Ar, string Ckb, string En);

    static readonly Dictionary<string, Row> Table = new()
    {
        ["app_name"] = new("إسلامي نشأت", "ئیسلامی نەشات", "Islamic Nashat"),
        ["fajr"] = new("الفجر", "بەیانی", "Fajr"),
        ["sunrise"] = new("الشروق", "خۆرهەڵاتن", "Sunrise"),
        ["dhuhr"] = new("الظهر", "نیوەڕۆ", "Dhuhr"),
        ["asr"] = new("العصر", "عەسر", "Asr"),
        ["maghrib"] = new("المغرب", "مەغریب", "Maghrib"),
        ["isha"] = new("العشاء", "عیشا", "Isha"),
        ["jumuah"] = new("الجمعة", "هەینی", "Jumu'ah"),
        ["imsak"] = new("الإمساك", "ئیمساک", "Imsak"),
        ["iftar"] = new("الإفطار", "ئیفتار", "Iftar"),

        ["since_adhan"] = new("مضى", "تێپەڕی", "since"),
        ["now"] = new("حان الآن", "ئێستا کاتیەتی", "now"),
        ["minutes_n"] = new("{0} دقيقة", "{0} خولەک", "{0} min"),
        ["stop_adhan"] = new("إيقاف الأذان", "وەستاندنی بانگ", "Stop adhan"),
        ["makruh_now"] = new("وقت كراهة حتى {0}", "کاتی کەراهەتە تا {0}", "Disliked time until {0}"),
        ["makruh_title"] = new("أوقات الكراهة", "کاتەکانی کەراهەت", "Disliked times"),
        ["makruh_sunrise"] = new("بعد الشروق", "دوای خۆرهەڵاتن", "After sunrise"),
        ["makruh_zenith"] = new("قبل الظهر (الزوال)", "پێش نیوەڕۆ (زەوال)", "Before Dhuhr (zenith)"),
        ["makruh_sunset"] = new("قبل المغرب", "پێش مەغریب", "Before Maghrib"),
        ["ramadan_day"] = new("رمضان، اليوم {0}", "ڕەمەزان، ڕۆژی {0}", "Ramadan, day {0}"),

        ["notify_adhan_title"] = new("حان وقت صلاة {0}", "کاتی نوێژی {0} هات", "It's time for {0}"),
        ["notify_adhan_body"] = new("{0} — {1}", "{0} — {1}", "{0} — {1}"),
        ["notify_reminder_title"] = new("اقترب وقت {0}", "کاتی {0} نزیک بووەوە", "{0} is near"),
        ["notify_reminder_body"] = new("بقي {0} — الأذان الساعة {1}", "{0} ماوە — بانگ لە {1}", "{0} left — the adhan is at {1}"),
        ["notify_imsak_title"] = new("قارب السحور على الانتهاء", "سەحەری کۆتایی دێت", "Suhoor is ending"),
        ["notify_imsak_body"] = new("الإمساك الآن، والفجر الساعة {0}", "ئێستا ئیمساکە، بەیانی لە {0}", "Imsak now — Fajr is at {0}"),
        ["notify_friday_title"] = new("سورة الكهف", "سوورەتی کەهف", "Surah Al-Kahf"),
        ["notify_friday_body"] = new("اليوم الجمعة — لا تنسَ قراءة سورة الكهف والإكثار من الصلاة على النبي ﷺ.",
            "ئەمڕۆ هەینییە — خوێندنی سوورەتی کەهف و زۆرکردنی صەڵات بۆ پێغەمبەر ﷺ لەبیر مەکە.",
            "It's Friday — don't forget Surah Al-Kahf and to send blessings on the Prophet ﷺ."),
        ["ready_title"] = new("الويدجت يعمل", "ویجێتەکە کاردەکات", "The widget is running"),
        ["ready_body"] = new("اضغط الأيقونة قرب الساعة لإظهاره أو إخفائه.", "کرتە لە ئایکۆنەکە بکە لە تەنیشت کاتژمێرەکە بۆ پیشاندان یان شاردنەوە.", "Click the icon by the clock to show or hide it."),

        ["tray_show"] = new("إظهار الويدجت", "پیشاندانی ویجێت", "Show widget"),
        ["tray_hide"] = new("إخفاء الويدجت", "شاردنەوەی ویجێت", "Hide widget"),
        ["tray_settings"] = new("الإعدادات", "ڕێکخستنەکان", "Settings"),
        ["tray_updates"] = new("البحث عن تحديث", "گەڕان بۆ نوێکردنەوە", "Check for updates"),
        ["tray_exit"] = new("إغلاق", "داخستن", "Exit"),

        ["settings"] = new("الإعدادات", "ڕێکخستنەکان", "Settings"),
        ["tab_general"] = new("عام", "گشتی", "General"),
        ["tab_adhan"] = new("الأذان", "بانگ", "Adhan"),
        ["tab_look"] = new("الشكل", "ڕووکار", "Look"),
        ["tab_more"] = new("المزيد", "زیاتر", "More"),
        ["language"] = new("اللغة", "زمان", "Language"),
        ["location"] = new("الموقع", "شوێن", "Location"),
        ["search_city"] = new("ابحث عن مدينتك…", "گەڕان بۆ شار…", "Search for your city…"),
        ["detect"] = new("تحديد تلقائي", "دیاریکردنی خۆکار", "Detect"),
        ["detecting"] = new("جارٍ التحديد…", "دیاری دەکرێت…", "Detecting…"),
        ["detect_failed"] = new("تعذّر تحديد الموقع. ابحث عن مدينتك بالاسم.", "شوێن دیاری نەکرا. بە ناو بگەڕێ بۆ شارەکەت.", "Couldn't detect your location. Search for your city instead."),
        ["detect_far"] = new("أقرب مدينة معروفة تبعد {0} كم — تأكد منها.", "نزیکترین شاری ناسراو {0} کم دوورە — دڵنیابەرەوە.", "The nearest known city is {0} km away — check it."),
        ["source_fixed"] = new("الأوقات من جدول المدينة — نفس تطبيق الهاتف", "کاتەکان لە خشتەی شارەکەوەن — وەک ئەپی مۆبایل", "Times from the city's timetable — same as the phone app"),
        ["source_calc"] = new("الأوقات محسوبة فلكياً — نفس حساب تطبيق الهاتف", "کاتەکان بە ئەستێرەناسی هەژمار کراون — وەک ئەپی مۆبایل", "Times calculated astronomically — same as the phone app"),
        ["method"] = new("طريقة الحساب", "شێوازی هەژمارکردن", "Calculation method"),
        ["method_auto"] = new("تلقائي حسب البلد", "خۆکار بەپێی وڵات", "Auto (by country)"),
        ["madhab"] = new("المذهب (لصلاة العصر)", "مەزهەب (بۆ عەسر)", "Madhab (for Asr)"),
        ["shafi"] = new("الجمهور (شافعي، مالكي، حنبلي)", "جومهوور (شافیعی، مالیکی، حەنبەلی)", "Standard (Shafi'i, Maliki, Hanbali)"),
        ["hanafi"] = new("حنفي", "حەنەفی", "Hanafi"),
        ["offsets"] = new("تعديل الأوقات (دقائق)", "ڕاستکردنەوەی کاتەکان (خولەک)", "Adjust times (minutes)"),
        ["hijri_offset"] = new("تصحيح التاريخ الهجري (أيام)", "ڕاستکردنەوەی ڕێکەوتی کۆچی (ڕۆژ)", "Hijri date correction (days)"),
        ["start_with_windows"] = new("التشغيل مع ويندوز", "دەستپێکردن لەگەڵ ویندۆز", "Start with Windows"),

        ["reciter"] = new("صوت الأذان", "دەنگی بانگ", "Adhan voice"),
        ["fajr_reciter"] = new("صوت أذان الفجر", "دەنگی بانگی بەیانی", "Fajr adhan voice"),
        ["preview"] = new("استماع", "گوێگرتن", "Listen"),
        ["downloading"] = new("جارٍ التنزيل…", "دادەبەزێت…", "Downloading…"),
        ["download_failed"] = new("تعذّر التنزيل، سيُستخدم أذان مكة.", "دانەبەزی، بانگی مەککە بەکاردێت.", "Download failed; the Makkah adhan will be used."),
        ["per_prayer_sound"] = new("الصوت لكل صلاة", "دەنگ بۆ هەر نوێژێک", "Sound for each prayer"),
        ["mode_full"] = new("أذان كامل", "بانگی تەواو", "Full adhan"),
        ["mode_short"] = new("تنبيه قصير", "ئاگادارکەرەوەی کورت", "Short tone"),
        ["mode_silent"] = new("صامت", "بێدەنگ", "Silent"),
        ["volume"] = new("مستوى الصوت", "ئاستی دەنگ", "Volume"),
        ["notifications"] = new("إشعار ويندوز عند دخول الوقت", "ئاگانامەی ویندۆز لە کاتی بانگ", "Windows notification at prayer time"),
        ["reminder"] = new("تنبيه قبل الأذان", "ئاگادارکردنەوە پێش بانگ", "Reminder before the adhan"),
        ["off"] = new("مطفأ", "کوژاوە", "Off"),
        ["elapsed"] = new("عرض «مضى على الأذان» لمدة", "پیشاندانی «تێپەڕی لە بانگ» بۆ ماوەی", "Show time since the adhan for"),

        ["theme"] = new("اللون", "ڕەنگ", "Colour"),
        ["theme_emerald"] = new("زمردي", "زومڕوودی", "Emerald"),
        ["theme_dark"] = new("داكن", "تاریک", "Dark"),
        ["theme_light"] = new("فاتح", "ڕووناک", "Light"),
        ["theme_glass"] = new("زجاجي", "شووشەیی", "Glass"),
        ["opacity"] = new("الشفافية", "ڕوونی", "Opacity"),
        ["size"] = new("الحجم", "قەبارە", "Size"),
        ["dock_taskbar"] = new("ثبّته فوق شريط المهام قرب الساعة", "لە سەرووی شریتی ئەرکەکان لە تەنیشت کاتژمێر", "Pin above the taskbar by the clock"),
        ["hide_fullscreen"] = new("إخفاء أثناء الألعاب والأفلام بملء الشاشة", "شاردنەوە لە کاتی یاری و فیلمی پڕشاشە", "Hide during fullscreen games and films"),
        ["hide_fullscreen_note"] = new("الأذان يبقى يعمل.", "بانگ هەر کاردەکات.", "The adhan still plays."),

        ["ramadan_mode"] = new("وضع رمضان: الإمساك والإفطار", "دۆخی ڕەمەزان: ئیمساک و ئیفتار", "Ramadan mode: Imsak and Iftar"),
        ["imsak_before"] = new("الإمساك قبل الفجر", "ئیمساک پێش بەیانی", "Imsak before Fajr"),
        ["friday"] = new("تذكير الجمعة (سورة الكهف)", "ئاگادارکردنەوەی هەینی (سوورەتی کەهف)", "Friday reminder (Surah Al-Kahf)"),
        ["show_makruh"] = new("إظهار أوقات الكراهة", "پیشاندانی کاتەکانی کەراهەت", "Show disliked prayer times"),
        ["version"] = new("الإصدار {0}", "وەشانی {0}", "Version {0}"),
        ["check_updates"] = new("البحث عن تحديث", "گەڕان بۆ نوێکردنەوە", "Check for updates"),
        ["up_to_date"] = new("لديك آخر إصدار.", "دوایین وەشانت هەیە.", "You have the latest version."),
        ["update_failed"] = new("تعذّر البحث عن تحديث. تأكد من الإنترنت.", "گەڕان بۆ نوێکردنەوە سەرکەوتوو نەبوو. ئینتەرنێت بپشکنە.", "Couldn't check for updates. Check your internet."),
        ["update_title"] = new("يتوفر إصدار جديد", "وەشانێکی نوێ هەیە", "A new version is available"),
        ["update_body"] = new("الإصدار {0} جاهز. سيتحدّث البرنامج ويعود للعمل وحده.", "وەشانی {0} ئامادەیە. بەرنامەکە نوێ دەبێتەوە و خۆی دەگەڕێتەوە.", "Version {0} is ready. The app will update and come back by itself."),
        ["update_now"] = new("حدّث الآن", "ئێستا نوێی بکەرەوە", "Update now"),
        ["later"] = new("لاحقاً", "دواتر", "Later"),
        ["close"] = new("إغلاق", "داخستن", "Close"),
        ["save"] = new("حفظ", "پاشەکەوت", "Save"),
        ["cancel"] = new("إلغاء", "پاشگەزبوونەوە", "Cancel"),
        ["data_note"] = new("بدون إعلانات وبدون حساب. يعمل بدون إنترنت.", "بێ ڕیکلام و بێ هەژمار. بێ ئینتەرنێتیش کاردەکات.", "No ads, no account. Works offline."),

        ["welcome_title"] = new("أهلاً بك", "بەخێربێیت", "Welcome"),
        ["welcome_body"] = new("اختر لغتك ومدينتك، والويدجت يعرض الوقت المتبقي للصلاة القادمة فوق كل النوافذ.",
            "زمان و شارەکەت هەڵبژێرە، ویجێتەکە کاتی ماوە بۆ نوێژی داهاتوو لە سەرووی هەموو پەنجەرەکان پیشان دەدات.",
            "Choose your language and city, and the widget shows the time left to the next prayer above every window."),
        ["start"] = new("ابدأ", "دەست پێبکە", "Start"),
        ["tip_drag"] = new("اسحبه لأي مكان • اضغطه لعرض أوقات اليوم • زر الفأرة الأيمن للإعدادات",
            "ڕایبکێشە بۆ هەر شوێنێک • کرتەی لێبکە بۆ کاتەکانی ئەمڕۆ • کلیکی ڕاست بۆ ڕێکخستنەکان",
            "Drag it anywhere • click it for today's times • right-click for settings"),
    };
}
