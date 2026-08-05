using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Раскладка КРУПНОЙ шкалы на §D-модалке. Это ДОБАВКА к существующим тестам экрана: они проверяют
    /// поведение (пауза, условия выхода, канон-тексты), а этот — геометрию, которую до появления кадров
    /// никто не проверял и которая молча разъехалась (батарея промахивалась мимо эталона на ~104 px,
    /// банка вылезала за правый край, трубка — за левый).
    ///
    /// Договор, против которого считаем:
    ///  • крупный виджет ЦЕЛИКОМ в кадре 1920×1080 с полем ≥16 px — намеренных выходов за край не осталось;
    ///  • он не накрывает ТЕКСТ ни окна-рассказа, ни окна-задачи и не налезает на бейдж возраста;
    ///  • он КРУПНЕЕ своего HUD-виджета и подобен ему (масштаб одинаков по осям);
    ///  • энергия дополнительно ложится в ЭТАЛОННЫЙ бокс (±10): внутренняя кромка обводки батареи
    ///    в x 207…412, y 145…546 — «Экран - появление новой шкалы.png» нарисован именно для энергии.
    ///
    /// Про кремовые ПОЛЯ (не текст): деньги и отношения разведены с ними полностью; энергия и ребёнок —
    /// нет, и это НАМЕРЕННО. У энергии так на самом эталоне (батарея перекрывает левые лучи плашки, см.
    /// комментарий к BuildNewScaleOverlay), а у ребёнка иначе не выходит арифметически: кремовое поле
    /// задачи начинается на x=393, значит «целиком слева от него» дало бы трубке ширину ≤377 px против
    /// 411 px её же HUD-размера — то есть УМЕНЬШЕНИЕ. Поэтому для этих двух граница — текст, а не плашка.
    /// </summary>
    public class NewScaleBigWidgetLayoutTests
    {
        private const float W = 1920f, H = 1080f;

        private static readonly NewScale[] All =
            { NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child };

        // Vector4(cx, cy-от-верха, w, h) → Rect в экранных координатах с y вниз.
        private static Rect R(Vector4 b) => new(b.x - b.z / 2f, b.y - b.w / 2f, b.z, b.w);

        private static string Fmt(Rect r) =>
            $"x {r.xMin:0.#}…{r.xMax:0.#}, y {r.yMin:0.#}…{r.yMax:0.#} ({r.width:0.#}×{r.height:0.#})";

        [Test]
        public void BigWidget_IsWhollyOnScreen_WithMargin([ValueSource(nameof(All))] NewScale s)
        {
            var d = R(GameDriver.BigScaleDst(s));
            float m = GameDriver.BigScaleFrameMargin;
            Assert.GreaterOrEqual(d.xMin, m, $"{s}: крупная шкала не заходит за левый край — {Fmt(d)}");
            Assert.GreaterOrEqual(d.yMin, m, $"{s}: …за верхний край — {Fmt(d)}");
            Assert.LessOrEqual(d.xMax, W - m, $"{s}: …за правый край — {Fmt(d)}");
            Assert.LessOrEqual(d.yMax, H - m, $"{s}: …за нижний край — {Fmt(d)}");
        }

        [Test]
        public void BigWidget_CoversNeitherWindowText_NorTheAgeBadge([ValueSource(nameof(All))] NewScale s)
        {
            var d = R(GameDriver.BigScaleDst(s));
            foreach (var (name, box) in new[]
                     {
                         ("текст окна-задачи", GameDriver.TaskTextBox),
                         ("текст окна-рассказа", GameDriver.StoryTextBox),
                         ("бейдж возраста", GameDriver.AgeBadgeBox),
                     })
            {
                var o = R(box);
                Assert.IsFalse(d.Overlaps(o),
                    $"{s}: крупная шкала {Fmt(d)} не должна накрывать {name} {Fmt(o)}");
            }
        }

        [Test]
        public void BigWidget_ClearsTheCreamPlates_WhereItCan(
            [Values(NewScale.Money, NewScale.Relations)] NewScale s)
        {
            var d = R(GameDriver.BigScaleDst(s));
            foreach (var (name, box) in new[]
                     {
                         ("кремовое поле задачи", GameDriver.TaskFieldRect),
                         ("кремовое поле рассказа", GameDriver.StoryFieldRect),
                     })
            {
                var o = R(box);
                Assert.IsFalse(d.Overlaps(o),
                    $"{s}: крупная шкала {Fmt(d)} разведена с {name} {Fmt(o)}");
            }
        }

        [Test]
        public void BigWidget_IsBiggerThanItsHudWidget_AndKeepsItsShape([ValueSource(nameof(All))] NewScale s)
        {
            var src = GameDriver.BigScaleSrc(s);
            var dst = GameDriver.BigScaleDst(s);
            Assert.Greater(src.z, 0f, s + ": HUD-бокс источника задан");

            float kx = dst.z / src.z, ky = dst.w / src.w;
            Assert.Greater(kx, 1f, $"{s}: копия КРУПНЕЕ HUD-виджета (k={kx:0.###})");
            Assert.AreEqual(kx, ky, 0.01f * kx,
                $"{s}: масштаб одинаков по осям — фигуру не плющит (kx={kx:0.###}, ky={ky:0.###})");
            Assert.AreEqual(kx, GameDriver.BigScaleFactor(s), 1e-4f, s + ": BigScaleFactor согласован");
        }

        /// <summary>
        /// Энергия против эталона. Считаем не по целевому боксу виджета, а по той самой фигуре, которую
        /// мерили на эталоне, — внутренней кромке обводки батареи: она едет вместе с виджетом (тот же
        /// масштаб, то же смещение), поэтому её место выводится из src/dst и сверяется с эталоном ±10.
        /// </summary>
        [Test]
        public void Energy_InnerStrokeBox_MatchesTheReference_Within10px()
        {
            var src = GameDriver.BigScaleSrc(NewScale.Energy);
            var dst = GameDriver.BigScaleDst(NewScale.Energy);
            var inner = GameDriver.BatteryInnerStrokeSrc;
            float k = dst.z / src.z;

            var got = new Vector4(
                dst.x + k * (inner.x - src.x),
                dst.y + k * (inner.y - src.y),
                inner.z * k,
                inner.w * k);
            var want = GameDriver.BatteryInnerStrokeRef;

            var g = R(got);
            var e = R(want);
            Assert.AreEqual(e.xMin, g.xMin, 10f, $"энергия: левая кромка обводки. Получили {Fmt(g)}, эталон {Fmt(e)}");
            Assert.AreEqual(e.xMax, g.xMax, 10f, $"энергия: правая кромка. Получили {Fmt(g)}, эталон {Fmt(e)}");
            Assert.AreEqual(e.yMin, g.yMin, 10f, $"энергия: верхняя кромка. Получили {Fmt(g)}, эталон {Fmt(e)}");
            Assert.AreEqual(e.yMax, g.yMax, 10f, $"энергия: нижняя кромка. Получили {Fmt(g)}, эталон {Fmt(e)}");
        }

        /// <summary>Крупная шкала стоит У СВОЕГО HUD-места: сдвиг центра не уводит виджет на чужую половину
        /// экрана (батарея и трубка остаются слева, банка — справа, бар отношений — в верхней половине).</summary>
        [Test]
        public void BigWidget_StaysOnItsOwnSideOfTheScreen()
        {
            Assert.Less(GameDriver.BigScaleDst(NewScale.Energy).x, W / 2f, "батарея слева");
            Assert.Less(GameDriver.BigScaleDst(NewScale.Child).x, W / 2f, "трубка слева");
            Assert.Greater(GameDriver.BigScaleDst(NewScale.Money).x, W / 2f, "банка справа");
            Assert.Less(GameDriver.BigScaleDst(NewScale.Relations).y, H / 2f, "бар отношений сверху");
        }
    }
}
