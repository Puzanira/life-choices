using System;
using System.Collections.Generic;
using System.Linq;

namespace ThanksNoThanks
{
    public enum GameState { Opener, Playing, Finale }

    /// <summary>
    /// Sub-mode of <see cref="GameState.Playing"/> during the midlife crisis (CR00–CR08). <see cref="None"/>
    /// is ordinary card play; <see cref="Blitz"/> is the 5×5s thought sprint (CR01–CR05); <see cref="Impulse"/>
    /// is the INVERT round (CR06–CR08) entered only after ≥2 blitz fails. The normal card loop is suspended
    /// while a crisis phase is active and resumes exactly where it left off afterwards.
    /// </summary>
    public enum CrisisPhase { None, Blitz, Impulse }

    /// <summary>
    /// Which way a card resolved — the semantic input to the host's speech-bubble tone.
    /// <see cref="Timeout"/> means the answer timer ran out (the mechanical answer was still a coin
    /// flip, but the host reacts to the silence, not the random pick).
    /// </summary>
    public enum AnswerSide { Yes, No, Timeout }

    /// <summary>
    /// Pure, engine-free spine of «Спасибо, не надо»: the 3-state machine
    /// (Opener → Playing → Finale → Opener), the event-time age model, the PHASED
    /// answer timer (§3: 10/8/6 s by age, 5 s in the blitz; timeout = random answer),
    /// passive Δ application, FATAL / burnout / natural endings, and necrolog assembly.
    /// Time is injected via <see cref="Tick"/> so PlayMode tests drive it by API instead
    /// of racing the wall clock.
    /// </summary>
    public sealed class Game
    {
        // ---- answer timer by age phase (meeting-revisions §3 — replaces the old single 5 s) ----
        // Финальные числа встречи 2026-07-29: 1–19 → 10 с · 20–29 → 8 с · 30–100 → 6 с · блиц → 5 с.
        // Таймер — ЧИСТАЯ функция возраста карточки (<see cref="AnswerSecondsFor"/>); блиц перебивает фазу.
        public const float AnswerSecondsYouth = 10f;     // A. детство/юность 1–19
        public const float AnswerSecondsYoung = 8f;      // B. молодость 20–29
        public const float AnswerSecondsMature = 6f;     // C. зрелость → старость 30–100
        public const int AnswerPhaseYoungFromAge = 20;   // граница A→B
        public const int AnswerPhaseMatureFromAge = 30;  // граница B→C

        /// <summary>
        /// Длина таймера ответа в секундах для карточки данного возраста (meeting-revisions §3).
        /// Приоритет: блиц &gt; фаза по возрасту. Чистая функция — ни состояния, ни движка.
        /// </summary>
        public static float AnswerSecondsFor(float age, bool blitz = false)
        {
            if (blitz) return BlitzSeconds;
            int a = (int)Math.Floor((double)age);
            if (a >= AnswerPhaseMatureFromAge) return AnswerSecondsMature;
            if (a >= AnswerPhaseYoungFromAge) return AnswerSecondsYoung;
            return AnswerSecondsYouth;
        }

        public const float AgeCatchUpPerSecond = 12f;    // age "catches up" to the card's age
        public const float AgeSlowTickPerSecond = 0.4f;  // ticks slowly once caught up

        // Natural old-age ending tone by relationships (GAME_SPEC: весёлая · спокойная · одинокая).
        // Serialized as tunable thresholds so the bigger deck can be re-balanced by playtest.
        public const int JoyfulOldAgeRelationships = 60;   // >= → весёлая старость
        public const int LonelyOldAgeRelationships = 50;   // <= → одинокая старость

        // ---- money crank (tunable; canon §Деньги + §Сводка констант) ----
        public const int MoneyOpenAge = 18;                  // деньги открываются в 18 («работа»)
        public const double MoneyTickIncome = 1.0;           // +1₽ × множитель за тик
        public const double CostOfLivingPerSec = 0.5;        // −0.5₽/сек, пока деньги открыты
        public const int UniversityMultFromAge = 25;         // универ-множитель включается с 25 (FROM:25)

        // ---- ЦЕНЫ ПОКУПАТЕЛЬСКИХ КАРТОЧЕК (отрезок 0, дизайн-док §3.4) ------------------------------
        // Цена живёт ЗДЕСЬ, а не в CSV: колонка Δ осталась качественной (−3…+3), а рубли — точные числа
        // (см. DeltaScale). Это ОДНО число на карточку: им же гейтится доступность (BLOCK$), им же
        // списывается ДА, оно же печатается на самой карточке.
        //
        // Потолки цен подобраны основательницей «с оглядкой на карман» (§3.3): 18–19 → 10–25 ₽ (в юности
        // больших покупок не бывает, иначе полотрезка гаснет), 20–24 → 15–50, 25–29 → 25–60, 30–40 →
        // 40–80, 55+ → 100–120 («накрутил или ленился» решает, доживёшь ли). Якорь всей системы — FC08
        // «новый флагман» = 50 ₽: «после этого на отпуск (60 ₽) уже не хватит, придётся выбирать».
        public static readonly IReadOnlyDictionary<string, double> BlockPrices = new Dictionary<string, double>
        {
            // 18–19 — юность лайтовая по природе
            { "FA01", 12 },   // ненужная вещь на распродаже
            { "FA02", 10 },   // забить холодильник едой
            { "FA05", 25 },   // автошкола за компанию
            { "FA07", 15 },   // годовой абонемент в зал
            { "FA10", 25 },   // спустить первую зарплату
            // 20–24
            { "FB02", 20 },   // квартира на двоих
            { "FB03", 15 },   // котёнок из приюта
            { "FB04", 35 },   // музыкальный фестиваль
            { "FB06", 45 },   // первый отпуск за свои
            { "FB09", 45 },   // спустить зарплату за вечер
            // кек-карточки 25+. Отрезок 4 §1 «кек перестаёт быть одинаковым»: пять карточек стоили по
            // 10 ₽ и не делали НИЧЕГО, что при новом масштабе Δ вообще ноль. Теперь каждая бьёт по своей
            // шкале, и цены разъехались (`KEK04` бьёт энергией и цены не имеет вовсе).
            { "KEK01", 25 }, { "KEK02", 25 }, { "KEK03", 15 }, { "KEK05", 40 },
            // 25–29
            { "FC02", 25 },   // ранняя ипотека — ВЗНОС (банк без взноса не даёт), §3.5
            { "FC04", 45 },   // записаться к терапевту
            { "FC05", 20 },   // бросить офис ради фриланса
            { "FC08", 50 },   // ⭐ ЯКОРЬ: новый флагман
            { "FC13", 50 },   // брекеты во взрослом возрасте
            { "FC15", 20 },   // танцы, где ты старше всех
            // 30–40
            { "FC09", 20 },   // осесть и смириться
            { "FC10", 60 },   // ремонт «на пару выходных»
            { "FC11", 20 },   // второй язык с нуля
            { "MD03", 60 },   // отпуск на море
            { "MD04", 40 },   // поздняя ипотека — ВЗНОС, §3.5
            { "MD05", 30 },   // помочь стареющим родителям
            { "MD07", 20 },   // завести блог
            // 55+
            { "LT01", 40 },   // заняться здоровьем всерьёз
            { "LT02", 120 },  // операция
            { "LT08", 100 },  // подлечиться (система, condition-triggered)
            // импульс-раунд кризиса: основательница решила правило денег НЕ ломать (§1.9), поэтому
            // импульсы платные — как все. Компенсирующие БЕСПЛАТНЫЕ импульсы CR10–CR12 (§4.3) — новые
            // карточки, они приезжают вместе с отрезками 1–7, не здесь.
            { "CR07", 70 },   // купить мотоцикл и гнать 200
            { "CR08", 70 },   // побриться налысо и на Шри-Ланку

            // ---- ОТРЕЗКИ 1–7 (2026-08-08) --------------------------------------------------------------
            // Цена КАЖДОЙ новой платной карточки выведена механически из её собственной ДА-Δ в CSV: доки
            // отрезков 1–7 пишут суммы уже в рублях («−30 ₽»), поэтому таблица и колонка не могут
            // разойтись по невнимательности. Гард — `GameScoringTests.EveryBlockCostCard_HasAPrice` +
            // `PriceTable_AgreesWithTheCsvMoneyDelta`.
            // 18–19 — потолок юности 10–25 ₽ держится
            { "FA03", 15 }, { "FA11", 20 }, { "FA20", 20 }, { "FA22", 12 }, { "FA25", 15 }, { "FA26", 15 },
            { "FA28", 20 }, { "FA32", 15 }, { "FA34", 20 },
            // 20–24
            { "FB05", 20 }, { "FB13", 45 }, { "FB15", 25 }, { "FB21", 40 }, { "FB22", 15 }, { "FB23", 20 },
            { "FB26", 25 }, { "FB27", 20 }, { "FB30", 45 },
            // 25–29
            { "FC24", 30 }, { "FC27", 30 }, { "FC29", 25 }, { "FC31", 30 }, { "FC32", 50 }, { "FC33", 40 },
            { "MD08", 80 }, { "MD12", 25 },
            // 30–39
            { "FC38", 40 }, { "FC39", 50 }, { "FC43", 50 }, { "FC47", 40 }, { "FC48", 30 },
            { "FC49", 100 }, { "FC52", 40 }, { "FC54", 30 }, { "FC55", 50 },
            // 40–55
            { "MD17", 30 }, { "MD18", 80 }, { "MD19", 25 }, { "MD21", 25 }, { "MD23", 50 },
            { "MD30", 25 }, { "MD32", 80 }, { "MD35", 30 },
            // 56+
            { "LT11", 120 }, { "LT15", 40 }, { "LT20", 40 }, { "LT22", 40 }, { "LT24", 20 },
            { "LT25", 25 }, { "LT29", 20 },
        };

        /// <summary>
        /// ЗАРАБОТКИ (отрезок 0, §3.4): карточки, у которых ДА приносит точную сумму в ₽. Это тот же случай,
        /// что и цена — автор написал настоящее число, а не качественный шаг, поэтому <see cref="DeltaScale"/>
        /// к ним не применяется и CSV-Δ по деньгам такой карточки ИГНОРИРУЕТСЯ (ровно как у BLOCK$).
        /// `BLOCK$` у заработков не стоит — за них не платят.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, double> CardYesIncome = new Dictionary<string, double>
        {
            { "FA06", 20 },   // смена курьером в дождь
            { "FB07", 25 },   // первый заказ на фрилансе
            { "FC01", 25 },   // дожать квартальный до полуночи
            { "FC07", 25 },   // уехать в другой город за мечтой
            { "FC12", 25 },   // копить, урезав кофе
            { "YA04", 40 },   // первый кредит на мечту — деньги сразу, дренаж на годы вперёд
        };

        /// <summary>
        /// КРЕДИТНЫЕ КАРТОЧКИ — единственные, которые могут увести счёт В МИНУС. Формулировка
        /// основательницы (2026-08-07): «в долг можно только то, что написано в кредит. Кредит это же долг,
        /// это нормально. А то, на что у нас не хватает денег, так и не должно быть доступно».
        ///
        /// У `FC02`/`MD04` кредитная часть — ДРЕНАЖ (ипотека платится годами), а сам ВЗНОС банк без денег
        /// не даёт, поэтому у них ещё и `BLOCK$`. У `YA04`/`FC14` кредит и есть содержание карточки:
        /// доступны всегда, деньги/машина сразу, расплата дренажом.
        /// </summary>
        public static readonly IReadOnlyCollection<string> CreditCards = new HashSet<string>
        {
            "YA04", "FC14", "FC02", "MD04",
        };

        /// <summary>
        /// Штраф за РАЗВОД в ₽ — брак распался, делёж состоялся. Гасится флагом `PRENUP` (брачный договор),
        /// см. <see cref="Card.IsPrenup"/>. Число из хендоффа дизайн-сессии («PRENUP отменяет −60 ₽ при
        /// разводе»); тюнимое. Списывается только когда рвётся именно БРАК (<see cref="Married"/>) и только
        /// когда деньги уже открыты.
        /// </summary>
        public const double DivorceCost = 60;

        /// <summary>
        /// Порог, ниже которого долг СЧИТАЕТСЯ долгом и Ведущий его объявляет. Не ноль намеренно: шкала
        /// денег открывается в 18 с нулём на счету, а стоимость жизни (−0.5 ₽/сек) уводит её в минус
        /// буквально на первом же кадре — «Ой, минус!» прилетело бы прямо поверх туториала крутилки.
        /// −5 ₽ ≈ десять секунд жизни: столько, чтобы это был выбор, а не округление. Тюнимо.
        /// </summary>
        public const double DebtAnnounceBelow = -5.0;

        // ---- live health (tunable; canon §Здоровье + §Сводка констант) ----
        public const int HealthDecayFromAge = 30;         // до 30 не убывает; с 30 тает
        /// <summary>
        /// Базовый декей здоровья за РЕАЛЬНУЮ секунду, пока оно тает. Канон-ориентир ≈1 %/с.
        ///
        /// ⚠ #1 ТЮНИМОЕ ЧИСЛО БАЛАНСА, и с отрезка 0 оно ПЕРЕМЕННАЯ, а не константа: масштаб Δ вырос на
        /// порядок («Здр −2» = −15 п.п. вместо −2), и подбирать декей теперь нужно ИЗМЕРЕНИЕМ, а не на
        /// глаз. Матрицу «декей × профиль игрока → возраст смерти» печатает
        /// <c>BalanceGuardTests.DecayMatrix_IsReportedForTheFounder</c> из живых прогонов; она же
        /// восстанавливает значение обратно. В игре число не меняется никогда — только в замерах.
        /// </summary>
        public static double HealthDecayPerSec = DefaultHealthDecayPerSec;

        /// <summary>
        /// Заводское значение <see cref="HealthDecayPerSec"/>.
        ///
        /// ⚠ 0.7 → 0.5 (отрезок 0, 2026-08-08) → 0.45 (r4, 2026-08-08, решение основательницы «подкрутить
        /// декей»). Причина второй правки — НЕ код: из колоды по её слову убрана карточка `YA03`, выборка
        /// сэмплера пересыпалась, и на сиде #1 ЛИНГЕРИНГ (медленный игрок — худший случай, декей идёт по
        /// реальному времени) стал умирать в 64 «здоровье не выдержало». Матрица «декей × профиль →
        /// возраст смерти», текущая колода 253 карточки, 12 сидов, ХУДШИЙ прогон (медиана в скобках):
        ///
        ///   декей | пассивный | умеренный | лингеринг | активный
        ///    0.50 |  25 (26)  |  77 (88)  |  64 (88)  |  77 (88)   ← было; лингеринг проваливает порог
        ///    0.45 |  25 (26)  |  77 (88)  |  77 (88)  |  77 (88)   ← выбрано
        ///    0.40 |  25 (26)  |  77 (88)  |  77 (88)  |  77 (88)
        ///    0.35 |  25 (26)  |  77 (88)  |  78 (88)  |  77 (88)
        ///    0.30 |  25 (26)  |  77 (88)  |  77 (88)  |  77 (88)
        ///
        /// Ниже 0.45 таблица ПЛОСКАЯ: смерть от здоровья исчезает вовсе, и 77 — это уже естественный конец
        /// колоды, а не износ. Поэтому берётся САМОЕ БОЛЬШОЕ из проходящих — максимум напряжения, который
        /// ещё оставляет медленному игроку его семьдесят. Обрыв РЕЗКИЙ и лежит между 0.49 и 0.50 (на 0.49
        /// сид #1 уже доживает до 86), но 0.49 проходит лишь формально: там сид #9 всё равно выходит в
        /// НОЛЬ здоровья, просто в 88 лет. Замер запаса — минимум здоровья за жизнь, худший сид из 12:
        /// 0.50 → 0, 0.49 → 0, 0.45 → 5, 0.40 → 9, 0.35 → 13. То есть 0.45 — первое значение, на котором
        /// НИ ОДИН прогон не упирается в ноль.
        /// Пассивного смягчение не спасает: он гибнет от ПОЛНОГО ВЫГОРАНИЯ в 25, а энергия декеем здоровья
        /// не управляется (строка «пассивный» неподвижна во всей матрице).
        /// </summary>
        public const double DefaultHealthDecayPerSec = 0.45;

        /// <summary>
        /// ⚠ #2 ТЮНИМОЕ ЧИСЛО БАЛАНСА (отрезки 1–7). Скорость, с которой здоровье восстанавливается, ПОКА
        /// оно ещё не начало таять (до <see cref="HealthDecayFromAge"/>). Переменная, а не константа, — по
        /// той же причине, что и декей: значение подбирается ИЗМЕРЕНИЕМ, матрицу печатает
        /// <c>BalanceGuardTests.YouthRegenMatrix_IsReportedForTheFounder</c>. Тест восстанавливает значение
        /// за собой.
        /// </summary>
        public static double HealthYouthRegenPerSec = DefaultHealthYouthRegenPerSec;

        /// <summary>
        /// Заводское значение <see cref="HealthYouthRegenPerSec"/>. ВЫБРАНО ПО ИЗМЕРЕНИЮ (матрица
        /// `YouthRegenMatrix_IsReportedForTheFounder`, 12 сидов): обрыв лежит между 0.10 (лингеринг
        /// худший 75) и 0.15 (77), а 0.00 роняет гард в 55/62. Выше 0.15 результат ПЛОСКИЙ — значит
        /// связывает не скорость, а потолок <see cref="HealthYouthRegenCeiling"/>, и точное число рейта
        /// малозначимо. Взято 0.25: ~2.5× запаса над обрывом (не на краю, в отличие от 0.15) и при этом
        /// не мгновенно — семь потерянных пунктов возвращаются примерно за три карточки, так что выбор
        /// в юности всё ещё чувствуется, а не стирается к следующему вопросу.
        /// </summary>
        public const double DefaultHealthYouthRegenPerSec = 0.25;

        /// <summary>
        /// Потолок восстановления в молодости. НИЖЕ ста намеренно: юность заживает, но не начисто —
        /// иначе выборы 18–24 не значили бы вообще ничего, а они и есть содержание отрезков 2–3.
        /// </summary>
        public const int HealthYouthRegenCeiling = 85;
        public const double Lt01NeglectDecayMult = 2.0;   // LT01=НЕТ «забросил» → декей ×2
        public const double Lt01CareDecayMult = 0.5;      // LT01=ДА «занялся» → декей ×0.5
        public const int Kek04HealthBonus = 5;            // KEK04=ДА ЗОЖ-секта → разовый небольшой плюс
        // LT08 «Пора подлечиться!» — ВЗВОД карточки (вставка в колоду). Условие показа у неё то же самое,
        // но читается уже из CSV («когда здоровье < 40% (возраст ≥ 30)»); что эти два источника сходятся,
        // стережёт валидатор колоды — иначе взвод и показ разъехались бы молча.
        public const int Lt08TriggerAge = 30;             // LT08 eligible age ≥30…
        public const int Lt08TriggerHealthBelow = 40;     // …AND when health < 40%
        // (`Lt02EligibleHealthBelow` удалён: порог операции живёт в CSV «55–70, если Здр<50%» и читается
        //  парсером в Card.RequiresHealthBelow — хардкод по id был частным случаем находки ревю.)

        // ---- live energy (tunable; canon §Энергия) ----
        public const int EnergyOpenAge = 25;              // энергия открывается в 25 (YA05)
        // Drain is INTENTIONALLY steeper than health's: energy REQUIRES active input (founder Gate-2 r3).
        // Passive → burnout mid-adulthood → «полное выгорание» if ignored.
        public const double EnergyDrainPerSec = 1.7;      // дренаж ≈1.7%/сек, пока энергия открыта
        // ⚠ РЕДИЗАЙН 2026-08-07 (живой плейтест, дословно: «просто зажать датчик высоты, пока батарейка не
        // заполнится»). Ритм-механика (импульс на подъёме + окно 0.4–3.0 с, +3% за валидный вдох) СНЯТА.
        // Теперь энергия — УДЕРЖАНИЕ: пока датчик физически поднят, батарея наполняется с этой скоростью.
        // Число выведено из контракта инкремента, а не «на глаз»: туториал стартует с
        // <see cref="EnergyOpenValue"/> = 20 %, а его условие — ПОЛНАЯ батарея, значит 80 пунктов должны
        // набираться за обещанные 4–6 с ⇒ 13.3…20 %/с; берём середину, 16 %/с (ровно 5.0 с под модалкой,
        // где дренаж заморожен, и ≈5.6 с в живой игре, где он вычитается).
        public const double EnergyRegenPerSec = 16.0;
        // Значение шкалы В МОМЕНТ ОТКРЫТИЯ (25, карточка YA05 «первая усталость»). До 2026-08-07 энергия
        // открывалась ПОЛНОЙ (100 %), и задача «держи, пока батарейка не заполнится» была бы уже выполнена
        // на первом кадре — экран ушёл бы, не потребовав ничего. Открытие на 20 % и буквально, и по смыслу
        // канона: устал — вот тебе шкала. Берётся МИНИМУМ с текущим значением, чтобы открытие никогда не
        // ДАРИЛО энергию игроку, которого карточки уже просадили ниже.
        public const int EnergyOpenValue = 20;

