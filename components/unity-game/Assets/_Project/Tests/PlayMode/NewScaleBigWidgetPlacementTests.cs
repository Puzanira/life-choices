using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Крупная шкала §D-модалки ДЕЙСТВИТЕЛЬНО встаёт туда, куда её послали, — проверка не по формуле
    /// раскладки, а по настоящим матрицам Unity: берём точку HUD-бокса виджета внутри группы, гоняем её
    /// через TransformPoint и обратно в дизайн-пиксели канваса и сверяем с целевым боксом.
    ///
    /// Второй тест — регрессия на КОРЕНЬ дефекта, из-за которого батарея уезжала на ~104 px: раскладка
    /// замораживалась в пикселях канваса на момент «одалживания», а дети группы сидят на ДОЛЯХ канваса и
    /// пересчитываются на каждом layout-проходе. Стоило канвасу поменять размер уже ПОСЛЕ показа модалки
    /// (ровно это делает харнесс скриншотов, пинная его на 1920×1080), как одно ехало, а другое нет.
    /// Поэтому здесь канвас РЕСАЙЗИТСЯ после показа — и позиция обязана остаться прежней.
    /// </summary>
    public class NewScaleBigWidgetPlacementTests
    {
        private static readonly NewScale[] All =
            { NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child };

        /// <summary>Где сейчас лежит точка <paramref name="designPoint"/> HUD-бокса виджета — в дизайн-пикселях.</summary>
        private static Vector2 DesignPositionOf(GameDriver driver, RectTransform widget, Vector2 designPoint)
        {
            var canvas = driver.CanvasRect;
            float cw = canvas.rect.width, ch = canvas.rect.height;
            // Дети группы посажены AnchorPx — долями канваса от ЦЕНТРА группы (пивот 0.5, 0.5).
            var local = new Vector3((designPoint.x / 1920f - 0.5f) * cw,
                                    (0.5f - designPoint.y / 1080f) * ch, 0f);
            var world = widget.TransformPoint(local);
            var back = canvas.InverseTransformPoint(world);
            return new Vector2(back.x / cw * 1920f + 960f, 540f - back.y / ch * 1080f);
        }

        private static IEnumerator ShowModal(NewScale which, System.Action<GameDriver> body)
        {
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            driver.Input = new PlayFakeInputSource();
            yield return null;                       // Start строит канвас и подписки

            driver.DebugPreviewNewScale(which);
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.IsTrue(driver.NewScaleShowing, which + ": модалка поднята");
            Assert.IsNotNull(driver.NewScaleBigWidget, which + ": виджет одолжен модалке");

            body(driver);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BigWidget_LandsOnItsTargetBox([ValueSource(nameof(All))] NewScale which)
        {
            yield return ShowModal(which, driver =>
            {
                var src = GameDriver.BigScaleSrc(which);
                var dst = GameDriver.BigScaleDst(which);
                var rt = (RectTransform)driver.NewScaleBigWidget.transform;

                var got = DesignPositionOf(driver, rt, new Vector2(src.x, src.y));
                Assert.AreEqual(dst.x, got.x, 1f, $"{which}: центр крупной шкалы по X (ждали {dst.x:0.#}, вышло {got.x:0.#})");
                Assert.AreEqual(dst.y, got.y, 1f, $"{which}: центр крупной шкалы по Y (ждали {dst.y:0.#}, вышло {got.y:0.#})");

                float k = GameDriver.BigScaleFactor(which);
                Assert.AreEqual(k, rt.localScale.x, 1e-3f, which + ": масштаб виджета");
                Assert.AreEqual(k, rt.localScale.y, 1e-3f, which + ": масштаб виджета по Y");
            });
        }

        /// <summary>
        /// ДОЛГ ГЕЙТА 2026-08-05: правая колонка модалки денег — бейдж возраста НАД крупной банкой —
        /// стояла двумя несоосными наклейками. Бейдж на месте (он живой HUD на эталонном боксе), банка
        /// выровнена по его оси. Считаем по НАРИСОВАННЫМ фигурам, а не по ректам.
        /// </summary>
        [UnityTest]
        public IEnumerator MoneyModal_BigJar_ShareTheAgeBadgeAxis()
        {
            yield return ShowModal(NewScale.Money, driver =>
            {
                var dst = GameDriver.BigScaleDst(NewScale.Money);
                var badge = GameDriver.AgeBadgeDrawnBox;
                Assert.AreEqual(badge.x, dst.x, 4f,
                    $"ось крупной банки совпадает с осью бейджа возраста (бейдж {badge.x:0.#}, банка {dst.x:0.#})");

                // …и это действительно та ось, на которой стоит НАСТОЯЩИЙ бейдж в кадре.
                var badgeRt = driver.AgeBadgeImage.rectTransform;
                Assert.AreEqual(badge.x, badgeRt.anchorMin.x * 1920f, 6f,
                    "бейдж возраста не двигали — он на своём эталонном месте");
            });
        }

        [UnityTest]
        public IEnumerator BigWidget_SurvivesACanvasResize_AfterTheModalIsUp(
            [ValueSource(nameof(All))] NewScale which)
        {
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            driver.Input = new PlayFakeInputSource();
            yield return null;

            driver.DebugPreviewNewScale(which);
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;

            var src = GameDriver.BigScaleSrc(which);
            var dst = GameDriver.BigScaleDst(which);
            var rt = (RectTransform)driver.NewScaleBigWidget.transform;
            float sizeBefore = driver.CanvasRect.rect.width;

            // …и ТЕПЕРЬ канвас меняет размер — ровно как в харнессе скриншотов, который пиннит его на
            // 1920×1080 уже после показа модалки. Раскладка обязана пережить это без сдвига.
            var scaler = driver.CanvasRect.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.Greater(Mathf.Abs(sizeBefore - driver.CanvasRect.rect.width), 0.5f,
                which + ": канвас в этом прогоне действительно поменял размер (иначе тест ничего не ловит)");

            var got = DesignPositionOf(driver, rt, new Vector2(src.x, src.y));
            Assert.AreEqual(dst.x, got.x, 1f,
                $"{which}: после ресайза канваса крупная шкала осталась на месте по X (ждали {dst.x:0.#}, вышло {got.x:0.#})");
            Assert.AreEqual(dst.y, got.y, 1f,
                $"{which}: после ресайза канваса — по Y (ждали {dst.y:0.#}, вышло {got.y:0.#})");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>Закрытие модалки возвращает виджет в HUD ровно туда, откуда его взяли.</summary>
        [UnityTest]
        public IEnumerator ClosingTheModal_PutsTheWidgetBackWhereItWas()
        {
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            var fake = new PlayFakeInputSource();
            driver.Input = fake;
            yield return null;

            var rt = (RectTransform)driver.EnergyGroup.transform;
            var anchorMin = rt.anchorMin;
            var anchorMax = rt.anchorMax;
            var offsetMin = rt.offsetMin;
            var offsetMax = rt.offsetMax;
            var scale = rt.localScale;
            var parent = rt.parent;

            // Живая жизнь до открытия энергии (25): модалка должна прийти сама, тем же путём, что у игрока.
            fake.Confirm();                              // → playing
            bool sawEnergyModal = false;
            int guard = 0;
            while (guard++ < 12000 && driver.Game.State == GameState.Playing && !sawEnergyModal)
            {
                if (driver.NewScaleShowing)
                {
                    if (driver.NewScaleKind == NewScale.Energy) { sawEnergyModal = true; break; }
                    NewScaleTut.Clear(driver, fake);     // пропустить более ранние модалки денег/отношений
                    yield return null;
                    continue;
                }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                fake.Fire(GameInput.MoneyTick);
                driver.Game.Tick(0.25f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3.5f) fake.No();
            }
            Assert.IsTrue(sawEnergyModal, "энергия открылась и подняла модалку");
            Assert.AreNotEqual(scale.x, rt.localScale.x, "виджет действительно увеличился на модалке");

            NewScaleTut.Clear(driver, fake);          // выполнить условие настоящим дыханием
            Assert.IsFalse(driver.NewScaleShowing, "модалка закрылась по выполненному условию");
            yield return null;

            Assert.AreEqual(parent, rt.parent, "виджет вернулся к своему родителю в HUD");
            Assert.AreEqual(anchorMin, rt.anchorMin, "…с прежним anchorMin");
            Assert.AreEqual(anchorMax, rt.anchorMax, "…с прежним anchorMax");
            Assert.AreEqual(offsetMin, rt.offsetMin, "…с прежним offsetMin");
            Assert.AreEqual(offsetMax, rt.offsetMax, "…с прежним offsetMax");
            Assert.AreEqual(scale, rt.localScale, "…и с прежним масштабом");

            Object.Destroy(go);
            yield return null;
        }
    }
}
