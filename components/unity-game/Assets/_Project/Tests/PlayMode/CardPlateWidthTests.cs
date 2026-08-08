using System.Collections;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using AiGameStudio.ArcadeControls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// ГАРД ШИРИНЫ ПЛАШКИ КАРТОЧКИ (отрезки 1–7, done contract §6). Колода выросла 85 → 253, и вопрос
    /// карточки — единственный текст, который дизайнер пишет свободной строкой, без счётчика символов.
    /// Плашка его АВТОМАТИЧЕСКИ ужимает (<c>resizeTextForBestFit</c>, 30…64), а переполнение стоит на
    /// <c>Truncate</c>, поэтому слишком длинный вопрос не «вылезет» заметно — он молча ОБРЕЖЕТСЯ. Ровно
    /// та же ловушка, что и с некрологом: тест обязан смотреть на ВИДИМЫЕ символы, а не на activeSelf.
    ///
    /// Проверяется САМЫЙ ДЛИННЫЙ вопрос ЖИВОЙ колоды (не зашитый список): дописали карточку длиннее
    /// прежнего рекорда — этот тест её и подхватит.
    /// </summary>
    public class CardPlateWidthTests
    {
        /// <summary>
        /// Пол читаемости. Кабинетный канвас масштабируется, поэтому смысл имеет не «сколько пикселей», а
        /// «не упёрлись ли в нижнюю границу best-fit»: если подобранный размер лёг на
        /// <c>resizeTextMinSize</c>, значит ужимать больше некуда и следующая карточка уже обрежется.
        /// </summary>
        private const int MinReadableFontSize = 31;   // resizeTextMinSize = 30 → на самом полу быть нельзя

        private static string[] LongestQuestions(int take)
        {
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "живая колода читается из Resources");
            return CardLoader.ParseAll(csv.text)
                             .Select(c => c.Question)
                             .Where(q => !string.IsNullOrEmpty(q))
                             .Distinct()
                             .OrderByDescending(q => q.Length)
                             .Take(take)
                             .ToArray();
        }

        [UnityTest]
        public IEnumerator LongestQuestionsOfTheLiveDeck_FitThePlate_WithoutTruncation()
        {
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            driver.Input = new PlayFakeInputSource();
            ArcadeInput.Initialize(new KeyboardBackend(KeyboardMapping.LoadDefault()));
            yield return null;

            driver.DebugPreviewArcadeShot();

            // КАНВАС — В МАСШТАБ КАБИНЕТА. Без этого best-fit считается в пикселях batch-окна, а не
            // эталонного кадра, и `fontSizeUsedForBestFit` возвращает число из другой системы координат
            // (замер: 24 вместо ≥30 при `resizeTextMinSize` = 30). Та же оговорка, что у гарда финала.
            var scaler = driver.CanvasRect.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = GameDriver.CabinetCanvasScale;
            scaler.referencePixelsPerUnit = 100f;
            yield return null;
            foreach (var t in driver.GetComponentsInChildren<Text>(true)) { t.FontTextureChanged(); t.SetAllDirty(); }
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.AreEqual(GameDriver.CabinetCanvasScale,
                driver.CanvasRect.GetComponent<Canvas>().scaleFactor, 0.001f,
                "канвас действительно встал в масштаб кабинета (иначе проверка ничего не значит)");

            var text = driver.CardRect.Find("CardText").GetComponent<Text>();
            Assert.IsNotNull(text, "плашка карточки построена");

            int worst = int.MaxValue;
            foreach (var q in LongestQuestions(5))
            {
                text.text = q;
                Canvas.ForceUpdateCanvases();
                yield return null;

                var gen = text.cachedTextGenerator;
                Assert.AreEqual(q.Length, gen.characterCountVisible,
                    $"вопрос «{q}» ({q.Length} симв.) обрезан плашкой: видно {gen.characterCountVisible}");

                int fitted = gen.fontSizeUsedForBestFit;
                worst = Mathf.Min(worst, fitted);
                Assert.GreaterOrEqual(fitted, MinReadableFontSize,
                    $"вопрос «{q}» ужат до {fitted} — best-fit упёрся в пол, следующая длиннее уже обрежется");

                var rect = text.rectTransform.rect;
                Assert.LessOrEqual(gen.rectExtents.size.y, rect.height + 1f,
                    $"вопрос «{q}» не помещается в плашку по ВЫСОТЕ");
                Assert.LessOrEqual(gen.rectExtents.size.x, rect.width + 1f,
                    $"вопрос «{q}» не помещается в плашку по ШИРИНЕ");
            }

            // Одна строка в отчёт: сколько запаса у самого длинного вопроса колоды. 64 = потолок
            // best-fit, то есть ужимать не пришлось вообще.
            Debug.Log($"[card-fit] худший из пяти длиннейших вопросов набран {worst}px "
                + $"(потолок {text.resizeTextMaxSize}, пол {text.resizeTextMinSize})");
            Object.DestroyImmediate(go);
        }
    }
}
