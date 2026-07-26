# Increment: arcade-packaging (life-choices)

Status: GATE 1 APPROVED («ок на все три», 2026-07-25, ночной прогон). Гейт 2 утром
одним заходом через хаб-меню.
Program: ARCADE_CABINET_SPEC этап ②; контракт ARCADE_INTEGRATION_CONTRACT.

## Что делаем
1. Контракт автомата: `Assets/_Project/` + `package.json`
   (com.aigamestudio.game-life-choices) + `game.json` (entry LifeChoices.unity).
2. **Ввод → ArcadeInput** (arcade-controls file:-зависимость):
   **ДА = GreenButton, НЕТ = RedButton** (вместо →/←; решение основательницы),
   рестарт = RedButton на финальном экране, **выход по MenuButton** = чистое
   завершение (контракт §5). Таймер/логика карточек не меняются.
3. Тесты: существующие (EditMode 29 + PlayMode 8) зелёные на FakeBackend;
   + чистый выход по MenuButton; + тест манифеста.

## Done-контракт
1. package.json + game.json валидны; 2. ввод только ArcadeInput (скан);
3. suite зелёный headless; 4. MenuButton-выход чистый; 5. скриншот игры
(batch, 0 мадженты) — проверяет Maintainer; 6. гейт 2 утром через хаб.