        // ---- burnout (temporary; canon §Энергия §Выгорание) ----
        public const int BurnoutEnterEnergyAtOrBelow = 10; // входит при энергии ≤10%
        public const int BurnoutExitEnergyAbove = 40;      // снимается сам при энергии >40%
        public const double BurnoutIncomeMult = 0.5;       // крутилка «тяжелеет» — доход ×0.5
        /// <summary>
        /// ГРЕЙС-ПЕРИОД после выхода из выгорания (основательница, живой плейтест 2026-08-07: «повторное
        /// выгорание встык — не даёт выдохнуть»). Столько секунд после снятия выгорания дренаж энергии НЕ
        /// идёт и выгорание не может защёлкнуться заново. Запрошенный коридор 5–8 с, берём середину.
        /// Реген под поднятым датчиком в грейсе работает как обычно — это пауза наказания, а не игры.
        /// </summary>
        public const float BurnoutGraceSeconds = 6f;

        // ---- live relationships balancer (tunable; canon §Отношения) ----
        // The fourth live scale: a balancer to hold inside a zone while cranking/holding the sensor/answering.
        // Opens at 20 ПО ВОЗРАСТУ (карточки-открывашки у Отн больше нет — r4 п.2), starts at 55%
        // (Scales.Reset), target zone 40–75%.
        public const int RelationshipsOpenAge = 20;        // балансир открывается в 20 — чисто по возрасту
        public const int RelZoneMin = 40;                  // ниже зелёной зоны (жёлтый) — начинает дрейфовать/рисковать
        public const int RelZoneMax = 75;                  // выше — «красная зона» (задушил вниманием)
        // Разрыв копится ТОЛЬКО в КРАСНОЙ зоне (глубоко внизу), не в жёлтой. Основательница (плейтест
        // 2026-07-23): «расставались уже в жёлтой, а должны — в конце красной». Жёлтая [15..40] = буфер-
        // предупреждение (таймер разрыва не идёт); красная (<15) = таймер разрыва. Значение тюнимое.
        public const int RelBreakupFloor = 15;
        // ⚠ ДРЕЙФ ПЕРЕКАЛИБРОВАН 2026-08-07 (живой плейтест: «до расставания при полном бездействии больше
        // минуты — бездействие не наказывается»). Требование основательницы: из зелёной середины (55 %) до
        // РАЗРЫВА при полном бездействии ≈30–45 с. Число выводится, а не «на глаз»:
        //   t = (СТАРТ − RelBreakupFloor)/d + RelBreakupSeconds = (55 − 15)/d + 10.
        // d = 1.6 ⇒ 25 + 10 = 35 с — ровно середина коридора 30–45 (было d = 0.6 ⇒ 66.7 + 10 = 76.7 с).
        // Удержание при этом по-прежнему уверенно вытягивает: нетто «держу ↑» = 4.0 − 1.6 = +2.4 %/с
        // (в браке +3.2). Пороги зон и сама механика не тронуты — изменено ОДНО число (и его половина).
        public const double RelDriftPerSec = 1.6;          // дрейф вниз ≈1.6%/сек, пока балансир открыт
        public const double RelDriftMarriedPerSec = 0.8;   // в браке (MD01=ДА) мягче — вдвое медленнее
        // ⚠ ТЯГА ПЕРЕКАЛИБРОВАНА 2026-09-22 (плейтест НА АВТОМАТЕ, п.1 панч-листа: «отношения всё время
        // выходят, удержать нельзя» — и на автомате, и на клавиатуре). При 4.0 нетто удержания было
        // +2.4 %/с: из нижнего края до середины зелёной зоны — 24 с, из края в край — 43 с. Это не
        // «медленно», это «шкала не отвечает»: игрок тянет рычаг и за секунду видит ДВА деления.
        // Требование основательницы (числовая цель контракта r5):
        //   • из края красной зоны до зелёной середины (57.5 %) — ~2–3 с;
        //   • из края в край (0 → 100) — ≤5 с.
        // Обе цели задают НЕТТО-скорость: 100 / 5 = 20 %/с. Берём НАИМЕНЬШУЮ тягу, которая проходит обе
        // (более быстрая сделала бы шкалу ещё менее требовательной, чем нужно): 22.0 − 1.6 = 20.4 %/с.
        //   0 → 57.5   : 2.85 с ✔ (коридор 2–3)      0 → 100 : 4.93 с ✔ (≤5)
        //   15 → 57.5  : 2.12 с ✔                    100 → 0 : 4.23 с ✔
        // ДРЕЙФ НЕ ТРОНУТ (1.6): он приколочен каноном r3 «бездействие → разрыв за 30–45 с», сим даёт
        // 35.6 с — ровно середина коридора. Следствие, которое надо знать: две цели вместе ФИКСИРУЮТ
        // точку равновесия — держать рычаг надо 1.6/22.0 ≈ 7 % времени, чтобы не падать (было 40 %).
        // Пассивный игрок при этом по-прежнему НЕ выживает (сим: duty 0 % → разрыв на 35.6 с, 5 % → на
        // 94.5 с, 8 % → выживает) — железный инвариант живых шкал цел, см. гарды r5.
        public const double RelBalancerPerSec = 22.0;      // RELATION_AXIS ↑/↓ тянет маркер ≈22%/сек
                                                          // (нетто +20.4%/с) — было 4.0, до этого 1.5
        public const double RelOverloadPenaltyPerSec = 0.3;// >75% — доп. штраф вниз (риск ссоры)
        // ⚠ ПОЛ УДЕРЖАНИЯ (плейтест-фиксы r4 п.3, живая жалоба «джойстиком двигаю — шкала не растёт»).
        // Обещание строчкой выше («нетто «держу ↑» = +2.4 %/с») держалось только на ГОЛОМ дрейфе. Сверху
        // на него множатся `DRIFT:Отн=xN` из колонки «Длительный эффект», и `MD01`-НЕТ («отказались от
        // свадьбы», scenes.csv:18) даёт ×2 БЕЗ `DUR` — то есть НАВСЕГДА: дрейф 1.6 → 3.2, нетто удержания
        // 4.0 − 3.2 = +0.8 %/с. Это ~1 деление шкалы за секунду с половиной — глазом «не растёт вообще»,
        // ровно то, что увидела основательница. Множители при этом легальны и стакаются (×2·×2 = −6.4,
        // нетто −2.4 — удержание УВОДИЛО БЫ ВНИЗ).
        // Лечим не отменой множителя (он — обещание прозы карточки «тает даже при поддержке») и не
        // задиранием RelBalancerPerSec (это разогнало бы и здоровый случай), а ПОЛОМ НЕТТО-СКОРОСТИ и
        // только пока игрок ТЯНЕТ ВВЕРХ: активное удержание всегда отыгрывает ≥2 %/с, а НАКАЗАНИЕ
        // БЕЗДЕЙСТВИЯ (axis = 0) множитель сохраняет целиком — «тает даже при поддержке» остаётся правдой,
        // но перестаёт быть «не тянется вовсе». Штраф перегрева (>75%) накладывается ПОСЛЕ пола и потому
        // по-прежнему работает: «задушил вниманием» не отменяется.
        //
        // ⚠ СТАТУС ПОСЛЕ r5: ПОЛ СПИТ, И ЭТО ОСОЗНАННО (находка код-скептика, MAJOR). Тяга выросла
        // 4.0 → 22.0, и на ВСЕХ сегодня достижимых множителях нетто удержания 15.6…21.2 %/с — до пола
        // (2.0) не достаёт ни один: чтобы он сработал, дрейф должен перевалить за 20 %/с, то есть
        // множитель ×12.5 от канонных 1.6. Такого в колоде нет. Пол ОСТАВЛЕН СТРАХОВКОЙ, а не по
        // инерции: он ловит не сегодняшнее число, а КЛАСС поломки «стак множителей съел удержание»,
        // ровно ту, которую поймала основательница в r4. Балансная правка тяги вниз или новая пачка
        // `DRIFT:Отн=xN` в колоде возвращает его в работу молча и без правок кода — а стоит он одно
        // сравнение на тик. Цена снятия несимметрична цене хранения, поэтому хранится.
        // Гард (`PlaytestFixesR4Tests`, п.3) после этой находки ПЕРЕПИСАН: он больше не мерит кламп
        // шкалы на пятисекундном окне (тот замер давал ровно 10 %/с и был зелёным даже с удалённым
        // полом), а проверяет пол на СИНТЕТИЧЕСКОМ множителе, при котором дрейф обгоняет тягу.
        public const double RelHoldNetFloorPerSec = 2.0;   // удержание ↑ даёт ≥2%/с нетто при ЛЮБОМ дрейфе
        public const float RelBreakupSeconds = 10f;        // суммарно ~10 сек ниже зоны → разрыв
        public const int RelBreakupValue = 20;             // после разрыва шкала падает сюда (одиноко)

        // ---- live child (signal-response; tunable; canon §Ребёнок) ----
        // Opens on MD02=ДА (which the deck places at «свадьба +2», gated CHAIN→MD01=ДА, so it can only
        // resolve ≥2 game-years after the wedding). Signal-response: the PHONE RINGS on a random interval
        // (the driver slides the handset in from the left edge — revisions §5b); a CHILD_PRESS inside the
        // open window is «поднял трубку»; missing 2+ calls in a row is «плохой родитель» (relationships
        // −10% + child scale drop). NO death — it only bends relationships (which fold into the show
        // tone/brightness) and the child scale. Numbers are the #1 feel-tunables.
        public const float ChildFlashIntervalMin = 15f;   // звонок каждые ~15–25с (нижняя граница)
        public const float ChildFlashIntervalMax = 25f;   // …верхняя граница (seeded/injectable roll)
        // Окно поднятия — РОВНО 5 с (meeting-revisions §5b, спековое число; было 2 с у старой «вспышки
        // кнопки-ребёнка», которую трубка заменила).
        public const float ChildFlashWindow = 5f;         // окно поднятия трубки, пока она звонит
        public const float ChildPressMinDelay = 1f;       // мин. задержка ~1с: пре-нажатие в этом окне
                                                           // перед вспышкой блокирует засчёт (анти-заспам)
        public const int ChildBadParentMisses = 2;        // пропуск 2+ вспышек подряд → «плохой родитель»
        public const int ChildBadParentRelPenalty = 10;   // …отношения −10% (разово за такой промах-лапс)
        public const int ChildBadParentScaleDrop = 12;    // …и шкала ребёнка проседает
        public const int ChildPressGain = 4;              // успел по вспышке → шкала ребёнка чуть вверх
        public const int ChildStartValue = 70;            // шкала ребёнка на открытии (MD02=ДА)

        // ---- midlife crisis: blitz + impulse (tunable; canon crisis-content.md §2) ----
        public const int CrisisTriggerAge = 45;            // кризис в 45–50: fires once when Age first reaches 45
        // Блиц-мысль: 5 сек (meeting-revisions §3, «блиц с 3 до 5» — финальное число встречи). Перебивает
        // фазу по возрасту: кризис живёт внутри фазы C (6 с), но его мысли идут по своему таймеру.
        public const float BlitzSeconds = 5f;
        public const int ImpulseFailThreshold = 2;         // ≥2 провала в блице → раунд импульса (иначе пропуск)
        // Impulse reaction window. Canon gives no number (it's «надо срочно нажать»); tuned to 3s so the
        // INVERT warning «молчание = ДА» is readable before silence auto-accepts. #1 crisis tunable (report).
        // meeting-revisions §3 раздаёт числа только фазам и БЛИЦУ — импульс там не оговорён, поэтому его
        // окно НЕ трогается этим инкрементом (менять только по отдельному решению основательницы).
        public const float ImpulseSeconds = 3f;

        // ---- depression / «тёмная полоса» (CR09) mini-game (tunable; canon crisis-content.md §1) ----
        // After the crisis resolves, a RANDOM_TRIGGER roll may drop the show into depression: a HARD
        // grayscale «собраться» mini-game DELIBERATELY OPPOSITE to the energy sensor (a steady HOLD).
        // Here the pulse is SLOW and SPARSE — wait and catch, don't mash. All values are dt/seed-injected.
        public const double DepressionChance = 0.5;        // per-life probability the crisis tail → depression
        /// <summary>
        /// ЗАЗОР «кризис → депрессия» в ОБЫЧНЫХ КАРТОЧКАХ (основательница, живой плейтест 2026-08-07:
        /// депрессия влетала ВСТЫК за блицем, два спецрежима подряд читались как один сплошной). Ролл
        /// по-прежнему делается хвостом кризиса (один раз за жизнь), но вход ОТКЛАДЫВАЕТСЯ, пока игрок не
        /// сыграет столько обычных карточек. Расширение пейсинг-правила «механики не открываются подряд».
        /// Запрошенный коридор 3–4; берём 4 (тюнимо).
        /// </summary>
        public const int DepressionGapCards = 4;
        public const int DepressionGraySteps = 5;          // 5 gray steps: 5 = full B&W, 0 = full colour (exit)
        // STEADY metronome (S8 playtest fix): the pulse is a predictable beat, not a random rare flash, so the
        // player can «дышать в такт». Equal min=max → a fixed ~1.5s tempo (was a random 2.5–3s wait, which read
        // as «пульс совсем не виден»). The window is widened to ~0.75s for fairness. All values are dt/seed-injected.
        public const float DepressionPulseIntervalMin = 1.5f; // steady beat — lower bound (~1.5s)
        public const float DepressionPulseIntervalMax = 1.5f; // …equal upper bound → a fixed, predictable tempo
        public const float DepressionPulseWindow = 0.75f;  // hit-window while the pulse is lit (~0.75s, widened for fairness)
        // Anti-mash lockout: any press that ISN'T a clean catch arms this; while it's up a press inside the
        // window is discarded (still a miss). A masher re-arms it every press, so a rapid/continuous press
        // can never coincide with an unlocked window — mashing can't win (canon «не долбить, а ловить»).
        public const float DepressionPressLockout = 1f;

        private List<Card> _deck;
        private List<Card> _reserve = new();             // top-up pool for skipped chain-gated cards
        private readonly Func<IReadOnlyList<Card>> _deckFactory; // re-samples a fresh deck per life
        private readonly Func<DeckPlan> _planFactory;    // re-samples deck + reserve per life
        private readonly Func<bool> _coin;               // true => ДА / "+" side of a ±N delta
        private readonly List<NecrologEntry> _entries = new();
        private readonly Dictionary<string, bool> _answers = new(); // card id → ДА(true)/НЕТ(false)
        private int _index;

        // Delayed FATAL (RND01): scheduled event-age at which «за вами пришли» fires. NaN = none.
        private float _scheduledFatalAge = float.NaN;
        private string _scheduledFatalCause;
        // Deck exhausted while a delayed fatal is pending: no card up, age fast-forwards to the
        // scheduled age (a brief beat, not «дожил») and then the fatal fires.
        private bool _coasting;

        public GameState State { get; private set; } = GameState.Opener;
        public Card CurrentCard { get; private set; }
        public Scales Scales { get; } = new Scales();
        public float Age { get; private set; }

        // ---- live money economy (float ₽; may go negative — «в минус», canon; no death from money) ----
        private struct ActiveMult { public double Value; public int FromAge; }
        private struct MoneyDrain { public double PerSec; public float StartAge; public float EndAge; }
        /// <summary>
        /// Разовая выплата, назначенная на возраст <see cref="AtAge"/> (грамматика <c>ONCE:Ny</c>, отрезки
        /// 1–7). Взводится ответом, падает один раз, тогда снимается. Отрицательная — расплата (`FA27`
        /// поручительство, `FB32` бумаги не читая), положительная — подушка безопасности (`FC32`).
        /// </summary>
        private struct MoneyOnce { public double Amount; public float AtAge; public bool IsCredit; }
        private readonly List<ActiveMult> _mults = new();
        private readonly List<MoneyDrain> _drains = new();
        private readonly List<MoneyOnce> _onces = new();

        // ---- live health / energy (dt-injected integration, like money; the int scale in Scales stays
        //      authoritative for card Δ, this layer decays/drains it in real time via a fractional
        //      accumulator so sub-1%-per-second steps integrate exactly and legacy Δ tests are untouched) ----
        private double _healthDecayFrac;    // accumulated fractional health decay pending a whole −1
        // ЗНАКОВЫЙ аккумулятор энергии: минус — дренаж, плюс — реген под поднятым датчиком. Один на оба
        // направления, чтобы «дожал полсекунды и отпустил» не терялось в округлении до целого процента.
        private double _energyFrac;
        // УДЕРЖИВАЕМЫЙ сигнал «датчик высоты поднят» на ЭТОТ тик (см. GameInput.EnergyHold). Ставится
        // вводом, гасится в начале каждого Tick — ровно модель оси балансира (_relAxis).
        private bool _breathHeld;
        // Грейс после выхода из выгорания: пока >0, дренаж энергии не идёт и выгорание не защёлкивается.
        private float _burnoutGrace;
        private double _healthDecayMult = 1.0; // set by LT01 (×2 забросил / ×0.5 занялся)
        private Card _lt08;                  // condition-triggered system heal card (from DeckPlan)
        private bool _lt08Triggered;         // single-shot per life

        /// <summary>True once the energy scale has opened (Age ≥ <see cref="EnergyOpenAge"/>). One-shot per life.</summary>
        public bool EnergyOpen { get; private set; }
        /// <summary>True once health has begun decaying (Age ≥ <see cref="HealthDecayFromAge"/>). One-shot per life.</summary>
        public bool HealthDecaying { get; private set; }
        /// <summary>Temporary «выгорание»: entered at energy ≤10%, exits above 40%. Halves crank income while on.</summary>
        public bool Burnout { get; private set; }

        /// <summary>
        /// Секунд грейса, оставшихся после выхода из выгорания (<see cref="BurnoutGraceSeconds"/> → 0).
        /// Пока >0: дренаж энергии заморожен и повторное выгорание защёлкнуться не может — «встык» запрещён.
        /// </summary>
        public float BurnoutGrace => _burnoutGrace;

        // ---- live relationships balancer state ----
        private int _relAxis;                 // RELATION_AXIS for THIS tick: -1/0/+1, consumed each tick
        private double _relFrac;              // fractional accumulator (sub-1%/s drift/pull integrates exact)
        private double _relBelowZoneSeconds;  // CUMULATIVE time spent below the zone floor (canon «суммарно»)

        // ---- отрезок 0: BREAK / DRIFT / EXCL / PRENUP ----
        /// <summary>Множитель дрейфа, поставленный длительным эффектом `DRIFT:Отн=xN` (снимается по DUR).</summary>
        private struct DriftEffect { public Scale Scale; public double Value; public float StartAge; public float EndAge; }
        private readonly List<DriftEffect> _drifts = new();
        // Отложенный разрыв `BREAK:Отн` + `DELAY(n)` (RND05: «через 2 года развод»). NaN — не запланирован.
        private float _scheduledBreakAge = float.NaN;
        // Ключи веток `EXCL:*`, УЖЕ выбранных в этом забеге (ипотека взята → вторая ипотека не придёт).
        private readonly HashSet<string> _exclusiveTaken = new();
        // Брачный договор подписан (`PRENUP`) — гасит DivorceCost.
        private bool _prenup;
        // Латч «счёт уже в минусе»: реплика Ведущего из пула `debt` звучит в МОМЕНТ ухода ниже нуля, а не
        // каждый кадр, пока там сидим.
        private bool _inDebt;

        /// <summary>True while the relationships balancer is live (open at 20 by AGE; closed again on a
        /// breakup). Drives the balancer HUD reveal and the drift/axis/breakup integration.</summary>
        public bool RelationshipsOpen { get; private set; }
        /// <summary>True once <see cref="Answer"/> resolves MD01=ДА (свадьба) — softens the drift (canon:
        /// «реже балансировать»). Cleared by a breakup and on a fresh life.</summary>
        public bool Married { get; private set; }
        /// <summary>True once a breakup has fired this life (partner gone). Keeps the balancer closed (the
        /// MD06 second chance that would reopen it is deferred) and folds the loss into the show tone.</summary>
        public bool RelationshipsLost { get; private set; }
        /// <summary>«Красная зона»: relationships above <see cref="RelZoneMax"/> while open — задушил
        /// вниманием, penalty pulling back down. Read by the driver for the red-zone HUD tint.</summary>
        public bool RelationshipRedZone => RelationshipsOpen && Scales.Relationships > RelZoneMax;

