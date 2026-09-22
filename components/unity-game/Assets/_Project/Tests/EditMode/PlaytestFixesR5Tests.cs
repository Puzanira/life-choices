using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// ГАРДЫ ПАНЧ-ЛИСТА АВТОМАТА r5 (живой плейтест основательницы НА СТОЙКЕ 2026-09-22), чистая часть —
    /// та, что доказывается на `Game` без сцены. Экранная половина (словарь органов, мини-режимы,
    /// типографика финала) живёт в PlayMode-файлах.
    ///
    /// Закрываемый пункт: п.1 «джойстик не слушается — отношения всё время выходят, удержать нельзя».
    /// Жалоба распадается на ДВЕ разные поломки, и гарды тоже парные:
    ///   • СКОРОСТЬ — из края красной зоны до зелёной середины было 24 с, стало ~2.9 с;
    ///   • ОТКЛИК — ввод обязан двигать то, что игрок ВИДИТ, в тот же тик, а не через три кадра.
    /// И оба не имеют права купить себе выживание пассивного игрока (memory: живую шкалу нельзя
    /// калибровать под выживание бездействующего).
    /// </summary>
    public class PlaytestFixesR5Tests
    {
        // ------------------------------------------------------------------ helpers

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Starter() { var c = Plain("I03", 1); c.StartsAgeTimer = true; return c; }

        /// <summary>Середина зелёной зоны — цель «доехать из края» из контракта r5.</summary>
        private static double ZoneMiddle => (Game.RelZoneMin + Game.RelZoneMax) / 2.0;

        /// <summary>
        /// Забег с ОТКРЫТЫМ балансиром и маркером, посаженным на заданное значение. Возраст держим на 22:
        /// энергия (25) и таяние здоровья (30) не открываются, поэтому меряется РОВНО балансир отношений,
        /// а не гонка «кто убьёт раньше». Карточек с запасом — колода не должна кончиться под замером.
        /// </summary>
        private static Game OpenRel(int at)
        {
            var deck = new List<Card> { Starter() };
            for (int i = 0; i < 80; i++) deck.Add(Plain("F" + i, 22));
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);      // разрешить стартер → возраст побежал
            g.Tick(2f);                             // возраст → 21, балансир открылся по возрасту
            Assume.That(g.RelationshipsOpen, Is.True, "предусловие: балансир отношений открыт");
            g.Scales.Relationships = at;
            return g;
        }

        /// <summary>
        /// Держать ось в направлении <paramref name="dir"/> кадрами по <paramref name="dt"/>, пока не
        /// выполнится условие. Возвращает время в секундах (или +∞, если не дождались).
        /// </summary>
        private static float HoldUntil(Game g, int dir, Func<Game, bool> done,
                                       float dt = 1f / 60f, float limit = 40f)
        {
            float t = 0f;
            while (t < limit && g.State == GameState.Playing && g.RelationshipsOpen)
            {
                if (dir != 0) g.HandleInput(dir > 0 ? GameInput.RelationUp : GameInput.RelationDown);
                g.Tick(dt);
                t += dt;
                if (done(g)) return t;
            }
            return float.PositiveInfinity;
        }

        // =====================================================================================
        // п.1а — ОТКЛИК: ввод двигает ТО, ЧТО ВИДИТ ИГРОК, в тот же тик
        // =====================================================================================

        /// <summary>
        /// ЖАЛОБА: «джойстик не слушается… без задержки отклика». Вторая половина корня — не скорость, а
        /// КВАНТОВАНИЕ: механика живёт в целых процентах, интегратор копит дробь и отдаёт шкале только
        /// целый шаг. На старой тяге 4 %/с целый процент набегал 0.42 с — четверть секунды экран НЕ
        /// РЕАГИРОВАЛ на зажатый рычаг, а потом прыгал. Даже на новой тяге первый ЦЕЛЫЙ шаг приходит
        /// только на третий кадр, поэтому маркер рисуется по <see cref="Game.RelationshipsPrecise"/>.
        ///
        /// Гард держит ровно это: ОДИН кадр (1/60 с) с зажатой осью — и видимая величина уже ВЫРОСЛА.
        /// Mutation-proof, причём двусторонний:
        ///   • верни отрисовку на целую шкалу (precise → Scales.Relationships) — за кадр целое не
        ///     меняется, тест краснеет;
        ///   • сломай порядок «ввод → тик» (латч гасится раньше, чем взводится) — за первый кадр
        ///     величина не вырастет, а УПАДЁТ на дрейф, тест краснеет.
        /// </summary>
        [Test]
        public void HeldAxis_MovesTheDrawnMarker_OnTheVeryFirstFrame()
        {
            const float frame = 1f / 60f;

            var g = OpenRel(50);
            float before = g.RelationshipsPrecise;
            g.HandleInput(GameInput.RelationUp);
            g.Tick(frame);
            float after = g.RelationshipsPrecise;

            Assert.Greater(after, before,
                $"ОДИН кадр с зажатой осью обязан сдвинуть видимый маркер ({before:F3} → {after:F3})");

            // …и ровно этот же кадр БЕЗ ввода уводит маркер ВНИЗ — значит вверх его двинул ввод, а не
            // что-то ещё. Без этой половины тест прошёл бы и на шкале, которая просто всегда растёт.
            var idle = OpenRel(50);
            float idleBefore = idle.RelationshipsPrecise;
            idle.Tick(frame);
            Assert.Less(idle.RelationshipsPrecise, idleBefore,
                "тот же кадр без ввода тянет маркер вниз — двигает именно ось, а не фон");
        }

        /// <summary>
        /// Обратная сторона точной отрисовки: она ТОЛЬКО отрисовка. Непрерывное значение обязано
        /// оставаться при своей целой шкале (остаток аккумулятора живёт в (−1; 1)), иначе «красивый»
        /// маркер начал бы врать про зоны и разрыв, которые считаются по целым.
        /// Mutation-proof: если аккумулятор перестанут вычитать (windup), расхождение уедет за 1.
        /// </summary>
        [Test]
        public void PreciseValue_NeverDriftsMoreThanOneStep_FromTheIntegerScale()
        {
            var g = OpenRel(50);
            for (int i = 0; i < 1200; i++)                       // 20 с: вверх, вниз и в упор в потолок
            {
                int dir = (i / 120) % 3 - 1;                     // −1 / 0 / +1 по очереди
                if (dir != 0) g.HandleInput(dir > 0 ? GameInput.RelationUp : GameInput.RelationDown);
                g.Tick(1f / 60f);
                if (g.State != GameState.Playing || !g.RelationshipsOpen) break;

                Assert.Less(Math.Abs(g.RelationshipsPrecise - g.Scales.Relationships), 1.0f,
                    $"кадр {i}: точное значение {g.RelationshipsPrecise:F3} разошлось со шкалой "
                    + $"{g.Scales.Relationships} больше чем на шаг — аккумулятор копит windup");
                Assert.That(g.RelationshipsPrecise, Is.InRange(0f, 100f), "…и не вылезает за шкалу");
            }
        }

        /// <summary>
        /// ПОРЯДОК ИСПОЛНЕНИЯ — БУТЕРБРОД ИЗ ТРЁХ СЛОЁВ, и держать надо ОБА стыка:
        ///   раннер пакета (опрос устройства) → наш источник (латч) → драйвер (<c>Game.Tick</c>).
        ///
        /// ⚠ ВТОРОЙ СТЫК ЗДЕСЬ НЕ ДЛЯ КРАСОТЫ — на нём я и ошибся. Первая редакция r5 пинила источник на
        /// −100 («пораньше всех»), и это ломало ПЕРВЫЙ стык: раннер пакета остаётся на 0, значит мы читали
        /// состояние устройства ДО его обновления, кадром старше (дельта крутилки считается per-poll и
        /// просто терялась). Гард, который смотрел только на «источник раньше драйвера», был ЗЕЛЁНЫЙ —
        /// поломку поймал живой прогон цепочки кабинета, встав на таймауте. Поэтому проверяются оба стыка.
        ///
        /// Mutation-proof: сними атрибут с любого из двух наших классов — соответствующий стык схлопнется
        /// (0 vs 0) и тест краснеет; верни источнику отрицательный приоритет — краснеет первый стык.
        /// </summary>
        [Test]
        public void InputSource_Runs_AfterTheDevicePump_AndBeforeTheTick()
        {
            static int OrderOf(Type t)
                => t.GetCustomAttributes(typeof(DefaultExecutionOrder), inherit: true)
                    .Cast<DefaultExecutionOrder>().Select(a => a.order).FirstOrDefault();

            int pump = OrderOf(typeof(AiGameStudio.ArcadeControls.ArcadeInputRunner));
            int input = OrderOf(typeof(ArcadeInputSource));
            int driver = OrderOf(typeof(GameDriver));

            Assert.Greater(input, pump,
                $"ArcadeInputSource ({input}) обязан идти ПОЗЖЕ раннера пакета ({pump}) — иначе он читает "
                + "состояние устройства до опроса, то есть кадром старше (дельта крутилки теряется)");
            Assert.Less(input, driver,
                $"…и строго РАНЬШЕ GameDriver ({driver}) — иначе латч оси сводится кадром позже, чем ввод");
            Assert.AreEqual(ArcadeInputSource.InputBeforeTick, input,
                "приоритет источника объявлен именованной константой, а не магическим числом");
            Assert.AreEqual(GameDriver.TickAfterInput, driver,
                "…и приоритет драйвера тоже");
        }

        /// <summary>
        /// СОФТЛОК ТУТОРИАЛА ОТНОШЕНИЙ, заведённый ускоренной тягой и пойманный живым прогоном.
        ///
        /// Экран §D закрывается по условию «маркер внутри зелёной зоны непрерывно N секунд», а под
        /// модалкой дрейфа НЕТ (это пауза). На 22 %/с игрок, который просто ДЕРЖИТ джойстик вверх,
        /// вылетает за <see cref="Game.RelZoneMax"/> за 0.9 с и упирается в потолок — вернуть маркер
        /// нечем, экран модальный и морозит игру: автомат висит намертво.
        ///
        /// Гард держит ИНВАРИАНТ ПРОХОДИМОСТИ: сколько бы игрок ни держал ось вверх на этом экране,
        /// маркер обязан остаться в зоне, из которой экран умеет закрыться.
        /// Mutation-proof: сними потолок в TickModalBalancer — маркер уедет в 100 и тест краснеет.
        /// </summary>
        [Test]
        public void TutorialModal_HeldAxis_CannotPinTheMarkerAboveTheGreenZone()
        {
            var g = OpenRel(Scales.RelationshipsStart);
            g.Paused = true;
            g.PausedInputsLive = true;          // ровно режим §D-модалки: время стоит, контрол шкалы живой

            for (int i = 0; i < 600; i++)       // 10 с непрерывного «держу вверх»
            {
                g.HandleInput(GameInput.RelationUp);
                g.Tick(1f / 60f);
            }

            Assert.LessOrEqual(g.Scales.Relationships, Game.RelZoneMax,
                $"под туториальной модалкой маркер не имеет права уехать выше зелёной зоны "
                + $"({g.Scales.Relationships} > {Game.RelZoneMax}) — иначе экран не закроется никогда");
            Assert.GreaterOrEqual(g.Scales.Relationships, Game.RelZoneMin,
                "…и остаётся в зоне, по которой экран умеет закрыться");
        }

        /// <summary>
        /// ПОТОЛОК БЛОКИРУЕТ РОСТ, А НЕ ТЕЛЕПОРТИРУЕТ ВНИЗ (находка код-скептика r5, MINOR).
        ///
        /// Войти в §D-модалку СВЕРХУ законно: живая игра пускает маркер в верхнюю красную зону («задушил
        /// вниманием»), и экран может открыться на 90. Глухое `Min(RelZoneMax, …)` сдёргивало маркер
        /// 90 → 75 ОДНИМ шагом на первом же шевелении рычага вверх — то есть экран сам делал за игрока
        /// ровно ту работу, которую просит сделать, да ещё и рывком на 15 делений.
        ///
        /// Гард держит ОБЕ половины: вверх с 90 не уехать, но и вниз никто не переставляет — спуск только
        /// рычагом, шаг за шагом.
        /// Mutation-proof: верни `int cap = Game.RelZoneMax;` — первый же тик даст 75 и тест краснеет.
        /// </summary>
        [Test]
        public void TutorialModal_EnteredAboveTheZone_BlocksTheRise_WithoutTeleportingDown()
        {
            const int entered = 90;              // вошли в модалку из верхней красной зоны

            var g = OpenRel(entered);
            g.Paused = true;
            g.PausedInputsLive = true;

            g.HandleInput(GameInput.RelationUp);
            g.Tick(1f / 60f);
            Assert.AreEqual(entered, g.Scales.Relationships,
                "первый же тик с зажатой осью вверх не имеет права ПЕРЕСТАВИТЬ маркер — только не пустить выше");

            for (int i = 0; i < 600; i++)        // 10 с «держу вверх» — выше не пускает и не роняет
            {
                g.HandleInput(GameInput.RelationUp);
                g.Tick(1f / 60f);
            }
            Assert.AreEqual(entered, g.Scales.Relationships,
                $"держать вверх сверху зоны = стоять на месте ({entered}), а не съезжать к {Game.RelZoneMax}");

            // …а вниз рычаг по-прежнему ведёт: иначе экран стал бы непроходимым с другой стороны.
            float t = HoldUntil(g, -1, x => x.Scales.Relationships <= Game.RelZoneMax);
            Assert.Less(t, 5f, "рычагом ВНИЗ маркер заводится в зону — экран проходим и сверху");
        }

        /// <summary>
        /// Обратная сторона потолка: он ТУТОРИАЛЬНЫЙ. В ЖИВОЙ игре перелёт выше зоны обязан остаться —
        /// «задушил вниманием» это отдельная механика со своим штрафом, и клампить её было нельзя.
        /// Mutation-proof: перенеси потолок из TickModalBalancer в IntegrateRelationships — тест краснеет.
        /// </summary>
        [Test]
        public void LiveGame_HeldAxis_StillOvershootsIntoTheRedZoneAbove()
        {
            var g = OpenRel(Scales.RelationshipsStart);
            for (int i = 0; i < 300; i++)       // 5 с удержания вверх в ОБЫЧНОЙ игре
            {
                g.HandleInput(GameInput.RelationUp);
                g.Tick(1f / 60f);
                if (g.State != GameState.Playing) break;
            }

            Assert.Greater(g.Scales.Relationships, Game.RelZoneMax,
                "в живой игре удержание вверх по-прежнему уводит выше зоны — «задушил вниманием» живо");
            Assert.IsTrue(g.RelationshipRedZone, "…и красная зона действительно поднята");
        }

        // =====================================================================================
        // п.1б — СКОРОСТЬ: числовые цели контракта r5
        // =====================================================================================

        /// <summary>
        /// ЦЕЛЬ ОСНОВАТЕЛЬНИЦЫ №1: «из края красной зоны до зелёной середины — ~2–3 с». Меряем из ОБОИХ
        /// прочтений «края» (самый низ шкалы и порог разрыва) — оба обязаны уложиться в 3 с.
        /// Mutation-proof: на прежней тяге 4.0 это 24.2 с и 17.9 с — мимо на порядок.
        /// </summary>
        [TestCase(0, TestName = "из самого низа шкалы")]
        [TestCase(Game.RelBreakupFloor, TestName = "с порога разрыва")]
        public void FromTheEdgeOfRed_ReachesTheGreenMiddle_InUnderThreeSeconds(int from)
        {
            var g = OpenRel(from);
            float t = HoldUntil(g, +1, x => x.Scales.Relationships >= ZoneMiddle);

            Assert.IsFalse(g.RelationshipsLost, "по дороге вверх партнёр не успевает уйти");
            Assert.LessOrEqual(t, 3.0f,
                $"из {from} % до зелёной середины ({ZoneMiddle} %) удержание тянет {t:F2} с — "
                + "цель основательницы ~2–3 с");
            Assert.Greater(t, 1.0f,
                $"…но не мгновенно ({t:F2} с): шкалой всё ещё УПРАВЛЯЮТ, а не переключают тумблером");
        }

        /// <summary>
        /// ЦЕЛЬ ОСНОВАТЕЛЬНИЦЫ №2: «из края в край — ≤5 с», в обе стороны. Именно эта пара и задала
        /// число <see cref="Game.RelBalancerPerSec"/>: 100 % / 5 с = 20 %/с НЕТТО, отсюда 22.0 при
        /// дрейфе 1.6 (взята НАИМЕНЬШАЯ тяга, проходящая обе цели).
        /// Mutation-proof: на 4.0 подъём занимает 43 с, спуск 17.6 с.
        /// </summary>
        [Test]
        public void EdgeToEdge_TakesNoMoreThanFiveSeconds_BothWays()
        {
            var up = OpenRel(0);
            float tUp = HoldUntil(up, +1, x => x.Scales.Relationships >= 100);
            Assert.LessOrEqual(tUp, 5.0f, $"снизу доверху удержание тянет {tUp:F2} с — цель ≤5 с");

            var down = OpenRel(100);
            float tDown = HoldUntil(down, -1, x => x.Scales.Relationships <= 0);
            Assert.LessOrEqual(tDown, 5.0f, $"сверху донизу — {tDown:F2} с, цель ≤5 с");
        }

        /// <summary>
        /// Связь чисел с целью, а не с сегодняшним значением: НЕТТО-скорость удержания обязана быть такой,
        /// чтобы «край в край за 5 с» вообще было достижимо. Держит смысл правки, если кто-то соберётся
        /// крутить дрейф: подняли дрейф — обязаны поднять и тягу.
        /// </summary>
        [Test]
        public void TheNetHoldRate_IsFastEnough_ToCrossTheWholeScaleInFiveSeconds()
        {
            double net = Game.RelBalancerPerSec - Game.RelDriftPerSec;
            Assert.GreaterOrEqual(net, 100.0 / 5.0,
                $"нетто удержания {net:F1} %/с — из края в край это {100.0 / net:F1} с, а цель ≤5 с");
        }

        // =====================================================================================
        // п.1в — ЖЕЛЕЗНЫЙ ИНВАРИАНТ: ускоренная шкала НЕ купила выживание бездействующему
        // =====================================================================================

        /// <summary>
        /// Прогнать забег, в котором игрок держит ось ВВЕРХ долю <paramref name="duty"/> каждого цикла в
        /// 2 с (остальное — бездействие). Возвращает, пережил ли он <paramref name="span"/> секунд.
        /// </summary>
        private static bool SurvivesDutyCycle(double duty, float span = 120f, float period = 2f)
        {
            var g = OpenRel(Scales.RelationshipsStart);
            const float dt = 1f / 60f;
            float t = 0f;
            while (t < span && g.State == GameState.Playing)
            {
                if (g.RelationshipsLost) return false;
                if (t % period < period * duty) g.HandleInput(GameInput.RelationUp);
                g.Tick(dt);
                t += dt;
            }
            return !g.RelationshipsLost;
        }

        /// <summary>
        /// MEMORY-УРОК ЭТОЙ ИГРЫ, ЖЕЛЕЗНО: живую шкалу нельзя калибровать так, чтобы пассивный игрок
        /// выживал (уже ловили на энергии, 0.7 → 1.7 %/с). Тяга выросла в 5.5 раза — проверяем, что
        /// наказание бездействия от этого не испарилось: не только полный ноль, но и «иногда дёргаю»
        /// обязаны кончиться разрывом.
        /// Mutation-proof: подними тягу так, чтобы равновесие уехало ниже 5 % — вторая строка краснеет.
        /// </summary>
        [TestCase(0.00, TestName = "полное бездействие")]
        [TestCase(0.05, TestName = "изредка дёргает (5 % времени)")]
        public void PassivePlayer_StillLosesTheRelationship(double duty)
        {
            Assert.IsFalse(SurvivesDutyCycle(duty),
                $"игрок, который держит рычаг {duty:P0} времени, обязан остаться без партнёра — "
                + "живая шкала требует ДЕЙСТВИЯ");
        }

        /// <summary>
        /// Вторая половина того же инварианта — шкала СЛУШАЕТСЯ, когда играют. Умеренный игрок, который
        /// возвращается к джойстику каждые пару секунд, отношения удерживает. Без этой половины «пассивный
        /// умирает» можно было бы выполнить, просто сделав шкалу неудерживаемой — то есть вернув ровно ту
        /// поломку, на которую и жаловалась основательница.
        /// </summary>
        [TestCase(0.20, TestName = "умеренный (20 % времени)")]
        [TestCase(0.50, TestName = "активный (50 % времени)")]
        public void PlayerWhoActuallyPlays_KeepsTheRelationship(double duty)
        {
            Assert.IsTrue(SurvivesDutyCycle(duty),
                $"игрок, который держит рычаг {duty:P0} времени, обязан удержать отношения — "
                + "иначе шкала снова «всё время выходит»");
        }

        /// <summary>
        /// ОКНО РАЗРЫВА ПРИ БЕЗДЕЙСТВИИ — канон r3 (30–45 с из зелёной середины), НЕ ТРОНУТО правкой
        /// тяги. Дрейф остался 1.6 %/с именно потому, что этот коридор — отдельное решение
        /// основательницы, и разгонять его заодно со скоростью отклика было нельзя.
        /// Mutation-proof: тронь <see cref="Game.RelDriftPerSec"/> в любую сторону — вылет из коридора.
        /// </summary>
        [Test]
        public void IdleBreakupWindow_StaysInTheR3Corridor_AfterTheSpeedUp()
        {
            var g = OpenRel(Scales.RelationshipsStart);
            float t = 0f;
            int guard = 0;
            while (!g.RelationshipsLost && g.State == GameState.Playing && guard++ < 6000)
            {
                g.Tick(0.1f);                        // ни одного ввода по оси
                t += 0.1f;
            }

            Assert.IsTrue(g.RelationshipsLost, "полное бездействие по-прежнему приводит к разрыву");
            Assert.That(t, Is.InRange(30f, 45f),
                $"из зелёной середины до разрыва при бездействии {t:F1} с — канон-коридор r3 30–45 с");
        }
    }
}
