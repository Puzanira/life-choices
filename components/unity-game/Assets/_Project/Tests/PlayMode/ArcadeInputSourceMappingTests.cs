using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AiGameStudio.ArcadeControls;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Physical→semantic mapping guards for <see cref="ArcadeInputSource"/>, driven end-to-end through the
    /// arcade-controls package's own <see cref="FakeBackend"/> (snapshot → ArcadeInput → source → GameInput).
    /// One test per control, and every test is ONE-HOT: injecting a single control must emit ONLY its own
    /// <see cref="GameInput"/> value(s) — any cross-talk (a button leaking a foreign event) fails the exact
    /// received-list assertion. Replaces the retired keyboard-mapping tests at the new integration boundary.
    /// </summary>
    public class ArcadeInputSourceMappingTests
    {
        private GameObject _rig;       // ArcadeInputRunner + FakeBackend (pumps ArcadeInput each frame)
        private GameObject _sourceGo;  // the ArcadeInputSource under test
        private FakeBackend _fake;
        private List<GameInput> _received;

        private IEnumerator Boot()
        {
            _fake = new FakeBackend();
            _rig = new GameObject("Rig");
            _rig.SetActive(false);
            _rig.AddComponent<ArcadeInputRunner>().BackendOverride = _fake;   // injected before Awake
            _rig.SetActive(true);                                             // Initialize(fake)

            _sourceGo = new GameObject("Source");
            var src = _sourceGo.AddComponent<ArcadeInputSource>();            // finds the rig's runner
            _received = new List<GameInput>();
            src.Received += i => _received.Add(i);

            // Settle two zero-snapshot frames so every control starts released/at-rest, then start clean.
            yield return null;
            yield return null;
            _received.Clear();
        }

        private IEnumerator Cleanup()
        {
            Object.Destroy(_sourceGo);
            Object.Destroy(_rig);
            yield return null;   // let Destroy complete so the next test's FindAnyObjectByType is clean
        }

        private IEnumerator Hold(BackendSnapshot snap, int frames = 4)
        {
            _fake.Next = snap;
            for (int i = 0; i < frames; i++) yield return null;
        }

        private IEnumerator Release(int frames = 3)
        {
            _fake.Next = default;
            for (int i = 0; i < frames; i++) yield return null;
        }

        // ---- buttons: one edge per press, one-hot ----------------------------------------------------

        [UnityTest]
        public IEnumerator GreenButton_Emits_AnswerYes_Once_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { GreenHeld = true });   // held over several frames
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.AnswerYes }, _received,
                "GREEN = ДА: exactly one AnswerYes per press (edge, not autorepeat) and no foreign events");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator RedButton_Emits_AnswerNo_Once_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { RedHeld = true });
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.AnswerNo }, _received,
                "RED = СПАСИБО НЕ НАДО: exactly one AnswerNo per press and no foreign events");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator MenuButton_Emits_Exit_Once_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { MenuHeld = true });
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.Exit }, _received,
                "MENU = выход: exactly one Exit per press and no foreign events");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator BangButton_Emits_ChildPress_Once_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { BangHeld = true });
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.ChildPress }, _received,
                "BANG = кнопка ребёнка: exactly one ChildPress per press and no foreign events");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Button_Repress_Emits_A_Second_Edge()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { GreenHeld = true });
            yield return Release();
            yield return Hold(new BackendSnapshot { GreenHeld = true });
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.AnswerYes, GameInput.AnswerYes }, _received,
                "release + repress = a second clean edge");
            yield return Cleanup();
        }

        // ---- crank → MoneyTick ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Crank_Rotation_Emits_Only_MoneyTicks()
        {
            yield return Boot();
            // One full degreesPerMoneyTick (12°) per poll while held → at least one tick, and NOTHING else.
            yield return Hold(new BackendSnapshot { CrankDeltaDegrees = 12f }, frames: 4);
            yield return Release();
            Assert.GreaterOrEqual(_received.Count, 1, "rotation produced at least one MoneyTick");
            Assert.IsTrue(_received.All(i => i == GameInput.MoneyTick),
                $"crank emits ONLY MoneyTick (got: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Crank_Small_Deltas_Accumulate_Into_Ticks()
        {
            yield return Boot();
            // 4° per poll — under the 12° threshold each frame, but 10 polls accumulate ≥ 40° → ticks.
            yield return Hold(new BackendSnapshot { CrankDeltaDegrees = 4f }, frames: 10);
            yield return Release();
            Assert.GreaterOrEqual(_received.Count, 1, "sub-threshold deltas accumulate into MoneyTicks");
            Assert.IsTrue(_received.All(i => i == GameInput.MoneyTick),
                $"accumulated crank emits ONLY MoneyTick (got: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        // ---- height sensor (HeightA) → HELD EnergyHold -------------------------------------------------

        [UnityTest]
        public IEnumerator HeightA_Held_Reemits_EnergyHold_Every_Frame_And_Nothing_Else()
        {
            // ⚠ РЕДИЗАЙН 2026-08-07: датчик — УДЕРЖИВАЕМЫЙ контрол, как и джойстик отношений. Пока он выше
            // середины хода, источник переиздаёт EnergyHold КАЖДЫЙ кадр (раньше выдавался один импульс на
            // подъём — под механику ритма, которой больше нет).
            yield return Boot();
            yield return Hold(new BackendSnapshot { HeightA = 0.8f }, frames: 6);
            Assert.GreaterOrEqual(_received.Count, 5,
                "поднятый датчик — HELD-сигнал: он переиздаётся каждый кадр, а не один раз на подъём");
            Assert.IsTrue(_received.All(i => i == GameInput.EnergyHold),
                $"…и ничего, кроме EnergyHold (получено: {string.Join(",", _received)})");

            // Опустили ниже порога отпускания — сигнал ГАСНЕТ, новых событий нет.
            yield return Release();
            int afterRelease = _received.Count;
            yield return Hold(new BackendSnapshot { HeightA = 0.2f }, frames: 4);   // ниже High и ниже Low
            Assert.AreEqual(afterRelease, _received.Count,
                "опущенный датчик не шлёт ничего — рост энергии обязан прекращаться");

            // Подняли снова — сигнал вернулся сам, без всякого «перевзвода».
            yield return Hold(new BackendSnapshot { HeightA = 0.8f }, frames: 4);
            Assert.Greater(_received.Count, afterRelease, "подняли снова — сигнал снова идёт");
            Assert.IsTrue(_received.All(i => i == GameInput.EnergyHold), "…и по-прежнему только EnergyHold");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator HeightA_BetweenTheHysteresisThresholds_DoesNotChatter()
        {
            // Гистерезис: значение между Low и High не переключает состояние. Поднялись выше High — держим;
            // просели до 0.4 (ниже High, но выше Low) — сигнал ДЕРЖИТСЯ (дрожание руки не рвёт удержание).
            yield return Boot();
            yield return Hold(new BackendSnapshot { HeightA = 0.8f }, frames: 3);
            int held = _received.Count;
            Assert.Greater(held, 0, "датчик поднят");
            yield return Hold(new BackendSnapshot { HeightA = 0.4f }, frames: 4);   // Low < 0.4 < High
            Assert.Greater(_received.Count, held,
                "между порогами сигнал НЕ рвётся — иначе дрожь руки мигала бы батареей");
            yield return Cleanup();
        }

        // ---- joystick vertical → relationship balancer -------------------------------------------------

        [UnityTest]
        public IEnumerator Joystick_Up_Reemits_RelationUp_Every_Frame_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { Joystick = new Vector2(0f, 1f) }, frames: 4);
            yield return Release();
            Assert.GreaterOrEqual(_received.Count, 2, "a HELD axis re-emits every frame (not a single edge)");
            Assert.IsTrue(_received.All(i => i == GameInput.RelationUp),
                $"joystick up emits ONLY RelationUp (got: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Joystick_Down_Reemits_RelationDown_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { Joystick = new Vector2(0f, -1f) }, frames: 4);
            yield return Release();
            Assert.GreaterOrEqual(_received.Count, 2, "a HELD axis re-emits every frame");
            Assert.IsTrue(_received.All(i => i == GameInput.RelationDown),
                $"joystick down emits ONLY RelationDown (got: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Joystick_Inside_Deadzone_Emits_Nothing()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { Joystick = new Vector2(0f, 0.3f) }, frames: 4);
            yield return Release();
            CollectionAssert.IsEmpty(_received, "a wobble inside the deadzone must not pull the balancer");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Zero_Snapshot_Emits_Nothing()
        {
            yield return Boot();
            yield return Hold(default, frames: 5);
            CollectionAssert.IsEmpty(_received, "an idle cabinet emits no GameInput at all");
            yield return Cleanup();
        }
    }
}