        /// <summary>
        /// ОТНОШЕНИЯ С ДРОБНОЙ ЧАСТЬЮ — то, что рисует маркер балансира (r5 п.1, «без задержки отклика»).
        ///
        /// Механика живёт в ЦЕЛЫХ (<see cref="Scales"/>.Relationships) — этого не трогаем: на целых стоят
        /// пороги зон, разрыв и весь канон. Но ОТРИСОВКА по целым и есть вторая половина жалобы «шкала не
        /// отвечает»: интегратор копит дробь в <c>_relFrac</c> и отдаёт в шкалу только целые шаги, поэтому
        /// при старой тяге маркер стоял неподвижно 25 кадров подряд, а потом прыгал на 2.2 px. Игрок давит
        /// рычаг — экран полсекунды молчит. Ровно это читается как «задержка», и одной скоростью оно не
        /// лечится: даже на 22 %/с первый ЦЕЛЫЙ шаг приходит на третий кадр.
        ///
        /// Непрерывное значение = целое + остаток аккумулятора. Остаток всегда в (−1; 1): интегратор
        /// вычитает из него ровно то целое, что отдал в шкалу, и делает это НЕЗАВИСИМО от того, приняла
        /// шкала этот шаг или срезала клампом. Сумма — это и есть точная позиция, а не «шкала плюс мусор».
        /// Маркер по ней движется КАЖДЫЙ кадр, в тот же кадр, в котором пришёл ввод.
        ///
        /// ДВЕ ЧЕСТНЫЕ ОГОВОРКИ (доки приведены к фактическому поведению, r5-ревю, NIT):
        ///  • НА УПОРЕ В 0 остаток НЕ обнуляется — он продолжает крутиться в (−1; 0], пока дрейф тянет
        ///    вниз упёршуюся в ноль шкалу. Наружу это не видно (внешний <c>Max(0, …)</c> держит 0), но
        ///    первый шаг вверх сперва гасит этот долг: до ~1 % шкалы, на тяге 22 %/с это ≤0.05 с. Обнуляет
        ///    остаток явно только <see cref="TickModalBalancer"/> на своём потолке.
        ///  • ТИНТ КРАСНОЙ ЗОНЫ идёт по ЦЕЛОЙ шкале (<see cref="RelationshipRedZone"/>), а маркер — по
        ///    дробной. В полосе шириной меньше процента маркер уже нарисован за отметкой 75, а подсветка
        ///    ещё не включилась. Так и задумано: пороги остаются на целых вместе со всем каноном, а разъезд
        ///    меньше одного деления шкалы (≈2 px) на стойке не читается.
        /// </summary>
        public float RelationshipsPrecise
            => (float)Math.Max(0.0, Math.Min(100.0, Scales.Relationships + _relFrac));

        // ---- live child (signal-response) state ----
        private readonly Random _childRng = new();  // engine-free RNG for the flash interval roll
        private float _childFlashElapsed;            // time since the last window closed (counts toward next flash)
        private float _childNextInterval;            // rolled length of the current wait (~15–25s)
        private float _childWindowRemaining;         // time left in the OPEN press-window (>0 while lit)
        private float _childPressLockout;            // anti-pre-spam: while >0 an in-window press is ignored
        private int _childConsecutiveMiss;           // flashes missed in a row (2+ → «плохой родитель»)

        /// <summary>Test/tuning seam: supplies the NEXT flash interval in seconds. null → a uniform draw in
        /// [<see cref="ChildFlashIntervalMin"/>,<see cref="ChildFlashIntervalMax"/>] from the internal RNG.
        /// Survives <see cref="StartLife"/> so a deterministic test can pin every interval.</summary>
        public Func<float> ChildFlashInterval;

        /// <summary>True while the child scale/button is live: opened by MD02=ДА, closed by LT04 or a fresh
        /// life. Drives the child-button HUD reveal and the flash/press integration.</summary>
        public bool ChildOpen { get; private set; }
        /// <summary>True while the flash window is OPEN (button lit) — a CHILD_PRESS now is good parenting.
        /// Read by the driver for the lit/flashing button state (S9).</summary>
        public bool ChildFlashing { get; private set; }

        /// <summary>
        /// The child call is FROZEN: the 5s window stops counting down and a press is not scored. One rule —
        /// «the player can't see the handset, so the clock must not run»: <see cref="Paused"/>, i.e. a hint,
        /// the §D modal or a спецрежим-входной экран is up over the board.
        ///
        /// ⚠ ВЫГОРАНИЕ БОЛЬШЕ НЕ МОРОЗИТ ЗВОНОК (2026-08-07). Оно морозило его ровно по одной причине:
        /// плашка S7 была ПОЛНОЭКРАННЫМ захватом, нарисованным поверх трубки, и окно копило НЕВИДИМЫЕ
        /// пропуски. Живой плейтест основательницы снял тот захват: первое выгорание объясняет входной
        /// D-экран (он ставит обычную <see cref="Paused"/>, и звонок замирает вместе со всем остальным), а
        /// повторные показывают КОРОТКУЮ плашку без блокировки — трубка при ней видна целиком, значит и
        /// замораживать нечего.
        ///
        /// Nothing is reset: the remaining window and the miss streak survive the freeze and continue from
        /// the same point the moment it lifts. (Crisis and depression freeze the call even earlier — there
        /// <see cref="Tick"/> never reaches <see cref="IntegrateChild"/> at all.)
        /// </summary>
        public bool ChildCallFrozen => InputsFrozen;

        /// <summary>Fired the instant the child scale opens (MD02=ДА, «свадьба+2») — drives the S5
        /// «ПОПОЛНЕНИЕ! жмите Enter по вспышке» hint + pause, one-shot per life.</summary>
        public event Action ChildOpened;

        /// <summary>
        /// Fired the instant a call window closes UNPRESSED — «проспал звонок» (живой плейтест 2026-08-07:
        /// «пропуск звонка вообще никак не отзывается»). Чистая семантика: штраф «плохого родителя» живёт
        /// отдельно (он даётся только за ДВА пропуска подряд), а это событие — про КАЖДЫЙ пропуск, чтобы
        /// драйвер увёл трубку в «поникшей» позе и Ведущий это озвучил.
        /// </summary>
        public event Action ChildCallMissed;

        // ---- midlife crisis (blitz + impulse) state ----
        private List<Card> _blitzThoughts;      // CR01..CR05 (from the plan); null when no crisis in this plan
        private List<Card> _impulseCards;       // ПУЛ импульсов CR06–CR08 + CR10–CR12; null when absent
        private List<Card> _impulseRound;       // подмножество пула, отобранное на ЭТОТ кризис (по карману)
        private bool _crisisDone;               // one-shot per life (set at trigger, so it can't re-fire)
        private CrisisPhase _phase = CrisisPhase.None;
        private int _blitzIndex;                // 0..4 current thought
        private int _blitzFails;                // промахи/«О НЕТ»/таймауты — ≥2 opens the impulse round
        private bool _blitzNormalOnYes;        // «ВСЁ НОРМАЛЬНО» lever for the CURRENT thought (seeded)
        private int _impulseIndex;              // 0..2 into CR06..CR08
        private float _crisisTimer;             // countdown for the current thought / impulse card
        private readonly Random _blitzRng = new();
        private Card _suspendedCard;            // normal card interrupted by the crisis (resumed after)
        private float _suspendedTimer;          // its remaining card timer, restored on resume
        private float _suspendedTimerMax;       // …and that card's FULL phase length (§3), for the dome arc
        private bool _suspendedBlocked;         // its BLOCK$ state, restored on resume

        /// <summary>Test/tuning seam: supplies whether «ВСЁ НОРМАЛЬНО» sits on the ДА/yes LEVER for the next
        /// thought. null → a coin from the internal RNG. Injected so a test can pin the sequence (both sides).
        /// NB: this names the LEVER, never a screen side — which physical/on-screen side each lever drives is
        /// the driver's business (since meeting-revisions §9 the ДА plate renders on the RIGHT).</summary>
        public Func<bool> BlitzNormalOnYesRoll;

        /// <summary>Current crisis phase (None while in ordinary play). Read by the driver to render S6/S13.</summary>
        public CrisisPhase Phase => _phase;
        /// <summary>True while any crisis phase is active — the normal card loop is suspended.</summary>
        public bool InCrisis => _phase != CrisisPhase.None;
        /// <summary>Blitz fails so far this crisis (промахи/«О НЕТ»/таймауты). ≥<see cref="ImpulseFailThreshold"/> → impulse.</summary>
        public int BlitzFails => _blitzFails;
        /// <summary>Current blitz thought number, 1..5 (for the S6 «мысль N/5» readout). 0 outside blitz.</summary>
        public int BlitzThoughtNumber => _phase == CrisisPhase.Blitz ? _blitzIndex + 1 : 0;
        /// <summary>Which LEVER «ВСЁ НОРМАЛЬНО» is on for the current thought: true = ДА/yes, false = НЕТ/no.
        /// Not a screen side — the driver decides which plate each lever drives (§9: ДА = right plate).</summary>
        public bool BlitzNormalOnYes => _blitzNormalOnYes;
        /// <summary>The current impulse card number, 1..3 (S13 readout). 0 outside the impulse round.</summary>
        public int ImpulseCardNumber => _phase == CrisisPhase.Impulse ? _impulseIndex + 1 : 0;
        /// <summary>Remaining time on the current crisis thought/impulse (mirrors <see cref="CardTimer"/>).</summary>
        public float CrisisTimer => _crisisTimer;
        /// <summary>The full duration of the current crisis timer (5s blitz / 3s impulse) for the dome fill.</summary>
        public float CrisisTimerMax => _phase == CrisisPhase.Impulse ? ImpulseSeconds : BlitzSeconds;

        /// <summary>Fired the instant the crisis begins (CR00): drives the S6 «КРИЗИС… БЛИЦ!» announce.</summary>
        public event Action CrisisStarted;
        /// <summary>Fired for each new blitz thought (incl. the first): drives the S6 host-nag bubble + relabel.</summary>
        public event Action CrisisBlitzAdvanced;
        /// <summary>Fired when the impulse round opens (≥2 fails): drives the S13 INVERT warning.</summary>
        public event Action CrisisImpulseStarted;
        /// <summary>Fired when the crisis ends and ordinary play resumes (the suspended card is restored).</summary>
        public event Action CrisisEnded;

        // ---- depression «тёмная полоса» (CR09) state ----
        private Card _depressionCard;        // CR09 carried on the plan (never a random draw); null → no depression
        private bool _depressionDone;        // one-shot per life: latched the moment the crisis tail rolls
        private int _depGray;                // gray steps remaining: DepressionGraySteps = full B&W, 0 = restored
        private float _depPulseElapsed;      // time since the last window closed (counts toward the next pulse)
        private float _depPulseNextInterval; // rolled length of the current wait (~2.5–3s)
        private float _depPulseWindow;       // time left in the OPEN hit-window (>0 while the dim pulse is lit)
        private float _depPressLockout;      // anti-mash: while >0 an in-window press is discarded (still a miss)
        private readonly Random _depRng = new();  // engine-free RNG for the pulse interval + the entry roll
        private bool _depArmed;              // ролл сработал — депрессия ЖДЁТ зазора в обычных карточках
        private int _depGapLeft;             // сколько обычных карточек ещё должно пройти до входа

        /// <summary>Test/tuning seam: supplies the NEXT pulse interval in seconds. null → a uniform draw in
        /// [<see cref="DepressionPulseIntervalMin"/>,<see cref="DepressionPulseIntervalMax"/>]. Survives
        /// <see cref="StartLife"/> so a deterministic test can pin every pulse.</summary>
        public Func<float> DepressionPulseInterval;

        /// <summary>Test/tuning seam: supplies whether the crisis tail rolls into depression. null → a
        /// <see cref="DepressionChance"/> coin from the internal RNG. Injected so a test can force both the
        /// occurs and the doesn't-occur cases deterministically.</summary>
        public Func<bool> DepressionTriggerRoll;

        /// <summary>True while the depression mini-game is active: the screen is B&W, the 5 scales are paused,
        /// and the ONLY input is the CONFIRM catch on the dim pulse. NO death path — exit is via 5 catches.</summary>
        public bool InDepression { get; private set; }

        /// <summary>Gray steps remaining, <see cref="DepressionGraySteps"/> (full B&W) down to 0 (full colour
        /// → exit). The driver drives the desaturation-overlay alpha off this (colour returns a step per catch).</summary>
        public int DepressionGray => _depGray;

        /// <summary>True while the dim pulse is lit (the ~0.6s hit-window is open) — a CONFIRM now is a catch.
        /// Read by the driver to reveal the faint centre pulse indicator (S8).</summary>
        public bool DepressionPulsing { get; private set; }

        /// <summary>
        /// Ролл хвоста кризиса УЖЕ выпал в пользу депрессии, но она ЖДЁТ зазора: депрессия не встаёт встык
        /// за блицем (<see cref="DepressionGapCards"/> обычных карточек между ними, плейтест 2026-08-07).
        /// </summary>
        public bool DepressionArmed => _depArmed;

        /// <summary>Сколько обычных карточек ещё должно пройти до отложенного входа в депрессию (0 — либо
        /// зазор выбран, либо депрессия не ждёт вовсе).</summary>
        public int DepressionGapLeft => _depArmed ? _depGapLeft : 0;

        /// <summary>Fired the instant depression begins — drives the muted «ТЁМНАЯ ПОЛОСА…» announce (S8).</summary>
        public event Action DepressionStarted;
        /// <summary>Fired on each successful catch (a step of colour returns) — drives a muted host mutter.</summary>
        public event Action DepressionProgressed;
        /// <summary>Fired when depression lifts (5 catches → full colour) and ordinary play resumes.</summary>
        public event Action DepressionEnded;

        /// <summary>Live money in ₽ (fractional; negative allowed). Authoritative for the HUD pill.</summary>
        public double Money { get; private set; }

        /// <summary>True once the money scale has opened (Age ≥ <see cref="MoneyOpenAge"/>). One-shot per life.</summary>
        public bool MoneyOpen { get; private set; }

        /// ЛЬГОТА ПЕРВОЙ КАРТОЧКИ ПОСЛЕ ОБУЧЕНИЯ ДЕНЬГАМ (r4 п.1б, живой плейтест основательницы:
        /// «сразу после туториала выпала карточка, на которую нет денег»).
        /// Шкала открывается с 0 ₽, а обучение закрывается семью тиками крутилки — то есть игрок выходит
        /// из §D-окна с почти пустым счётом, и первая же BLOCK$-карточка встречает его баннером «нет
        /// денег». Механически это честно, но читается как «игра сломана на обучении»: первое, что
        /// показали после урока, — запертую дверь.
        /// Лечим ОТБОРОМ, а не контентом и не ценами: ровно одна следующая выдача пропускает карточки,
        /// которые были бы заблокированы ПРЯМО СЕЙЧАС, и берёт следующую подходящую (пропущенная уходит
        /// в обычную подмену из резерва, длина забега не страдает). Льгота одноразовая и гаснет на первой
        /// же выданной карточке — дальше BLOCK$ работает как работал.
        private bool _moneyGraceCard;

        /// <summary>
        /// Взвести льготу «следующая карточка — по карману» (см. <see cref="_moneyGraceCard"/>).
        ///
        /// ⚠ ВЗВОДИТ ДРАЙВЕР НА ЗАКРЫТИИ §D-ОКНА ДЕНЕГ, А НЕ САМО ОТКРЫТИЕ ШКАЛЫ. Жалоба основательницы
        /// дословно — «сразу ПОСЛЕ ТУРИАЛА выпала карточка, на которую нет денег», и это не то же самое,
        /// что «после открытия шкалы»: между открытием и закрытием окна игрок КРУТИТ КРУТИЛКУ (условие
        /// выхода — семь тиков дохода), так что денег у него на выходе больше, чем на входе.
        /// Считать «по карману» надо по счёту, с которым игра РЕАЛЬНО продолжится, — то есть на закрытии.
        /// Побочная польза той же точности: чистые (бездрайверные) тесты `Game`, где никакого обучения
        /// нет, льготу не получают и проверяют механику BLOCK$ ровно как раньше.
        /// </summary>
        public void ArmAffordableNextCard()
        {
            _moneyGraceCard = true;
            ReplaceBlockedCurrentCard();
        }

        /// <summary>
        /// ВТОРАЯ ПОЛОВИНА ЛЬГОТЫ — ПЕРЕОФОРМЛЕНИЕ УЖЕ ВЫДАННОЙ КАРТОЧКИ (находка код-скептика r4).
        ///
        /// Первая редакция взводила только флаг на СЛЕДУЮЩУЮ выдачу — и жалоба основательницы осталась
        /// живой, потому что реальный порядок событий другой:
        ///   <c>I03</c> → <c>YA01</c> (`OPEN:Дн`, игрок отвечает) → <see cref="Advance"/> УЖЕ выдаёт
        ///   <c>FA05</c> (BLOCK$) и фиксирует <see cref="CurrentCardBlocked"/> → и только СЛЕДУЮЩИМ тиком
        ///   <see cref="CheckMoneyOpen"/> поднимает §D-окно ПОВЕРХ этой карточки.
        /// То есть к моменту закрытия обучения запертая дверь уже стоит на экране, и льгота на будущее её
        /// не трогает: игрок видит ровно то, на что пожаловался.
        ///
        /// Поэтому текущая карточка переоформляется ТЕМ ЖЕ правилом отбора: она уходит в обычную подмену
        /// из резерва (<see cref="Substitute"/>), а <see cref="Advance"/> под взведённой льготой выдаёт
        /// следующую доступную и ЧЕСТНО перезапускает таймер — карточка приходит целой, а не доигрывает
        /// чужие секунды.
        ///
        /// ⚠ ЭТО ПОДМЕНА ДО ВЗАИМОДЕЙСТВИЯ, А НЕ SKIP ОТВЕТА: путь ровно тот же, каким уходит любая
        /// гейтованная карточка (ответ не пишется, Δ не применяется, строки некролога не появляется), —
        /// игрок этой карточки ещё не касался, так что «побочных эффектов пропущенной» просто нет.
        ///
        /// Льгота при этом ОСТАЁТСЯ одноразовой: переоформление и есть та самая «первая карточка после
        /// обучения», <see cref="Advance"/> гасит флаг на ней. Если же текущая карточка по карману —
        /// ничего не происходит, и флаг доживает до следующей выдачи, как раньше.
        ///
        /// Спецрежимы исключены намеренно: в блице <see cref="CurrentCard"/> — мысль, а в депрессии
        /// нормальная карточка ПРИОСТАНОВЛЕНА (её таймер заморожен), и дёргать колоду из-под них нельзя.
        /// Совпасть с обучением деньгам они всё равно не могут (деньги открываются в 18, кризис — в 45+),
        /// так что это страховка, а не рабочая ветка.
        /// </summary>
        private void ReplaceBlockedCurrentCard()
        {
            if (State != GameState.Playing) return;
            if (_phase != CrisisPhase.None || InDepression) return;
            if (CurrentCard == null || !CurrentCardBlocked) return;

            Substitute(CurrentCard);   // …та же подмена из резерва, что у гейтованной карточки
            Advance();                 // …и та же выдача: льгота пропустит неподъёмные, таймер стартует заново
        }

        /// <summary>
        /// Tutorial-pause flag (set by the driver while the S5 overlay is up). While true, <see cref="Tick"/>
        /// freezes EVERYTHING — age, cost-of-living, installment drains and the card timer (canon §Подсказки).
        /// Pure Game exposes it; the visual overlay lives in the driver.
        /// </summary>
        public bool Paused { get; set; }

        /// <summary>
        /// MODAL-TUTORIAL variant of <see cref="Paused"/> (increment «экран появления новой шкалы», D).
        /// The D-screen does not close on a button: it closes only when the player actually WORKS the new
        /// scale's control (crank ticks / hold the height sensor until the battery fills / bring the marker into
        /// the zone / pick up a call).
        /// So the freeze must be split in two:
        /// <list type="bullet">
        /// <item>time keeps standing still — <see cref="Tick"/> still returns on <see cref="Paused"/>, so age,
        /// cost-of-living, all drains, the breakup timer and the card timer are frozen exactly as before;</item>
        /// <item>but the four scale INPUTS stay live — the crank, the breath, the balancer axis and the child
        /// press are no longer swallowed by the pause, otherwise the exit condition would be unreachable.</item>
        /// </list>
        /// The only things that still INTEGRATE under this flag are the two HELD controls: the balancer's
        /// axis (<see cref="TickModalBalancer"/>) — the marker has to MOVE while the player pushes the lever —
        /// and the height sensor (<see cref="TickModalBreath"/>) — the battery has to FILL while the player
        /// holds it, that being the literal task of the energy screen. Drift, the over-attention penalty, the
        /// breakup clock and the energy DRAIN stay frozen — this is a pause, not gameplay.
        /// Ignored unless <see cref="Paused"/> is also set. Cleared by the driver together with the pause.
        /// </summary>
        public bool PausedInputsLive { get; set; }

        /// <summary>True while the run is frozen AND the scale controls are frozen with it (the plain S5
        /// hint pause). The D-modal pause (<see cref="PausedInputsLive"/>) is deliberately NOT
        /// «input frozen» — that is the whole point of the screen.</summary>
        private bool InputsFrozen => Paused && !PausedInputsLive;

