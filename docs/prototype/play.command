#!/bin/bash
# Двойной клик — запускает прототип «Спасибо, не надо» в браузере.
# Сервер поднимается из корня проекта, чтобы игра читала живой docs/scenes.csv.
cd "$(dirname "$0")/.." || exit 1
PORT=8842
echo "Запускаю прототип…"
python3 -m http.server "$PORT" >/dev/null 2>&1 &
SRV=$!
sleep 1
open "http://localhost:$PORT/prototype/index.html"
echo ""
echo "Прототип открыт: http://localhost:$PORT/prototype/index.html"
echo "Управление: ← ДА · → СПАСИБО НЕ НАДО · Пробел — деньги · ↑↓ — отношения · E — энергия · Enter — ребёнок."
echo "Чтобы закрыть — просто закрой это окно Терминала."
wait $SRV
