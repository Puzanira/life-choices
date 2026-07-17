# Подключение кастомных контроллеров («Спасибо, не надо»)

Как привязать свой контроллер (рычаг, кнопки, Arduino/COM, жесты) к игре.
Актуально для рантайма `ThanksNoThanks` (пивот 2026-07-17); канон слоя ввода —
`docs/new_concept/handoff.md` §4.

## Архитектура: смысловые события, не клавиши

Игра слушает **семантические события**, железо к логике не прибито:

```
[твой девайс] → IInputSource (один класс) → event Received(GameInput) → Game.HandleInput
```

Файлы (`components/unity-game/Assets/_Project/Scripts/`):

| Файл | Что это |
|------|---------|
| `Input.cs` | `enum GameInput { AnswerYes, AnswerNo, Confirm }` + `interface IInputSource { event Action<GameInput> Received; }` |
| `KeyboardInputSource.cs` | клавиатурная реализация: **← = AnswerYes (ДА)**, **→ = AnswerNo (СПАСИБО, НЕ НАДО)**, Enter/Numpad/Пробел = Confirm. Чистый статический `Map()` — маппинг тестируем без Unity. |
| `GameDriver.cs` | строка 61: если `Input` не задан — вешает `KeyboardInputSource`; строка 62: `Input.Received += _game.HandleInput`. |

Логика (`Game.cs`) НЕ знает про клавиши — только про `GameInput`.

## Как подключить свой контроллер

1. Создай класс рядом с `KeyboardInputSource.cs`:

```csharp
using System;
using UnityEngine;

namespace ThanksNoThanks
{
    /// Читает COM-порт (Arduino) и превращает строки в семантические события.
    public sealed class SerialInputSource : MonoBehaviour, IInputSource
    {
        public event Action<GameInput> Received;

        private void Update()
        {
            // 1) прочитать свой девайс (System.IO.Ports.SerialPort / жесты / HID)
            // 2) на строку "LEVER_L\n"  -> Received?.Invoke(GameInput.AnswerYes);
            //    на строку "LEVER_R\n"  -> Received?.Invoke(GameInput.AnswerNo);
            //    на строку "BTN\n"      -> Received?.Invoke(GameInput.Confirm);
        }
    }
}
```

2. В сцене `ThanksNoThanks.unity` на объекте `Game` (где `GameDriver`) добавь свой
   компонент и присвой его в поле `GameDriver.Input` ДО `Start` (или правкой одной
   строки 61 в `GameDriver.cs`). Больше ничего в игре не меняется.

- Альтернатива без кода вообще: Arduino как **HID-клавиатура**, шлющая ←/→/Enter —
  тогда работает штатный `KeyboardInputSource`.
- Событие шли **один раз на жест** (по фронту), не каждый кадр удержания.
- Будущие события живых шкал (`MONEY_TICK`, `RELATION_AXIS`, `ENERGY_PULSE`,
  `CHILD_PRESS`) добавятся в `GameInput` в инкременте живых шкал — тем же паттерном.

## Точный маппинг (канон handoff.md §4)

| Событие | Механика | Клавиатура | Железо (потом) |
|---------|----------|------------|----------------|
| `AnswerYes` | ДА | ← | рычаг влево |
| `AnswerNo` | СПАСИБО, НЕ НАДО | → | рычаг вправо |
| `Confirm` | старт/рестарт экрана | Enter/Пробел | любая кнопка |
