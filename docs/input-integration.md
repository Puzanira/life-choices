# Подключение кастомных контроллеров (жесты / да-нет)

Как привязать свой контроллер (жесты, физические кнопки, серийный девайс/Arduino,
распознавание жестов с камеры) к игре life-choices. Игре нужны всего **два
действия**: «да» и «нет» (плюс «начать заново» на экране некролога).

Статус на 2026-07-15: инкремент 1 (`c653e6d`). Весь ввод — в одном файле.

---

## TL;DR — куда звонить

Вся точка входа — публичные методы на компоненте `LifeGameController`:

| Метод | Что делает | Когда звать |
|-------|-----------|-------------|
| `ChooseYes()` | выбор **да / принять** | во время карточки |
| `ChooseNo()`  | выбор **нет / отклонить** | во время карточки |
| `RestartLife()` | новая жизнь с начала | на экране некролога |

Полный путь файла:
`components/unity-game/Assets/_Project/Scripts/LifeGameController.cs`

Твой контроллер должен просто **вызвать `ChooseYes()` или `ChooseNo()`** — и всё.
Таймер, применение эффектов, смерть, некролог и рестарт уже завязаны внутри.
Клавиатура (→/←) при этом продолжит работать параллельно.

---

## Где сейчас читается ввод

Файл `LifeGameController.cs`, метод `Update()` (строки **87–121**). Три куска:

1. **Экран некролога → рестарт** (строки 91–104): любая из →, ←, пробел, Enter
   вызывает `RestartLife()`.
2. **Отсчёт таймера** (строки 106–112): каждый кадр уменьшает `TimeRemaining`; на
   нуле — `ForceTimeout()` (= «нет» + Настроение−4). **Это трогать не надо.**
3. **Игровой ввод** (строки 114–118): клавиатура →/← :
   ```csharp
   if (kb != null)
   {
       if (kb.rightArrowKey.wasPressedThisFrame) { ChooseYes(); return; }
       if (kb.leftArrowKey.wasPressedThisFrame)  { ChooseNo();  return; }
   }
   ```

Именно строки 114–118 (игровой ввод) и 96–102 (рестарт) — то, что заменяет/дополняет
кастомный контроллер.

Полезное состояние для чтения (только чтение):

| Свойство | Смысл |
|----------|-------|
| `IsShowingObituary` | сейчас показан некролог (можно рестартить) |
| `Game.IsDead` | жизнь закончена |
| `TimeRemaining` | сколько секунд осталось на текущий выбор |

---

## Способ A (рекомендую) — отдельный скрипт-мост, БЕЗ правки игры

Методы `ChooseYes/ChooseNo/RestartLife` — `public`, поэтому лучший способ: положить
**рядом новый компонент**, который читает твой контроллер и дёргает эти методы.
`LifeGameController.cs` при этом не меняется, клавиатура остаётся как запасной ввод,
тесты не ломаются.

Пример-заготовка — создай файл
`components/unity-game/Assets/_Project/Scripts/CustomControllerInput.cs`:

```csharp
using UnityEngine;

namespace LifeChoices
{
    /// Мост между кастомным контроллером (жесты / да-нет) и игрой.
    /// Повесь на тот же GameObject, где висит LifeGameController.
    [RequireComponent(typeof(LifeGameController))]
    public sealed class CustomControllerInput : MonoBehaviour
    {
        private LifeGameController _game;

        private void Awake() => _game = GetComponent<LifeGameController>();

        private void Update()
        {
            // 1) прочитать свой девайс/жест здесь:
            bool yes = ReadYesSignal();   // <-- твоя логика
            bool no  = ReadNoSignal();    // <-- твоя логика

            // 2) на экране некролога любой сигнал = начать заново
            if (_game.IsShowingObituary)
            {
                if (yes || no) _game.RestartLife();
                return;
            }

            // 3) во время карточки — прокинуть выбор
            if (yes) { _game.ChooseYes(); return; }
            if (no)  { _game.ChooseNo();  return; }
        }

        // Верни true в ТОТ кадр, когда пришёл жест/нажатие «да».
        private bool ReadYesSignal()
        {
            // TODO: опрос своего контроллера. Примеры:
            //  - HID/геймпад через Input System: Gamepad.current.buttonSouth.wasPressedThisFrame
            //  - серийный порт/Arduino: прочитать строку и сравнить
            //  - распознаватель жестов: подписаться на событие и выставить флаг
            return false;
        }

        private bool ReadNoSignal()
        {
            return false;
        }
    }
}
```

Дальше в Unity: выдели в сцене объект **`GameController`** (на нём висит
`LifeGameController`) и **Add Component → Custom Controller Input**. Готово —
играбельно и с клавиатуры, и с твоего девайса.

> Если сигнал приходит асинхронно (событие от распознавателя жестов, колбэк
> серийного порта) — не читай в `Update`, а в обработчике события выставляй
> `_pendingYes = true`, а в `Update` разбирай флаг и сбрасывай его.

---

## Способ B — править `Update()` в игре напрямую

Если хочешь вообще убрать клавиатуру и читать только свой девайс — замени тело
блоков в `LifeGameController.Update()`:

- строки **114–118** (игровой выбор) — на опрос своего контроллера с вызовом
  `ChooseYes()` / `ChooseNo()`;
- строки **96–102** (рестарт из некролога) — на свой сигнал с вызовом
  `RestartLife()`.

Таймер (строки 106–112) не трогай. Способ более инвазивный и требует прогон
гейта (`studio-go suite`) — поэтому способ A обычно удобнее.

---

## Важные детали

- **Система ввода — новая (Input System).** В файле уже `using
  UnityEngine.InputSystem;` и чтение через `Keyboard.current`. Если твой девайс —
  HID/геймпад, читай его так же через Input System (`Gamepad.current`,
  `InputSystem.onDeviceChange` и т.п.).
- **asmdef.** Новый скрипт в папке `Scripts/` попадает в сборку `LifeChoices.Runtime`
  (`Scripts/LifeChoices.Runtime.asmdef`). Он уже ссылается на `Unity.InputSystem` и
  `UnityEngine.UI`. Если контроллеру нужна доп. библиотека/пакет — добавь ссылку в
  этот asmdef.
- **Готовый actions-ассет.** В `Assets/InputSystem_Actions.inputactions` лежит
  шаблонный набор действий из Unity — **игра его сейчас НЕ использует** (читает
  `Keyboard.current` напрямую). Если захочешь вести ввод через Action Maps, можно,
  но это отдельная переделка `LifeGameController`.
- **Тайминг «да».** Возвращай сигнал в тот кадр, когда действие произошло
  (`wasPressedThisFrame`-стиль), иначе за один зажатый жест проскочит несколько
  карт.
- **Timeout.** Не нажал за окно карты — само сработает как «нет» + Настроение−4.
  Кастомному контроллеру про таймаут думать не надо.
- **Тесты.** Тесты дёргают `StartRun()/ChooseYes()/ChooseNo()` напрямую и от ввода
  не зависят — способ A их не затрагивает.

---

## Где это в сцене

- Сцена: `components/unity-game/Assets/_Project/Scenes/LifeChoices.unity`.
- Объект **`GameController`** — на нём `LifeGameController` (сам строит UI в `Awake`,
  других ссылок в сцене нет). Твой скрипт-мост вешаешь на этот же объект.
