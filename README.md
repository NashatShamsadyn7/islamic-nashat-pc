# إسلامي نشأت — ويدجت الصلاة لويندوز

<div dir="rtl">

ويدجت صغير يطفو فوق كل النوافذ ويعرض الصلاة القادمة والوقت المتبقي لها بالثانية، بنفس أوقات تطبيق الهاتف.
عربي، کوردی، English. بدون إعلانات وبدون حساب، ويعمل بدون إنترنت.

</div>

```
   ╭───────────────────────────────────────╮
   │  ●  الظهر     ٠٠:١٧:٢٣     ١١:٥٥ ص    │
   ╰───────────────────────────────────────╯
```

## التنزيل

<div dir="rtl">

نزّل `IslamicNashat-Setup-x.y.z.exe` من [أحدث إصدار](https://github.com/NashatShamsadyn7/islamic-nashat-pc/releases/latest) وشغّله.
لا يحتاج صلاحيات مدير. يتحدّث وحده بعدها: يظهر إشعار، ضغطة واحدة، ويرجع يشتغل.

</div>

## الاستخدام

<div dir="rtl">

- **اسحبه** لأي مكان — يلتصق بحافة الشاشة ويتذكّر مكانه.
- **اضغطه** لعرض أوقات اليوم والتاريخ الهجري وأوقات الكراهة.
- **زر الفأرة الأيمن** يفتح الإعدادات.
- **أيقونة قرب الساعة:** ضغطة تُظهر الويدجت أو تخفيه، وأثناء الأذان توقفه.

</div>

| الإعداد | ماذا يفعل |
|---|---|
| الموقع | ابحث بين ١٦٦٬٤٠٤ مدينة، أو «تحديد تلقائي» |
| الصوت لكل صلاة | أذان كامل، تنبيه قصير، أو صامت — وأذان خاص للفجر |
| ١٢ صوت أذان | نفس أصوات تطبيق الهاتف، **اسمعها قبل أن تختار** |
| إيقاف الأذان | زر على الويدجت وعلى الأيقونة |
| تنبيه قبل الأذان | ٥ إلى ٣٠ دقيقة، والعدّ يصير ذهبياً |
| «مضى على الأذان» | بعد دخول الوقت يعرض كم مضى، ثم ينتقل للصلاة التالية |
| وضع رمضان | العدّ للإمساك، والمغرب يصير «الإفطار» |
| تذكير الجمعة | سورة الكهف والصلاة على النبي ﷺ |
| أوقات الكراهة | بعد الشروق، قبل الزوال، قبل المغرب |
| الشكل | زمردي، داكن، فاتح، زجاجي — والشفافية والحجم |
| الألعاب والأفلام | يختفي وحده بملء الشاشة، والأذان يبقى يعمل |

---

## Matching the phone

The times come from the Android app (`com.nashat.prayers`) and are resolved the same way, in the same order:

1. **A fixed timetable, when the city has one.** 252 of the 166,404 places are flagged `has_fixed_prayer_time`
   and carry a row per `MM-DD`. `Assets/locations.db` holds the `country`, `location` and `prayer_time` tables
   lifted straight out of the app's `muslim_db_v2.5.1.db`, so for those cities both apps read the same rows.
2. **Astronomical calculation, everywhere else.** `PrayerCalculator.cs` is a line-by-line port of
   `MuslimDataCalculator.kt`. `CalculatorGoldenTests` runs it over 15 cities, every method, both Asr juristics,
   high latitudes and half-hour time zones, and compares against `calculator_golden.json` — output of the Kotlin
   class itself (`CalculatorGoldenDumpTest` in the Android repository) — **to the second**.

Method "Auto" picks the same method per country as `PrayerManager.autoMethodForCountry`. The Hijri date is
Umm al-Qura with the user's day offset, like `java.time.HijrahDate` in the app.

Regenerate the database after the app updates its own:

```bash
python tools/extract_locations.py src/IslamicNashat/Assets/locations.db
```

## Building

.NET 10, WPF. No other runtime is needed on the user's PC (self-contained).

```powershell
dotnet test IslamicNashat.slnx               # calculator, timetable, alerts, calendar, database
dotnet run --project src/IslamicNashat
.\release.ps1 -Version 2.0.1                 # tests, exe, installer, GitHub release, site download
```

The time logic (`Core/Timetable.cs`, `Core/Schedule.cs`, `Core/WidgetModel.cs`) has no UI dependencies and is
tested directly: the countdown crossing midnight, a prayer at the current second, a restart just after a prayer
that must not replay it, a laptop waking after hours asleep, Friday, Ramadan and Imsak.

Settings live in `%APPDATA%\IslamicNashat\settings.json`; downloaded recitations in
`%LOCALAPPDATA%\IslamicNashat\adhan`. On first run the 1.x widget's settings are imported and its startup entry removed.
