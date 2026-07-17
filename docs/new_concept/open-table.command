#!/bin/bash
# Двойной клик открывает таблицу сцен в браузере, читая живой scenes.csv.
# Правь scenes.csv в текстовом редакторе и обновляй страницу (Cmd+R).
cd "$(dirname "$0")" || exit 1
PORT=8823
echo "Запускаю локальный сервер и открываю таблицу…"
python3 -m http.server "$PORT" >/dev/null 2>&1 &
SRV=$!
sleep 1
open "http://localhost:$PORT/scenes.html"
echo ""
echo "Таблица открыта: http://localhost:$PORT/scenes.html"
echo "Правь scenes.csv → обновляй страницу (Cmd+R)."
echo "Чтобы закрыть — просто закрой это окно Терминала."
wait $SRV
