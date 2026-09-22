using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Driver-level wiring of the live layer: the burnout state plate and the show-brightness veil are
    /// assembled (veil transparent at full state), and the S5 energy hint fires + pauses when energy
    /// opens at 25. Time is driven by explicit Game.Tick calls (no wall-clock).
    /// </summary>
    public class DriverLiveHudTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        [UnityTest]
        public IEnumerator Assembles_BurnoutPlate_And_BrightnessVeil()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                              // → playing
            yield return null;
            yield return null;

            Assert.IsNotNull(driver.BurnoutPlate, "burnout plate assembled");
            Assert.IsFalse(driver.BurnoutPlate.activeSelf, "burnout plate hidden at full energy");
            Assert.IsNotNull(driver.BrightnessVeil, "brightness veil assembled");
            Assert.Less(driver.BrightnessVeil.color.a, 0.05f, "veil transparent while state is full");

            Object.Destroy(go);
            yield return null;
        }

        // ---- BLOCK$ price line, driven by a REAL priced card through the live card flow ----

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new System.Collections.Generic.List<string>() };

        private static Card Starter()
        {
            var c = Plain("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        // A deck that lands on MD03 (a genuine BLOCK$ card, price 60₽) as the current card: starter →
        // filler(18, where money opens) → MD03(30). Driven through Advance/CardChanged, not a hook.
        private static Game BlockDeck()
        {
            var deck = new System.Collections.Generic.List<Card>
            {
                Starter(), Plain("FILL", 18), Plain("MD03", 30), Plain("NORMAL", 40),
            };
            deck[2].IsBlockCost = true;   // MD03 → BLOCK$ (Game.BlockPrices["MD03"] = 60)
            return new Game(deck, coin: () => false);
        }

        [UnityTest]
        public IEnumerator PriceLine_Blocked_RealCard_ShowsСТОИТ_WithBanner()
        {
            // Broke path: a REAL BLOCK$ card (MD03, 60₽) is drawn while money < price → the on-card price
            // line reads «цена 60 ₽» (S10 mockup wording) and the S10 block banner is up. Asserts the Text via the
            // live flow (StartLife → Advance → CardChanged → OnCardChanged), not a debug setter.
            var driver = Boot(out var go, out var fake);
            yield return null;                           // Awake + Start (CSV game wired)
            driver.DebugReplaceGame(BlockDeck());        // swap in the deterministic BLOCK$ deck
            fake.Confirm();                              // Opener → StartLife → draws the starter
            fake.No();                                   // resolve starter → age timer on, FILL drawn
            fake.No();                                   // resolve FILL → MD03 drawn (Money 0 < 60 → blocked)

            Assert.AreEqual("MD03", driver.Game.CurrentCard.Id, "landed on the real BLOCK$ card");
            Assert.IsTrue(driver.Game.CurrentCardBlocked, "drawn while broke → blocked");
            Assert.IsTrue(driver.CardPriceText.gameObject.activeSelf, "price line shown on a blocked BLOCK$ card");
            StringAssert.Contains("60", driver.CardPriceText.text, "shows the required amount");
            // ⚠ ФОРМУЛИРОВКА СВЕДЕНА К ОДНОЙ (дизайн-гейт r6): было «цена N ₽» в блоке против
            // «СТОИТ N ₽» в доступном — разный регистр и скачок ширины чипа на живом фронте r6 п.2.
            // Состояние теперь несёт ЦВЕТ, а не текст (см. PlaytestFixesR6Tests.PriceChip_SaysTheSame…).
            StringAssert.Contains("СТОИТ", driver.CardPriceText.text, "blocked wording (S10: «СТОИТ N ₽»)");
            Assert.IsTrue(driver.BlockBanner.activeSelf, "S10 block banner is up alongside the price");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PriceLine_Affordable_RealCard_ShowsСТОИТ_NoBanner()
        {
            // Afforded path: bank ≥ 60₽, then the SAME real BLOCK$ card is drawn affordable → «СТОИТ 60 ₽»
            // and no block banner. All steps are synchronous (no yields) so the driver's own Update never
            // ticks mid-sequence — money stays banked and the card's phase timer (§3: 10/8/6 s) never fires.
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = BlockDeck();
            driver.DebugReplaceGame(g);
            fake.Confirm();                              // → StartLife, starter drawn
            fake.No();                                   // starter resolved → FILL drawn, age timer on
            g.Tick(2f);                                  // age → 18, money opens (driver shows the S5 hint + pauses)
            NewScaleTut.Clear(driver, fake);             // §D money screen: crank once → it closes, unpause
            yield return null;                           // let Update clear the same-frame dismiss guard (tiny tick)
            for (int i = 0; i < 100; i++) g.HandleInput(GameInput.MoneyTick); // bank ≥ 60₽ (direct game, no cap)
            Assert.GreaterOrEqual(g.Money, 60.0, "banked past the price");

            fake.No();                                   // resolve FILL → MD03 drawn affordable
            Assert.AreEqual("MD03", driver.Game.CurrentCard.Id, "landed on the real BLOCK$ card");
            Assert.IsFalse(driver.Game.CurrentCardBlocked, "affordable → not blocked");
            Assert.IsTrue(driver.CardPriceText.gameObject.activeSelf, "price line shown on an affordable BLOCK$ card");
            StringAssert.Contains("60", driver.CardPriceText.text, "shows the amount");
            StringAssert.Contains("СТОИТ", driver.CardPriceText.text, "affordable wording");
            Assert.IsFalse(driver.BlockBanner.activeSelf, "no block banner when affordable");

            // And the line clears on a normal (non-BLOCK$) card: answer MD03 → deck ends, no priced card up.
            fake.Yes();
            Assert.IsFalse(driver.Game.CurrentCardHasPrice, "no priced card after MD03");
            Assert.IsFalse(driver.CardPriceText.gameObject.activeSelf, "price line hidden once off the BLOCK$ card");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PriceLine_SitsOnDarkPlate_BehindAndCoveringTheText()
        {
            // Contrast invariant (S10): the price sub-line must never be bare gold/light text on the
            // yellow sunburst. Assert a dark plate exists, is drawn BEHIND the text, and its rect fully
            // COVERS the text rect — so a regression back to bare-text-on-background fails here.
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(BlockDeck());
            fake.Confirm();                              // → StartLife, starter drawn
            fake.No();                                   // starter resolved → FILL drawn
            fake.No();                                   // → MD03 drawn (Money 0 < 60 → blocked, price shown)
            yield return null;

            Assert.IsTrue(driver.CardPriceText.gameObject.activeSelf, "price text is shown");
            Assert.IsNotNull(driver.CardPricePlate, "a plate sits behind the price sub-line");
            Assert.IsTrue(driver.CardPricePlate.gameObject.activeSelf, "the plate is shown with the text");

            // Plate colour is dark (the S10 block-tag) — asserted on the RENDERED colour, because uGUI
            // MULTIPLIES the tint by bar-track's own fill and the raw tint would lie. Three teeth now:
            //  (a) it is EXACTLY the DEEP COBALT token #1F3A96 — pinned to the hex, because a comment
            //      claiming one hex while the code drew another is how this drifted in the first place
            //      (Codex MINOR 2026-08-05). OnBarTrack divides the token by bar-track's fill and uGUI
            //      multiplies it back, so the rendered colour round-trips to the token itself. On the
            //      captured frame the same chip measures #1D3996 — Δ≤2/255, because the project renders
            //      in LINEAR colour space while this model multiplies in sRGB (the identical ≤2/255
            //      shift shows on every other OnBarTrack plate); the frame value is documented at the
            //      chip's construction site in GameDriver.
            //  (b) it really is a DARK tag (light text on it reads) — luminance well below mid-grey;
            //  (c) it is NOT the same colour as its own black keyline. The chip used to be tinted with
            //      the very INK its keyline is drawn in, so the keyline added in the polish increment
            //      rendered invisible («обводка есть, а глазом её нет»).
            var c = driver.CardPricePlate.color;
            var rendered = new Color(GameDriver.BarTrackFillToken.r * c.r,
                                     GameDriver.BarTrackFillToken.g * c.g,
                                     GameDriver.BarTrackFillToken.b * c.b);
            var cobaltDeep = GameDriver.CobaltDeepToken;   // #1F3A96
            Assert.AreEqual(cobaltDeep.r, rendered.r, 1f / 255f, "chip renders the DEEP COBALT token: R of #1F3A96");
            Assert.AreEqual(cobaltDeep.g, rendered.g, 1f / 255f, "chip renders the DEEP COBALT token: G of #1F3A96");
            Assert.AreEqual(cobaltDeep.b, rendered.b, 1f / 255f, "chip renders the DEEP COBALT token: B of #1F3A96");
            Assert.AreEqual(0x1F, Mathf.RoundToInt(rendered.r * 255f), "…and the token itself is still #1F3A96");
            Assert.AreEqual(0x3A, Mathf.RoundToInt(rendered.g * 255f), "…and the token itself is still #1F3A96");
            Assert.AreEqual(0x96, Mathf.RoundToInt(rendered.b * 255f), "…and the token itself is still #1F3A96");

            float lum = 0.2126f * rendered.r + 0.7152f * rendered.g + 0.0722f * rendered.b;
            Assert.Less(lum, 0.35f, "the price chip renders DARK (light text on it reads)");
            var ink = GameDriver.InkToken;
            float delta = Mathf.Abs(rendered.r - ink.r) + Mathf.Abs(rendered.g - ink.g)
                          + Mathf.Abs(rendered.b - ink.b);
            Assert.Greater(delta, 0.15f,
                "the chip is distinguishable from its own INK keyline (else the keyline is invisible)");

            // Plate is drawn BEHIND the text (lower sibling index → earlier in the draw order).
            Assert.Less(driver.CardPricePlate.transform.GetSiblingIndex(),
                        driver.CardPriceText.transform.GetSiblingIndex(),
                        "the plate renders behind the text");

            // Plate rect fully covers the text rect (world-space corners: [0]=bottom-left, [2]=top-right).
            var pc = new Vector3[4]; var tc = new Vector3[4];
            driver.CardPricePlate.rectTransform.GetWorldCorners(pc);
            driver.CardPriceText.rectTransform.GetWorldCorners(tc);
            Assert.LessOrEqual(pc[0].x, tc[0].x, "plate covers the text on the left");
            Assert.LessOrEqual(pc[0].y, tc[0].y, "plate covers the text on the bottom");
            Assert.GreaterOrEqual(pc[2].x, tc[2].x, "plate covers the text on the right");
            Assert.GreaterOrEqual(pc[2].y, tc[2].y, "plate covers the text on the top");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnergyHint_Shows_AndPauses_When_EnergyOpensAt25()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                              // → playing

            // §D: открытие энергии (25) поднимает МОДАЛЬНЫЙ экран новой шкалы, а не текстовый S5-хинт.
            bool sawEnergyModal = false;
            int guard = 0;
            while (guard++ < 12000 && driver.Game.State == GameState.Playing && !sawEnergyModal)
            {
                if (driver.SpecialModeShowing) { NewScaleTut.ClearSpecial(driver, fake); yield return null; continue; }
                if (driver.NewScaleShowing)
                {
                    if (driver.NewScaleKind == NewScale.Energy)
                    {
                        Assert.IsTrue(driver.Game.Paused, "the energy modal pauses the game (age/drains frozen)");
                        Assert.GreaterOrEqual(driver.Game.Age, 24f, "energy modal fires around age 25");
                        Assert.IsTrue(driver.Game.EnergyOpen, "energy scale opened");
                        Assert.AreEqual(GameDriver.EnergyTaskText, driver.NewScaleTaskText.text,
                            "…and it carries the canon energy task (host-content §4)");
                        sawEnergyModal = true;
                        break;
                    }
                    NewScaleTut.Clear(driver, fake);     // pass the earlier money/relations modals
                    yield return null;
                    continue;
                }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                fake.Fire(GameInput.MoneyTick);
                driver.Game.Tick(0.25f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3.5f)
                    fake.No();
            }

            Assert.IsTrue(sawEnergyModal, "the energy modal appeared when energy opened at 25");

            // Same-card liveness (founder Gate-2): the energy bar had the same one-card reveal lag as
            // the money pill — closing the screen must reveal the bar IMMEDIATELY, not one card later.
            NewScaleTut.Clear(driver, fake);             // выполнить условие настоящим дыханием
            Assert.IsFalse(driver.NewScaleShowing, "energy modal closed once the task was done");
            Assert.IsTrue(driver.EnergyGroup.activeSelf,
                "energy bar visible the moment the screen closes — no one-card lag");
            Assert.AreEqual(1f, ((RectTransform)driver.EnergyGroup.transform).localScale.x, 1e-3f,
                "…and back at its ordinary HUD size");

            Object.Destroy(go);
            yield return null;
        }

        // Deterministic age-walk deck: a neutral filler card for every year, so the money(18) / energy(25) /
        // health(30) age gates fire on a KNOWN schedule. The driver's own deck is sampled from the CSV with
        // `new System.Random()` on every boot (GameDriver.LoadGame), so a run that must reach the age-30
        // health hint was not guaranteed to get there — that is the flake this replaces. Same shape as the
        // other DebugReplaceGame decks in this suite (Starter → fillers).
        private static Game AgeWalkDeck()
        {
            var deck = new System.Collections.Generic.List<Card> { Starter() };
            for (int a = 2; a <= 60; a++) deck.Add(Plain("F" + a, a));
            return new Game(deck, coin: () => false);
        }

        [UnityTest]
        public IEnumerator HealthScreen_Dismiss_SameFrameChord_DoesNotLeak_Crank_Or_EnergyHold()
        {
            // The ENERGY_HOLD variant of the same-frame chord (skeptic HIGH), at the HEALTH hint (age 30)
            // where energy is open and money is bankable — so a leaked crank/hold would be observable.
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(AgeWalkDeck());      // seeded/deterministic walk to 30 (no CSV sampling)
            fake.Confirm();                              // → playing

            // Дойти до ЗДОРОВЬЯ (30). r3: здоровье объясняет ВХОДНОЙ ЭКРАН спецрежима, а не S5-подсказка.
            bool atHealth = false;
            int guard = 0;
            while (guard++ < 20000 && driver.Game.State == GameState.Playing && !atHealth)
            {
                if (driver.SpecialModeShowing)
                {
                    if (driver.SpecialModeKind == SpecialMode.Health) { atHealth = true; break; }
                    NewScaleTut.ClearSpecial(driver, fake); yield return null; continue;
                }
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                fake.Fire(GameInput.MoneyTick);
                driver.Game.Tick(0.25f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3.5f)
                    fake.No();
            }
            Assert.IsTrue(atHealth, "reached the health screen at 30");
            Assert.IsTrue(driver.Game.Paused, "health screen paused the game");
            Assert.IsTrue(driver.Game.EnergyOpen, "energy is open (drained below full) by 30");

            // Cap breathes while paused so a leaked crank WOULD land — proving the guard, not the cap.
            yield return new WaitForSeconds(0.3f);
            double money0 = driver.Game.Money;
            int energy0 = driver.Game.Scales.Energy;

            // The chord in source order: Confirm FIRST, then MoneyTick + EnergyHold the SAME frame.
            fake.Confirm();
            Assert.IsFalse(driver.SpecialModeShowing, "Enter (dev-эмуляция зелёной) сняла экран здоровья");
            fake.Fire(GameInput.MoneyTick);
            fake.Fire(GameInput.EnergyHold);
            Assert.AreEqual(money0, driver.Game.Money,
                "no crank leaked onto the dismiss frame (strong observable — cap was armed)");
            // Удержание — сигнал ВРЕМЕННОЙ, поэтому «протёк ли он» видно только на следующем такте: если бы
            // латч прошёл, батарея прыгнула бы вверх на ~4 % (реген 16 %/с), а не просела дренажом.
            driver.Game.Tick(0.25f);
            Assert.LessOrEqual(driver.Game.Scales.Energy, energy0,
                "no energy hold leaked onto the dismiss frame (same guarded path) — battery only drains");

            Object.Destroy(go);
            yield return null;
        }
    }
}
