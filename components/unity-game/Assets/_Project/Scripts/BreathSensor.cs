namespace ThanksNoThanks
{
    /// <summary>
    /// PURE level detector for the cabinet's height sensor («датчик высоты»). It answers ONE question every
    /// frame — «датчик сейчас поднят?» — turning the sensor's continuous 0..1 value into the HELD signal the
    /// energy mechanic runs on: while the sensor sits above mid-travel the battery fills, the moment it drops
    /// the filling stops (founder's live playtest 2026-08-07: «просто зажать датчик высоты, пока батарейка
    /// не заполнится»).
    ///
    /// ⚠ ЭТО НЕ ДЕТЕКТОР ФРОНТА. До 2026-08-07 тот же класс (BreathLever) выдавал ОДИН импульс на подъём и
    /// кормил ритм-гейт BreathRhythm — механика «дыши раз в ~2 секунды». Основательница её сняла целиком:
    /// удержание стало легитимным и ЕДИНСТВЕННЫМ способом восстанавливать энергию, ритм-гейт удалён.
    ///
    /// Гистерезис (<see cref="High"/> вверх / <see cref="Low"/> вниз) описывает ФИЗИКУ датчика: рука должна
    /// перевалить середину хода, чтобы сигнал встал, и уйти заметно ниже, чтобы он снялся — иначе дрожание
    /// руки (или шум платы) ровно на пороге давало бы рваный сигнал и мигающую батарею. Числа НЕ менялись
    /// сменой механики: это тот же физический жест, что основательница уже подписала.
    ///
    /// Вынесен из <see cref="ArcadeInputSource"/> (там живёт Unity-обвязка, её не прошагать детерминированно),
    /// поэтому ВЕСЬ клавиатурный путь — HeightSimulator пакета → этот детектор → <see cref="Game"/> — гоняется
    /// в EditMode-симе (BreathKeyboardPathTests).
    /// </summary>
    public sealed class BreathSensor
    {
        /// <summary>Порог ПОДЪЁМА: перевалили середину хода — сигнал «поднят» встал.</summary>
        public const float High = 0.5f;
        /// <summary>Порог ОТПУСКАНИЯ (ниже High — гистерезис): опустились сюда — сигнал снялся.</summary>
        public const float Low = 0.35f;

        private bool _raised;

        /// <summary>True, пока датчик считается поднятым (последнее состояние гистерезиса).</summary>
        public bool Raised => _raised;

        /// <summary>Скормить текущее значение датчика; возвращает «поднят ли он СЕЙЧАС».</summary>
        public bool Step(float value)
        {
            if (!_raised) { if (value >= High) _raised = true; }
            else if (value < Low) _raised = false;
            return _raised;
        }

        public void Reset() => _raised = false;
    }
}
