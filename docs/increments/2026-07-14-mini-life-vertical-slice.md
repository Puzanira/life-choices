# Increment: mini-life-vertical-slice

<!-- Governed by system/specs/INCREMENT_MODEL_SPEC.md (§4, §5, §7).
     First gameplay increment on the fresh Unity baseline (0b56df7). -->

## 1. Пересказ дизайна

Появляется **играбельная мини-жизнь целиком** — то самое ядро игры, ради
которого всё затевалось. На экране одна карточка-выбор с текстом; тикает
таймер; ты жмёшь **да** или **нет** (клавиатура: → это «да/принять», ← это
«нет/отклонить»). Выбор сразу двигает **4 шкалы** (настроение, здоровье, деньги,
люди — старт у каждой 50), и видно, как цифры прыгнули. Дальше следующая
карточка.

Жизнь идёт **4 этапами** (детство → юность → зрелость → старость), у каждого
свой мешок карточек из `docs/mvp-content.md` (~40 карт всего). Не успел нажать
за окно таймера — считается как **«нет»** и настроение −4 («жизнь прошла мимо»).
Длина окна зависит от карточки: короткое ≈2с, среднее ≈3.5с, договор с мелким
шрифтом ≈5с.

**Умереть** можно тремя способами (в этом приоритете): ☠-карточка (сунул вилку
в розетку → мгновенно), любая шкала улетела в **≤0 или ≥100** (8 смешных
смертей), либо ты **дожил** все 4 этапа. В финале — **некролог текстом**:
причина смерти, ярлык жизни (по самой высокой шкале), кто пришёл на похороны
(по шкале «люди») и три строки «а помнишь, как ты…» — три карточки этого забега
с самым сильным сдвигом шкал. С финала по клавише — новая жизнь заново.

**Чего в этом инкременте ещё НЕТ** (осознанно отложено): персонаж-картинка,
меняющийся от шкал, и картиночное слайд-шоу — оба завязаны на ещё не решённый
визуальный стиль (слайды пока текстом). Также отложено: 🔗-логика (карточки,
которые включаются от прошлых выборов — в этом инкременте 🔗-карты просто
появляются как обычные), звук, кабинет. Числа баланса берём из mvp-content
как есть — крутить будем плейтестом.

> Размер: инкремент крупный — на пустом проекте он строит сразу спину игры
> (карты, шкалы, таймер, финал). Это ОДНА механика (петля решений), поэтому
> берём вертикалью, а не режем; но это осознанно больше типового инкремента.
> Соответствует GAME_SPEC (Core Loop, 4 шкалы, 4 этапа, финал-некролог) —
> противоречий нет.

## 2. Done contract

| # | Class | Criterion |
|---|-------|-----------|
| 1 | boots — сцена грузится, ошибок нет | Запуск Play → грузится игровая сцена без ошибок в консоли; видна первая карточка детства, 4 шкалы (по 50) и идёт обратный отсчёт таймера. |
| 2 | player path — управление работает | → регистрируется как «да», ← как «нет»; после нажатия текущая карточка сменяется следующей из мешка текущего этапа; когда карты этапа кончились — переход к следующему этапу. |
| 3 | interaction path — механика end-to-end | Выбор «да»/«нет» применяет эффекты карточки к 4 шкалам (числа меняются немедленно и видимо); истёкший таймер = «нет» + настроение −4; ☠-карточка на «смертельной» стороне убивает сразу. |
| 4 | failure/restart path — проигрыш + рестарт | Смерть (☠, или шкала ≤0/≥100, или дожил все 4 этапа) → экран некролога; по клавише — новая жизнь с начала (детство, все шкалы сброшены в 50). Приоритет причины: ☠ → сломанная шкала → естественная. |
| 5 | observability — изменение ВИДНО | На экране постоянно: 4 значения шкал (меняются сразу после выбора), текущий этап, таймер. Некролог показывает причину смерти, ярлык жизни (по макс. шкале), кто пришёл (по шкале «люди») и 3 строки-воспоминания (карты с макс. суммарным сдвигом). |
| 6 | hygiene — чисто | Ни ошибок, ни варнингов в консоли; нет missing references; все ~40 карточек mvp-content грузятся и доступны в своих этапах. |
| 7 | regressions — проверки по системам | Первый геймплейный инкремент, прошлых систем нет → регресс n/a; вместо него машинный гейт: EditMode+PlayMode сьют зелёный (`studio-go suite`), покрывает шкалы/смерти/таймаут/приоритет/этапы/сборку некролога + сквозной прогон жизни. |

