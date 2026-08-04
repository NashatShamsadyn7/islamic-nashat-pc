# أوقات الصلاة — ويدجت ويندوز

<div dir="rtl">

ويدجت صغير يطفو فوق كل النوافذ ويعرض الوقت المتبقّي للصلاة القادمة.

</div>

```
         ┌──────────────────┐
         │     الظّهر        │
         │     11:00        │
         │   باقٍ 17 دقيقة   │
         └──────────────────┘
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
| الإشعارات | إشعار ويندوز عند دخول الوقت |
| صوت الأذان | تشغيل الأذان، مع زرّ إيقاف على الويدجت |
| اختيار الأذان | ١١ تلاوة — المُضمَّنة تعمل دون إنترنت، والبقية تُنزَّل مرّة واحدة |
| التشغيل مع ويندوز | اختصار في مجلد بدء التشغيل |

<div dir="rtl">

الإعدادات في `%APPDATA%\IslamicNashatWidget\settings.json`، ويمكن تحريرها يدوياً.
حقل `offsets` يزيح كل صلاة بالدقائق، و`pre_athan_minutes` ينبّه قبل الأذان (0 = معطّل).

</div>

---

## Why there is no astronomical calculation

This mirrors an Android app (`com.nashat.prayers`) that shows prayer times for **Bardarash,
Iraq**. That app does not compute those times: the city is flagged
`has_fixed_prayer_time = 1` in its bundled muslim-data database, so it reads a pre-computed
row out of a `prayer_time` table keyed by `MM-DD`.

`assets/timetable.json` is that exact table, extracted for Bardarash — 366 days including
29 February. Reading the same rows is the only way this widget can be guaranteed to agree
with the phone to the minute. Adding a calculation here would reintroduce the disagreement
it exists to avoid.

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

## Changing city

Only 252 of the locations in that database carry a fixed timetable. For one of those:

```bash
python tools/extract_timetable.py "Erbil" assets/timetable.json
```

For any other city the Android app falls back to astronomical calculation, and matching it
would mean porting that calculation — which this project deliberately does not do. Extract
will refuse rather than emit times that disagree with the phone.

## Releasing

Every push to `main` builds the exe, publishes a GitHub release tagged `v1.0.<run>`, and
attaches the binary. `src/prayer_widget/__init__.py` carries the version the updater
compares against; CI rewrites it to match the tag.