        /// <summary>
        /// BLOCK$-состояние ТЕКУЩЕЙ карточки. Любой ответ/таймаут по гашёной = пропуск без Δ, без строки
        /// некролога и без переноса (design-agent вариант «а», мокап S10); таймер при этом идёт.
        ///
        /// ⚠ r6 п.2 — ЭТО БОЛЬШЕ НЕ СНИМОК НА ВЫДАЧЕ, А ЖИВОЕ СВОЙСТВО. Раньше значение фиксировалось
        /// один раз в <see cref="Advance"/> и не менялось, пока карточка висит: игрок докручивал нужную
        /// сумму на глазах у запертой карточки, и она оставалась запертой до самого таймаута (жалоба
        /// основательницы, живой плейтест 2026-09-22). Теперь доступность считается ОТ ТЕКУЩИХ ДЕНЕГ
        /// и ходит В ОБЕ СТОРОНЫ: накрутил до цены — карточка ожила; стоимость жизни съела разницу —
        /// заперлась обратно. Пересчёт бесплатный (одно сравнение), поэтому никакого кэша нет вовсе —
        /// нечему и рассинхронизироваться.
        ///
        /// ⚠ КРЕДИТНЫЕ (<see cref="CreditCards"/>) — ИСКЛЮЧЕНИЕ, И ОНО ЖИВЁТ НА ОДНОСТОРОННЕЙ ЗАЩЁЛКЕ.
        /// У них уход в минус и есть содержание карточки, а взнос по ипотеке банк гейтит РОВНО ОДИН РАЗ,
        /// на показе (<see cref="UnaffordableNow"/> их тем же правилом освобождает от перепроверки на
        /// ответе). Отбирать УЖЕ ОДОБРЕННЫЙ кредит нельзя — поэтому защёлка выдачи
        /// (<see cref="_blockedAtDeal"/>) работает ТОЛЬКО В ОДНУ СТОРОНУ: она может удержать «не заперта»
        /// (одобрили на показе — значит одобрено до конца карточки), но НЕ МОЖЕТ удержать «заперта».
        ///
        /// ⚠ ЗАЧЕМ ИМЕННО ОДНОСТОРОННЯЯ (находка код-скептика r6, MAJOR). Первая редакция читала для
        /// кредитных голый `_blockedAtDeal`, и защёлка держала В ОБЕ СТОРОНЫ: `FC02` (ранняя ипотека,
        /// взнос 25 ₽), ВЫДАННАЯ НА МЕЛИ, оставалась запертой НАВСЕГДА — игрок докручивал 25 ₽ на её
        /// глазах, а карточка не отпиралась. Это ровно та жалоба, с которой основательница пришла в r6,
        /// и вдобавок прямое противоречие канону §3.5 «кредитные не блокируются вовсе». Теперь путь
        /// «заперта → ожила» открыт и кредитным, а обратный («банк передумал, пока ты думал») закрыт.
        /// </summary>
        public bool CurrentCardBlocked =>
            CurrentCard != null
            && (IsCreditCard(CurrentCard)
                    ? (_blockedAtDeal && WouldBeBlocked(CurrentCard))
                    : WouldBeBlocked(CurrentCard));

        /// <summary>
        /// Вердикт ВЫДАЧИ: была ли карточка заперта в момент, когда её показали. Для обычных BLOCK$ это
        /// теперь лишь историческая отметка (живое состояние считает <see cref="CurrentCardBlocked"/>),
        /// а для КРЕДИТНЫХ — единственный источник истины на всю жизнь карточки.
        /// </summary>
        private bool _blockedAtDeal;

        /// <summary>
        /// Последнее значение <see cref="CurrentCardBlocked"/>, о котором уже сообщено наружу, — чтобы
        /// <see cref="CardBlockedChanged"/> поднималось на ФРОНТЕ, а не каждый тик.
        /// </summary>
        private bool _blockedAnnounced;

        /// <summary>
        /// Доступность текущей карточки ПЕРЕКЛЮЧИЛАСЬ (r6 п.2) — в любую сторону. Драйвер по этому
        /// событию снимает/возвращает баннер «Как жаль…», приглушение карточки и чип цены; тинт зелёной
        /// плашки он и так переписывает каждый кадр. Поднимается из <see cref="Tick"/> после того, как
        /// деньги за этот такт уже сдвинулись (<see cref="IntegrateMoney"/>), и только на фронте.
        /// </summary>
        public event Action CardBlockedChanged;

        /// <summary>
        /// Свести «объявленное» состояние с фактическим и поднять фронт, если он есть. Зовётся и из
        /// тика (живой пересчёт), и из точек выдачи — чтобы новая карточка не унесла с собой чужой
        /// фронт от предыдущей.
        /// </summary>
        private void NoteBlockedChanged()
        {
            bool now = CurrentCardBlocked;
            if (now == _blockedAnnounced) return;
            _blockedAnnounced = now;
            CardBlockedChanged?.Invoke();
        }

        /// <summary>
        /// True when the CURRENT card is a BLOCK$ card with a known price. Drives the on-card price line
        /// (shown both affordable and blocked) — read-only view onto the single source <see cref="BlockPrices"/>.
        /// </summary>
        public bool CurrentCardHasPrice =>
            CurrentCard != null && CurrentCard.IsBlockCost && BlockPrices.ContainsKey(CurrentCard.Id);

        /// <summary>
        /// The current BLOCK$ card's price in ₽ — the ONE number used for the affordability gate, the ДА
        /// spend, and the on-card display. 0 when the current card carries no BLOCK$ price.
        /// </summary>
        public double CurrentCardPrice =>
            CurrentCardHasPrice ? BlockPrices[CurrentCard.Id] : 0;

        /// <summary>
        /// Будет ли ЭТОТ ответ обработан как BLOCK$-ПРОПУСК — то есть карточка уйдёт без Δ, без некролога и
        /// без записи ответа. Два случая: карточка пришла уже гашёной (<see cref="CurrentCardBlocked"/>)
        /// ЛИБО цена стала неподъёмной МЕЖДУ показом и ответом (<see cref="UnaffordableNow"/>) — стоимость
        /// жизни капает всё время, пока игрок думает.
        ///
        /// Публично — потому что драйверу нужно знать это ЗАРАНЕЕ: пропуск не отмечает работу по шкале и не
        /// панчит плашку (r3), а <see cref="HandleInput"/> возвращает true в обоих случаях.
        /// </summary>
        public bool AnswerWouldSkipAsBlocked(bool yes)
            => CurrentCard != null && (CurrentCardBlocked || (yes && UnaffordableNow(CurrentCard)));

        /// <summary>
        /// Current income multiplier (product of active FROM-gated multipliers). ≥ 1 unless wiped.
        /// While <see cref="Burnout"/> is active the crank «тяжелеет» — the product is halved
        /// (<see cref="BurnoutIncomeMult"/>), folded in here so every income path pays the same.
        /// </summary>
        public double IncomeMultiplier
        {
            get
            {
                double m = 1.0;
                foreach (var a in _mults)
                    if (a.FromAge == 0 || Age >= a.FromAge) m *= a.Value;
                if (Burnout) m *= BurnoutIncomeMult;
                return m;
            }
        }

        /// <summary>
        /// The age timer starts only when the card flagged <see cref="Card.StartsAgeTimer"/>
        /// (I03 «Сделать первый шаг?») resolves — with either answer. Until then Age stays 0.
        /// </summary>
        public bool AgeRunning { get; private set; }
        public float CardTimer { get; private set; }
        /// <summary>
        /// Full length of the timer the CURRENT card was dealt (§3 phase length, or the blitz/impulse
        /// window during a crisis). The dome-timer's arc is <see cref="CardTimer"/> / this — so the arc
        /// always empties over exactly the phase's own seconds, never over a hardcoded 5.
        /// </summary>
        public float CardTimerMax { get; private set; } = AnswerSecondsYouth;
        public string Cause { get; private set; }
        public NecrologResult Necrolog { get; private set; }

        public event Action StateChanged;
        public event Action CardChanged;
        /// <summary>
        /// Fired the instant a card resolves — with the card and the chosen <see cref="AnswerSide"/> —
        /// BEFORE the next card is drawn, so the driver's host bubble reacts to THIS choice. Semantic
        /// only: the bubble text and its RNG live in the driver's HostVoice, never here. NOT fired for a
        /// BLOCK$-blocked card (that is skipped, not answered).
        /// </summary>
        public event Action<Card, AnswerSide> AnswerResolved;
        /// <summary>Fired the first time money opens in a life (drives the S5 tutorial overlay + pause).</summary>
        public event Action MoneyOpened;
        /// <summary>Fired the first time energy opens (Age 25) — drives the §D «датчик высоты» screen + pause.</summary>
        public event Action EnergyOpened;
        /// <summary>Fired the first time health starts decaying (Age 30) — drives the S5 health hint + pause.</summary>
        public event Action HealthOpened;
        /// <summary>Fired the first time the relationships balancer opens (Age 20, by age alone) — drives the S5
        /// «держите отношения в зоне — ↑/↓» hint + pause.</summary>
        public event Action RelationshipsOpened;
        /// <summary>Fired the instant a breakup resolves (relationships spent ~10s cumulative below the
        /// zone floor): partner gone, balancer closed. NO death — drives the transient «РАССТАЛИСЬ» plate.</summary>
        public event Action RelationshipBrokeUp;
        /// <summary>
        /// Fired whenever burnout is entered (energy ≤10%). The driver shows the brief S5 hint only on
        /// the FIRST time per life (one-shot) and drives the S7 state plate off <see cref="Burnout"/>.
        /// </summary>
        public event Action BurnoutEntered;

        /// <summary>
        /// Счёт УШЁЛ ниже нуля (отрезок 0, §3.2: «долг должен звучать, а не просто краснеть»). Драйвер
        /// показывает реплику из пула Ведущего <c>debt</c>. Одноразово на каждый заход в минус: пока сидим
        /// в долгу, событие не повторяется; вышли в плюс — латч сбрасывается и следующий минус снова звучит.
        /// </summary>
        public event Action DebtEntered;

        /// <summary>
        /// «ПЛОХОЙ РОДИТЕЛЬ»: пропущен второй звонок ПОДРЯД — штраф уже применён (отношения −10,
        /// шкала ребёнка вниз). Отдельно от <see cref="ChildCallMissed"/>, который звучит на КАЖДЫЙ
        /// пропуск: этот — редкий и страшный, и у него свой низкий акцент в звуковом манифесте.
        /// Как и остальные события здесь — чисто семантика, никакой подачи.
        /// </summary>
        public event Action ChildBadParent;

        public Game(IEnumerable<Card> deck, Func<bool> coin = null, IEnumerable<Card> reserve = null)
        {
            _deck = deck?.ToList() ?? new List<Card>();
            _reserve = reserve?.ToList() ?? new List<Card>();
            _coin = coin ?? DefaultCoin();
        }

        /// <summary>
        /// Per-run sampling constructor: <paramref name="deckFactory"/> is invoked once now (so the
        /// opener already reports a deck) and again on every <see cref="StartLife"/>, giving each
        /// life a freshly sampled deck. Used by <see cref="GameDriver"/> with <see cref="DeckSampler"/>.
        /// </summary>
        public Game(Func<IReadOnlyList<Card>> deckFactory, Func<bool> coin = null)
        {
            _deckFactory = deckFactory;
            _coin = coin ?? DefaultCoin();
            _deck = deckFactory?.Invoke()?.ToList() ?? new List<Card>();
        }

        /// <summary>
        /// Plan constructor (deck + reserve): the reserve tops up the run when chain-gated cards are
        /// skipped, keeping the actually drawn count inside the 25–30 contract for any answer path.
        /// </summary>
        public Game(Func<DeckPlan> planFactory, Func<bool> coin = null)
        {
            _planFactory = planFactory;
            _coin = coin ?? DefaultCoin();
            AdoptPlan(planFactory?.Invoke());
        }

        private void AdoptPlan(DeckPlan plan)
        {
            _deck = plan?.Deck?.ToList() ?? new List<Card>();
            _reserve = plan?.Reserve?.ToList() ?? new List<Card>();
            _lt08 = plan?.Lt08;   // condition-triggered system heal card for this life (may be null)
            _depressionCard = plan?.Depression;   // CR09 carried on the plan (may be null); entered by Game only
            BuildCrisisLists(plan?.Crisis);
        }

        // Slice the plan's crisis block (CR00..CR08, id-sorted) into the blitz thoughts (CR01–CR05) and the
        // impulse cards (CR06–CR08) Game sequences. Null lists (no crisis in this plan) → crisis never fires.
        private void BuildCrisisLists(List<Card> crisis)
        {
            _blitzThoughts = null;
            _impulseCards = null;
            _impulseRound = null;
            if (crisis == null || crisis.Count == 0) return;
            var blitz = crisis.Where(c => c.Id == "CR01" || c.Id == "CR02" || c.Id == "CR03"
                                          || c.Id == "CR04" || c.Id == "CR05").ToList();
            // Импульс-ПУЛ: CR06–CR08 (платные, кроме разрыва) + CR10–CR12 (бесплатные, отрезок 0 §4.3).
            // Сам раунд по-прежнему короткий — какие именно карточки в него попадут, решает
            // <see cref="EnterImpulse"/> уже зная, сколько у игрока денег.
            var impulse = crisis.Where(c => c.IsInvert).ToList();
            if (blitz.Count > 0) _blitzThoughts = blitz;
            if (impulse.Count > 0) _impulseCards = impulse;
        }

        private static Func<bool> DefaultCoin()
        {
            var rng = new Random();                  // engine-free RNG
            return () => rng.Next(2) == 0;
        }

        public int DeckCount => _deck.Count;
        public int CardIndex => _index;

        /// <summary>
        /// Route a semantic input into the current state. The ONLY entry point for input.
        ///
        /// ВОЗВРАЩАЕТ «ПРИНЯТО ШКАЛОЙ»: true — только если ввод действительно ушёл в живую механику СВОЕЙ
        /// шкалы (ответ на карточку → здоровье, крутилка → деньги, датчик высоты → энергия, ось → отношения),
        /// то есть игрок прямо сейчас поработал этой шкалой. Именно по этому признаку драйвер открывает
        /// §6-окно «недавнего ввода» (салют даётся за КАЛИБРОВКУ, а не за нажатие).
        ///
        /// FALSE отдают все остальные исходы, и это НЕ мелочь:
        ///  • кризис и депрессия ГЛУШАТ шкальные контролы (крутилка/датчик/ось инертны) либо ПЕРЕНАЗНАЧАЮТ
        ///    рычаги ДА/НЕТ под блиц/импульс — это не ответ на карточку и не калибровка шкалы;
        ///  • шкала ещё не открыта или идёт «глухая» пауза (<see cref="InputsFrozen"/>) — ввод отвергнут;
        ///  • служебные вводы без шкалы: подтверждение на опенере/финале, ловля импульса в депрессии,
        ///    кнопка ребёнка.
        /// Без этого «нажал, но механика отвергла» открывало бы окно §6 — и рост шкалы КАРТОЧКОЙ внутри
        /// окна выдавал бы салют за чужую работу.
        /// </summary>
        public bool HandleInput(GameInput input)
        {
            switch (State)
            {
                case GameState.Opener:
                    if (input == GameInput.Confirm) StartLife();
                    break;
                case GameState.Playing:
                    // Depression intercepts EVERYTHING: the only live input is the pulse catch (scales are
                    // paused — crank/breath/axis/answers are all inert).
                    // ⚠ КОНТРОЛ ЛОВЛИ — КНОПКА «!» (CHILD_PRESS). РЕШЕНИЕ ОСНОВАТЕЛЬНИЦЫ 2026-08-08 по
                    // открытому вопросу п.3г: ловля стоит на «!», как и было в спеке встречи, а не на
                    // зелёной. Зелёная (ДА) в депрессии ИНЕРТНА — она сюда доходит и не делает ничего
                    // (значит и §6-окно не открывает, и плашку не панчит). Трубкой «!» в депрессии тоже
                    // не является: окно звонка под депрессией не крутится (Tick сюда не доходит).
                    if (InDepression)
                    {
                        if (input == GameInput.ChildPress) DepressionPress();
                        break;
                    }
                    // Crisis intercepts the two answer levers (ДА/НЕТ); crank/breath/axis/child are inert
                    // during the crisis (hands are on the blitz buttons — canon, scales paused too).
                    if (_phase == CrisisPhase.Blitz)
                    {
                        if (input == GameInput.AnswerYes) BlitzPress(pressedYes: true);       // рычаг ДА
                        else if (input == GameInput.AnswerNo) BlitzPress(pressedYes: false);  // рычаг НЕТ
                        break;
                    }
                    if (_phase == CrisisPhase.Impulse)
                    {
                        // INVERT: рычаг НЕТ = «СПАСИБО, НЕ НАДО» (отказ); рычаг ДА = поддаться. Молчание = ДА (в Tick).
                        if (input == GameInput.AnswerNo) ResolveImpulse(false);
                        else if (input == GameInput.AnswerYes) ResolveImpulse(true);
                        break;
                    }
                    if (input == GameInput.AnswerYes || input == GameInput.AnswerNo)
                    {
                        if (CurrentCard == null) return false;   // колода исчерпана / докат — отвечать нечем
                        Answer(input == GameInput.AnswerYes);
                        return true;
                    }
                    if (input == GameInput.MoneyTick) return Crank();
                    if (input == GameInput.EnergyHold) return HoldBreath();  // датчик поднят ЭТОТ кадр
                    if (input == GameInput.RelationUp) return SetRelationAxis(+1);
                    if (input == GameInput.RelationDown) return SetRelationAxis(-1);
                    if (input == GameInput.ChildPress) ChildPress();
                    break;
                case GameState.Finale:
                    if (input == GameInput.Confirm) ToOpener();
                    break;
            }
            return false;
        }

        public void StartLife()
        {
            if (_planFactory != null)
                AdoptPlan(_planFactory());                            // fresh deck + reserve each life
            else if (_deckFactory != null)
                _deck = _deckFactory()?.ToList() ?? new List<Card>(); // fresh deck each life
            Scales.Reset();
            _entries.Clear();
            _answers.Clear();
            _index = -1;
            Age = 0f;
            CardTimer = 0f;
            CardTimerMax = AnswerSecondsYouth;   // рестарт сбрасывает купол на полную дугу первой фазы
            AgeRunning = false;
            Cause = null;
            Necrolog = null;
            _scheduledFatalAge = float.NaN;
            _scheduledFatalCause = null;
            _coasting = false;
            ResetMoney();
            ResetHealthEnergy();
            ResetRelationships();
            ResetChild();
            ResetCrisis();
            ResetDepression();
            State = GameState.Playing;
            StateChanged?.Invoke();
            Advance();
        }