<!-- Balance: числа из mvp-content зашиты как данные; тайминги окон (2.0/3.5/5.0с)
     проверяются PlayMode-тестами с запасом — известная хрупкость time-window тестов. -->

## 3. Approved by founder

`Approved by founder:` 2026-07-14 — «ок» (Gate 1, в терминале). Согласована деталь
управления: → «да» / ← «нет» (тюнится плейтестом). Персонаж-визуал и картиночные
слайды осознанно отложены (завязаны на визуальный стиль); 🔗-логика, звук, кабинет
вне scope.

## 4. Checkpoint log

- **(a) after Gate 1:** Одобрен полный вертикальный слайс мини-жизни. Systems to
  build (greenfield): card/deck data model, per-stage bag draw, 4-scale state +
  death thresholds, per-card timer (S/M/L) + timeout→NO+Н−4, instant-death (☠),
  death-priority resolver (☠ → scale → natural), text obituary (cause / label by
  max scale / funeral by people / top-3 shift memories), keyboard input (→/←),
  restart. Content source: `docs/mvp-content.md` (~40 cards). Machine gate:
  `studio-go suite` (EditMode+PlayMode). Deferred: character visual, image
  slideshow, 🔗 conditional unlocks, sound, cabinet.

## 5. Skeptic verdict

- Model: **Codex (read-only sandbox), 2026-07-14** — reconstructed the contract from
  §1/§2 + mvp-content, read the untracked implementation directly (git diff hidden).
  Log: `logs/codex/skeptic-mini-life-20260714-170541.log`.
- **Verdict: PASS (proceed to founder playtest). No blockers.** Confirmed the
  failure-prone mechanics are correct: inclusive scale-death after clamp (95+10→100→
  dies), death priority (☠ → scale → natural), timeout = НЕТ + Настроение−4, correct
  instant-death sides (1.10/2.10 ДА, 4.10 ДА = peaceful natural, 3.10 crypto = normal),
  staged shuffled bags, restart, obituary assembly. Static spot-checks of card numbers
  across all 4 stages matched mvp-content. 37 tests judged real behavioral assertions.
- **Findings (2 minor, non-blocking) + resolutions:**
  1. *minor (CoreModelTests.cs:90):* card-data tests verify count/stage/title + special
     cases, not all 40 exact effects/windows — future content regressions could slip.
     **Resolution (accepted, deferred):** spot-checks passed + gate green; a full
     40-card data-lock test is a cheap follow-up, not a playtest blocker. Logged for a
     later content increment.
  2. *minor (DeathTables.cs:12):* finale death-text not byte-exact to mvp-content
     punctuation/capitalization. **Resolution (n/a by design):** GAME_SPEC marks the
     death phrasings as working drafts for a later scenario pass; byte-exactness is not
     a requirement now. Founder's playtest is the text pass.
- Not statically verifiable → carried to Gate 2 / machine gate: timer *feel*,
  readability + text layout at real resolution/font, live console cleanliness.

## 6. Playtest verdict

`Playtest ok:` 2026-07-14 — основательница: «да все отлично даже комментариев нет)
еще и посмеялась». Gate 2 принят с первого прохода, без фикс-лупа; финал/юмор
считывается (некролог рассмешил). Балансовые числа остаются черновыми (тюнинг —
будущие инкременты), персонаж-визуал / картиночные слайды / 🔗-логика / звук —
отложены по контракту.
`Landed:` c653e6d (increment code/scene/tests); Landed-hash recorded in this
follow-up bookkeeping commit.
