# Increment: live-child

<!-- OVERNIGHT (2026-07-19): авторизация основательницы. Gate 1 заочный (канон),
     Gate 2 = утро. Машинный гейт HEADLESS + скептик обязательны. Canon:
     GAME_SPEC §Ребёнок + §Контроллеры→механики (CHILD_PRESS) + §Обучающие
     подсказки; scenes.csv (MD02 OPEN:Реб после свадьбы+2, LT04 дети выросли);
     мокапы S9 (вспышка-кнопка). Baseline: live-relationships (сядет до этого). -->

## 1. Пересказ дизайна

**Пятая живая шкала — ребёнок.** «Рук всего две» в полной силе: деньги-крутилка,
дыхание, балансир отношений — и теперь ещё кнопка-тамагочи.

- **Открытие после свадьбы+2** (`MD02`=ДА «завести ребёнка», OPEN:Реб) с
  подсказкой-паузой S5; механика — **сигнал-реакция**: кнопка **вспыхивает
  каждые ~15–25 сек**, окно нажатия ~2 сек, мин. задержка ~1 сек (нельзя
  заспамить заранее). Смысловое событие `CHILD_PRESS` — клавиша **Enter** (в
  геймплее Enter свободна: confirm только вне геймплея; на кабинете — кнопка с
  подсветкой).
- **Пропуск 2+ вспышек подряд → «плохой родитель»:** отношения −10%, шкала
  ребёнка проседает. Успел по вспышке → шкала/родительство ок.
- **Дети выросли** `LT04` (55–65, если `MD02`=ДА): шкала/кнопка гаснут; ДА
  (навязчивая опека) → отношения −1, НЕТ (отпустил) → всё ок.
- Смерти от ребёнка нет; влияет на отношения, тон шоу и некролог.

> Вне scope: кризис-блок, звук. Зависит от свадьбы (`MD01`) и брака — они
> из инкремента отношений. Числа (интервал вспышки, окно, штраф) — тюнимые.

## 2. Done contract

| # | Class | Criterion |
|---|-------|-----------|
| 1 | boots | Старт как раньше; без свадьбы+ребёнка кнопки нет. |
| 2 | player path | После `MD02`=ДА (свадьба+2) — подсказка-пауза + кнопка-ребёнок; вспышка каждые ~15–25с, Enter в окне ~2с засчитывается; всё ПАРАЛЛЕЛЬНО остальным рукам. |
| 3 | interaction path | Успел по вспышке → ок; пропустил 2+ подряд → отношения −10% + шкала ребёнка вниз; нельзя заспамить заранее (мин. задержка); `LT04` гасит кнопку (ДА → отношения −1, НЕТ → ок). |
| 4 | failure/restart path | Ребёнок не убивает; влияет на отношения/тон/некролог; рестарт сбрасывает шкалу/кнопку/таймеры вспышки/подсказку. |
| 5 | observability | Кнопка-ребёнок видна и вспыхивает (S9); засчитанное нажатие/пропуск читаются; влияние на отношения видно. |
| 6 | hygiene | Ни ошибок/варнингов; снапшот-гард зелёный (CSV не трогаем). |
| 7 | regressions | Все тесты зелёные (деньги/здоровье/энергия/отношения/Host); новые: открытие после свадьбы+2 + пауза, интервал/окно вспышки, засчитанное нажатие, пропуск 2+ → штраф, анти-заспам (мин. задержка), LT04-гашение (ДА/НЕТ), рестарт-сброс, Enter=CHILD_PRESS только в геймплее. `studio-go suite` HEADLESS зелёный. |

<!-- Время/вспышки — инъекцией dt; CHILD_PRESS через слой ввода. -->

## 3. Approved by founder

`Approved by founder:` заочно — ночной режим («максимум за ночь», 2026-07-19).
Утром — Gate 2.

## 4. Checkpoint log

- **(a) start:** живой ребёнок в Game (сигнал-реакция: интервал/окно/мин.задержка,
  пропуск→штраф отношений, LT04-гашение), CHILD_PRESS=Enter в слой ввода
  (только геймплей), подсказка S5 после свадьбы+2, кнопка-HUD со вспышкой.

## 5. Skeptic verdict

- Model: **Codex GPT-5.5 (read-only, независимый — НЕ owner) = авторитетный гейт.**
  Log: `logs/codex/skeptic-child-*.log`. (Owner-self-review ниже — вспомогательный
  контекст, не заменяет независимый проход.)
- На Gate 2 (утро): «отношения −10%» = −10 абсолютных пунктов (шкалы 0–100),
  шкала ребёнка старт 70 — подтвердить/тюнить; спрайт кнопки — код-плейсхолдер.
- **Codex verdict:** implementation PASS across the board (Enter double-duty, scheduler/lockout, miss
  penalty, LT04, no-death all trace correctly). BLOCK on ONE test-quality gap → **resolved:** the
  PlayMode `ChildButtonTests` now asserts the button's VISIBLE lit state — captures the dim idle tint,
  asserts a bright-gold warm LIT tint (distinct from idle) on the real `Image.color` while flashing
  (through `ReflectChildButton`), and asserts it returns to dim after the press. Tests-only; production
  UI code unchanged. Re-ran HEADLESS gate → GREEN, PlayMode XML 21/21 with the strengthened test.
- Findings / resolutions:
  - **Enter double-duty (highest risk).** Routing lives in `GameDriver.OnInput`, NOT the keymap:
    an Enter/CONFIRM becomes `CHILD_PRESS` only when `State==Playing && ChildOpen` and no tutorial is
    up (the `_tutorialShowing` branch returns first). `KeyboardInputSource.Map` is unchanged (Enter→
    CONFIRM), so the pure key-map tests hold and Enter still starts / restarts / dismisses. At the
    finale `ChildOpen` may still be true, but the `State==Playing` guard keeps CONFIRM=restart there —
    covered by the PlayMode test (opener-start, hint-dismiss, in-window press, finale-restart).
  - **Age-gate hints pause the flash in the driver.** Unlike the pure-Game tests, money/rel/energy
    hints fire+pause as the child ages up; the PlayMode test dismisses them in-loop (also proving
    CONFIRM still dismisses hints mid-gameplay). Resolved.
  - **Drift confounds the relationships assertions.** The single-miss test now asserts «no −10% hit»
    with a drift-tolerant bound (drift ≪ penalty); the child-scale step (clean, only press/lapse move
    it) is the load-bearing assertion.
  - **No death / no brightness coupling.** The child path calls `End()` nowhere (test bottoms the
    scale to 0 while still Playing); `ReflectChildButton`/`UpdateBrightness` never fold child directly
    into brightness — it flows only through the relationships penalty (canon).
- **Machine gate (HEADLESS `studio-go suite --project life-choices`, Editor closed):** GREEN, exit 0.
  Fresh `.studio/test-editmode.xml` = 194/194, `.studio/test-playmode.xml` = 21/21 (baseline 196 →
  215; +18 EditMode `GameChildTests`, +1 PlayMode `ChildButtonTests`; all prior green). No
  ProjectSettings/Packages/scenes.csv drift. Editor reopened.

## 6. Playtest verdict

`Playtest ok:` — (утро, Gate 2)
`Landed:` — (ночной режим: код + машинный гейт зелёные; посадка/коммит — за основательницей утром)
