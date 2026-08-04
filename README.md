# أوقات الصلاة — ويدجت ويندوز

<div dir="rtl">

ويدجت صغير يطفو فوق كل النوافذ ويعرض الوقت المتبقّي للصلاة القادمة.

</div>

```
   ╭──────────────────────────────────╮
   │  12:17  │  الظّهر  │  00:17:23   │
   ╰──────────────────────────────────╯
```

## التنزيل

<div dir="rtl">

نزّل `IslamicNashatWidget.exe` من [أحدث إصدار](https://github.com/NashatShamsadyn7/islamic-nashat-desktop/releases/latest)
وشغّله. لا تثبيت، ولا متطلّبات.

سيخبرك البرنامج بنفسه عند صدور نسخة أجدد.

</div>

## الاستخدام

<div dir="rtl">

- **اسحب الويدجت** بالفأرة إلى أي مكان — يتذكّر موضعه.
- **اضغط الأيقونة** قرب الساعة لإخفائه أو إظهاره.
- **زرّ الفأرة الأيمن على الأيقونة** يفتح أوقات اليوم الستّة والإعدادات.

</div>

| الإعداد | ماذا يفعل |
|---|---|
| الموقع | ابحث عن مدينتك، أو اضغط «تحديد تلقائياً» |
| الأذان | ١١ تلاوة — **اسمعها قبل أن تختار** بزرّ ▶ |
| شريط مضغوط | سطر واحد، أو البطاقة الأكبر |
| الإشعارات | إشعار ويندوز عند دخول الوقت |
| تنبيه قبل الأذان | ٥ إلى ٣٠ دقيقة، أو معطّل |
| إزاحة الأوقات | ±٦٠ دقيقة لكل صلاة على حدة |
| التشغيل مع ويندوز | اختصار في مجلد بدء التشغيل |

<div dir="rtl">

الإعدادات في `%APPDATA%\IslamicNashatWidget\settings.json`، ويمكن تحريرها يدوياً.

</div>

---

## Matching the phone

This mirrors an Android app (`com.nashat.prayers`). Both resolve a day's times the same
two ways, in the same order:

**A fixed timetable, when the city has one.** 252 of the 166,404 known places are flagged
`has_fixed_prayer_time` and carry a pre-computed row per `MM-DD`. `assets/locations.db`
holds those 45,749 rows, lifted straight out of the app's bundled muslim-data database, so
for those cities the two agree by construction. Bardarash is one of them.

**Astronomical calculation, everywhere else.** `calculator.py` is a line-by-line port of
`MuslimDataCalculator.kt`, itself a port of `Prayer.swift` from MuslimData.

The port is not trusted on inspection. `CalculatorGoldenDumpTest` in the Android repository
runs the Kotlin class over 15 cities — every calculation method, both Asr juristics, high
latitudes, the southern hemisphere, half-hour timezones — across 6 dates, and writes the
result to `tests/calculator_golden.json`. `test_calculator.py` asserts the Python output
equals it **to the second**. A well-meaning cleanup that changes behaviour fails the suite.

Re-generate the dump after changing the calculation:

```bash
./gradlew :app:testDebugUnitTest --tests '*CalculatorGoldenDumpTest*' -i
```

## Running from source

```bash
pip install -r requirements.txt
set PYTHONPATH=src
python -m prayer_widget
```

```bash
python -m pytest          # 42 tests, no Qt required
pyinstaller build.spec --noconfirm
```

The time logic in `src/prayer_widget/timetable.py` imports nothing from Qt, so the parts
that are easy to get wrong are tested directly: the countdown crossing midnight into
tomorrow's Fajr, a prayer landing on the current minute, a restart shortly after a prayer
that must not replay it, a laptop waking from hours of sleep, and 29 February.

## Rebuilding the location database

`assets/locations.db` is 16.6 MB — the `country`, `location` and `prayer_time` tables from
the Android app's 28.8 MB muslim-data file, with the Quran, azkar and names of Allah left
behind. Regenerate it after the app updates that database:

```bash
python tools/extract_locations.py assets/locations.db
```

## Releasing

Every push to `main` builds the exe, publishes a GitHub release tagged `v1.0.<run>`, and
attaches the binary. `src/prayer_widget/__init__.py` carries the version the updater
compares against; CI rewrites it to match the tag.
