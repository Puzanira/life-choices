using System.Collections;
using System.Collections.Generic;
using AiGameStudio.ArcadeControls;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// ЗВОНОК РЕБЁНКА — трубка у левого края (meeting-revisions §5b / build-spec §E), через РЕАЛЬНЫЙ драйвер.
    /// Заменяет тесты старой «вспышки кнопки-ребёнка»: механика счёта в <see cref="Game"/> та же
    /// (ChildOpen / ChildFlashing / ChildPress / серия пропусков), сменились ВИД и окно (ровно 5 с).
    ///
    /// Здесь: обе позы (покой без дуг за левым краем ⇄ звонок с запечёнными дугами, выехавший внутрь),
    /// выезд/уезд 0.3 с, качание, длина окна через <c>DebugTick</c>, драйверный путь «!»-кнопки
    /// (<c>GameInput.ChildPress</c>, как его шлёт ArcadeInputSource) → салют и отсутствие штрафа, тот же
    /// путь через РЕАЛЬНУЮ цепочку кабинета (FakeBackend → ArcadeInput → ArcadeInputSource → драйвер),
    /// заморозка звонка на ВЫГОРАНИИ (плашка S7 накрывает трубку — окно стоять, трубку прятать),
    /// проспанный звонок → серия пропусков → «плохой родитель», двойная роль CONFIRM вплоть до финала
    /// (рестарт), и рестарт-выход (трубка в покое, звонок оборван).
    /// </summary>
    public class ChildPhoneTests
    {
        private const float Tol = 1.5f;      // реф-px допуск на позу (константы позы = замер по эталону)

        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();   // Awake builds HUD + loads sampled deck
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Starter()
        {
            var c = Plain("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        // Колода: открыть ребёнка на MD02=ДА и ДЕРЖАТЬ возраст на holdAge (возраст событийный = возраст
        // текущей карты). Так после набора возраста НИ ОДИН возрастной туториал больше не всплывает и не
        // ставит паузу посреди 5-секундного окна — окно меряется чисто. Интервал звонка приколот к 2 с.
        // holdAge=26 нужен тесту выгорания: шкала энергии открывается в 25, без неё выгорания не бывает.
        private static Game CallDeck(int holdAge = 22)
        {
            var deck = new List<Card> { Starter(), Plain("MD02", 21) };
            for (int i = 0; i < 80; i++)
            {
                var c = Plain("F" + i, holdAge);
                c.Order = holdAge + i;                   // возраст держим, порядок двигаем
                deck.Add(c);
            }
            return new Game(deck, coin: () => false) { ChildFlashInterval = () => 2f };
        }

        // Довести до открытого ребёнка: опенер → I03 → MD02=ДА (+ снять S5-подсказку).
        private static IEnumerator ToOpenChild(GameDriver driver, PlayFakeInputSource fake, int holdAge = 22)
        {
            driver.DebugReplaceGame(CallDeck(holdAge));
            fake.Confirm();                              // опенер → игра (I03)
            fake.No();                                   // I03 решена → MD02 текущая
            fake.Yes();                                  // MD02=ДА → механика ребёнка открыта (+ §D-модалка)
            NewScaleTut.ClearAny(driver, fake);          // поднять туториальный звонок → экран уходит
            yield return null;                           // Update прогоняет ReflectChildPhone
        }

        // Дотикать до ОТКРЫТОГО окна звонка. Тики идут через DebugTick — тем же путём, что Update,
        // так что трубка реально едет и качается, а не только флаг в Game меняется.
        private static void DriveToCall(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (!driver.Game.ChildFlashing && driver.Game.State == GameState.Playing && guard++ < 600)
            {
                if (driver.NewScaleShowing || driver.TutorialShowing) NewScaleTut.ClearAny(driver, fake);
                else driver.DebugTick(0.1f);
            }
            Assert.IsTrue(driver.Game.ChildFlashing, "окно звонка открылось");
        }

        private static void Tick(GameDriver driver, float seconds, float step = 0.1f)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += step)
                driver.DebugTick(Mathf.Min(step, seconds - t));
        }

        // Дотикать до ОТКРЫТОЙ шкалы энергии (25) и дождаться, пока текущий звонок (если он идёт) закончится:
        // выгорание живёт только при открытой энергии, а мерить остаток окна можно только со СВЕЖЕГО звонка.
        private static void DriveToEnergyOpen(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (!driver.Game.EnergyOpen && driver.Game.State == GameState.Playing && guard++ < 900)
            {
                if (driver.NewScaleShowing || driver.TutorialShowing) NewScaleTut.ClearAny(driver, fake);
                else driver.DebugTick(0.1f);
            }
            Assert.IsTrue(driver.Game.EnergyOpen, "шкала энергии открылась (25) — есть чему выгорать");
            guard = 0;
            while (driver.Game.ChildFlashing && driver.Game.State == GameState.Playing && guard++ < 200)
            {
                if (driver.NewScaleShowing || driver.TutorialShowing) NewScaleTut.ClearAny(driver, fake);
                else driver.DebugTick(0.1f);
            }
            if (driver.TutorialShowing) fake.Confirm();
        }

        // ---- реальная цепочка кабинета: FakeBackend → ArcadeInput → ArcadeInputSource → GameDriver -------

        private static BackendSnapshot Green => new BackendSnapshot { GreenHeld = true };
        private static BackendSnapshot Red => new BackendSnapshot { RedHeld = true };
        private static BackendSnapshot Bang => new BackendSnapshot { BangHeld = true };

        /// <summary>Физическое нажатие кнопки кабинета: удержана 2 кадра, отпущена 2 (как в ArcadeExitTests).</summary>
        private static IEnumerator Press(FakeBackend backend, BackendSnapshot snap)
        {
            backend.Next = snap;
            yield return null;
            yield return null;
            backend.Next = default;
            yield return null;
            yield return null;
        }

        // Дотикать до открытого окна звонка, снимая подсказки ФИЗИЧЕСКОЙ зелёной кнопкой (аркадный CONFIRM).
        private static IEnumerator DriveToCallArcade(GameDriver driver, FakeBackend backend)
        {
            int guard = 0;
            while (!driver.Game.ChildFlashing && driver.Game.State == GameState.Playing && guard++ < 600)
            {
                if (driver.NewScaleShowing) { yield return NewScaleTut.ClearArcade(driver, backend); continue; }
                if (driver.TutorialShowing) { yield return Press(backend, Green); continue; }
                driver.DebugTick(0.1f);
            }
            Assert.IsTrue(driver.Game.ChildFlashing, "окно звонка открылось");
        }

        /// <summary>Центр ректа в реф-px (x от ЛЕВОГО края, y от ВЕРХНЕГО) — как в HudConformanceTests.</summary>
        private static Vector2 RefCentre(RectTransform canvas, RectTransform rt)
        {
            var local = canvas.InverseTransformPoint(rt.position);
            return new Vector2((local.x / canvas.rect.width + 0.5f) * 1920f,
                               (0.5f - local.y / canvas.rect.height) * 1080f);
        }

        private static float TiltDegrees(RectTransform rt)
            => Mathf.DeltaAngle(0f, rt.localRotation.eulerAngles.z);

        // ============================================================ поза ПОКОЯ

        [UnityTest]
        public IEnumerator Rest_Pose_IsTheNoArcsSprite_BehindTheLeftEdge()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return ToOpenChild(driver, fake);

            Assert.IsTrue(driver.Game.ChildOpen, "MD02=ДА открыл механику ребёнка");
            Assert.IsTrue(driver.ChildGroup.activeInHierarchy, "трубка видна на игровом экране");
            Assert.IsFalse(driver.Game.ChildFlashing, "звонка ещё нет");

            // Поза покоя = спрайт БЕЗ дуг (asset-map §5.7: две позы одной трубки — дуги запечены только
            // в позе звонка), проверяем ПО ИМЕНИ спрайта, а не по факту «что-то нарисовано».
            Assert.AreEqual("phone-rest-v2", driver.ChildPhoneImage.sprite.name,
                "в покое рисуется трубка БЕЗ красных дуг");
            Assert.AreEqual(0f, driver.ChildPhoneOut, 1e-3f, "в покое трубка полностью убрана за край");

            var rt = driver.ChildPhoneImage.rectTransform;
            var c = RefCentre(driver.CanvasRect, rt);
            Assert.AreEqual(GameDriver.PhoneRestRect.x, c.x, Tol, "покой: центр по X (замер эталона)");
            Assert.AreEqual(GameDriver.PhoneRestRect.y, c.y, Tol, "покой: центр по Y");
            Assert.AreEqual(GameDriver.PhoneRestRect.z, rt.rect.width, Tol, "покой: ширина ректа");
            Assert.AreEqual(GameDriver.PhoneRestRect.w, rt.rect.height, Tol, "покой: высота ректа");
            Assert.AreEqual(GameDriver.PhoneRestTilt, TiltDegrees(rt), 0.5f, "покой: наклон");

            // …и она реально ТОРЧИТ ИЗ-ЗА ЛЕВОГО КРАЯ: рект уходит в минус по X, а его правый край не
            // доезжает до карточки-вопроса (её нарисованный левый край — 413 реф-px).
            Assert.Less(c.x - rt.rect.width / 2f, 0f, "трубка в покое частично за левым краем кадра");
            Assert.Less(c.x + rt.rect.width / 2f, 413f, "трубка в покое не заезжает на карточку-вопрос");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ ЯРКОСТЬ: покой притушен, звонок горит

        /// <summary>
        /// Эталон «Экран спокойный обычный.png» намеренно ГАСИТ спящую трубку (замер корпуса: у эталона
        /// G≈95 / B≈188…222, у неприглушённого спрайта G≈110 / B≈241) — иначе она тянет глаз наравне со
        /// звонком. Тинт живёт на ТОМ ЖЕ лерпе, что и поза: покой = <see cref="GameDriver.PhoneRestTint"/>,
        /// звонок = белый, между ними — плавный разгон за 0.3 с.
        /// </summary>
        [UnityTest]
        public IEnumerator Rest_Pose_IsDimmed_AndTheRingingCall_BurnsFullBrightness()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return ToOpenChild(driver, fake);

            Assert.AreEqual(0f, driver.ChildPhoneOut, 1e-3f, "стартуем из покоя");
            var rest = driver.ChildPhoneImage.color;
            Assert.AreEqual(GameDriver.PhoneRestTint.r, rest.r, 1e-3f, "покой: тинт по R");
            Assert.AreEqual(GameDriver.PhoneRestTint.g, rest.g, 1e-3f, "покой: тинт по G");
            Assert.AreEqual(GameDriver.PhoneRestTint.b, rest.b, 1e-3f, "покой: тинт по B");
            Assert.Less(rest.g, 0.95f, "спящая трубка ПРИТУШЕНА, а не белая");
            Assert.Less(rest.b, 0.90f, "…и сильнее всего по синему — эталонная спящая трубка тусклее и глуше");
            Assert.Less(rest.b, rest.g, "гашение не серое: синий гасится сильнее зелёного (замер эталона)");

            // Звонок: трубка выехала — тинт снят, спрайт горит в полную яркость.
            DriveToCall(driver, fake);
            Tick(driver, GameDriver.PhoneSlideSeconds, 0.05f);
            Assert.AreEqual(1f, driver.ChildPhoneOut, 1e-3f, "трубка выехала полностью");
            var ring = driver.ChildPhoneImage.color;
            Assert.AreEqual(1f, ring.r, 1e-3f, "звонок: полная яркость по R");
            Assert.AreEqual(1f, ring.g, 1e-3f, "…по G");
            Assert.AreEqual(1f, ring.b, 1e-3f, "…и по B");

            // Переход плавный: НА ПОЛПУТИ уезда яркость строго между покоем и звонком (а не щёлкает).
            int guard = 0;
            while (driver.Game.ChildFlashing && guard++ < 200) driver.DebugTick(0.1f);   // проспали звонок
            Assert.IsFalse(driver.Game.ChildFlashing, "окно закрылось — трубка поехала за край");
            driver.DebugTick(GameDriver.PhoneSlideSeconds / 2f);
            var mid = driver.ChildPhoneImage.color;
            Assert.Greater(driver.ChildPhoneOut, 0f, "трубка ещё в пути");
            Assert.Less(driver.ChildPhoneOut, 1f, "…но уже не в позе звонка");
            Assert.Greater(mid.b, rest.b + 1e-3f, "на полпути яркость ещё не упала до покоя");
            Assert.Less(mid.b, 1f - 1e-3f, "…и уже не полная — тинт лерпается вместе с позой");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ поза ЗВОНКА

        [UnityTest]
        public IEnumerator Call_SlidesIn_SwapsToTheArcsSprite_Tilts_AndWobbles()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return ToOpenChild(driver, fake);
            DriveToCall(driver, fake);

            Assert.AreEqual("phone-ring-v2", driver.ChildPhoneImage.sprite.name,
                "на звонке рисуется поза С запечёнными дугами");

            // Выезд за 0.3 с (build-spec §6): дотикать ровно длительность выезда → трубка внутри.
            Tick(driver, GameDriver.PhoneSlideSeconds, 0.05f);
            Assert.AreEqual(1f, driver.ChildPhoneOut, 1e-3f, "за 0.3 с трубка выехала полностью");

            var rt = driver.ChildPhoneImage.rectTransform;
            var c = RefCentre(driver.CanvasRect, rt);
            Assert.AreEqual(GameDriver.PhoneRingRect.x, c.x, Tol, "звонок: центр по X (замер эталона)");
            Assert.AreEqual(GameDriver.PhoneRingRect.y, c.y, Tol, "звонок: центр по Y");
            Assert.AreEqual(GameDriver.PhoneRingRect.z, rt.rect.width, Tol, "звонок: ширина ректа");
            Assert.AreEqual(GameDriver.PhoneRingRect.w, rt.rect.height, Tol, "звонок: высота ректа");
            // Наклон = поза звонка ± качание (±6°), т.е. заметно СИЛЬНЕЕ наклона покоя.
            Assert.AreEqual(GameDriver.PhoneRingTilt, TiltDegrees(rt), GameDriver.PhoneWobbleDegrees + 0.5f,
                "звонок: наклон позы звонка (± качание)");
            Assert.Less(TiltDegrees(rt), GameDriver.PhoneRestTilt,
                "на звонке трубка наклонена сильнее, чем в покое");

            // Качание: угол ЖИВОЙ — четверть периода меняет его заметно (фаза детерминирована часами
            // звонка, не Time.time, поэтому шаг воспроизводим).
            float a0 = TiltDegrees(rt);
            driver.DebugTick(GameDriver.PhoneWobblePeriod / 4f);
            float a1 = TiltDegrees(rt);
            Assert.AreNotEqual(a0, a1, "качание живое — угол меняется между тиками");
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(a0, a1)), 1f, "качание заметное, а не дрожь в 0.01°");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ окно РОВНО 5 секунд

        [UnityTest]
        public IEnumerator CallWindow_IsExactlyFiveSeconds_ThenThePhoneLeaves()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return ToOpenChild(driver, fake);
            DriveToCall(driver, fake);

            Assert.AreEqual(5f, Game.ChildFlashWindow, 1e-4f, "спек §5b: окно поднятия — ровно 5 с");

            // Окно открылось В КОНЦЕ тика — на этом тике оно ещё не убывало. 4.9 с спустя оно ЖИВО…
            Tick(driver, Game.ChildFlashWindow - 0.1f, 0.1f);
            Assert.IsTrue(driver.Game.ChildFlashing, "за 0.1 с до конца окно ещё открыто");

            // …а перешагнув 5 с — закрыто, трубка успокоилась (спрайт без дуг) и поехала за край.
            driver.DebugTick(0.2f);
            Assert.IsFalse(driver.Game.ChildFlashing, "на 5.1 с окно закрыто");
            Assert.AreEqual("phone-rest-v2", driver.ChildPhoneImage.sprite.name,
                "трубка успокоилась — дуги сняты сразу, как окно закрылось");
            Assert.Less(driver.ChildPhoneOut, 1f, "трубка поехала обратно за край");
            Tick(driver, GameDriver.PhoneSlideSeconds, 0.05f);
            Assert.AreEqual(0f, driver.ChildPhoneOut, 1e-3f, "за 0.3 с уехала полностью");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ ПОДНЯЛ трубку («!» кабинета)

        [UnityTest]
        public IEnumerator BangButton_PicksUpTheCall_FiresStars_AndCostsNothing()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return ToOpenChild(driver, fake);
            DriveToCall(driver, fake);
            // Кадр вхолостую: DriveToCall мог снять возрастную подсказку через CONFIRM, а драйвер на ЭТОМ
            // кадре глотает любой НЕ-Confirm ввод (защита от аккорда «снял подсказку + нажал»). Ждём
            // следующего кадра, иначе «!» уйдёт в никуда — и тест мерил бы не механику, а эту защиту.
            yield return null;

            int child0 = driver.Game.Scales.Child;
            int rel0 = driver.Game.Scales.Relationships;
            int bursts0 = driver.StarBurstCount;

            // Ровно то событие, которое шлёт кнопка «!» кабинета (ArcadeInputSource: BangButton →
            // GameInput.ChildPress) — драйверный путь, не прямой вызов Game.
            fake.Fire(GameInput.ChildPress);

            Assert.IsFalse(driver.Game.ChildFlashing, "поднял — окно закрылось");
            Assert.AreEqual(child0 + Game.ChildPressGain, driver.Game.Scales.Child,
                "удачно поднятый звонок — это хорошее родительство");
            Assert.AreEqual(bursts0 + 1, driver.StarBurstCount,
                "за поднятую трубку положен САЛЮТ ЗВЁЗД (§6)");
            Assert.GreaterOrEqual(driver.Game.Scales.Relationships, rel0 - 1,
                "штрафа за поднятый звонок нет");

            // Трубка успокаивается и уезжает — без дуг и до конца.
            Tick(driver, GameDriver.PhoneSlideSeconds, 0.05f);
            Assert.AreEqual("phone-rest-v2", driver.ChildPhoneImage.sprite.name, "поза покоя, дуг нет");
            Assert.AreEqual(0f, driver.ChildPhoneOut, 1e-3f, "уехала за край");

            // …и счётчик пропусков НЕ вырос: следующий ОДИН пропуск ещё не делает плохим родителем.
            DriveToCall(driver, fake);
            int relBefore = driver.Game.Scales.Relationships;
            int childBefore = driver.Game.Scales.Child;
            Tick(driver, Game.ChildFlashWindow + 0.2f, 0.1f);
            Assert.IsFalse(driver.Game.ChildFlashing, "проспал следующий звонок");
            Assert.Greater(driver.Game.Scales.Relationships, relBefore - Game.ChildBadParentRelPenalty,
                "серия пропусков обнулилась поднятой трубкой — один пропуск ещё не штраф");
            Assert.AreEqual(childBefore, driver.Game.Scales.Child,
                "и шкала ребёнка на одном пропуске не проседает");

            Object.Destroy(go);
            yield return null;
        }

        // ================================================ «!» через РЕАЛЬНУЮ цепочку кабинета (не фейк-событие)

        /// <summary>
        /// Тест выше — быстрый ЮНИТ: он вбрасывает готовый <c>GameInput.ChildPress</c> в драйвер и минует
        /// весь путь кнопки. Здесь звонок поднимается ФИЗИЧЕСКОЙ «!»: снимок бэкенда пакета → ArcadeInput →
        /// СОБСТВЕННЫЙ <c>ArcadeInputSource</c> драйвера (семантический фейк не подсовываем — ровно как в
        /// отгружаемой сцене и на автомате) → GameDriver.OnInput → трубка. Если цепочка порвётся где угодно
        /// между кнопкой и трубкой, красным станет ЭТОТ тест, а не юнит.
        /// </summary>
        [UnityTest]
        public IEnumerator BangButton_ThroughTheRealCabinetChain_PicksUpTheCall_AndBanksNoMiss()
        {
            var backend = new FakeBackend();
            var rig = new GameObject("Rig");
            rig.SetActive(false);
            rig.AddComponent<ArcadeInputRunner>().BackendOverride = backend;   // инъекция до Awake
            rig.SetActive(true);

            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();   // Start сам вешает ArcadeInputSource и найдёт ригу
            yield return null;                            // Start
            yield return null;                            // кадр «все контролы отпущены»

            driver.DebugReplaceGame(CallDeck());
            yield return Press(backend, Green);           // GREEN на опенере = старт жизни (аркадный CONFIRM)
            Assert.AreEqual(GameState.Playing, driver.Game.State, "зелёная кнопка начала жизнь");
            yield return Press(backend, Red);             // RED = НЕТ на I03 → MD02 текущая
            Assert.AreEqual("MD02", driver.Game.CurrentCard.Id, "дошли до карточки ребёнка");
            yield return Press(backend, Green);           // GREEN = ДА на MD02 → механика ребёнка открыта
            yield return NewScaleTut.ClearArcade(driver, backend);   // «!» кабинета поднимает туториальный звонок
            int guard = 0;
            while (driver.TutorialShowing && guard++ < 20) yield return Press(backend, Green);
            Assert.IsTrue(driver.Game.ChildOpen, "MD02=ДА открыл механику ребёнка");

            yield return DriveToCallArcade(driver, backend);
            int child0 = driver.Game.Scales.Child;
            int bursts0 = driver.StarBurstCount;

            yield return Press(backend, Bang);            // ← ФИЗИЧЕСКАЯ «!» кабинета

            Assert.IsFalse(driver.Game.ChildFlashing, "«!» кабинета подняла трубку — окно закрылось");
            Assert.AreEqual(child0 + Game.ChildPressGain, driver.Game.Scales.Child,
                "поднятый звонок — это хорошее родительство");
            Assert.AreEqual(bursts0 + 1, driver.StarBurstCount,
                "…и за него отстрелил САЛЮТ ЗВЁЗД (§6) — фидбек живёт на реальном пути кнопки");

            // Счётчик пропусков не вырос: следующий ОДИН проспанный звонок ещё не «плохой родитель».
            yield return DriveToCallArcade(driver, backend);
            int rel1 = driver.Game.Scales.Relationships;
            int child1 = driver.Game.Scales.Child;
            Tick(driver, Game.ChildFlashWindow + 0.2f, 0.1f);
            Assert.IsFalse(driver.Game.ChildFlashing, "следующий звонок проспан");
            Assert.Greater(driver.Game.Scales.Relationships, rel1 - Game.ChildBadParentRelPenalty,
                "серия пропусков была обнулена поднятой трубкой — один пропуск ещё не штраф");
            Assert.AreEqual(child1, driver.Game.Scales.Child,
                "…и шкала ребёнка на одном пропуске не проседает");

            Object.Destroy(go);
            Object.Destroy(rig);
            yield return null;
        }

        // ================================================ ВЫГОРАНИЕ: звонок замирает и прячется

        /// <summary>
        /// Плашка «ВЫГОРАНИЕ» (S7) — полноэкранный захват, нарисованный ПОВЕРХ трубки. Пока она висит,
        /// звонящей трубки не видно, поэтому окно поднятия ОБЯЗАНО стоять: иначе игрок копит невидимые
        /// пропуски и получает «плохого родителя» за звонок, которого не видел. Проверяем обе половины:
        /// Game не убавляет окно (и не штрафует), драйвер прячет трубку, а по выходу звонок продолжается
        /// РОВНО с того же остатка (2 с из 5), а не начинается заново.
        /// </summary>
        [UnityTest]
        public IEnumerator Burnout_FreezesTheCallWindow_AndHidesTheHandset_UntilItLifts()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return ToOpenChild(driver, fake, holdAge: 26);   // 26 → шкала энергии успевает открыться
            DriveToEnergyOpen(driver, fake);
            DriveToCall(driver, fake);                             // свежий звонок = ровно 5 с окна

            // Сжигаем 3 с окна вживую — остаток 2 с.
            Tick(driver, 3f, 0.1f);
            Assert.IsTrue(driver.Game.ChildFlashing, "звонок ещё идёт");
            Assert.IsTrue(driver.ChildGroup.activeSelf, "трубка на экране");
            Assert.AreEqual(1f, driver.ChildPhoneOut, 1e-3f, "…и полностью выехала");

            // ВЫГОРАНИЕ приходит ПОСРЕДИ звонка: энергия проваливается в зону латча (≤10 %), ближайший
            // тик её защёлкивает. Одноразовую подсказку выгорания снимаем сразу — дальше морозить обязано
            // именно выгорание, а не пауза-подсказка (иначе тест мерил бы не ту заморозку).
            driver.Game.Scales.Energy = Game.BurnoutEnterEnergyAtOrBelow;
            driver.DebugTick(0.05f);
            Assert.IsTrue(driver.Game.Burnout, "выгорание включилось посреди звонка");
            if (driver.TutorialShowing) fake.Confirm();
            Assert.IsFalse(driver.Game.Paused, "подсказка снята — игра НЕ на паузе");
            Assert.IsTrue(driver.Game.ChildFlashing, "звонок никуда не делся — он ЗАМЕР");
            Assert.IsFalse(driver.ChildGroup.activeSelf,
                "трубка спрятана: плашка выгорания всё равно накрывает её целиком");

            // 10 с «вслепую» — ВДВОЕ дольше окна. Ни окно, ни серия пропусков не двигаются.
            int child0 = driver.Game.Scales.Child;
            int rel0 = driver.Game.Scales.Relationships;
            for (int i = 0; i < 100; i++)
            {
                driver.Game.Scales.Energy = Game.BurnoutEnterEnergyAtOrBelow;  // держим в зоне выгорания:
                driver.DebugTick(0.1f);                                        // тест про звонок, не про смерть
            }
            Assert.IsTrue(driver.Game.Burnout, "всё ещё выгорание");
            Assert.IsFalse(driver.Game.Paused, "и всё ещё без паузы-подсказки");
            Assert.IsTrue(driver.Game.ChildFlashing,
                "окно ЗАМОРОЖЕНО: за 10 с под плашкой оно не истекло");
            Assert.IsFalse(driver.ChildGroup.activeSelf, "…и трубка всё это время спрятана");
            Assert.AreEqual(child0, driver.Game.Scales.Child, "невидимых пропусков не начислено");
            Assert.Greater(driver.Game.Scales.Relationships, rel0 - Game.ChildBadParentRelPenalty,
                "…и «плохого родителя» вслепую не выдали");

            // Выход из выгорания (энергия выше порога снятия) → трубка снова на экране, звонок доигрывает.
            driver.Game.Scales.Energy = Game.BurnoutExitEnergyAbove + 20;
            driver.DebugTick(0.05f);
            Assert.IsFalse(driver.Game.Burnout, "выгорание снято");
            Assert.IsTrue(driver.ChildGroup.activeSelf, "трубка вернулась на экран");
            Assert.AreEqual("phone-ring-v2", driver.ChildPhoneImage.sprite.name, "…и она всё ещё звонит");
            Assert.AreEqual(1f, driver.ChildPhoneOut, 1e-3f,
                "поза сохранилась — за время выгорания трубка не уезжала за край");

            // Остаток ПРОДОЛЖИЛСЯ, а не начался заново: на 1.85 с после разморозки окно ещё живо, а на
            // 2.05 с — истекло (сожгли 3 из 5 до выгорания). Если бы окно перезапустилось, оно бы дожило.
            Tick(driver, 1.8f, 0.1f);
            Assert.IsTrue(driver.Game.ChildFlashing, "остаток окна ещё не вышел");
            driver.DebugTick(0.2f);
            Assert.IsFalse(driver.Game.ChildFlashing,
                "окно истекло ровно на своём остатке (2 с), а не отсчиталось заново");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ ПРОСПАЛ два звонка → плохой родитель

        [UnityTest]
        public IEnumerator SleepingThroughTwoCalls_TurnsOnTheBadParentBranch()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return ToOpenChild(driver, fake);

            // Пропуск №1 — без штрафа.
            DriveToCall(driver, fake);
            int rel1 = driver.Game.Scales.Relationships;
            int child1 = driver.Game.Scales.Child;
            Tick(driver, Game.ChildFlashWindow + 0.2f, 0.1f);
            Assert.IsFalse(driver.Game.ChildFlashing, "первый звонок проспан");
            Assert.Greater(driver.Game.Scales.Relationships, rel1 - Game.ChildBadParentRelPenalty,
                "первый пропуск ещё не «плохой родитель»");
            Assert.AreEqual(child1, driver.Game.Scales.Child, "…и шкала ребёнка цела");

            // Пропуск №2 ПОДРЯД — ветка «плохой родитель» (Game: 2+ пропуска = штраф).
            DriveToCall(driver, fake);
            int rel2 = driver.Game.Scales.Relationships;
            int child2 = driver.Game.Scales.Child;
            Tick(driver, Game.ChildFlashWindow + 0.2f, 0.1f);
            Assert.IsFalse(driver.Game.ChildFlashing, "второй звонок тоже проспан");
            Assert.LessOrEqual(driver.Game.Scales.Relationships, rel2 - Game.ChildBadParentRelPenalty,
                "второй пропуск подряд — «плохой родитель»: отношения −10 %");
            Assert.AreEqual(child2 - Game.ChildBadParentScaleDrop, driver.Game.Scales.Child,
                "…и шкала ребёнка проседает");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ интервал звонков (канон 15–25 с)

        [Test]
        public void CallInterval_KeepsTheCanonFifteenToTwentyFiveSeconds()
        {
            Assert.AreEqual(15f, Game.ChildFlashIntervalMin, 1e-4f, "нижняя граница интервала звонков");
            Assert.AreEqual(25f, Game.ChildFlashIntervalMax, 1e-4f, "верхняя граница интервала звонков");
        }

        // ============================================================ Enter double-duty (не украл CONFIRM)

        [UnityTest]
        public IEnumerator Confirm_RoutesToChildPress_OnlyInGameplayWithOpenChild_ElseConfirm()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(CallDeck());

            // (1) Опенер: CONFIRM запускает игру (ребёнок ещё не открыт — трубки нет).
            fake.Confirm();
            Assert.AreEqual(GameState.Playing, driver.Game.State, "CONFIRM в опенере запускает игру");
            Assert.IsFalse(driver.Game.ChildOpen);
            Assert.IsFalse(driver.ChildGroup.activeInHierarchy, "до MD02=ДА трубки на экране нет");

            fake.No();                                   // I03 решена → MD02 текущая
            Assert.AreEqual("MD02", driver.Game.CurrentCard.Id);

            // (2) MD02=ДА открывает ребёнка → §D-экран новой шкалы «ПОПОЛНЕНИЕ» и пауза.
            fake.Yes();
            Assert.IsTrue(driver.Game.ChildOpen, "MD02=ДА открыл ребёнка");
            Assert.IsTrue(driver.NewScaleShowing, "модальный экран ребёнка поднялся");
            Assert.AreEqual(NewScale.Child, driver.NewScaleKind, "это экран про ребёнка");
            StringAssert.Contains("Пополнение", driver.NewScaleStoryText.text, "рассказ про пополнение");
            StringAssert.Contains("телефон", driver.NewScaleTaskText.text,
                "задача канон host-content §4: «когда телефон слева зазвонит»");
            StringAssert.Contains("«!»", driver.NewScaleTaskText.text, "…и называет кнопку «!»");

            // (3) Под экраном CONFIRM = ПОДНЯТЬ ТРУБКУ (условие выхода), а не «понятно»: туториал
            // заводит звонок принудительно, поэтому dev-Enter здесь работает как «!» кабинета.
            Assert.IsTrue(driver.Game.ChildFlashing, "туториал завёл звонок");
            fake.Confirm();
            Assert.IsFalse(driver.Game.ChildFlashing, "трубка поднята");
            driver.DebugAdvanceNewScale(0.05f);                      // условие засчитано → фейд
            driver.DebugAdvanceNewScale(GameDriver.NewScaleFadeSeconds);
            Assert.IsFalse(driver.NewScaleShowing, "условие выполнено — экран ушёл");
            Assert.IsFalse(driver.Game.Paused, "игра пошла дальше");
            yield return null;
            Assert.IsTrue(driver.ChildGroup.activeSelf, "после открытия механики трубка на экране");

            // (4) На ЗВОНКЕ тот же CONFIRM = поднять трубку (и салют), а не «подтвердить/рестарт».
            DriveToCall(driver, fake);
            int child0 = driver.Game.Scales.Child;
            int bursts0 = driver.StarBurstCount;
            fake.Confirm();
            Assert.AreEqual(GameState.Playing, driver.Game.State,
                "поднятие ничего не подтвердило и не начало заново");
            Assert.IsFalse(driver.Game.ChildFlashing, "звонок поднят");
            Assert.AreEqual(child0 + Game.ChildPressGain, driver.Game.Scales.Child);
            Assert.AreEqual(bursts0 + 1, driver.StarBurstCount, "салют за поднятую трубку");

            // (5) ФИНАЛ: тот же CONFIRM снова означает «начать заново» — Playing-гейт не даёт ему стать
            // поднятием трубки даже при открытом ребёнке. Зуб перенесён из снятого ChildButtonTests:
            // без него двойная роль Enter проверялась только со стороны геймплея.
            // Кадр вхолостую: пока подсказки снимались через CONFIRM без единого Update, у драйвера висит
            // защита «снял подсказку в этом кадре» — она глотает НЕ-Confirm ввод, и НЕТ уходило бы в никуда.
            yield return null;
            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 2000)
            {
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                if (driver.HostBannerVisible) { driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f); continue; }
                if (driver.Game.CurrentCard != null) fake.No();   // доигрываем жизнь «СПАСИБО, НЕ НАДО»
                else driver.DebugTick(0.5f);
            }
            Assert.AreEqual(GameState.Finale, driver.Game.State, "жизнь доиграна до финала");

            fake.Confirm();
            Assert.AreEqual(GameState.Opener, driver.Game.State,
                "на финале CONFIRM = начать заново (вне Playing он никогда не «поднять трубку»)");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ рестарт: трубка в покое, звонок оборван

        [UnityTest]
        public IEnumerator Restart_PutsThePhoneBackToRest_AndCutsTheCall()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return ToOpenChild(driver, fake);
            DriveToCall(driver, fake);
            Tick(driver, GameDriver.PhoneSlideSeconds, 0.05f);
            Assert.AreEqual(1f, driver.ChildPhoneOut, 1e-3f, "трубка выехала — есть что обрывать");

            // Выход в свежую жизнь (кнопка МЕНЮ кабинета — тот же путь, что рестарт с финала).
            fake.Fire(GameInput.Exit);
            Assert.AreEqual(GameState.Opener, driver.Game.State, "вернулись в опенер");
            Assert.IsFalse(driver.ChildGroup.activeInHierarchy, "на опенере трубки нет");

            fake.Confirm();                              // новая жизнь
            yield return null;
            Assert.IsFalse(driver.Game.ChildOpen, "свежая жизнь — ребёнок закрыт");
            Assert.IsFalse(driver.ChildGroup.activeInHierarchy, "…и трубки на экране нет");
            Assert.AreEqual(0f, driver.ChildPhoneOut, 1e-3f, "поза сброшена в покой — звонок оборван");
            Assert.AreEqual("phone-rest-v2", driver.ChildPhoneImage.sprite.name, "…и спрайт вернулся без дуг");

            Object.Destroy(go);
            yield return null;
        }
    }
}
