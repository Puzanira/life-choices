using System;
using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Сам звуковой слой, без игры: клипы грузятся, вата садится и снимается по ступеням, лупы
    /// помечены лупами, рестарт чистит всё. Художественного суда тут нет — только структура.
    /// </summary>
    public class AudioLayerTests
    {
        private GameObject _go;

        private AudioLayer Boot()
        {
            _go = new GameObject("AudioLayerTest");
            var layer = _go.AddComponent<AudioLayer>();
            layer.DebugRecord = true;
            return layer;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        [UnityTest]
        public IEnumerator EveryManifestEvent_FindsItsClip()
        {
            var layer = Boot();
            yield return null;
            Assert.AreEqual(AudioCatalog.Map.Count, layer.LoadedClipCount,
                "каждое событие каталога обязано найти свой клип — битых путей нет (done contract §1)");
            foreach (SoundEvent e in Enum.GetValues(typeof(SoundEvent)))
                Assert.IsTrue(layer.ClipLoaded(e), "клип не загрузился: " + e);
        }

        [UnityTest]
        public IEnumerator PlayingEverySound_DoesNotThrow()
        {
            var layer = Boot();
            yield return null;
            foreach (SoundEvent e in Enum.GetValues(typeof(SoundEvent)))
                Assert.DoesNotThrow(() => layer.Play(e), "проигрывание не должно падать: " + e);
        }

        [UnityTest]
        public IEnumerator FiltersHang_OnFilteredSourcesOnly()
        {
            var layer = Boot();
            yield return null;
            Assert.GreaterOrEqual(layer.CascadesPerSource, 1, "лоу-пасс повесился хотя бы одним каскадом");
            // Фильтруемых источников ровно SFX-пул + три луп-канала; нефильтруемый пул фильтров не несёт.
            int filteredSources = AudioLayer.SfxVoices + 3;
            Assert.AreEqual(filteredSources * layer.CascadesPerSource, layer.DebugFilterCount,
                "фильтры висят ровно на фильтруемых источниках — пульс депрессии обязан остаться чистым");
            Assert.AreEqual(0, layer.DebugEnabledFilterCount, "вне депрессии фильтр выключен");
        }

        [UnityTest]
        public IEnumerator Depression_SitsInCotton_AndReturnsByFiveSteps()
        {
            var layer = Boot();
            yield return null;

            layer.EnterDepression();
            Assert.IsTrue(layer.DepressionActive);
            Assert.AreEqual(0, layer.DepressionHits, "вход = ступень ноль");
            Assert.AreEqual(500f, layer.CurrentCutoff, 0.01f, "вход — 500 Гц, «в вату» разом");
            Assert.AreEqual(layer.DebugFilterCount, layer.DebugEnabledFilterCount,
                "в депрессии фильтр включён на ВСЕХ фильтруемых источниках, включая музыку");

            float[] expected = { 900f, 1700f, 3200f, 6000f, DepressionMix.CutoffOff };
            for (int hit = 1; hit <= DepressionMix.Steps; hit++)
            {
                layer.SetDepressionHits(hit);
                Assert.AreEqual(expected[hit - 1], layer.CurrentCutoff, 0.01f,
                    "ступень " + hit + " возвращает мир не по спеке");
            }
            Assert.AreEqual(0, layer.DebugEnabledFilterCount, "пятое попадание снимает фильтр целиком");
        }

        [UnityTest]
        public IEnumerator ExitDepression_LiftsEverything()
        {
            var layer = Boot();
            yield return null;
            layer.EnterDepression();
            layer.SetDepressionHits(2);
            layer.ExitDepression();

            Assert.IsFalse(layer.DepressionActive);
            Assert.AreEqual(DepressionMix.CutoffOff, layer.CurrentCutoff, 0.01f);
            Assert.AreEqual(0, layer.DebugEnabledFilterCount, "«мир снова включили» — ни одного фильтра");
        }

        [UnityTest]
        public IEnumerator SetHits_IsInertWhenNotDepressed()
        {
            var layer = Boot();
            yield return null;
            layer.SetDepressionHits(0);   // никто не входил в депрессию
            Assert.IsFalse(layer.DepressionActive, "ступени без входа не должны включать вату");
            Assert.AreEqual(DepressionMix.CutoffOff, layer.CurrentCutoff, 0.01f);
        }

        [UnityTest]
        public IEnumerator Loops_CarryTheLoopFlag_AndTheirClip()
        {
            var layer = Boot();
            yield return null;

            layer.PlayLoop(SoundEvent.MusicTheme);
            layer.PlayLoop(SoundEvent.PhoneRing);
            layer.PlayLoop(SoundEvent.Impulse);

            foreach (var ch in new[] { LoopChannel.Music, LoopChannel.PhoneRing, LoopChannel.Impulse })
            {
                var src = layer.DebugLoopSource(ch);
                Assert.IsNotNull(src, "у канала есть источник: " + ch);
                Assert.IsTrue(src.loop, "канал обязан быть лупом (бесшовность): " + ch);
                Assert.IsNotNull(src.clip, "каналу назначен клип: " + ch);
            }
        }

        [UnityTest]
        public IEnumerator OneShots_AreNotLoops()
        {
            var layer = Boot();
            yield return null;
            // Обратная сторона того же инварианта: одноразовые события не имеют права зациклиться.
            Assert.IsFalse(AudioCatalog.Get(SoundEvent.AnswerYes).Loop);
            Assert.IsFalse(AudioCatalog.Get(SoundEvent.DepressionPulse).Loop);
            Assert.IsFalse(AudioCatalog.Get(SoundEvent.Opener).Loop);
        }

        [UnityTest]
        public IEnumerator StopLoop_StopsIt()
        {
            var layer = Boot();
            yield return null;
            layer.PlayLoop(SoundEvent.PhoneRing);
            layer.StopLoop(LoopChannel.PhoneRing);
            Assert.IsFalse(layer.DebugLoopSource(LoopChannel.PhoneRing).isPlaying,
                "рингтон обязан замолчать, когда окно звонка закрылось");
        }

        [UnityTest]
        public IEnumerator ResetAll_ClearsCotton_AndSilencesLoops()
        {
            var layer = Boot();
            yield return null;

            layer.PlayLoop(SoundEvent.MusicTheme);
            layer.PlayLoop(SoundEvent.PhoneRing);
            layer.EnterDepression();          // умерли прямо в депрессии, с играющим рингтоном
            layer.ResetAll();

            Assert.IsFalse(layer.DepressionActive, "рестарт снимает депрессию");
            Assert.AreEqual(DepressionMix.CutoffOff, layer.CurrentCutoff, 0.01f, "«застрявшей ваты» нет");
            Assert.AreEqual(0, layer.DebugEnabledFilterCount, "ни один фильтр не пережил рестарт");
            Assert.IsFalse(layer.DebugLoopSource(LoopChannel.Music).isPlaying, "тема остановлена");
            Assert.IsFalse(layer.DebugLoopSource(LoopChannel.PhoneRing).isPlaying, "рингтон остановлен");
        }

        [UnityTest]
        public IEnumerator NoQueue_WhileNobodyAsks()
        {
            var layer = Boot();
            yield return null;
            layer.DebugClear();
            for (int i = 0; i < 5; i++) yield return null;   // просто идут кадры
            CollectionAssert.IsEmpty(layer.DebugPlayed,
                "слой ничего не копит и не играет сам по себе — очереди у него нет по устройству");
        }
    }
}
