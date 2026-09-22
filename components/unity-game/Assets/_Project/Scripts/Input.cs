using System;

namespace ThanksNoThanks
{
    /// <summary>
    /// Semantic input events the game logic reacts to. Game logic subscribes to these
    /// and NEVER reads a keyboard/serial device directly — swapping the input hardware
    /// (keyboard now, Arduino/COM later) is a drop-in replacement of a single
    /// <see cref="IInputSource"/> implementation, the logic untouched.
    /// </summary>
    public enum GameInput
    {
        AnswerYes,       // ДА          (keyboard: ← )
        AnswerNo,        // СПАСИБО, НЕ НАДО (keyboard: → )
        Confirm,         // start / dismiss-hint / restart — keyboard Enter ONLY (the sole confirm key)
        MoneyTick,       // крутилка денег, FRESH physical keydown — crank-only, inert outside gameplay

        /// <summary>
        /// Autorepeat crank from HELD Space (~4/с) — input-layer kind, income-only. The driver lets it
        /// crank during gameplay (translated to <see cref="MoneyTick"/> after the income cap) but keeps
        /// it INERT outside Playing and on the tutorial: held Space must never confirm screens or
        /// dismiss hints. <see cref="Game"/> never receives this value.
        /// </summary>
        MoneyTickRepeat,

        /// <summary>
        /// ENERGY_HOLD — «датчик высоты ПОДНЯТ» (кабинет: ЛЮБОЙ из двух датчиков — HeightA ИЛИ HeightB —
        /// выше середины хода; эмуляция: зажатая клавиша любого из них). Как и
        /// <see cref="RelationRight"/>, это НЕ дискретное событие, а УДЕРЖИВАЕМЫЙ
        /// сигнал: источник переиздаёт его КАЖДЫЙ кадр, пока датчик поднят, а <see cref="Game"/> латчит его
        /// и применяет ровно один такт роста энергии, после чего гасит латч (модель «потребить за тик»).
        /// Держишь — батарея наполняется <see cref="Game.EnergyRegenPerSec"/> %/с; отпустил — рост встал, а
        /// обычный дренаж продолжается. Инертен, пока энергия не открыта и пока идёт S5-пауза.
        ///
        /// ⚠ Заменил прежний ENERGY_PULSE (импульс на подъёме + ритм-гейт «раз в ~2 секунды») по слову
        /// основательницы, живой плейтест 2026-08-07: «просто зажать датчик высоты, пока батарейка не
        /// заполнится». Ритма в игре больше нет — ни гейта, ни отклика «не в ритм».
        ///
        /// ⚠ r7 п.2 («сделать оба датчика, чтобы работали»): значение производит ЛЮБОЙ из двух датчиков
        /// высоты, а не только A. Источник шлёт РОВНО ОДИН EnergyHold за кадр, даже когда подняты оба —
        /// две руки заряжают ровно так же быстро, как одна (MAX, не сумма).
        /// </summary>
        EnergyHold,

        /// <summary>
        /// RELATION_AXIS → — «тянуть балансир отношений ВПРАВО» (кабинет: джойстик вправо; эмуляция: →).
        /// Unlike the discrete answer/crank events this is a HELD axis: the source re-emits it EVERY frame
        /// the lever is over, and <see cref="Game"/> applies one tick's worth of pull then clears the axis
        /// (a consume-per-tick model), so holding pulls the marker steadily while released lets the drift
        /// take over. Inert unless relationships are open and the run is live/unpaused. Keeps
        /// <see cref="Game"/> semantic-only.
        ///
        /// ⚠ r7 п.1 — ОСЬ И ЕЁ СЕМАНТИКА. До r7 это звалось RelationUp и ехало по ВЕРТИКАЛИ джойстика,
        /// а шкала отношений нарисована ГОРИЗОНТАЛЬНЫМ балансиром: рычаг спорил с картинкой. ВПРАВО
        /// поднимает шкалу, потому что маркер-сердце едет вправо с ростом значения (GameDriver:
        /// RelationsTrackFraction монотонно растёт, 0 → у лица парня слева, 100 → у лица девушки справа).
        /// Правило одно: тяни туда, куда хочешь сдвинуть маркер.
        /// </summary>
        RelationRight,

        /// <summary>RELATION_AXIS ← — same held-axis model as <see cref="RelationRight"/>, pulling the
        /// balancer marker LEFT, i.e. DOWN the scale (кабинет: джойстик влево; эмуляция: ←). Emitted every
        /// frame the lever is held. If both ← and → are held the source resolves to → (safe direction —
        /// away from the break-up end) — the two are mutually exclusive on the wire.</summary>
        RelationLeft,

        /// <summary>
        /// CHILD_PRESS — «поднять трубку» звонящего ребёнка (кабинет: кнопка «!» / BangButton, dev-клавиша
        /// Enter; имеет смысл только в геймплее). Enter is CONFIRM everywhere else, so the DRIVER
        /// decides context: an Enter/CONFIRM becomes CHILD_PRESS ONLY while Playing with the child scale
        /// open and no tutorial up; otherwise it stays <see cref="Confirm"/> (start/restart/dismiss). A
        /// discrete keydown — <see cref="Game"/> honours it only inside the open flash window (else it
        /// arms the anti-pre-spam lockout). Keeps <see cref="Game"/> semantic-only (no keys/ports).
        /// </summary>
        ChildPress,

        /// <summary>
        /// EXIT — the cabinet MenuButton. Arcade contract §5: the game must end the current run cleanly
        /// (stop coroutines/timers, drop back to a fresh opener life) without <c>Application.Quit</c> and
        /// without leaving static / DontDestroyOnLoad state. Handled entirely by the driver; the pure
        /// <see cref="Game"/> never receives this value (the driver resets Game via a clean opener return).
        /// </summary>
        Exit
    }

    /// <summary>
    /// A source of semantic game inputs. The only contract between the physical world
    /// (keyboard, serial controller, test harness) and the game logic.
    /// </summary>
    public interface IInputSource
    {
        event Action<GameInput> Received;
    }
}