        /// <summary>Advance injected time: event-age progression + card countdown (timeout = random answer).</summary>
        public void Tick(float dt)
        {
            // Потребляем УДЕРЖИВАЕМЫЙ сигнал датчика ровно за этот тик (модель оси балансира): источник
            // переиздаёт его каждый кадр, пока рука держит датчик, поэтому «залипнуть» латч не может — если
            // игрок отпустил, следующий тик уже видит false. Гасим ДО всех ранних return'ов (кризис/
            // депрессия/пауза/не-Playing), чтобы сигнал не пронёсся в следующий тик.
            //
            // …но ТОЛЬКО на тике, который реально ИНТЕГРИРУЕТ время (dt > 0). Tick(0) — это кадр, за который
            // не прошло ни секунды: он не вправе ни вырастить батарею, ни СЪЕСТЬ удержание. Иначе кадр с
            // нулевым dt (пауза кадра, первый кадр после загрузки, ручной Tick(0) в тесте) молча съедал бы
            // поднятый датчик, и игрок терял бы удержание, которое честно держал.
            bool breathHeld = false;
            if (dt > 0f)
            {
                breathHeld = _breathHeld;
                _breathHeld = false;
            }

            if (State != GameState.Playing) return;
            if (Paused)
            {
                // Tutorial overlay up — age, drains, cost-of-living and the card timer stay frozen. The ONE
                // exception is the D-modal (PausedInputsLive): there the balancer's held axis and the height
                // sensor must still be integrated, or «hold the marker in the zone» / «держи, пока батарейка
                // не заполнится» would be dead controls. Everything else about the freeze is unchanged.
                if (PausedInputsLive) { TickModalBalancer(dt); TickModalBreath(dt, breathHeld); }
                return;
            }

            // Crisis owns the whole tick while active: only the fast blitz/impulse timer runs. The 5 live
            // scales are DELIBERATELY paused during the crisis (see TickCrisis) — no passive drain, no
            // spurious scale death; the crisis can only kill via an impulse-card Δ/fatal (contract).
            if (_phase != CrisisPhase.None) { TickCrisis(dt); return; }

            // Depression owns the whole tick while active: only the slow pulse scheduler runs. The 5 scales
            // are paused (like the crisis) and there is NO death path — the ONLY exit is the mini-game.
            if (InDepression) { TickDepression(dt); return; }

            if (CurrentCard == null)
            {
                // Deck exhausted with a delayed fatal pending: fast-forward age (brief beat at the
                // catch-up rate) until «за вами пришли» — never a «дожил» finale while it's pending.
                if (_coasting)
                {
                    Age += AgeCatchUpPerSecond * dt;
                    // NO open-checks here (deliberate): nothing NEW opens while coasting to the
                    // reckoning — an S5 hint pausing the death coast would be absurd. Scales already
                    // open keep draining below; an unopened scale has no drain to compete with anyway.
                    IntegrateMoney(dt);
                    IntegrateHealth(dt);               // live drains keep running while coasting —
                    IntegrateEnergy(dt, breathHeld);   // the reckoning doesn't freeze your body
                    // Deaths compete by first threshold crossed. TIE-BREAK (documented): when a scale
                    // hits zero within the SAME tick the scheduled age is reached, the scale death wins —
                    // it «happened» during the coast, before the knock on the door.
                    if (Scales.HealthDepleted) { End("здоровье не выдержало"); return; }
                    if (EnergyOpen && Scales.EnergyDepleted) { End("полное выгорание"); return; }
                    CheckScheduledFatal();
                }
                return;
            }

            if (AgeRunning) // age timer starts only once I03 has resolved
            {
                float target = CurrentCard.Age;
                if (Age < target)
                    Age = Math.Min(target, Age + AgeCatchUpPerSecond * dt);
                // Once caught up to the current card's age, HOLD there — do NOT creep past it. The old
                // creep (AgeSlowTickPerSecond ≈0.4/s → ~2 years for every 5s lingered on a card) raced the
                // displayed age far ahead of the card ages, so mechanics opened back-to-back during
                // childhood and «первая любовь» (card age 20) showed at ~33. Age is now purely event-based
                // (= the current card's age): reveals (money@18/rel@20/energy@25/health@30) fire only when a
                // card of that age is actually drawn, so the 10-cards-between-mechanics pacing holds in real
                // time (founder playtest 2026-07-23).

                if (CheckMoneyOpen()) return;      // open money (18) → tutorial pause may freeze this frame
                if (CheckRelationshipsOpen()) return; // open relationships balancer (20) → hint + pause
                if (CheckEnergyOpen()) return;     // open energy (25) → «датчик высоты» screen + pause
                if (CheckHealthDecayOpen()) return;// health starts decaying (30) → hint + pause
                if (CheckCrisisTrigger()) return;  // кризис среднего возраста (45–50) → blitz, one-shot
                if (CheckScheduledFatal()) return; // «за вами пришли» once age crosses card.Age+n
                CheckScheduledBreak();             // `BREAK:Отн`+DELAY(n): «через 2 года развод» (RND05)
            }

            IntegrateMoney(dt);                    // cost-of-living + installment drains (real-time)
            IntegrateHealth(dt);                   // decay from 30 (×LT01 modifier), real-time
            IntegrateEnergy(dt, breathHeld);       // дренаж с 25 + реген под поднятым датчиком + выгорание
            IntegrateRelationships(dt);            // drift + RELATION_AXIS + breakup (no death), real-time
            IntegrateChild(dt);                    // flash scheduler + missed-flash bad-parent penalty (no death)

            // r6 п.2: деньги за этот такт уже сдвинулись (стоимость жизни/дренажи выше, крутилка — в
            // HandleInput этого же кадра), значит доступность текущей карточки могла ПЕРЕКЛЮЧИТЬСЯ —
            // в любую сторону. Само значение живое и читается свойством; здесь только поднимается
            // фронт для драйвера, чтобы он снял/вернул баннер и чип цены, не перекладывая их каждый кадр.
            NoteBlockedChanged();

            if (Scales.HealthDepleted) { End("здоровье не выдержало"); return; }
            if (EnergyOpen && Scales.EnergyDepleted) { End("полное выгорание"); return; }
            MaybeTriggerLt08();                    // «Пора подлечиться!» when health<40% & age≥30

            CardTimer -= dt;
            if (CardTimer <= 0f)
                Answer(_coin(), timeout: true);   // не успел — берём ДА или НЕТ случайно (тон — «пропуск»)
        }

        /// <summary>
        /// ПОРЯДОК «карточка → открытие шкалы» (основательница, живой плейтест 2026-08-07: «карточка
        /// „начать встречаться“ приходит ПОСЛЕ открытия шкалы отношений — нелогично»).
        ///
        /// Причина была не в колоде, а в момент срабатывания: возраст догоняет возраст ТЕКУЩЕЙ карточки
        /// сразу, как её выдали, поэтому возрастной гейт (20) щёлкал ПОКА `YA03` ещё висела неотвеченной —
        /// туториал шкалы вставал поверх собственного вопроса. Колода при этом правильная: `YA03` и так
        /// первая среди карточек 20 лет (сортировка по возрасту, потом по порядку в CSV).
        ///
        /// Правило: открытие ПРИДЕРЖИВАЕТСЯ, пока текущая карточка сама несёт флаг `OPEN:{scale}`. Как
        /// только она отвечена и выдана следующая — возраст уже перейден, и гейт щёлкает первым же тиком.
        /// Канон-возрасты (18/20/25) не тронуты; правило одно на все три шкалы.
        /// </summary>
        private bool HeldByItsOwnCard(string scale)
            => CurrentCard != null && CurrentCard.Opens(scale);

        // Opens the money scale the first time Age reaches 18 and announces it (tutorial + pause hook).
        // Returns true if the frame should stop here (a listener paused the game on open).
        private bool CheckMoneyOpen()
        {
            if (MoneyOpen || Age < MoneyOpenAge) return false;
            if (HeldByItsOwnCard(Card.OpenMoney)) return false;   // сначала ответь на СВОЮ карточку
            MoneyOpen = true;
            MoneyOpened?.Invoke();   // driver shows the S5 overlay and sets Paused
            return Paused;
        }

        // Opens the energy scale the first time Age reaches 25 (YA05 «первая усталость»). Fires the §D
        // modal; energy starts draining from here. Returns true if a listener paused the frame.
        //
        // ⚠ Шкала открывается ПРОСЕВШЕЙ (<see cref="EnergyOpenValue"/>), а не полной — иначе задача экрана
        // «держи, пока батарейка не заполнится» была бы выполнена ещё до того, как игрок коснулся датчика
        // (2026-08-07). Минимум с текущим значением: открытие не может подарить энергию.
        private bool CheckEnergyOpen()
        {
            if (EnergyOpen || Age < EnergyOpenAge) return false;
            if (HeldByItsOwnCard(Card.OpenEnergy)) return false;  // сначала ответь на СВОЮ карточку (YA05)
            EnergyOpen = true;
            Scales.Energy = Math.Min(Scales.Energy, EnergyOpenValue);
            _energyFrac = 0;
            EnergyOpened?.Invoke();
            return Paused;
        }

        // Health starts decaying at 30 (canon: до 30 не убывает). Fires the health hint. Returns true
        // if a listener paused the frame.
        private bool CheckHealthDecayOpen()
        {
            if (HealthDecaying || Age < HealthDecayFromAge) return false;
            HealthDecaying = true;
            HealthOpened?.Invoke();
            return Paused;
        }

        // The relationships balancer opens the first time Age reaches 20: starts at 55% (already restored
        // by Scales.Reset), drift + axis + breakup begin from here. Fires the S5 «держите отношения в
        // зоне» hint. Once a breakup has closed it (RelationshipsLost) the age gate does NOT reopen it —
        // the MD06 second chance that would is deferred. Returns true if a listener paused the frame
        // (parallels money/energy/health opens).
        //
        // ⚠ ГЕЙТ `HeldByItsOwnCard` ДЛЯ ОТН СНЯТ (r4 п.2, решение основательницы). Раньше открытие
        // придерживалось, пока на экране стоит YA03 «ПЕРВАЯ ЛЮБОВЬ! Начать встречаться?» — шкала
        // появлялась «по карточке». Основательница убрала YA03 из колоды целиком: карточка, на которую
        // можно ответить НЕТ без последствий (шкала всё равно откроется по возрасту), — обман игрока.
        // Шкала отношений теперь открывается ЧИСТО ПО ВОЗРАСТУ (20), без карточки-привратника.
        // Деньги (18/YA01) и энергия (25/YA05) свой `HeldByItsOwnCard` СОХРАНЯЮТ — их карточки-открывашки
        // в колоде остались. MD06 («Второй шанс на любовь?», ~40, OPEN:Отн) ничего не теряет: пока она на
        // экране, `RelationshipsLost` ещё true и строка выше уже возвращает false — гейт для неё был мёртв.
        private bool CheckRelationshipsOpen()
        {
            if (RelationshipsOpen || RelationshipsLost || Age < RelationshipsOpenAge) return false;
            RelationshipsOpen = true;
            RelationshipsOpened?.Invoke();
            return Paused;
        }

        // A CHAIN child is drawable only after its parent resolved ДА. Parents always precede their
        // children in age order, so by the time we reach a gated card its parent is already answered.
        private bool GatedOff(Card c)
            => c.RequiresParentYes != null
               && !(_answers.TryGetValue(c.RequiresParentYes, out var yes) && yes);

        /// <summary>
        /// Условие «если Здр&lt;N%» из колонки «Когда» (`LT02` операция, `LT08` подлечиться): карточка
        /// приходит только тому, у кого здоровье НИЖЕ порога в момент показа.
        ///
        /// ⚠ Раньше это условие было ЗАШИТО по id (`c.Id == "LT02"`), хотя стоит в CSV у двух карточек —
        /// частный случай той же дыры, что и «если Отн открыта»: колонка пишет условие, а работает оно
        /// только у той строки, которую кто-то вспомнил захардкодить. Читается из карточки.
        /// </summary>
        private bool HealthGatedOff(Card c)
            => !double.IsNaN(c.RequiresHealthBelow) && Scales.Health >= c.RequiresHealthBelow;

        /// <summary>
        /// Условие «если {шкала} открыта» (`MD01` свадьба, `FB31`, `YA04`, `RND05`) — гейт ПО ЖИВОМУ
        /// СОСТОЯНИЮ на момент показа, ровно как здоровье у <see cref="HealthGatedOff"/>.
        ///
        /// ⚠ НАХОДКА РЕВЮ (MAJOR). Форма молча игнорировалась парсером, и «Сделать предложение?» приходила
        /// игроку, который на `YA03` ответил НЕТ и шкалу отношений не открывал; следом `MD02` предлагала
        /// ребёнка от несуществующего партнёра. Скобка «шкала закрыта» — это НЕ «шкала в нуле»: закрытая
        /// шкала не показана на экране вовсе, и Δ по ней не применяется (то же правило, что у
        /// <see cref="ScaleOpenForDelta"/>), поэтому карточка про неё бессмысленна.
        /// </summary>
        private bool ScaleGatedOff(Card c)
            => c.RequiresScaleOpen != null && !ScaleIsOpenNow(c.RequiresScaleOpen);

        /// <summary>
        /// Условие «если в браке» (r4 п.5) — ЖИВОЙ гейт свадебной ветки поверх чейна ответов.
        ///
        /// ⚠ ЖАЛОБА ОСНОВАТЕЛЬНИЦЫ (живой плейтест 2026-08-08): «после расставания приходят карточки про
        /// свадьбу — ветка должна уходить целиком». Корень — в том, что <see cref="GatedOff"/> смотрит
        /// ТОЛЬКО в <c>_answers</c>: `свадьба +N` подставляет `RequiresParentYes = "MD01"`, а пак «Брак на
        /// износе» пишет «если MD01=ДА» руками. Обе формы спрашивают «сказал ли игрок ДА на свадьбе
        /// КОГДА-ТО», и ответ остаётся ДА и через тридцать лет после развода: <see cref="BreakUp"/> гасит
        /// <see cref="Married"/>, но историю не переписывает (и не должен — некролог по ней строится).
        ///
        /// Поэтому гейт живого состояния добавляется ОТДЕЛЬНОЙ строкой в колонке «Когда», а не подменяет
        /// чейн: «когда-то поженились» И «женаты сейчас» — разные вопросы, ветке нужны оба ответа.
        /// </summary>
        private bool MarriedGatedOff(Card c) => c.RequiresMarried && !Married;

        /// <summary>
        /// Была бы эта карточка выдана ЗАБЛОКИРОВАННОЙ (BLOCK$ дороже, чем есть на счету СЕЙЧАС) —
        /// ровно то правило, по которому <see cref="CurrentCardBlocked"/> фиксируется на выдаче.
        /// Один источник истины на две точки: сама выдача и льгота первой карточки после обучения
        /// деньгам (<see cref="_moneyGraceCard"/>).
        /// </summary>
        private bool WouldBeBlocked(Card c)
            => c.IsBlockCost && BlockPrices.TryGetValue(c.Id, out var price) && Money < price;

        // Токены те же, что в колонке Δ и во флагах `OPEN:*`. Незнакомого токена сюда не приходит:
        // CardLoader.NormalizeScaleToken его не выдаёт, а нераспознанная форма краснит валидатор колоды.
        private bool ScaleIsOpenNow(string token) => token switch
        {
            Card.OpenRelations => RelationshipsOpen,
            Card.OpenMoney => MoneyOpen,
            Card.OpenEnergy => EnergyOpen,
            Card.OpenChild => ChildOpen,
            _ => true,      // «Здр» — здоровье открыто с первого кадра
        };

        /// <summary>Условие «(возраст ≥ N)» (`LT08`) — нижняя граница показа по живому возрасту.</summary>
        private bool AgeGatedOff(Card c) => c.RequiresMinAge >= 0 && Age < c.RequiresMinAge;

        /// <summary>
        /// Карточка принадлежит ветке `EXCL:*`, которая в этом забеге УЖЕ выбрана (ипотека взята → вторая
        /// ипотека не приходит; решение по детям принято → встречная карточка не приходит). Обрабатывается
        /// ровно как CHAIN-гейт: пропуск + подмена из резерва, чтобы длина забега не поехала.
        /// </summary>
        private bool ExcludedByBranch(Card c)
            => !string.IsNullOrEmpty(c.ExclusiveGroup) && _exclusiveTaken.Contains(c.ExclusiveGroup);

        /// <summary>
        /// Условие «на счету &lt; N ₽» из колонки «Когда» (`FA35` первый ноль, `FC16` вернуться к родителям).
        /// Считается НА МОМЕНТ ПОКАЗА, как и <c>BLOCK$</c>: карточка приходит только тому, у кого сейчас
        /// пусто. Тот, кто крутил, этой сцены не увидит — так и задумано (дизайн-док отрезка 2 §3).
        /// Деньги закрыты → условие не выполнимо, карточка не приходит.
        /// </summary>
        private bool MoneyGatedOff(Card c)
            => (!double.IsNaN(c.RequiresMoneyBelow) && (!MoneyOpen || Money >= c.RequiresMoneyBelow))
               // …и зеркальное «на счету ≥ N ₽» (`MD03` машина, `MD04` поздняя ипотека). Тоже молча
               // игнорировалось до находки ревю: «купить машину за 60 ₽» приходила пустому кошельку.
               || (!double.IsNaN(c.RequiresMoneyAtLeast) && (!MoneyOpen || Money < c.RequiresMoneyAtLeast));

        /// <summary>
        /// Условие «если Отн потеряна» (`MD06` второй шанс). Пока партнёр есть, второго шанса не предлагают.
        /// </summary>
        private bool RelationshipsGatedOff(Card c)
            => c.RequiresRelationshipsLost && !RelationshipsLost;

        private void Advance()
        {
            if (CheckScheduledFatal()) return;
            while (true)
            {
                _index++;
                if (_index >= _deck.Count) { EndOfDeck(); return; }
                var c = _deck[_index];
                if (GatedOff(c) || HealthGatedOff(c) || ExcludedByBranch(c)
                    || MoneyGatedOff(c) || RelationshipsGatedOff(c)
                    || ScaleGatedOff(c) || MarriedGatedOff(c) || AgeGatedOff(c)   // parent≠ДА / здоровье / живой брак
                    || (_moneyGraceCard && WouldBeBlocked(c)))                    // / нечем платить сразу после обучения
                {                                 // / ветка `EXCL:*` уже занята / не тот счёт
                                                  // / отношения ещё целы / шкала условия ЗАКРЫТА
                                                  // / рано по возрасту → skip…
                    Substitute(c);                // …and top up from the reserve (drawn count 25–30)
                    continue;
                }
                CurrentCard = c;
                // Вердикт ВЫДАЧИ («на момент показа денег меньше цены»). Для обычных BLOCK$ это
                // отметка истории — живое состояние считает CurrentCardBlocked; для КРЕДИТНЫХ это
                // и есть их единственный гейт (r6 п.2).
                _blockedAtDeal = WouldBeBlocked(c);
                _blockedAnnounced = CurrentCardBlocked;   // новая карточка не тащит чужой фронт
                _moneyGraceCard = false;   // льгота одноразовая — тратится на ПЕРВОЙ же выданной карточке
                // §3: длительность = фаза возраста ЭТОЙ карточки (блиц идёт своей веткой).
                CardTimerMax = AnswerSecondsFor(c.Age);
                CardTimer = CardTimerMax;
                CardChanged?.Invoke();
                CountDepressionGapCard();   // зазор «кризис → депрессия» считается ОБЫЧНЫМИ карточками
                return;
            }
        }

        /// <summary>
        /// Replace a skipped chain-gated card with a reserve card so the run length holds.
        /// Deterministic (no RNG): (a) the first reserve card (age-sorted) already at an age ≥ the
        /// skipped card's age; (b) else the earliest reserve card whose «Когда» window still reaches
        /// that age, re-aged to it; (c) else no substitute (reserve exhausted). The substitute is
        /// inserted into the remaining deck at its age-sorted slot.
        /// </summary>
        private void Substitute(Card skipped)
        {
            if (_reserve.Count == 0) return;

            int ri = _reserve.FindIndex(r => r.Age >= skipped.Age);
            Card sub = null;
            if (ri >= 0)
            {
                sub = _reserve[ri];
                _reserve.RemoveAt(ri);
            }
            else
            {
                ri = _reserve.FindIndex(r => DeckSampler.AgeWindow.Parse(r.When).Max >= skipped.Age);
                if (ri < 0) return;
                sub = _reserve[ri];
                _reserve.RemoveAt(ri);
                sub.Age = skipped.Age;
            }

            int at = _deck.FindIndex(_index + 1, c => c.Age > sub.Age);
            if (at < 0) at = _deck.Count;
            _deck.Insert(at, sub);
        }

        // Deck exhausted: with a delayed fatal pending we coast (age runs on) instead of «дожил».
        private void EndOfDeck()
        {
            if (!float.IsNaN(_scheduledFatalAge))
            {
                _coasting = true;
                CurrentCard = null;
                CardChanged?.Invoke();
                return;
            }
            EndNatural();
        }

        // Delayed FATAL (RND01): fires the moment event-age reaches the scheduled age.
        private bool CheckScheduledFatal()
        {
            if (float.IsNaN(_scheduledFatalAge) || Age < _scheduledFatalAge)
                return false;
            Age = _scheduledFatalAge;             // death age snaps to «resolved age + n» exactly
            var cause = _scheduledFatalCause;
            _scheduledFatalAge = float.NaN;
            _scheduledFatalCause = null;
            _coasting = false;
            End(cause);
            return true;
        }

        /// <summary>
        /// Отложенный разрыв (`BREAK:Отн` + `DELAY(n)`): партнёр уходит в тот момент, когда событийный
        /// возраст доходит до «возраст карточки + n». Разрыв — не смерть, поэтому кадр не прерывается.
        /// Если отношения к этому моменту уже потеряны (успел разойтись раньше), запись просто гасится.
        /// </summary>
        private void CheckScheduledBreak()
        {
            if (float.IsNaN(_scheduledBreakAge) || Age < _scheduledBreakAge) return;
            _scheduledBreakAge = float.NaN;
            if (RelationshipsOpen) BreakUp(byCard: true);
        }

        private void Answer(bool yes) => Answer(yes, timeout: false);

        private void Answer(bool yes, bool timeout)
        {
            var card = CurrentCard;
            if (card == null) return;

            // BLOCK$ при нехватке денег: любой ответ/таймаут = пропуск без Δ, без некролога, без
            // записи ответа (CHAIN-гейт не считает это ДА) и без повторного выпадения — просто дальше.
            // Сюда же (находка ревью, MAJOR) попадает ДА по карточке, которая на ПОКАЗЕ была по карману, а
            // к моменту ответа перестала: стоимость жизни капает, пока игрок думает, и без перепроверки
            // ответ применял бы последствия покупки при неполной оплате (зажим в AddCardMoney списал бы
            // остаток — «купил флагман за 49 ₽»). Кредитные карточки сюда не ходят: им в минус можно.
            if (AnswerWouldSkipAsBlocked(yes))
            {
                Advance();
                return;
            }

            _answers[card.Id] = yes;   // recorded before consequences so CHAIN gates can read it

            if (card.StartsAgeTimer)
                AgeRunning = true; // ДА и НЕТ равнозначны: «шаг всё равно происходит»

            if (ApplyResolvedConsequences(card, yes,
                    timeout ? AnswerSide.Timeout : yes ? AnswerSide.Yes : AnswerSide.No))
                return;   // the run ended (fatal / scale depletion) — no advance

            Advance();
        }

