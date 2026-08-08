using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// ГЛАВНЫЙ ГАРД ОТРЕЗКА 0 (done contract §4). Масштаб Δ вырос на порядок: «Здр −2» стоит теперь
    /// −15 п.п., а не −2. Дизайн-док прямо требует проверить, «не начал ли умеренный игрок умирать
    /// раньше семидесяти» (§8) — и одновременно чтобы ПАССИВНЫЙ по-прежнему умирал рано, иначе живая
    /// шкала перестаёт требовать ввода (memory «life-choices: live scales must require input»).
    ///
    /// Симуляция ЛИНГЕРИНГ-типа: живой забег на чистом <see cref="Game"/> с инъекцией времени (dt), без
    /// движка и без стены часов. Профиль игрока задаётся долями: как часто он крутит крутилку, держит
    /// датчик, работает балансиром и успевает отвечать. Никакой «идеальной игры» — умеренный именно
    /// смешивает: половину карточек берёт, половину отвергает, реагирует с опозданием.
    /// </summary>
    public class BalanceGuardTests
    {
        private const float Dt = 0.05f;
        private const int MaxTicks = 40000;   // ~33 минуты модельного времени — заведомо больше жизни

        /// <summary>Профиль игрока: доли участия в каждой из живых механик.</summary>
        private sealed class Profile
        {
            public string Name;
            public bool Answers;         // отвечает ли на карточки вообще (иначе всё уходит в таймаут)
            public double AnswerAt;      // на какой доле таймера отвечает (0.5 — на середине)
            public double CrankDuty;     // доля тиков, в которые крутит крутилку
            public int BreathBelow;      // держит датчик, пока энергия ниже этого уровня
            public int BalanceBelow;     // тянет балансир вверх, пока отношения ниже этого уровня
            public double BlitzAccuracy; // доля верных нажатий в блице
            public bool CatchesPulse;    // ловит ли импульс в депрессии
            public bool CoinSaysNo;      // монетка (таймауты, ±N) всегда НЕТ — см. Passive
        }

        /// <summary>
        /// ПАССИВНЫЙ — не трогает ничего. Монетка прибита к НЕТ намеренно: иначе половина прогонов
        /// кончалась бы случайной ФАТАЛЬНОЙ карточкой (палец в розетке в четыре года), и тест доказывал
        /// бы не «бездействие убивает», а «рандом убивает». Нам нужно первое: смерть ОТ ШКАЛ.
        /// </summary>
        private static readonly Profile Passive = new()
        {
            Name = "пассивный",
            Answers = false, AnswerAt = 0, CrankDuty = 0,
            BreathBelow = 0, BalanceBelow = 0, BlitzAccuracy = 0, CatchesPulse = false,
            CoinSaysNo = true,
        };

        /// <summary>
        /// УМЕРЕННЫЙ — реалистичная смесь, а не идеальная игра: крутит примерно половину времени,
        /// вспоминает про датчик только когда энергия просела ниже 40, балансир трогает лишь когда
        /// отношения вываливаются из зоны, в блице ошибается каждый третий раз, отвечает не мгновенно.
        /// </summary>
        private static readonly Profile Moderate = new()
        {
            Name = "умеренный",
            Answers = true, AnswerAt = 0.5, CrankDuty = 0.5,
            BreathBelow = 40, BalanceBelow = 50, BlitzAccuracy = 0.66, CatchesPulse = true,
        };

        /// <summary>
        /// ЛИНГЕРИНГ — тот же умеренный, но МЕДЛЕННЫЙ: думает почти до конца таймера на каждой карточке.
        /// Худший случай для декея здоровья: он идёт по РЕАЛЬНОМУ времени, а не по возрасту, поэтому
        /// именно медленный игрок проводит под ним больше всего секунд. Гард обязан держаться и на нём.
        /// </summary>
        private static readonly Profile Lingering = new()
        {
            Name = "лингеринг",
            Answers = true, AnswerAt = 0.95, CrankDuty = 0.5,
            BreathBelow = 40, BalanceBelow = 50, BlitzAccuracy = 0.66, CatchesPulse = true,
        };

        private static readonly Profile Active = new()
        {
            Name = "активный",
            Answers = true, AnswerAt = 0.25, CrankDuty = 1.0,
            BreathBelow = 95, BalanceBelow = 70, BlitzAccuracy = 1.0, CatchesPulse = true,
        };

        /// <summary>Один прожитый забег: возраст смерти и причина.</summary>
        private static (int Age, string Cause) LiveOneLife(Profile p, int seed)
        {
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "живая колода читается из Resources");
            var all = CardLoader.ParseAll(csv.text);

            var rng = new System.Random(seed);
            var g = new Game(() => DeckSampler.BuildPlan(all, new System.Random(seed)),
                             coin: () => !p.CoinSaysNo && rng.Next(2) == 0)
            {
                // Кризис/депрессия детерминируются, чтобы прогон не зависел от внутреннего RNG.
                BlitzNormalOnYesRoll = () => rng.Next(2) == 0,
                DepressionTriggerRoll = () => rng.NextDouble() < 0.5,
            };
            g.StartLife();

            for (int i = 0; i < MaxTicks && g.State == GameState.Playing; i++)
            {
                FeedInputs(g, p, rng);
                g.Tick(Dt);
            }
            return ((int)g.Age, g.Cause);
        }

        private static void FeedInputs(Game g, Profile p, System.Random rng)
        {
            if (g.InDepression)
            {
                // Ловля стоит на кнопке «!» (CHILD_PRESS) — решение основательницы 2026-08-08.
                if (p.CatchesPulse && g.DepressionPulsing) g.HandleInput(GameInput.ChildPress);
                return;
            }
            if (g.Phase == CrisisPhase.Blitz)
            {
                bool correct = rng.NextDouble() < p.BlitzAccuracy;
                bool pressYes = correct == g.BlitzNormalOnYes;   // верно = нажать рычаг «ВСЁ НОРМАЛЬНО»
                g.HandleInput(pressYes ? GameInput.AnswerYes : GameInput.AnswerNo);
                return;
            }
            if (g.Phase == CrisisPhase.Impulse)
            {
                // Умеренный импульсам поддаётся через раз — на то они и импульсы.
                if (p.Answers && rng.Next(2) == 0) g.HandleInput(GameInput.AnswerNo);
                return;
            }

            // Живые шкалы: крутилка, датчик высоты, балансир, трубка ребёнка.
            if (rng.NextDouble() < p.CrankDuty) g.HandleInput(GameInput.MoneyTick);
            if (g.Scales.Energy < p.BreathBelow) g.HandleInput(GameInput.EnergyHold);
            if (g.RelationshipsOpen)
            {
                if (g.Scales.Relationships < p.BalanceBelow) g.HandleInput(GameInput.RelationUp);
                else if (g.Scales.Relationships > Game.RelZoneMax) g.HandleInput(GameInput.RelationDown);
            }
            if (g.ChildOpen && g.ChildFlashing && p.Answers) g.HandleInput(GameInput.ChildPress);

            // Ответ на карточку — не мгновенный: игрок думает до заданной доли таймера.
            if (p.Answers && g.CurrentCard != null
                && g.CardTimer <= g.CardTimerMax * (1.0 - p.AnswerAt))
            {
                // ФАТАЛЬНЫЕ КАРТОЧКИ ЧЕЛОВЕК ЧИТАЕТ. «Сунуть палец в розетку», «белый порошок», «селфи
                // на краю крыши» — это не выбор в смеси, а объявленная смерть; игрок, который на них
                // соглашается, не «умеренный», а суицидальный, и мерил бы такой прогон не баланс, а
                // рандом. Отказ от них — единственная неслучайная часть профиля.
                bool deadly = g.CurrentCard.YesIsFatal || g.CurrentCard.DelayedFatalYears > 0;
                bool yes = !deadly && rng.Next(2) == 0;
                g.HandleInput(yes ? GameInput.AnswerYes : GameInput.AnswerNo);
            }
        }

        private static (int Min, int Median) AgesAcrossSeeds(Profile p, int seeds = 12, bool report = false)
        {
            var ages = new List<int>();
            var sb = report ? new StringBuilder($"[balance-seeds] {p.Name}: ") : null;
            for (int s = 1; s <= seeds; s++)
            {
                var (age, cause) = LiveOneLife(p, s);
                ages.Add(age);
                sb?.Append($"#{s}={age} «{cause}»  ");
            }
            if (sb != null) Debug.Log(sb.ToString());
            ages.Sort();
            return (ages[0], ages[ages.Count / 2]);
        }

        // ================================================================ гард

        [TestCase("умеренный")]
        [TestCase("лингеринг")]
        public void ModeratePlayer_LivesToSeventy_AcrossSeeds(string which)
        {
            // ГЛАВНЫЙ ГАРД. Держится и на быстром умеренном, и на медленном (лингеринг) — декей идёт по
            // РЕАЛЬНОМУ времени, поэтому «думаю до последней секунды» это худший случай, а не придирка.
            var p = which == "лингеринг" ? Lingering : Moderate;
            var (min, median) = AgesAcrossSeeds(p, report: true);
            Debug.Log($"[balance] {p.Name}: min={min} median={median} (декей {Game.HealthDecayPerSec}%/с)");
            Assert.GreaterOrEqual(min, 70,
                $"{p.Name} игрок обязан доживать до 70 (худший прогон {min}, медиана {median}). "
                + "Новый масштаб Δ съел жизнь — крутить декей здоровья, см. §4 дока инкремента.");
        }

        [Test]
        public void Passive_StillDiesEarly()
        {
            // Зеркало гарда: смягчение баланса не должно превратить бездействие в выигрышную стратегию.
            var (min, median) = AgesAcrossSeeds(Passive, report: true);
            Debug.Log($"[balance] пассивный: min={min} median={median}");
            Assert.Less(median, 70, $"пассивный игрок обязан умирать рано (медиана {median})");
        }

        [Test]
        public void ActivePlayer_OutlivesTheModerate_WhoOutlivesThePassive()
        {
            // Порядок профилей — то самое «накрутил или ленился решает, доживёшь ли» (§3.3).
            int active = AgesAcrossSeeds(Active).Median;
            int moderate = AgesAcrossSeeds(Moderate).Median;
            int passive = AgesAcrossSeeds(Passive).Median;
            Debug.Log($"[balance] медианы: активный={active} умеренный={moderate} пассивный={passive}");
            Assert.GreaterOrEqual(active, moderate, "активный живёт не меньше умеренного");
            Assert.Greater(moderate, passive, "умеренный живёт дольше пассивного");
        }

        [Test]
        public void DecayMatrix_IsReportedForTheFounder()
        {
            // Матрица «декей × профиль → возраст смерти» из §4 дока инкремента. Прогоняется как тест,
            // чтобы цифры в утреннем отчёте нельзя было выдумать: они печатаются из живого прогона.
            double saved = Game.HealthDecayPerSec;
            try
            {
                // Диапазон намеренно уходит ДАЛЕКО за рабочее значение: смысл матрицы — показать, где
                // порог 70 действительно ломается, то есть какой у выбранного числа запас.
                var sb = new StringBuilder(
                    "[balance-matrix] декей %/с | пассивный | умеренный | лингеринг | активный   (медиана (худший) из 6 сидов)\n");
                foreach (var decay in new[] { 0.30, 0.50, 0.70, 1.00, 1.50, 2.00 })
                {
                    Game.HealthDecayPerSec = decay;
                    var pas = AgesAcrossSeeds(Passive, 6);
                    var mod = AgesAcrossSeeds(Moderate, 8);
                    var lin = AgesAcrossSeeds(Lingering, 8);
                    var act = AgesAcrossSeeds(Active, 6);
                    sb.AppendLine($"  {decay:0.00} | {pas.Median} ({pas.Min}) | {mod.Median} ({mod.Min}) "
                                  + $"| {lin.Median} ({lin.Min}) | {act.Median} ({act.Min})");
                }
                Debug.Log(sb.ToString());
            }
            finally
            {
                Game.HealthDecayPerSec = saved;
            }
            Assert.AreEqual(saved, Game.HealthDecayPerSec, "тюнимая константа восстановлена после матрицы");
        }
    }
}