        /// <summary>
        /// Apply a resolved card's consequences on the chosen side: Δ (BLOCK$ price handling), long effects,
        /// necrolog line, card-id specials, the host reaction hook, and the delayed/immediate fatal + live
        /// scale-death checks. Returns true if the run ended (so the caller skips advancing). Shared by the
        /// ordinary <see cref="Answer(bool,bool)"/> path and the crisis impulse round, so both apply identical
        /// consequences. Does NOT touch the age timer or the BLOCK$-blocked skip — those are Answer-specific.
        /// </summary>
        private bool ApplyResolvedConsequences(Card card, bool yes, AnswerSide side)
        {
            if (!card.IsNoCons)
            {
                // ТОЧНАЯ СУММА ПОБЕЖДАЕТ CSV-Δ. Если у карточки есть цена (<see cref="BlockPrices"/>) или
                // заработок (<see cref="CardYesIncome"/>) — это ОДНО число и есть её денежная история: им
                // гейтится доступность, оно списывается/начисляется на ДА и оно же печатается на карточке.
                // Денежная Δ такой строки CSV игнорируется НА ОБЕИХ сторонах, иначе списание было бы двойным
                // и в другом масштабе. Остальные шкалы Δ применяются как обычно (MD03 «Эн +2», LT02 «Здр
                // +40», LT08 «Здр → 80%»). НЕТ не тратит и не зарабатывает.
                // (Сюда попадаем только когда карточка НЕ гашёная — гашёная вернулась выше без Δ и трат.)
                bool hasExplicitMoney = BlockPrices.ContainsKey(card.Id) || CardYesIncome.ContainsKey(card.Id);
                ApplyCardDeltas(card, yes ? card.YesDeltas : card.NoDeltas, skipMoney: hasExplicitMoney);
                if (yes)
                {
                    if (card.IsBlockCost && BlockPrices.TryGetValue(card.Id, out var price))
                        AddCardMoney(card, -price);                    // spend exactly the price on ДА
                    else if (CardYesIncome.TryGetValue(card.Id, out var income))
                    {
                        Money += income;                               // заработок/кредит — точная сумма
                        NoteMoneyChanged();
                    }
                }
                // Множители/дренажи/дрейф — той стороны, которая выпала (у НЕТ они появились с `MD01`).
                ApplyLongEffects(card, yes);
                // FORCED cards (вехи/объявления, no real choice) NEVER write a necrolog line — canon.
                // Enforced structurally here, independent of what the CSV cell happens to hold.
                if (!card.IsForced)
                {
                    string line = yes ? card.YesNecrolog : card.NoNecrolog;
                    if (!string.IsNullOrEmpty(line))
                        _entries.Add(new NecrologEntry
                        {
                            Age = card.Age,
                            Order = card.Order,
                            Line = line,
                            IsRond = card.IsRond,
                            IsMilestone = card.IsTimeline,   // вехи не выкидываются при отборе (§6.2)
                        });
                }
            }

            // Ветка `EXCL:*` выбрана (ипотека взята / решение по детям принято) — остальные карточки той
            // же группы в этом забеге больше не появятся. Только на ДА: отказ ветку не закрывает.
            if (yes && !string.IsNullOrEmpty(card.ExclusiveGroup))
                _exclusiveTaken.Add(card.ExclusiveGroup);

            // Брачный договор — разово и на всю жизнь.
            if (yes && card.IsPrenup) _prenup = true;

            // `BREAK:Отн` — карточка РВЁТ отношения. Сразу либо через DELAY(n) лет (RND05: «через 2 года
            // развод»). Ставится ДО объявления результата, чтобы Ведущий комментировал уже случившееся.
            //
            // ⚠ РВАТЬ МОЖНО ТОЛЬКО ОТКРЫТУЮ ШКАЛУ (находка ревью, MAJOR). Отложенный путь этот гард нёс с
            // самого начала (<see cref="CheckScheduledBreak"/>), а немедленный — нет: карточка `BREAK:Отн`,
            // попавшая до двадцати или уже ПОСЛЕ разрыва, переворачивала RelationshipsLost, роняла шкалу в
            // <see cref="RelBreakupValue"/> и объявляла разрыв во второй раз — по партнёру, которого нет.
            // Сторона разрыва — обычно ДА, но `MD24` («Потерпеть ради ребёнка?») рвёт брак на НЕТ.
            if (yes != card.BreakOnNoSide && card.BreaksRelationships)
            {
                if (card.BreakDelayYears > 0) _scheduledBreakAge = card.Age + card.BreakDelayYears;
                else if (RelationshipsOpen) BreakUp(byCard: true);
            }

            // ВТОРОЙ ШАНС (`MD06`, отрезок 6). Балансир отношений открывается ЗАНОВО после расставания.
            // До отрезков 1–7 карточка была hard-excluded из колоды именно потому, что механики не было:
            // <see cref="CheckRelationshipsOpen"/> проверяет <see cref="RelationshipsLost"/> и после разрыва
            // не открывает шкалу уже никогда. Точка встройки — здесь: карточка с `OPEN:Отн`, отвеченная ДА
            // по ПОТЕРЯННОЙ шкале, снимает этот флаг, и обычный возрастной путь открытия снова доступен.
            // Шкала возвращается в стартовое значение — новые отношения начинаются с чистого листа, а не с
            // «одиноко»-уровня прошлого разрыва; сама Δ карточки («Отн +12») ложится поверх уже открытой
            // шкалы по правилу `HeldByItsOwnCard`, ровно как у `YA03`.
            if (yes && RelationshipsLost && card.Opens(Card.OpenRelations))
            {
                RelationshipsLost = false;
                Scales.Relationships = Scales.RelationshipsStart;
            }

            // Card-id specials on the live layer: LT01 sets the health-decay modifier (both answers),
            // KEK04=ДА gives a small one-shot health plus. Runs regardless of NOCONS (KEK04 is NOCONS).
            ApplyCardSpecial(card, yes);

            // Host reaction hook: announce the resolution (card + side) BEFORE the fatal/advance fork,
            // so the bubble reflects THIS choice no matter what happens next. Timeout carries the «skip»
            // tone even though the mechanical pick above was a coin flip.
            AnswerResolved?.Invoke(card, side);

            // Delayed fatal (RND01): ДА schedules «за вами пришли» for card.Age + n, life continues.
            if (yes && card.DelayedFatalYears > 0)
            {
                _scheduledFatalAge = card.Age + card.DelayedFatalYears;
                _scheduledFatalCause = card.FatalCause;
            }

            if (yes && card.YesIsFatal) { End(card.FatalCause); return true; }
            if (Scales.HealthDepleted) { End("здоровье не выдержало"); return true; }
            if (EnergyOpen && Scales.EnergyDepleted) { End("полное выгорание"); return true; }

            MaybeTriggerLt08();   // a health-hit card may drop below 40% → «Пора подлечиться!»
            return false;
        }

        // LT01 modifier + KEK04 bonus. LT02→80% / LT08→80% heals ride the normal Δ path (Set), so they
        // are NOT duplicated here. LT01 also carries its own ±2 Δ via the normal path; this only sets
        // the ongoing decay multiplier.
        private void ApplyCardSpecial(Card card, bool yes)
        {
            switch (card.Id)
            {
                case "LT01":
                    _healthDecayMult = yes ? Lt01CareDecayMult : Lt01NeglectDecayMult;
                    break;
                case "KEK04":
                    if (yes) HealHealth(Kek04HealthBonus);
                    break;
                case "MD01":
                    // Свадьба (MD01=ДА): relationships enter «реже балансировать» — the drift softens to
                    // RelDriftMarriedPerSec. НЕТ (свобода) leaves the full drift. Only meaningful while the
                    // balancer is open (canon MD01 «если Отн открыта») — never latch married after a
                    // breakup has closed it. Cleared by a breakup.
                    if (yes && RelationshipsOpen) Married = true;
                    break;
                case "MD02":
                    // Ребёнок (MD02=ДА «завести ребёнка», OPEN:Реб): opens the child scale + tamagotchi
                    // button and fires the S5 hint. The deck places MD02 at «свадьба +2» and gates it
                    // CHAIN→MD01=ДА, so this only fires ≥2 game-years after the wedding. НЕТ → nothing opens.
                    if (yes) OpenChild();
                    break;
                case "LT04":
                    // Дети выросли (LT04, «55–65, если MD02=ДА»): the button/scale go dark either way. The
                    // ДА «навязчивая опека» отношения −1 rides the normal CSV Δ (applied before this); НЕТ
                    // «отпустил» carries no Δ. Both just close the child mechanic — never a death.
                    CloseChild();
                    break;
            }
        }

        // Raise health by a whole amount, clamped to 100; card Δ owns the rest (this is only for the
        // KEK04 system bonus, which is otherwise a NOCONS card).
        private void HealHealth(int amount)
        {
            Scales.Health = Math.Min(100, Scales.Health + amount);
        }

        // Survived the whole deck with nothing pending → reached old age; tone by relationships.
        // (A pending delayed fatal never gets here — EndOfDeck coasts to «за вами пришли» instead.)
        private void EndNatural()
        {
            string tone =
                Scales.Relationships >= JoyfulOldAgeRelationships ? "весёлая старость" :
                Scales.Relationships <= LonelyOldAgeRelationships ? "одинокая старость" :
                                                                    "спокойная старость";
            End(tone);
        }

        private void End(string cause)
        {
            Cause = cause;
            Necrolog = ThanksNoThanks.Necrolog.Build(cause, _entries);
            CurrentCard = null;
            State = GameState.Finale;
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Arcade contract §5 clean-exit hook (MenuButton / <see cref="GameInput.Exit"/>): abandon the
        /// current run from ANY state and return to a fresh opener life. Same full reset as a
        /// finale→opener restart — no card logic changed, just made callable mid-run.
        /// </summary>
        public void AbortToOpener() => ToOpener();

        private void ToOpener()
        {
            Scales.Reset();
            _entries.Clear();
            _answers.Clear();
            _index = -1;
            Age = 0f;
            CardTimer = 0f;
            CardTimerMax = AnswerSecondsYouth;   // рестарт сбрасывает купол на полную дугу первой фазы
            AgeRunning = false;
            Cause = null;
            Necrolog = null;
            _scheduledFatalAge = float.NaN;
            _scheduledFatalCause = null;
            _coasting = false;
            ResetMoney();
            ResetHealthEnergy();
            ResetRelationships();
            ResetChild();
            ResetCrisis();
            ResetDepression();
            CurrentCard = null;
            State = GameState.Opener;
            StateChanged?.Invoke();
        }

        // ================================================================ midlife crisis (blitz + impulse)

        private void ResetCrisis()
        {
            _phase = CrisisPhase.None;
            _crisisDone = false;
            _blitzIndex = 0;
            _blitzFails = 0;
            _blitzNormalOnYes = false;
            _impulseIndex = 0;
            _crisisTimer = 0f;
            _suspendedCard = null;
            _suspendedTimer = 0f;
            _suspendedTimerMax = AnswerSecondsYouth;
            _suspendedBlocked = false;
        }

        // Fires once when Age first reaches 45 (canon 45–50 window floor), provided the plan carries the
        // crisis block. One-shot per life (_crisisDone latches at trigger). Returns true when it fired so
        // the tick stops here — the next tick runs TickCrisis instead of ordinary play.
        private bool CheckCrisisTrigger()
        {
            if (_crisisDone || _phase != CrisisPhase.None) return false;
            if (_blitzThoughts == null || _blitzThoughts.Count == 0) return false;
            if (Age < CrisisTriggerAge) return false;
            EnterCrisis();
            return true;
        }

        // Suspend the current normal card and open the blitz. The CR00 announce is a driver-only flourish
        // (HostContent.CrisisAnnounce, in the host bubble); the mechanic is the 5 thoughts. Latches
        // _crisisDone so nothing
        // can re-trigger this life (not even mid-crisis).
        private void EnterCrisis()
        {
            _crisisDone = true;
            _suspendedCard = CurrentCard;         // resumed verbatim after the crisis
            _suspendedTimer = CardTimer;
            _suspendedTimerMax = CardTimerMax;
            _suspendedBlocked = _blockedAtDeal;   // сохраняем ВЕРДИКТ ВЫДАЧИ: живое состояние пересчитается само
            _phase = CrisisPhase.Blitz;
            _blitzIndex = 0;
            _blitzFails = 0;
            CrisisStarted?.Invoke();              // driver: «КРИЗИС СРЕДНЕГО ВОЗРАСТА! БЛИЦ!»
            StartBlitzThought();
        }

        // Put up the current thought (CR0N): its text as CurrentCard, a fresh 2s timer, and a seeded
        // «ВСЁ НОРМАЛЬНО» side. Blitz thoughts carry NO Δ — only the fail counter matters.
        private void StartBlitzThought()
        {
            CurrentCard = _blitzThoughts[_blitzIndex];
            _blockedAtDeal = false;                  // мысль блица — не BLOCK$-карточка ни на каком счету
            _blockedAnnounced = CurrentCardBlocked;
            _blitzNormalOnYes = BlitzNormalOnYesRoll != null
                ? BlitzNormalOnYesRoll()
                : _blitzRng.Next(2) == 0;
            _crisisTimer = BlitzSeconds;
            CardTimer = CardTimerMax = BlitzSeconds;   // mirror for the driver's dome timer
            CrisisBlitzAdvanced?.Invoke();        // driver: host-nag bubble + relabel the two buttons
        }

        // A blitz button press. Correct = pressing the lever that currently carries «ВСЁ НОРМАЛЬНО» in time;
        // pressing «О НЕТ» (the other lever) is a fail. pressedYes = the ДА lever (GameInput.AnswerYes) —
        // NOT a screen side: the driver paints the ДА lever's plate on the right since §9.
        private void BlitzPress(bool pressedYes)
        {
            bool pressedNormal = pressedYes == _blitzNormalOnYes;   // hit «ВСЁ НОРМАЛЬНО»?
            if (!pressedNormal) _blitzFails++;                      // «О НЕТ» / wrong lever → +1 провал
            AdvanceBlitz();
        }

        // Timer ran out on a thought — «не успел» counts as a fail (canon), then the next thought.
        private void BlitzTimeout()
        {
            _blitzFails++;
            AdvanceBlitz();
        }

        private void AdvanceBlitz()
        {
            _blitzIndex++;
            if (_blitzIndex >= _blitzThoughts.Count) { EndBlitz(); return; }
            StartBlitzThought();
        }

        // After the 5 thoughts: ≥2 fails opens the impulse round; otherwise the crisis ends and ordinary
        // play resumes (impulse skipped entirely at 0–1 fails — canon).
        private void EndBlitz()
        {
            if (_blitzFails >= ImpulseFailThreshold && _impulseCards != null && _impulseCards.Count > 0)
                EnterImpulse();
            else
                ResumeAfterCrisis();
        }

        /// <summary>
        /// Сколько импульсов показывает раунд. Было ровно три (весь пул `CR06`–`CR08`); с приездом
        /// `CR10`–`CR12` пул вырос до шести, а раунд остался прежней длины — иначе кризис из вспышки
        /// превратился бы в вторую колоду. Тюнимо.
        /// </summary>
        public const int ImpulseRoundSize = 3;

        private void EnterImpulse()
        {
            _phase = CrisisPhase.Impulse;
            _impulseIndex = 0;
            _impulseRound = PickImpulseRound();
            StartImpulseCard();
            CrisisImpulseStarted?.Invoke();       // driver: S13 INVERT warning «молчание = ДА»
        }

        /// <summary>
        /// Отобрать импульсы этого раунда из пула. ПО КАРМАНУ ВПЕРЁД: платный импульс, который игрок не
        /// потянет, выпадает гашёным и пропускается без всякого выбора — до `CR10`–`CR12` это означало, что
        /// у безденежного весь кризис сводился к одному «БРОСИТЬ ПАРТНЁРА». Поэтому сначала берутся те, что
        /// он МОЖЕТ отыграть (бесплатные и оплатимые), и лишь потом остальные — деньги на входе в кризис
        /// уже известны, гадать не нужно.
        ///
        /// Отбор СТАБИЛЬНЫЙ (порядок пула сохраняется внутри каждой группы), а не перемешанный: у платящего
        /// игрока раунд остаётся каноническим `CR06`→`CR07`→`CR08`, у безденежного платные уезжают в хвост и
        /// вместо них приходят `CR10`–`CR12`. Побочное следствие, которое надо знать основательнице:
        /// СОСТОЯТЕЛЬНЫЙ игрок бесплатных импульсов не увидит вовсе — разнообразие импульс-раунда для него
        /// осталось прежним. Лечится перемешиванием, но оно стоит детерминизма кризис-тестов; вынесено в
        /// открытые вопросы, а не решено здесь.
        /// </summary>
        private List<Card> PickImpulseRound()
        {
            bool Affordable(Card c) => !c.IsBlockCost
                                       || !BlockPrices.TryGetValue(c.Id, out var price)
                                       || Money >= price;
            var round = _impulseCards.Where(Affordable).ToList();
            round.AddRange(_impulseCards.Where(c => !Affordable(c)));
            if (round.Count > ImpulseRoundSize) round.RemoveRange(ImpulseRoundSize, round.Count - ImpulseRoundSize);
            return round;
        }

        // Put up the current impulse card (CR06..CR08). Its «Когда» has no real age, so we stamp the
        // player's current age onto it so its necrolog line sorts around midlife rather than at 0/end.
        private void StartImpulseCard()
        {
            var card = _impulseRound[_impulseIndex];
            card.Age = Math.Max(CrisisTriggerAge, (int)Age);
            CurrentCard = card;
            // BLOCK$ И В ИМПУЛЬСЕ (отрезок 0). Основательница решила правило денег не ломать: мотоцикл и
            // Шри-Ланка — привилегия тех, кто накрутил, а не «импульс денег не спросил». Гейт считается
            // ровно так же, как в Advance — по деньгам НА МОМЕНТ ПОКАЗА.
            _blockedAtDeal = WouldBeBlocked(card);   // то же правило, что в Advance — один источник
            _blockedAnnounced = CurrentCardBlocked;
            _crisisTimer = ImpulseSeconds;
            CardTimer = CardTimerMax = ImpulseSeconds;
        }

        // Resolve an impulse card. INVERT: yes = поддаться (impulsive act, consequences apply); no =
        // «СПАСИБО, НЕ НАДО» (declined). Silence/timeout routes here as yes (handled in TickCrisis). The
        // resolved side's Δ/flags/necrolog apply exactly like a normal card — an impulse Δ/fatal CAN end
        // the run (the only way the crisis kills). Otherwise advance to the next impulse card / resume.
        private void ResolveImpulse(bool yes)
        {
            var card = CurrentCard;
            if (card == null) return;
            // Гашёная BLOCK$-карточка импульса — пропуск без Δ, без некролога и без записи ответа, ровно
            // как в обычном ходу (Answer): «как жаль, у вас нет денег на это».
            if (CurrentCardBlocked) { AdvanceImpulse(); return; }
            _answers[card.Id] = yes;
            if (ApplyResolvedConsequences(card, yes, yes ? AnswerSide.Yes : AnswerSide.No))
                return;   // impulse-card Δ/fatal ended the run
            AdvanceImpulse();
        }

        private void AdvanceImpulse()
        {
            _impulseIndex++;
            if (_impulseIndex >= _impulseRound.Count) { ResumeAfterCrisis(); return; }
            StartImpulseCard();
        }

        // Crisis over — restore the suspended normal card exactly and hand control back to ordinary play.
        private void ResumeAfterCrisis()
        {
            _phase = CrisisPhase.None;
            CurrentCard = _suspendedCard;
            CardTimer = _suspendedTimer;
            CardTimerMax = _suspendedTimerMax;
            _blockedAtDeal = _suspendedBlocked;
            _blockedAnnounced = CurrentCardBlocked;
            _suspendedCard = null;
            CrisisEnded?.Invoke();
            CardChanged?.Invoke();                // driver re-renders the resumed card (normal HUD)
            MaybeArmDepression();                 // RANDOM_TRIGGER tail: ролл сейчас, вход — через зазор карточек
        }

        // Injected-time tick while a crisis phase is active: only the fast blitz/impulse timer runs — the
        // 5 live scales are paused (no drain, no spurious death). Blitz timeout = a fail; impulse timeout =
        // ДА (INVERT: silence accepts).
        private void TickCrisis(float dt)
        {
            _crisisTimer -= dt;
            CardTimer = Math.Max(0f, _crisisTimer);
            if (_crisisTimer > 0f) return;
            if (_phase == CrisisPhase.Blitz) BlitzTimeout();
            else if (_phase == CrisisPhase.Impulse) ResolveImpulse(true); // молчание = ДА
        }

        // ================================================================ depression «тёмная полоса» (CR09)

        private void ResetDepression()
        {
            InDepression = false;
            DepressionPulsing = false;
            _depressionDone = false;
            _depGray = 0;
            _depPulseElapsed = 0f;
            _depPulseNextInterval = 0f;
            _depPulseWindow = 0f;
            _depPressLockout = 0f;
            _depArmed = false;
            _depGapLeft = 0;
        }

        // Crisis tail: a one-shot RANDOM_TRIGGER roll (canon: «иногда после кризиса»). Latches _depressionDone
        // either way so it can never re-roll this life; only fires when the plan actually carries CR09.
        //
        // ⚠ ЗАЗОР (2026-08-07). Ролл делается ЗДЕСЬ (сразу хвостом кризиса, как и был), но вход в депрессию
        // ОТКЛАДЫВАЕТСЯ на DepressionGapCards обычных карточек: два спецрежима встык читались как один
        // сплошной, и игрок не успевал вернуться в обычную игру между ними.
        private void MaybeArmDepression()
        {
            if (_depressionDone || _depressionCard == null) return;
            _depressionDone = true;   // one-shot per life whether or not it hits
            bool roll = DepressionTriggerRoll != null
                ? DepressionTriggerRoll()
                : _depRng.NextDouble() < DepressionChance;
            if (!roll) return;
            _depArmed = true;
            _depGapLeft = DepressionGapCards;
        }

        // Одна обычная карточка прошла (вызов из Advance, уже ПОСЛЕ того как карточка выставлена). Когда
        // зазор выбран — депрессия входит немедленно, на этой самой карточке: её таймер замирает вместе с
        // остальным, а по выходу игра продолжается ровно с неё.
        private void CountDepressionGapCard()
        {
            if (!_depArmed) return;
            if (--_depGapLeft > 0) return;
            _depArmed = false;
            _depGapLeft = 0;
            EnterDepression();
        }

        // Enter depression: full B&W, scales paused, the slow-pulse scheduler armed. The suspended normal
        // card stays CurrentCard (its timer is frozen while InDepression) and resumes on exit — no death path.
        private void EnterDepression()
        {
            InDepression = true;
            DepressionPulsing = false;
            _depGray = DepressionGraySteps;   // start fully desaturated
            _depPulseElapsed = 0f;
            _depPulseWindow = 0f;
            _depPressLockout = 0f;
            _depPulseNextInterval = PickDepressionInterval();
            DepressionStarted?.Invoke();      // driver: muted «ТЁМНАЯ ПОЛОСА…» announce + B&W overlay
        }

        // The next wait between dim pulses: an injected value (test/tuning) or a uniform draw in [min,max].
        private float PickDepressionInterval()
        {
            if (DepressionPulseInterval != null) return Math.Max(0.01f, DepressionPulseInterval());
            return DepressionPulseIntervalMin
                   + (float)(_depRng.NextDouble() * (DepressionPulseIntervalMax - DepressionPulseIntervalMin));
        }

        // Injected-time tick while depressed: count down the anti-mash lockout, hold the ~0.6s hit-window,
        // and count a MISS (colour slips a step toward gray) when a lit pulse closes UNPRESSED (canon: «не
        // нажал в окне … цвет уползает обратно»). NO scale drain, NO death — the ONLY exit is 5 catches.
        private void TickDepression(float dt)
        {
            if (_depPressLockout > 0f)
                _depPressLockout = Math.Max(0f, _depPressLockout - dt);

            if (DepressionPulsing)
            {
                _depPulseWindow -= dt;
                if (_depPulseWindow <= 0f)          // window closed with no valid catch → missed pulse
                {
                    DepressionPulsing = false;
                    SlipDepressionBack();            // colour uползает обратно (floored at full gray)
                    _depPulseElapsed = 0f;
                    _depPulseNextInterval = PickDepressionInterval();
                }
            }
            else
            {
                _depPulseElapsed += dt;
                if (_depPulseElapsed >= _depPulseNextInterval)   // time to flash → open the hit-window
                {
                    DepressionPulsing = true;
                    _depPulseWindow = DepressionPulseWindow;
                }
            }
        }

        // CONFIRM during depression. A press strictly INSIDE the open window (and not inside the anti-mash
        // lockout) is a clean catch: one step of colour returns; 5 catches (gray → 0) lifts depression. Any
        // other press — before/after the window, or while locked out (mashing) — is a MISS: colour slips a
        // step back AND (re)arms the lockout, so a rapid/continuous press can never bank a catch.
        private void DepressionPress()
        {
            if (!InDepression) return;
            if (DepressionPulsing && _depPressLockout <= 0f)
            {
                DepressionPulsing = false;
                _depGray = Math.Max(0, _depGray - 1);   // +1 step of colour
                _depPulseElapsed = 0f;
                _depPulseNextInterval = PickDepressionInterval();
                DepressionProgressed?.Invoke();         // driver: muted host mutter «…ну же…»
                if (_depGray <= 0) ExitDepression();    // full colour → depression lifted
            }
            else
            {
                SlipDepressionBack();                   // мимо/долбёж → colour slips a step back
                _depPressLockout = DepressionPressLockout;
            }
        }

        // A miss: colour slips one step back toward full gray, floored at the start (never below 0 colour —
        // i.e. never above DepressionGraySteps). There is NO death, so a stuck player simply stays gray.
        private void SlipDepressionBack()
            => _depGray = Math.Min(DepressionGraySteps, _depGray + 1);

        // Depression lifted — hand control back to ordinary play on the (frozen) suspended card.
        private void ExitDepression()
        {
            InDepression = false;
            DepressionPulsing = false;
            DepressionEnded?.Invoke();
        }

        // ================================================================ money crank

        private void ResetMoney()
        {
            Money = 0;
            MoneyOpen = false;
            _moneyGraceCard = false;
            _inDebt = false;
            Paused = false;
            PausedInputsLive = false;
            _blockedAtDeal = false;
            _blockedAnnounced = false;
            _mults.Clear();
            _drains.Clear();
            _onces.Clear();
        }

        /// <summary>MONEY_TICK: +1₽ × multiplier. No-op unless money is open and the run is live/unpaused.
        /// Returns whether the tick was ACCEPTED (see <see cref="HandleInput"/>).</summary>
        private bool Crank()
        {
            if (!MoneyOpen || InputsFrozen) return false;
            Money += MoneyTickIncome * IncomeMultiplier;
            return true;
        }

        // ================================================================ live health / energy

        private void ResetHealthEnergy()
        {
            EnergyOpen = false;
            HealthDecaying = false;
            Burnout = false;
            _burnoutGrace = 0f;
            _healthDecayFrac = 0;
            _energyFrac = 0;
            _breathHeld = false;
            _healthDecayMult = 1.0;
            _lt08Triggered = false;
            // Scales.Reset() (in StartLife/ToOpener) has already restored Health/Energy = 100.
        }

        // Health decay from 30, per REAL second, scaled by the LT01 modifier. Fractional accumulator so a
        // sub-1%/s rate decrements the int scale exactly on whole crossings (dt-injected — no wall clock).
        private void IntegrateHealth(float dt)
        {
            if (Scales.Health <= 0) return;

            // МОЛОДОСТЬ ЗАЖИВАЕТ (отрезки 1–7). До тридцати здоровье не только не убывает — оно
            // ВОССТАНАВЛИВАЕТСЯ, медленно и до потолка <see cref="HealthYouthRegenCeiling"/>.
            //
            // Зачем: канон говорит «до 30 здоровье не убывает», и с колодой из 85 карточек этого хватало —
            // за юность прилетало два-три щелчка. Отрезки 2–3 добавили в 18–24 около сорока карточек, у
            // которых здоровье — ЕДИНСТВЕННАЯ живая шкала («в детстве и юности считать нечего», дизайн-док
            // отрезка 1 §0), и почти каждая снимает 7–15 п.п.: ночная смена, хамский клиент, четыре часа
            // сна, диета из одного продукта, вписка до утра. Сэмплер берёт по десять карточек из каждого
            // окна, умеренный игрок соглашается примерно на половину — и к двадцати пяти приходит с 30 %
            // здоровья НАВСЕГДА, потому что до тридцати его нечем поднять, а после тридцати оно только
            // тает. Замер: главный гард падал (умеренный худший 55, лингеринг 62), причём декей был НИ ПРИ
            // ЧЁМ — матрица показала те же 61/62 даже на 0.30 %/с.
            //
            // Смысл правки не в том, чтобы простить игроку выборы, а в том, что бессонная ночь в
            // девятнадцать не должна стоить семи процентов здоровья ВОСЕМЬДЕСЯТ ЛЕТ спустя. Потолок ниже
            // ста намеренно: юность заживает, но не начисто — след от сорока плохих решений остаётся.
            if (!HealthDecaying)
            {
                if (Scales.Health >= HealthYouthRegenCeiling) return;
                _healthDecayFrac -= HealthYouthRegenPerSec * dt;
                int heal = (int)(-_healthDecayFrac);
                if (heal <= 0) return;
                _healthDecayFrac += heal;
                Scales.Health = Math.Min(HealthYouthRegenCeiling, Scales.Health + heal);
                return;
            }

            _healthDecayFrac += HealthDecayPerSec * _healthDecayMult * dt;
            int whole = (int)_healthDecayFrac;
            if (whole <= 0) return;
            _healthDecayFrac -= whole;
            Scales.Health = Math.Max(0, Scales.Health - whole);
        }

        /// <summary>
        /// Живая энергия за один тик: дренаж идёт ВСЕГДА (с 25), реген — ТОЛЬКО пока датчик высоты физически
        /// поднят (<paramref name="held"/>). Складываются в одно НЕТТО и интегрируются знаковым дробным
        /// аккумулятором, поэтому и «держал полсекунды», и «отпустил на полсекунды» считаются точно, без
        /// потерь на округление до целого процента.
        ///
        /// Инвариант «живая шкала требует ввода» (memory 2026-07) держится буквально: без поднятого датчика
        /// нетто всегда отрицательное — пассивный игрок выгорает и умирает ровно как раньше.
        /// </summary>
        private void IntegrateEnergy(float dt, bool held)
        {
            if (!EnergyOpen) return;
            // ГРЕЙС после выгорания: несколько секунд без дренажа, чтобы «встык» не случился (5в).
            // Тикает ровно здесь — в живом ходе; под паузой (как и всё остальное) он стоит.
            bool grace = _burnoutGrace > 0f;
            if (grace) _burnoutGrace = Math.Max(0f, _burnoutGrace - dt);
            double net = (held ? EnergyRegenPerSec : 0.0) - (grace ? 0.0 : EnergyDrainPerSec);
            if (Scales.Energy > 0 || net > 0)
            {
                _energyFrac += net * dt;
                int whole = (int)_energyFrac;      // усечение К НУЛЮ — симметрично для обоих знаков
                if (whole != 0)
                {
                    _energyFrac -= whole;
                    Scales.Energy = Math.Max(0, Math.Min(100, Scales.Energy + whole));
                }
            }
            UpdateBurnout();
        }

        /// <summary>
        /// Тот же реген, но ПОД §D-модалкой (<see cref="PausedInputsLive"/>): время стоит, дренажа нет — но
        /// датчик живой, и батарея наполняется, пока его держат. Это и есть задача экрана энергии («держи,
        /// пока батарейка не заполнится»); без этого условие выхода было бы недостижимо, ровно как у оси
        /// балансира в <see cref="TickModalBalancer"/>. Пауза остаётся паузой: ничего, кроме роста, не идёт.
        /// </summary>
        private void TickModalBreath(float dt, bool held)
        {
            if (!EnergyOpen || !held) return;
            _energyFrac += EnergyRegenPerSec * dt;
            int whole = (int)_energyFrac;
            if (whole > 0)
            {
                _energyFrac -= whole;
                Scales.Energy = Math.Min(100, Scales.Energy + whole);
            }
            UpdateBurnout();
        }

        /// <summary>
        /// ENERGY_HOLD: датчик высоты поднят ЭТОТ кадр. Только латч — вся арифметика живёт в
        /// <see cref="IntegrateEnergy"/>/<see cref="TickModalBreath"/>, чтобы рост шёл ПО ВРЕМЕНИ, а не по
        /// числу кадров (иначе частота кадров стала бы балансом). Инертен до открытия энергии и под
        /// «глухой» S5-паузой (<see cref="InputsFrozen"/>); под §D-модалкой — живой, это её задача.
        /// Возвращает, ПРИНЯТО ли удержание (см. <see cref="HandleInput"/>): отвергнутое удержание не
        /// открывает §6-окно недавнего ввода.
        /// </summary>
        private bool HoldBreath()
        {
            if (!EnergyOpen || InputsFrozen) return false;
            _breathHeld = true;
            return true;
        }

        // Temporary «выгорание»: latch on at energy ≤10%, release above 40% (hysteresis, re-enterable).
        // Entering fires an event; the driver shows the D-style intro screen (first time) / the short plate.
        // ГРЕЙС (5в): пока _burnoutGrace > 0, латч ЗАПРЕЩЁН — повторное выгорание встык невозможно даже
        // если карточка мгновенно уронила энергию обратно на дно (дренаж в грейсе и так не идёт).
        private void UpdateBurnout()
        {
            if (!Burnout && _burnoutGrace <= 0f && Scales.Energy <= BurnoutEnterEnergyAtOrBelow)
            {
                Burnout = true;
                BurnoutEntered?.Invoke();
            }
            else if (Burnout && Scales.Energy > BurnoutExitEnergyAbove)
            {
                Burnout = false;
                _burnoutGrace = BurnoutGraceSeconds;   // выдох: несколько секунд без дренажа
            }
        }

        // ================================================================ live relationships balancer

        private void ResetRelationships()
        {
            RelationshipsOpen = false;
            Married = false;
            RelationshipsLost = false;
            _relAxis = 0;
            _relFrac = 0;
            _relBelowZoneSeconds = 0;
            _drifts.Clear();
            _scheduledBreakAge = float.NaN;
            _exclusiveTaken.Clear();
            _prenup = false;
            // Scales.Reset() (in StartLife/ToOpener) has already restored Relationships = 55.
        }

        /// <summary>
        /// ДЕЙСТВУЮЩИЙ дрейф отношений, %/сек. База — <see cref="RelDriftPerSec"/>, в браке
        /// <see cref="RelDriftMarriedPerSec"/> («реже балансировать» — механика брака была в игре и до
        /// отрезка 0, поэтому `MD01`-ДА свой ×0.5 получает отсюда, а не из CSV). Сверху НАКЛАДЫВАЮТСЯ
        /// множители `DRIFT:Отн=xN` из колонки «Длительный эффект» — так `MD01`-НЕТ («отказ от свадьбы»)
        /// получает ×2 и отношения тают даже при поддержке, ровно как обещает проза карточки. Эффект с
        /// `DUR:Ny` перестаёт учитываться сам, как только возраст выходит за срок.
        /// </summary>
        public double RelationshipDriftPerSec
        {
            get
            {
                double d = Married ? RelDriftMarriedPerSec : RelDriftPerSec;
                foreach (var f in _drifts)
                    if (f.Scale == Scale.Relationships && Age >= f.StartAge && Age < f.EndAge)
                        d *= f.Value;
                return d;
            }
        }

        // RELATION_AXIS ↑/↓: latch the held direction for the NEXT integration tick, which consumes and
        // clears it. No-op unless relationships are open and the run is live/unpaused (inert in the
        // opener/finale/tutorial — HandleInput only routes it in Playing; this adds the open+pause guard).
        // Returns whether the axis input was ACCEPTED (see HandleInput) — an ignored lever must not open the
        // §6 recent-input window.
        private bool SetRelationAxis(int dir)
        {
            if (!RelationshipsOpen || InputsFrozen) return false;
            _relAxis = dir;
            return true;
        }

        // Relationships balancer integration (real-time, fractional accumulator like health/energy):
        // constant downward drift (softened while married), the held RELATION_AXIS pull (±1.5%/s), an
        // over-attention penalty above the zone, and the CUMULATIVE below-zone breakup timer. There is
        // NO death here — failure is a breakup (partner leaves), not a game over.
        private void IntegrateRelationships(float dt)
        {
            if (!RelationshipsOpen) return;

            int axis = _relAxis;
            _relAxis = 0;                                                      // consume this tick's axis

            double rate = -RelationshipDriftPerSec;                            // drift down (× DRIFT-эффекты)
            rate += axis * RelBalancerPerSec;                                  // held axis (±)
            // ПОЛ УДЕРЖАНИЯ — только пока рычаг ТЯНЕТ ВВЕРХ (см. RelHoldNetFloorPerSec). Бездействие
            // (axis = 0) и тяга ВНИЗ (axis < 0) проходят мимо: наказание за то, что не держишь, остаётся
            // ровно таким, каким его написала карточка.
            if (axis > 0 && rate < RelHoldNetFloorPerSec)
                rate = RelHoldNetFloorPerSec;
            if (Scales.Relationships > RelZoneMax)                             // задушил вниманием →
                rate -= RelOverloadPenaltyPerSec;                             // extra pull back toward zone

            _relFrac += rate * dt;
            int whole = (int)_relFrac;   // truncates toward zero → symmetric for up and down
            if (whole != 0)
            {
                _relFrac -= whole;
                Scales.Relationships = Math.Max(0, Math.Min(100, Scales.Relationships + whole));
            }

            // Breakup: CUMULATIVE time spent in the RED zone (< RelBreakupFloor), ~10s total → разрыв. The
            // yellow band [RelBreakupFloor..RelZoneMin] is a warning buffer: drifting there does NOT arm the
            // timer, so you have room to pull back before it's fatal (founder playtest: «разрыв должен быть в
            // конце красной, а не в жёлтой»). Cumulative, so brief repeated red dips still add up over a life.
            if (Scales.Relationships < RelBreakupFloor)
            {
                _relBelowZoneSeconds += dt;
                if (_relBelowZoneSeconds >= RelBreakupSeconds)
                    BreakUp();
            }
        }

        // D-MODAL ONLY (PausedInputsLive): integrate the HELD AXIS and nothing else. The drift, the
        // over-attention penalty and the cumulative breakup clock all stay frozen with the rest of the run —
        // this is still a pause. Its only job is that the balancer lever visibly moves the marker while the
        // tutorial asks the player to bring it into the zone. Same rate and same fractional accumulator as
        // the live integration, so the feel is identical.
        private void TickModalBalancer(float dt)
        {
            if (!RelationshipsOpen) { _relAxis = 0; return; }
            if (_relAxis == 0) return;

            _relFrac += _relAxis * RelBalancerPerSec * dt;
            _relAxis = 0;                                  // consume this tick's axis
            int whole = (int)_relFrac;
            if (whole == 0) return;
            _relFrac -= whole;
            // ⚠ ПОТОЛОК ТУТОРИАЛА (r5 п.1). Под §D-модалкой маркер НЕ МОЖЕТ уехать выше зелёной зоны.
            //
            // Это не украшение, а лечение СОФТЛОКА, который завела ускоренная тяга. Условие выхода экрана —
            // «маркер внутри [RelZoneMin..RelZoneMax] непрерывно NewScaleHoldSeconds» (GameDriver.TickNewScale),
            // а под модалкой дрейфа НЕТ по определению (это пауза). На прежних 4 %/с игрок, который просто
            // держал джойстик вверх, выходил из зоны за ≈5 с и успевал набрать удержание раньше. На 22 %/с
            // он вылетает за 75 уже через 0.9 с, упирается в 100 — и обратно его НЕЧЕМ тянуть: дрейф стоит,
            // а рычаг он держит. Экран не закрывается никогда, а он МОДАЛЬНЫЙ и морозит всю игру: на стойке
            // это намертво повешенный автомат. Поймал живой прогон реальной цепочки кабинета
            // (ChildPhoneTests.BangButton_ThroughTheRealCabinetChain…), вставший на таймауте.
            //
            // Клампим ТОЛЬКО ВЕРХ и ТОЛЬКО на экране-туториале: вниз уйти по-прежнему можно (иначе
            // исчезло бы то самое «верни маркер в зону», ради чего экран и стоит), а в живой игре
            // перелёт в красную зону «задушил вниманием» остаётся ровно таким, каким был.
            //
            // ⚠ ПОТОЛОК БЛОКИРУЕТ РОСТ, А НЕ ТЕЛЕПОРТИРУЕТ ВНИЗ (находка код-скептика r5, MINOR).
            // Глухое `Min(RelZoneMax, …)` мгновенно сдёргивало бы маркер к 75 из ЛЮБОЙ позиции выше
            // зоны — а войти в модалку сверху вполне законно: живая игра пускает в красную зону
            // («задушил вниманием»), и §D-экран может открыться на 90. Игрок шевельнул рычагом вверх —
            // и маркер прыгнул 90 → 75 одним кадром, то есть экран САМ сделал за него работу, которую
            // просит сделать. Потолок берётся как максимум зоны и ТЕКУЩЕГО значения: выше того, с чем
            // вошёл, не поднимешься, но и вниз тебя никто не переставит — спускайся рычагом.
            int cap = Math.Max(RelZoneMax, Scales.Relationships);
            Scales.Relationships = Math.Max(0, Math.Min(cap, Scales.Relationships + whole));
            if (Scales.Relationships >= cap) _relFrac = 0;   // не копить дробь в упоре
        }

        /// <summary>
        /// Партнёр уходит. Два входа, одна механика:
        ///  • ДРЕЙФ — накопленные ~10 с в красной зоне (как было);
        ///  • КАРТОЧКА — флаг `BREAK:Отн` (`CR06` «БРОСИТЬ ПАРТНЁРА ПРЯМО СЕЙЧАС!», `RND05` через 2 года).
        ///
        /// Шкала гаснет, брак снимается, значение падает в «одиноко» — и это НЕ смерть, забег продолжается.
        /// <paramref name="byCard"/>: карточка пишет в некролог СВОЮ строку, поэтому служебную
        /// «Отношения не удержали — расстались.» в этом случае не добавляем — иначе разрыв прозвучал бы
        /// дважды подряд в семи строках финала.
        ///
        /// РАЗВОД СТОИТ ДЕНЕГ (<see cref="DivorceCost"/>) — но только если рвётся именно БРАК и если не
        /// подписан брачный договор (`PRENUP`). Списывается как карточная трата: не кредитная, значит в
        /// минус не уводит.
        /// </summary>
        private void BreakUp(bool byCard = false)
        {
            bool divorce = Married;
            Married = false;
            RelationshipsOpen = false;
            RelationshipsLost = true;
            _relAxis = 0;
            _relFrac = 0;
            _relBelowZoneSeconds = 0;
            _scheduledBreakAge = float.NaN;   // отложенный разрыв уже неактуален — рвать больше нечего
            Scales.Relationships = RelBreakupValue;

            // ⚠ ДЛИТЕЛЬНЫЕ ЭФФЕКТЫ ПО ОТНОШЕНИЯМ УМИРАЮТ ВМЕСТЕ С ОТНОШЕНИЯМИ (находка ревю, MAJOR).
            // `DRIFT:Отн=xN` описывает КОНКРЕТНУЮ связь («отказались от свадьбы — тает вдвое быстрее»),
            // а не характер игрока. Без этой чистки сценарий «НЕТ на MD01 (×2) → разрыв → MD06 ДА» отдавал
            // новым отношениям УНАСЛЕДОВАННЫЙ ×2 от ветки, которой больше нет: второй шанс приходил уже
            // отравленным. Симметрично правилу «DRIFT по ЗАКРЫТОЙ шкале не принимается»
            // (<see cref="ApplyLongEffects"/>): раз закрытая шкала эффект не берёт, закрывшаяся — не хранит.
            // Прочие шкалы не трогаем — их дрейф к партнёру отношения не имеет.
            _drifts.RemoveAll(f => f.Scale == Scale.Relationships);

            if (divorce && !_prenup && MoneyOpen)
            {
                double affordable = Money > 0 ? Money : 0;
                Money -= Math.Min(DivorceCost, affordable);
                NoteMoneyChanged();
            }

            if (!byCard)
                _entries.Add(new NecrologEntry
                {
                    Age = (int)Age,
                    Order = int.MaxValue - 1,   // sorts after same-age card lines
                    Line = "Отношения не удержали — расстались.",
                    IsRond = false,
                });

            RelationshipBrokeUp?.Invoke();
        }

        // ================================================================ live child (signal-response)

        private void ResetChild()
        {
            ChildOpen = false;
            ChildFlashing = false;
            _childFlashElapsed = 0f;
            _childNextInterval = 0f;
            _childWindowRemaining = 0f;
            _childPressLockout = 0f;
            _childConsecutiveMiss = 0;
            // Scales.Reset() (in StartLife/ToOpener) has already restored Child = 0.
        }

        /// <summary>
        /// D-MODAL hook: ring the handset RIGHT NOW (increment «экран появления новой шкалы»). The child
        /// tutorial's exit condition is «pick up one call», so the tutorial has to have a call to pick up —
        /// waiting out the ordinary 15–25 s scheduler under a frozen clock would never produce one. Opens the
        /// normal press window through the normal fields, so the press is scored by the ordinary
        /// <see cref="ChildPress"/> path (no tutorial-only scoring). The window itself does not run out while
        /// the modal is up: <see cref="IntegrateChild"/> is not reached under the pause, exactly as during any
        /// other freeze — the handset simply keeps ringing until the player answers.
        /// No-op unless the child scale is open.
        /// </summary>
        public void RingChildNow()
        {
            if (!ChildOpen || State != GameState.Playing) return;
            ChildFlashing = true;
            _childWindowRemaining = ChildFlashWindow;
            _childFlashElapsed = 0f;
            _childPressLockout = 0f;   // the call starts NOW, so no anti-pre-spam debt carries into it
        }

        // MD02=ДА: open the child scale + button. Seeds the first flash interval, lifts the child scale to
        // its starting value (overriding the token «Реб +2» the CSV Δ just applied), and fires the S5 hint.
        private void OpenChild()
        {
            if (ChildOpen) return;
            ChildOpen = true;
            ChildFlashing = false;
            _childFlashElapsed = 0f;
            _childWindowRemaining = 0f;
            _childPressLockout = 0f;
            _childConsecutiveMiss = 0;
            _childNextInterval = PickChildInterval();
            if (Scales.Child < ChildStartValue) Scales.Child = ChildStartValue;
            ChildOpened?.Invoke();
        }

        // LT04 (either answer): the children have grown — the button/scale go dark for good this life.
        private void CloseChild()
        {
            ChildOpen = false;
            ChildFlashing = false;
            _childWindowRemaining = 0f;
            _childPressLockout = 0f;
            _childConsecutiveMiss = 0;
        }

        // The next wait between flashes: an injected value (test/tuning) or a uniform draw in [min,max].
        private float PickChildInterval()
        {
            if (ChildFlashInterval != null) return Math.Max(0.01f, ChildFlashInterval());
            return ChildFlashIntervalMin
                   + (float)(_childRng.NextDouble() * (ChildFlashIntervalMax - ChildFlashIntervalMin));
        }

        // Signal-response integration (real-time, dt-injected): count down to the next call, hold the 5s
        // open window (STOPPED while ChildCallFrozen — a window the player can't see must not run out), and
        // register a MISS when the window closes unpressed. NO death path — a lapse only
        // bends relationships (which fold into the show tone) and the child scale.
        private void IntegrateChild(float dt)
        {
            if (!ChildOpen || ChildCallFrozen) return;

            // Anti-pre-spam lockout always winds down (so a press >1s before a flash is harmless again).
            if (_childPressLockout > 0f)
                _childPressLockout = Math.Max(0f, _childPressLockout - dt);

            if (ChildFlashing)
            {
                _childWindowRemaining -= dt;
                if (_childWindowRemaining <= 0f)   // window closed with no valid press → missed flash
                {
                    ChildFlashing = false;
                    RegisterChildMiss();
                    _childFlashElapsed = 0f;
                    _childNextInterval = PickChildInterval();
                }
            }
            else
            {
                _childFlashElapsed += dt;
                if (_childFlashElapsed >= _childNextInterval)   // time to flash → open the press window
                {
                    ChildFlashing = true;
                    _childWindowRemaining = ChildFlashWindow;
                }
            }
        }

        // CHILD_PRESS (Enter, routed by the driver only in gameplay-with-open-child). A press INSIDE the open
        // window (and not inside the anti-pre-spam lockout) is good parenting: child scale up, miss-streak
        // cleared, next flash rescheduled. Any other press — before/after the window, or while locked out —
        // is discarded AND (re)arms the ~1s lockout, so mashing ahead of the flash can't bank a success.
        private void ChildPress()
        {
            if (!ChildOpen || ChildCallFrozen) return;
            if (ChildFlashing && _childPressLockout <= 0f)
            {
                ChildFlashing = false;
                _childConsecutiveMiss = 0;
                Scales.Child = Math.Min(100, Scales.Child + ChildPressGain);
                _childFlashElapsed = 0f;
                _childNextInterval = PickChildInterval();
            }
            else
            {
                _childPressLockout = ChildPressMinDelay;   // premature / locked → punish the pre-spam
            }
        }

        // A missed flash. Two in a row = «плохой родитель»: relationships −10% + child scale drop, applied
        // ONCE per lapse (the streak resets, so it takes two fresh misses to be penalised again).
        // КАЖДЫЙ пропуск (не только штрафной) объявляется событием — драйвер уводит трубку «поникшей».
        private void RegisterChildMiss()
        {
            ChildCallMissed?.Invoke();
            _childConsecutiveMiss++;
            if (_childConsecutiveMiss < ChildBadParentMisses) return;
            _childConsecutiveMiss = 0;
            Scales.Relationships = Math.Max(0, Scales.Relationships - ChildBadParentRelPenalty);
            Scales.Child = Math.Max(0, Scales.Child - ChildBadParentScaleDrop);
            ChildBadParent?.Invoke();
        }

        // «Пора подлечиться!» (LT08): a condition-triggered system card, single-shot per life. Eligible
        // only when health < 40% AND age ≥ 30; inserted into the remaining deck near the current age so
        // it comes up next. It is a BLOCK$ card (100₽) — if broke it shows blocked and skips (canon:
        // «ленился по здоровью — чинить нечем»); if paid, its ДА Δ heals health → 80% via the normal path.
        private void MaybeTriggerLt08()
        {
            if (_lt08Triggered || _lt08 == null) return;
            if (Age < Lt08TriggerAge || Scales.Health >= Lt08TriggerHealthBelow) return;
            _lt08Triggered = true;

            var card = _lt08;
            card.Age = Math.Max(Lt08TriggerAge, (int)Math.Ceiling(Age));
            int at = _deck.FindIndex(_index + 1, c => c.Age > card.Age);
            if (at < 0) at = _deck.Count;
            _deck.Insert(at, card);
        }

        // Real-time money integration for one Tick step (cost-of-living + active installment drains).
        // Rate is per REAL second; drain windows are measured in GAME-years (canon reading, see report).
        private void IntegrateMoney(float dt)
        {
            if (!MoneyOpen) return;
            Money -= CostOfLivingPerSec * dt;
            foreach (var d in _drains)
                if (Age >= d.StartAge && Age < d.EndAge)
                    Money += d.PerSec * dt;   // PerSec is signed (e.g. −0.3)

            // РАЗОВЫЕ выплаты (`ONCE:Ny`) — созревшие падают одним ударом и снимаются со списка.
            //
            // ⚠ ЧЕРЕЗ ТОТ ЖЕ ЗАЖИМ, ЧТО И ЛЮБАЯ КАРТОЧНАЯ ТРАТА (находка ревю, MAJOR). Раньше здесь стояло
            // голое `Money += …`, и `FA27` (−25 ₽ поручительство) с `FB32` (−60 ₽ бумаги не читая) уводили
            // счёт В МИНУС мимо <see cref="AddCardMoney"/> — то есть НЕкредитная карточка нарушала главное
            // правило отрезка 0 «в минус уводят только кредитные». Отложенность платежа его не отменяет:
            // это по-прежнему расплата ПО КАРТОЧКЕ, просто выписанная задним числом, поэтому кредитность
            // запоминается при взводе (<see cref="MoneyOnce.IsCredit"/>) и применяется при выплате.
            // Пассивные механики (стоимость жизни, дренажи выше) — другое дело, они в минус уводят по канону.
            for (int i = _onces.Count - 1; i >= 0; i--)
            {
                if (Age < _onces[i].AtAge) continue;
                var due = _onces[i];
                _onces.RemoveAt(i);
                AddClampedMoney(due.IsCredit, due.Amount);
            }
            // Пассивные механики (стоимость жизни, дренажи ипотеки/кредита) уводят в минус по канону —
            // это и есть «расплата за молодость». Но прозвучать долг обязан (§3.2).
            NoteMoneyChanged();
        }

        // Apply a card's «Длительный эффект» entries for the RESOLVED side (multipliers + installment
        // drains on money; DRIFT multipliers on a passive scale). Записи без префикса стороны — ДА.
        private void ApplyLongEffects(Card card, bool yes)
        {
            foreach (var e in card.LongEffects)
            {
                if (e.OnNoSide == yes) continue;        // запись не для этой стороны

                // DRIFT:{шкала}=xN — множитель ПАССИВНОГО дрейфа. Единственная шкала с дрейфом — отношения;
                // прочие парсятся и лежат инертными, как и не-денежные MULT.
                if (e.Kind == LongEffectKind.Drift)
                {
                    // ⚠ ТОЛЬКО ПО ОТКРЫТОЙ ШКАЛЕ (находка ревью, MAJOR). Дрейф — это Δ, растянутая во
                    // времени, поэтому правило отрезка 0 «Δ только по открытым шкалам» распространяется и
                    // на неё. Эффект по ЗАКРЫТОЙ шкале ОТБРАСЫВАЕТСЯ, а не взводится на потом: иначе
                    // карточка юности «отношения будут таять вдвое» тихо ждала бы двадцатилетия и
                    // сработала бы по шкале, которой в момент выбора на экране не было. Симметрично гарду
                    // отложенного разрыва (<see cref="CheckScheduledBreak"/>), который тоже гаснет, если
                    // рвать уже нечего. Карточка с `OPEN:{шкала}` считается работающей по открытой (та же
                    // поправка, что и в <see cref="ScaleOpenForDelta"/>).
                    if (!ScaleOpenForDelta(e.Scale, card)) continue;
                    _drifts.Add(new DriftEffect
                    {
                        Scale = e.Scale,
                        Value = e.MultValue,
                        StartAge = Age,
                        // DUR:Ny → эффект СНИМАЕТСЯ вместе с окончанием срока; без DUR — на всю жизнь.
                        EndAge = e.DurYears > 0 ? Age + e.DurYears : float.PositiveInfinity,
                    });
                    continue;
                }

                if (e.Scale != Scale.Money) continue;   // non-money (e.g. health MULT) inert this increment
                if (e.Kind == LongEffectKind.Mult)
                {
                    if (e.RandomZero)
                    {
                        // «×5 или обнуление денег» (YA02): coin win → ongoing ×MultValue income multiplier;
                        // coin loss → wipe current money to 0 (one-shot). Reading noted in the report.
                        if (_coin()) _mults.Add(new ActiveMult { Value = e.MultValue, FromAge = e.FromAge });
                        else Money = 0;
                    }
                    else
                    {
                        _mults.Add(new ActiveMult { Value = e.MultValue, FromAge = e.FromAge });
                    }
                }
                else if (e.Kind == LongEffectKind.Once)
                {
                    // Разовая сумма через N игровых лет от ответа (`FA27` −25 ₽ через 3, `FB32` −60 ₽
                    // через 5, `FC32` +100 ₽ через 10).
                    // Кредитность запоминается ЗДЕСЬ: в момент выплаты карточки уже нет под рукой, а
                    // правило «в минус уводят только кредитные» решает именно она.
                    _onces.Add(new MoneyOnce
                    {
                        Amount = e.OnceAmount,
                        AtAge = Age + e.OnceYears,
                        IsCredit = IsCreditCard(card),
                    });
                }
                else // Drain: starts NOW (on ДА), lasts DurYears game-years
                {
                    _drains.Add(new MoneyDrain
                    {
                        PerSec = e.DrainPerSec,
                        StartAge = Age,
                        EndAge = Age + e.DurYears,
                    });
                }
            }
        }

        /// <summary>
        /// СЛОЙ ПРИМЕНЕНИЯ Δ — здесь качественный шаг из CSV становится настоящей величиной.
        /// Три правила отрезка 0, все на этом одном шве:
        ///
        ///  1. <b>КОНВЕРСИЯ</b> — <see cref="DeltaScale.Resolve(ScaleDelta)"/>: «Дн −2» это −50 ₽, а не
        ///     −2 ₽; «Отн −3» роняет на 30 п.п., а не на 3. Абсолютные значения («Здр +40», «→ 80%»)
        ///     проходят насквозь.
        ///  2. <b>ТОЛЬКО ОТКРЫТЫЕ ШКАЛЫ</b> (<see cref="ScaleOpenForDelta"/>) — общее правило на всю игру:
        ///     Δ по шкале, которой ещё нет на экране, просто не применяется (в детстве это были энергия и
        ///     отношения). Исключение ровно одно и очевидное: карточка, которая САМА открывает шкалу
        ///     (`OPEN:*`), свою Δ применяет — шкала откроется сразу после ответа на неё.
        ///  3. <b>ПОТОЛОК И ПОЛ</b> — процентные шкалы зажимаются в 0…100. При старом масштабе (±1…±3)
        ///     переполнение было теоретическим, при новом (±35) — обычным делом.
        ///
        /// <paramref name="skipMoney"/> = true снимает денежную часть целиком (BLOCK$/заработок: точная
        /// сумма — единственный источник истины по деньгам, CSV-Δ не должна списать второй раз).
        /// </summary>
        private void ApplyCardDeltas(Card card, IReadOnlyList<ScaleDelta> deltas, bool skipMoney = false)
        {
            if (deltas == null) return;
            foreach (var raw in deltas)
            {
                if (!ScaleOpenForDelta(raw.Scale, card)) continue;      // правило 2
                var d = DeltaScale.Resolve(raw);                        // правило 1

                if (d.Scale == Scale.Money)
                {
                    if (skipMoney) continue;   // цена/заработок owns the money cost — ignore CSV money-Δ
                    switch (d.Kind)
                    {
                        case DeltaKind.Add: AddCardMoney(card, d.Value); break;
                        case DeltaKind.RandomPlusMinus: AddCardMoney(card, _coin() ? d.Value : -d.Value); break;
                        case DeltaKind.Set: Money = d.Value; NoteMoneyChanged(); break;
                    }
                    continue;
                }

                int current = Scales.Get(d.Scale);
                int next = d.Kind switch
                {
                    DeltaKind.Set => d.Value,
                    DeltaKind.RandomPlusMinus => current + (_coin() ? d.Value : -d.Value),
                    _ => current + d.Value,
                };
                Scales.Set(d.Scale, ClampScale(next));                  // правило 3
            }
        }

        /// <summary>Процентные шкалы живут в 0…100 — Δ не вправе выкинуть маркер за края.</summary>
        private static int ClampScale(int v) => v < 0 ? 0 : v > 100 ? 100 : v;

        /// <summary>
        /// Открыта ли шкала ДЛЯ Δ прямо сейчас. Здоровье открыто с первого кадра (единственная шкала
        /// детства); деньги/отношения/энергия/ребёнок — по своим флагам открытия. Карточка, несущая
        /// `OPEN:{шкала}`, считается работающей по УЖЕ открытой шкале: механически шкала откроется тиком
        /// позже (её придерживает <see cref="HeldByItsOwnCard"/>, чтобы туториал не встал поверх
        /// собственного вопроса), и без этой поправки «ПЕРВАЯ ЛЮБОВЬ! Отн +2» потеряла бы свою же Δ.
        /// </summary>
        private bool ScaleOpenForDelta(Scale scale, Card card) => scale switch
        {
            Scale.Health => true,
            Scale.Money => MoneyOpen || (card != null && card.Opens(Card.OpenMoney)),
            Scale.Relationships => RelationshipsOpen || (card != null && card.Opens(Card.OpenRelations)),
            Scale.Energy => EnergyOpen || (card != null && card.Opens(Card.OpenEnergy)),
            Scale.Child => ChildOpen || (card != null && card.Opens(Card.OpenChild)),
            _ => false,
        };

        /// <summary>
        /// Изменить счёт КАРТОЧКОЙ (Δ, цена или заработок) с соблюдением правила долга: в минус уводят
        /// ТОЛЬКО кредитные карточки (<see cref="CreditCards"/>). У всех прочих трата зажимается остатком —
        /// «а то, на что у нас не хватает денег, так и не должно быть доступно». Основной страж — `BLOCK$`
        /// (карта приходит гашёной и вовсе не применяется); этот зажим закрывает щели вокруг него: цена без
        /// `BLOCK$`, стоимость жизни, съевшая разницу между показом и ответом, отрицательная сторона «±N».
        ///
        /// Пассивные механики (стоимость жизни, дренажи ипотеки/кредита) сюда НЕ ходят — они по канону
        /// уводят в минус, это и есть «расплата за молодость».
        /// </summary>
        private void AddCardMoney(Card card, double amount) => AddClampedMoney(IsCreditCard(card), amount);

        /// <summary>
        /// Тот же зажим, но по УЖЕ ИЗВЕСТНОЙ кредитности — для платежей, у которых карточки под рукой нет:
        /// разовые выплаты `ONCE:Ny` созревают спустя годы после ответа (<see cref="MoneyOnce"/>).
        /// Единственная точка, где правило долга записано арифметикой; все карточные пути ведут сюда.
        /// </summary>
        private void AddClampedMoney(bool isCredit, double amount)
        {
            if (amount < 0 && !isCredit)
            {
                double affordable = Money > 0 ? Money : 0;
                if (-amount > affordable) amount = -affordable;
            }
            Money += amount;
            NoteMoneyChanged();
        }

        private static bool IsCreditCard(Card card) => card != null && CreditCards.Contains(card.Id);

        /// <summary>
        /// Не хватает ли денег на цену ЭТОЙ карточки ПРЯМО СЕЙЧАС. Та же арифметика, что и гейт показа в
        /// <see cref="Advance"/> (`BLOCK$` + цена + Money &lt; price), но считанная в момент ответа —
        /// одна щель между показом и ответом (стоимость жизни, дренаж ипотеки/кредита).
        ///
        /// КРЕДИТНЫЕ (<see cref="CreditCards"/>) исключены: у них уход в минус и есть содержание карточки,
        /// а взнос по ипотеке банк уже погейтил на показе — второй раз отбирать её нельзя.
        /// </summary>
        private bool UnaffordableNow(Card card)
            => card != null && card.IsBlockCost && !IsCreditCard(card)
               && BlockPrices.TryGetValue(card.Id, out var price) && Money < price;

        /// <summary>
        /// Отследить пересечение нуля и один раз объявить долг (пул реплик Ведущего `debt`, §3.2).
        /// Вызывается отовсюду, где счёт меняется, — и карточками, и пассивным дренажом.
        /// </summary>
        private void NoteMoneyChanged()
        {
            if (!MoneyOpen) { _inDebt = false; return; }
            if (!_inDebt && Money <= DebtAnnounceBelow)
            {
                _inDebt = true;
                DebtEntered?.Invoke();
            }
            else if (_inDebt && Money >= 0)   // гистерезис: латч снимается только выходом в плюс
            {
                _inDebt = false;
            }
        }
    }
}
