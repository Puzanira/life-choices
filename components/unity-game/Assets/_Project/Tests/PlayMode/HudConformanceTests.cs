using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Layer-2 HUD-conformance guards for the ART-PACK row (design gate: `explainers/Экран спокойный
    /// обычный.png` is pixel-truth, boxes from `design-tasks/2026-07-29-hud-asset-map.md` §2/§8).
    ///
    /// These assert the RENDERED result, not activeSelf — and specifically the DRAWN ARTWORK, not the
    /// RectTransform: every art-pack PNG carries a transparent margin, so an Image/Simple whose rect equals
    /// the measured box would draw the picture too small. Each check therefore takes the live rect, shrinks
    /// it by that sprite's own alpha-tight fraction (measured on the imported PNG) and compares the result
    /// to the asset map's measured box within ±6 px — the increment's done-contract tolerance.
    /// </summary>
    public class HudConformanceTests
    {
        /// <summary>Done-contract §6: every element in its measured box ±6 px.</summary>
        private const float MapTol = 6f;

        // ---- sprite geometry, measured on the imported PNGs (asset-map §1) ----------------------------
        // bx,by,bw,bh = alpha-tight bbox inside a tw×th texture; then the element's measured DRAWN box on
        // the 1920×1080 reference (asset-map §2).
        private readonly struct ArtBox
        {
            public readonly float Bx, By, Bw, Bh, Tw, Th, X, Y, W, H;
            public readonly string What;
            public ArtBox(string what, float bx, float by, float bw, float bh, float tw, float th,
                float x, float y, float w, float h)
            { What = what; Bx = bx; By = by; Bw = bw; Bh = bh; Tw = tw; Th = th; X = x; Y = y; W = w; H = h; }
        }

        private static readonly ArtBox Battery =
            new("энергия (батарея)", 18, 21, 379, 859, 420, 910, 127, 58, 114, 218);
        private static readonly ArtBox RelBar =
            new("отношения (бар)", 18, 11, 1136, 286, 1172, 309, 413, 35, 523, 132);
        private static readonly ArtBox HealthBar =
            new("здоровье (бар)", 18, 11, 1136, 286, 1172, 309, 1042, 38, 504, 127);
        // Jar box = the EXPLAINER, not the asset-map §2 row (design gate round 2: the table is wrong here).
        private static readonly ArtBox MoneyJar =
            new("деньги (банка)", 126, 103, 772, 835, 1024, 1024, 1664, 65, 173, 185);
        private static readonly ArtBox Coin =
            new("монета", 1, 0, 858, 868, 860, 869, 1714, 6, 68, 68);
        private static readonly ArtBox AgeBadge =
            new("возраст (бейдж)", 35, 63, 947, 923, 1024, 1024, 1639, 392, 212, 207);
        private static readonly ArtBox CardPlate =
            new("карточка-вопрос", 34, 26, 1468, 968, 1536, 1024, 413, 229, 1093, 721);

        private static GameDriver BootToAdult(out GameObject go)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            driver.Input = new PlayFakeInputSource();
            return driver;
        }

        // The rect's OWN four corners in the canvas' local space (excludes children).
        private static Bounds OwnBounds(RectTransform canvas, RectTransform rt)
        {
            var wc = new Vector3[4];
            rt.GetWorldCorners(wc);
            var b = new Bounds(canvas.InverseTransformPoint(wc[0]), Vector3.zero);
            for (int i = 1; i < 4; i++) b.Encapsulate(canvas.InverseTransformPoint(wc[i]));
            return b;
        }

        /// <summary>Canvas-local point → 1920×1080 reference px (x from LEFT, y from TOP).</summary>
        /// <remarks>
        /// POSITIONS only. A batch game view is not 16:9, so the CanvasScaler leaves canvas.rect at something
        /// other than 1920×1080 — anchor-driven POSITIONS scale with it while sizeDelta-driven SIZES do not.
        /// So: map centres through here, and take extents straight off `rect.width/height` (already reference
        /// px by construction). Mixing the two — converting a centre±halfSize point — silently mis-scales.
        /// </remarks>
        private static Vector2 ToReference(RectTransform canvas, Vector3 local)
            => new Vector2((local.x / canvas.rect.width + 0.5f) * 1920f,
                           (0.5f - local.y / canvas.rect.height) * 1080f);

        /// <summary>Centre of a rect in reference px (x from LEFT, y from TOP).</summary>
        private static Vector2 RefCentre(RectTransform canvas, RectTransform rt)
            => ToReference(canvas, canvas.InverseTransformPoint(rt.position));

        /// <summary>Rect as a reference-px box (left, top, right, bottom) — centre mapped, extents raw.</summary>
        private static (float L, float T, float R, float B) RefBox(RectTransform canvas, RectTransform rt)
        {
            var c = RefCentre(canvas, rt);
            float hw = rt.rect.width / 2f, hh = rt.rect.height / 2f;
            return (c.x - hw, c.y - hh, c.x + hw, c.y + hh);
        }

        /// <summary>The DRAWN picture inside <paramref name="rt"/> as a reference-px box (L, T, R, B).</summary>
        private static (float L, float T, float R, float B) DrawnRefBox(
            RectTransform canvas, RectTransform rt, in ArtBox a)
        {
            // drawn size = rect size × the sprite's alpha-tight fraction
            float w = rt.rect.width * a.Bw / a.Tw;
            float h = rt.rect.height * a.Bh / a.Th;
            // drawn centre: the art's centre sits at this fraction of the rect, x from left / y from top.
            // Rect centre is mapped through the canvas; the offset inside the rect is raw reference px.
            float fx = (a.Bx + a.Bw / 2f) / a.Tw;
            float fy = (a.By + a.Bh / 2f) / a.Th;
            var rectCentre = RefCentre(canvas, rt);
            float cx = rectCentre.x + (fx - 0.5f) * rt.rect.width;
            float cy = rectCentre.y + (fy - 0.5f) * rt.rect.height;
            return (cx - w / 2f, cy - h / 2f, cx + w / 2f, cy + h / 2f);
        }

        /// <summary>
        /// The core art-pack guard: the DRAWN picture of <paramref name="rt"/> lands on the asset map's
        /// measured box within ±6 px, in both size and position. Size comes from the rect's own local size
        /// (rotation-independent), position from the art's centre inside the rect mapped back to reference px.
        /// </summary>
        private static void AssertDrawnBox(RectTransform canvas, RectTransform rt, in ArtBox a)
        {
            var (l, t, r, b) = DrawnRefBox(canvas, rt, a);
            Assert.AreEqual(a.W, r - l, MapTol, a.What + ": drawn WIDTH matches asset-map §2");
            Assert.AreEqual(a.H, b - t, MapTol, a.What + ": drawn HEIGHT matches asset-map §2");
            Assert.AreEqual(a.X + a.W / 2f, (l + r) / 2f, MapTol,
                a.What + ": drawn centre-X matches asset-map §2");
            Assert.AreEqual(a.Y + a.H / 2f, (t + b) / 2f, MapTol,
                a.What + ": drawn centre-Y matches asset-map §2");
        }

        private static bool Overlap(Bounds a, Bounds b)
            => a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y;

        /// <summary>Two reference-px boxes (L, T, R, B) intersect.</summary>
        private static bool RefOverlap((float L, float T, float R, float B) a,
                                       (float L, float T, float R, float B) b)
            => a.L < b.R && a.R > b.L && a.T < b.B && a.B > b.T;

        /// <summary>
        /// A TILTED rect's axis-aligned extent in reference px. Centre is mapped through the canvas (a
        /// position), the half-extents are computed from the rect's own size and z-rotation (ship px) —
        /// never from mapped corners, which would mis-scale in a non-16:9 batch game view.
        /// </summary>
        private static (float L, float T, float R, float B) RefAabb(RectTransform canvas, RectTransform rt)
        {
            var c = RefCentre(canvas, rt);
            float ang = rt.localEulerAngles.z * Mathf.Deg2Rad;
            float ca = Mathf.Abs(Mathf.Cos(ang)), sa = Mathf.Abs(Mathf.Sin(ang));
            float w = rt.rect.width, h = rt.rect.height;
            float hw = (w * ca + h * sa) / 2f, hh = (w * sa + h * ca) / 2f;
            return (c.x - hw, c.y - hh, c.x + hw, c.y + hh);
        }

        /// <summary>
        /// AABB нарисованного КОРПУСА трубки в реф-px. Корпус в обоих спрайтах пиксельно один и тот же
        /// (asset-map §1: `hf_phone.png` и `hf_phone copy.png` — две позы одного рисунка), его alpha-tight
        /// подпрямоугольник в текстуре 662×715 — `113,53,434,608`. Считается как повёрнутый вместе с
        /// ректом прямоугольник: центры мапятся через канвас, габариты берутся из ректа (реф-px).
        /// </summary>
        private static (float L, float T, float R, float B) PhoneBodyBox(RectTransform canvas, RectTransform rt)
        {
            const float bx = 113f, by = 53f, bw = 434f, bh = 608f, tw = 662f, th = 715f;
            float w = rt.rect.width * bw / tw, h = rt.rect.height * bh / th;
            float ox = ((bx + bw / 2f) / tw - 0.5f) * rt.rect.width;
            float oy = ((by + bh / 2f) / th - 0.5f) * rt.rect.height;
            float ang = rt.localEulerAngles.z * Mathf.Deg2Rad;
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            var c = RefCentre(canvas, rt);
            float cx = c.x + ox * ca + oy * sa;      // реф-система: y вниз, поэтому знак у sa зеркальный
            float cy = c.y - ox * sa + oy * ca;
            float hw = (Mathf.Abs(w * ca) + Mathf.Abs(h * sa)) / 2f;
            float hh = (Mathf.Abs(w * sa) + Mathf.Abs(h * ca)) / 2f;
            return (cx - hw, cy - hh, cx + hw, cy + hh);
        }

        /// <summary>
        /// Centre of a CARD CHILD in reference px. The card itself is canvas-anchored (so its own centre
        /// maps exactly), and inside it every offset is in the card's local units — which ARE reference px,
        /// because the card's rect is sized in them. Plain <see cref="RefCentre"/> would route the child's
        /// offset through the canvas fraction and compress it whenever the batch game view is not 16:9.
        /// </summary>
        private static Vector2 OnCardRefCentre(RectTransform canvas, RectTransform card, RectTransform rt)
        {
            var cc = RefCentre(canvas, card);
            var o = card.InverseTransformPoint(rt.position);   // card-local px, y UP
            return new Vector2(cc.x + o.x, cc.y - o.y);
        }

        /// <summary>A reference-px box from a centre and the rect's own (unscaled) size.</summary>
        private static (float L, float T, float R, float B) BoxAt(Vector2 c, RectTransform rt)
        {
            float hw = rt.rect.width / 2f, hh = rt.rect.height / 2f;
            return (c.x - hw, c.y - hh, c.x + hw, c.y + hh);
        }

        /// <summary>
        /// The box the text ACTUALLY paints, in reference px: the generated glyph mesh (best-fit honoured),
        /// not the rect. Same source as <see cref="AssertGeneratedInPill"/> — cachedTextGenerator.verts.
        /// </summary>
        private static (float L, float T, float R, float B) GlyphBoxAt(Text t, Vector2 centre)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.characterCountVisible, 0, "«" + t.name + "» renders glyphs (not empty/tofu)");

            float upp = 1f / t.pixelsPerUnit;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position;
                float x = p.x * upp, y = p.y * upp;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            // Glyph verts are local to the text rect (pivot-centred); local +y is reference −y.
            return (centre.x + minX, centre.y - maxY, centre.x + maxX, centre.y - minY);
        }

        /// <summary>Everything the card draws must stay on the plate's cream field (asset-map §8).</summary>
        private static void AssertInsideCreamField((float L, float T, float R, float B) box, string what)
        {
            Assert.GreaterOrEqual(box.L, GameDriver.FieldX - MapTol, what + ": внутри кремового поля (слева)");
            Assert.LessOrEqual(box.R, GameDriver.FieldX + GameDriver.FieldW + MapTol,
                what + ": внутри кремового поля (справа)");
            Assert.GreaterOrEqual(box.T, GameDriver.FieldTop - MapTol, what + ": внутри кремового поля (сверху)");
            Assert.LessOrEqual(box.B, GameDriver.FieldTop + GameDriver.FieldH + MapTol,
                what + ": внутри кремового поля (снизу)");
        }

        private static IEnumerator ToAdult(GameDriver driver)
        {
            yield return null;                               // Start wires input + subscriptions
            ((PlayFakeInputSource)driver.Input).Confirm();
            yield return null;                               // enter Playing, first card drawn
            // The card plays a ~0.24 s entry animation (CardEntry slides it up from −60): let it SETTLE, or
            // every card-rect assertion below reads a frame of that slide instead of the laid-out position.
            yield return new WaitForSeconds(0.35f);
            driver.DebugApplyAgeGates(34f);                  // reveal the full adult HUD
            yield return null;                               // let the canvas lay out
            yield return null;
        }

        // ============================================================ §2 · every element in its box

        [UnityTest]
        public IEnumerator HudRow_DrawnArt_LandsOnTheMeasuredBoxes()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            AssertDrawnBox(canvas, driver.BatteryImage.rectTransform, Battery);
            AssertDrawnBox(canvas, driver.RelBarImage.rectTransform, RelBar);
            AssertDrawnBox(canvas, driver.HealthBarImage.rectTransform, HealthBar);
            AssertDrawnBox(canvas, driver.MoneyJarImage.rectTransform, MoneyJar);
            AssertDrawnBox(canvas, driver.MoneyCoin.rectTransform, Coin);
            AssertDrawnBox(canvas, driver.AgeBadgeImage.rectTransform, AgeBadge);
            AssertDrawnBox(canvas, driver.CardFrameImage.rectTransform, CardPlate);

            Object.Destroy(go);
            yield return null;
        }

        // Every HUD widget draws the ART-PACK sprite (the -v2 imports), never the old programmer art. A
        // stale name here is exactly how a «green test, old picture» regression would slip through.
        [UnityTest]
        public IEnumerator HudRow_Widgets_Carry_TheArtPackSprites()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);

            Assert.AreEqual("energy-battery-v2", driver.BatteryImage.sprite.name, "энергия = батарея арт-пака");
            Assert.AreEqual("rel-bar-v2", driver.RelBarImage.sprite.name, "отношения = бар арт-пака");
            Assert.AreEqual("health-bar-v2", driver.HealthBarImage.sprite.name, "здоровье = выпатченный бар");
            Assert.AreEqual("money-jar-v2", driver.MoneyJarImage.sprite.name, "деньги = банка арт-пака");
            Assert.AreEqual("coin-v2", driver.MoneyCoin.sprite.name, "монета арт-пака");
            Assert.AreEqual("age-badge-v2", driver.AgeBadgeImage.sprite.name, "возраст = бейдж арт-пака");
            Assert.AreEqual("choice-plate-v2", driver.CardFrameImage.sprite.name, "карточка = кремовая плашка");
            Assert.AreEqual("sunburst-bg-v3", driver.BackgroundImage.sprite.name,
                "фон = ЭКРАННЫЙ синтез лучей (v2 в оверскане выцветал — дизайн-гейт)");
            Assert.AreEqual("rel-marker-heart-v2", driver.BalancerMarker.GetComponent<Image>().sprite.name,
                "маркер отношений = НАРИСОВАННОЕ сердце арт-пака (канон §12-1)");
            Assert.AreEqual("health-marker-v2", driver.HealthMarker.GetComponent<Image>().sprite.name,
                "маркер здоровья = ОРИГИНАЛЬНЫЙ чёрный человечек арт-пака (канон §12-1)");
            Assert.AreEqual("battery-bolt-v2", driver.EnergyBolt.sprite.name,
                "молния батареи — отдельный слой (канон §12-3)");
            Assert.AreEqual("energy-battery-alarm-v2", driver.BatteryAlarmImage.sprite.name,
                "§4-тревога = ОФЛАЙН-перекрас той же батареи (палитра эталона), не тинт");
            Assert.IsNotNull(Resources.Load<Sprite>("Sprites/star-burst-v2"),
                "§6 салют рисуется звездой арт-пака (`v3_stars_flat` → star-burst-v2)");
            // The plate carries tabs mid-side and stars in the corners — a 9-slice would stretch both.
            Assert.AreEqual(Image.Type.Simple, driver.CardFrameImage.type,
                "the choice plate draws Simple — NEVER 9-sliced (asset-map §1.1/§5)");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ §8 · live readings inside the art

        // Энергия = заливка полости. The cream «empty» rect's BOTTOM edge is the level line, so at p the
        // level must sit exactly p of the way up the measured cavity (asset-map §8: 141,88,86,175).
        [UnityTest]
        public IEnumerator Battery_FillLevel_TracksEnergy_InsideTheMeasuredCavity()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            void AssertLevel(float energy)
            {
                driver.DebugReflectScales(energy, 100f, 50f);
                var (l, t, r, b) = RefBox(canvas, driver.EnergyEmpty.rectTransform);
                float expectedLevel = GameDriver.CavityTop + GameDriver.CavityH * (1f - energy / 100f);
                Assert.AreEqual(GameDriver.CavityX, l, MapTol,
                    energy + "%: fill starts at the cavity's left edge");
                Assert.AreEqual(GameDriver.CavityX + GameDriver.CavityW, r, MapTol,
                    energy + "%: fill spans the cavity's full width");
                Assert.AreEqual(GameDriver.CavityTop, t, MapTol,
                    energy + "%: the empty rect starts at the cavity top");
                Assert.AreEqual(expectedLevel, b, MapTol,
                    energy + "%: the level line sits at " + energy + "% of the measured cavity");
            }

            AssertLevel(100f);   // full → the level line IS the cavity top (empty rect collapses)
            AssertLevel(75f);
            AssertLevel(40f);
            AssertLevel(0f);     // empty → the cream rect covers the whole cavity

            Object.Destroy(go);
            yield return null;
        }

        // The OTHER half of the battery reading: the sprite carries a BAKED 75.1 % level, so anything ABOVE
        // it has to be painted in — that is the yellow top-up layer. It was previously untested, so a broken
        // (or missing) top-up would have read as «энергия 100 % рисуется как 75 %» with a green suite.
        [UnityTest]
        public IEnumerator Battery_TopUp_PaintsOnlyAboveTheBakedLevel_InsideTheCavity()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            // The baked line the constant claims — everything below is checked against THIS.
            float bakedTop = GameDriver.CavityTop + GameDriver.CavityH * (1f - GameDriver.BakedEnergyLevel);
            Assert.Greater(GameDriver.BakedEnergyLevel, 0f, "the baked level is a real fraction of the cavity");
            Assert.Less(GameDriver.BakedEnergyLevel, 1f, "…and it is not a full cavity");

            (float L, float T, float R, float B) TopUp(float energy)
            {
                driver.DebugReflectScales(energy, 100f, 50f);
                return RefBox(canvas, driver.EnergyTopUp.rectTransform);
            }

            // (1) above the baked level: the top-up spans EXACTLY level line → baked line, full cavity width.
            foreach (float energy in new[] { 100f, 90f })
            {
                var (l, t, r, b) = TopUp(energy);
                float level = GameDriver.CavityTop + GameDriver.CavityH * (1f - energy / 100f);
                Assert.AreEqual(bakedTop - level, b - t, MapTol,
                    energy + "%: добор ровно от живого уровня до запечённого");
                Assert.AreEqual(level, t, MapTol, energy + "%: добор начинается на линии живого уровня");
                Assert.AreEqual(bakedTop, b, MapTol, energy + "%: добор кончается на ЗАПЕЧЁННОЙ линии");
                Assert.AreEqual(GameDriver.CavityX, l, MapTol, energy + "%: добор от левого края полости");
                Assert.AreEqual(GameDriver.CavityX + GameDriver.CavityW, r, MapTol,
                    energy + "%: добор во всю ширину полости");
                // never spills out of the cavity the art draws
                Assert.GreaterOrEqual(t, GameDriver.CavityTop - MapTol, energy + "%: добор не выше полости");
                Assert.LessOrEqual(b, GameDriver.CavityTop + GameDriver.CavityH + MapTol,
                    energy + "%: добор не ниже полости");
            }

            // (2) the constant is BOUND to the geometry: the top-up collapses exactly at the baked level.
            var atBaked = TopUp(GameDriver.BakedEnergyLevel * 100f);
            Assert.AreEqual(0f, atBaked.B - atBaked.T, 0.01f,
                "на самом запечённом уровне (" + GameDriver.BakedEnergyLevel * 100f + " %) добор нулевой");
            var justAbove = TopUp(GameDriver.BakedEnergyLevel * 100f + 5f);
            Assert.Greater(justAbove.B - justAbove.T, 1f,
                "чуть выше запечённого уровня добор уже рисуется (иначе константа ни к чему не привязана)");

            // (3) below the baked level there is nothing to top up — the layer is a zero-height no-op.
            foreach (float energy in new[] { 75f, 40f, 0f })
            {
                var (_, t, _, b) = TopUp(energy);
                Assert.AreEqual(0f, b - t, 0.01f, energy + "%: ниже запечённого уровня добора нет");
            }

            Object.Destroy(go);
            yield return null;
        }

        // Маркеры честны: механическая граница зоны стоит на НАРИСОВАННОЙ границе (asset-map §11-2/§11-4).
        // This is the done-contract's «маркер визуально честен» criterion, asserted on the rendered rect.
        [UnityTest]
        public IEnumerator BarMarkers_LandOnTheDrawnZoneBorders_ThroughTheNonLinearMap()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            float MarkerRefX(RectTransform rt) => RefCentre(canvas, rt).x;
            var rel = (RectTransform)driver.BalancerMarker.transform;
            var hp = (RectTransform)driver.HealthMarker.transform;

            // relationships: the drawn red/green borders are at 20.7 % and 81.5 % of the track
            driver.DebugReflectScales(100f, 100f, 40f);
            Assert.AreEqual(GameDriver.RelTrackX + 0.207f * GameDriver.RelTrackW, MarkerRefX(rel), MapTol,
                "механические 40 % отношений стоят на НАРИСОВАННОЙ границе красное/зелёное (20.7 %)");
            driver.DebugReflectScales(100f, 100f, 75f);
            Assert.AreEqual(GameDriver.RelTrackX + 0.815f * GameDriver.RelTrackW, MarkerRefX(rel), MapTol,
                "механические 75 % отношений стоят на НАРИСОВАННОЙ границе зелёное/красное (81.5 %)");
            // Ends: NOT the bare ends of the rail — those belong to the drawn faces/скулу-сердцу. The marker
            // stops in front of them (travel window of the geometry block), and the pixel guard
            // BarMarkers_ClearTheDrawnEndDecorations_AndStayOnThePlate is what proves that window is right.
            driver.DebugReflectScales(100f, 100f, 0f);
            Assert.AreEqual(GameDriver.RelMarkerMinCx, MarkerRefX(rel), MapTol,
                "0 отношений — начало хода: вплотную (но с зазором) к лицу мальчика");
            driver.DebugReflectScales(100f, 100f, 100f);
            Assert.AreEqual(GameDriver.RelMarkerMaxCx, MarkerRefX(rel), MapTol,
                "100 отношений — конец хода: вплотную (но с зазором) к лицу девочки");

            // health: the drawn red/green border is at 52 % of the track = the alarm threshold 20
            driver.DebugReflectScales(100f, 20f, 50f);
            Assert.AreEqual(GameDriver.HealthTrackX + 0.52f * GameDriver.HealthTrackW, MarkerRefX(hp), MapTol,
                "механические 20 % здоровья стоят на НАРИСОВАННОЙ границе красное/зелёное (52 %)");
            driver.DebugReflectScales(100f, 0f, 50f);
            Assert.AreEqual(GameDriver.HealthMarkerMinCx, MarkerRefX(hp), MapTol,
                "0 здоровья — начало хода: перед черепом, в глубине нарисованной красной зоны");
            Assert.Less(MarkerRefX(hp), GameDriver.HealthTrackX + 0.52f * GameDriver.HealthTrackW,
                "…и это всё ещё КРАСНАЯ зона (маркер честен)");
            driver.DebugReflectScales(100f, 100f, 50f);
            Assert.AreEqual(GameDriver.HealthMarkerMaxCx, MarkerRefX(hp), MapTol,
                "100 здоровья — конец хода: перед сердцем-торцом");
            Assert.Greater(MarkerRefX(hp), GameDriver.HealthTrackX + 0.52f * GameDriver.HealthTrackW,
                "…и это ЗЕЛЁНАЯ зона");

            // §12-1: each marker IS the art's own element, so it keeps the art's size and the art's height
            // on the bar — the heart centred on the track, the figure sitting on it with legs below — and
            // only X moves. Asserted on the drawn rect against the measured boxes.
            foreach (var (rt, cy, w, h, what) in new[]
            {
                (rel, GameDriver.RelMarkerCy, GameDriver.RelMarkerW, GameDriver.RelMarkerH, "сердце-маркер"),
                (hp, GameDriver.HealthMarkerCy, GameDriver.HealthMarkerW, GameDriver.HealthMarkerH,
                    "человечек-маркер"),
            })
            {
                var (bl, bt, br, bb) = RefBox(canvas, rt);
                Assert.AreEqual(cy, (bt + bb) / 2f, MapTol, what + " сидит на своей нарисованной высоте");
                Assert.AreEqual(w, br - bl, MapTol, what + " нарисован в свой размер (ширина)");
                Assert.AreEqual(h, bb - bt, MapTol, what + " нарисован в свой размер (высота)");
            }
            // …and each really overlaps its track vertically (a marker floating off the bar is the bug the
            // old «feet on the bottom edge» guard caught).
            foreach (var (rt, top, th, what) in new[]
            {
                (rel, GameDriver.RelTrackTop, GameDriver.RelTrackH, "сердце-маркер"),
                (hp, GameDriver.HealthTrackTop, GameDriver.HealthTrackH, "человечек-маркер"),
            })
            {
                var (_, bt, _, bb) = RefBox(canvas, rt);
                Assert.Less(bt, top + th, what + " заходит на дорожку сверху");
                Assert.Greater(bb, top, what + " заходит на дорожку снизу");
            }

            Object.Destroy(go);
            yield return null;
        }

        // ============================================ маркер не наезжает на торцевые украшения дорожки
        // Design gate, round 3: на 100 здоровья человечек накрывал НАРИСОВАННОЕ сердце-торец до середины —
        // чёрное на чёрном, нечитаемая клякса (а рект-проверки этого не видят: рект честно стоял на конце
        // дорожки). Ниже всё меряется ПО ПИКСЕЛЯМ импортированных PNG: торцевые украшения — по их собственным
        // цветам в баре, маркер — по альфе своего спрайта, плашка — по альфе бара.

        /// <summary>The imported PNG, straight off disk (the atlas texture is not Read/Write — asset-map §6.2).</summary>
        private static Texture2D LoadArtPng(string file)
        {
            string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Resources/Sprites/" + file);
            Assert.IsTrue(System.IO.File.Exists(path), "imported PNG on disk: " + path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(tex.LoadImage(System.IO.File.ReadAllBytes(path)), file + " decodes");
            return tex;
        }

        /// <summary>Sprite-px box (x0,yTop0,x1,yTop1, inclusive) → reference px through an Image's rect.
        /// The bars draw Simple/unrotated, so the whole texture maps linearly onto the rect.</summary>
        private static (float L, float T, float R, float B) SpriteBoxToRef(
            RectTransform canvas, RectTransform rt, Texture2D tex, (int X0, int Y0, int X1, int Y1) b)
        {
            var c = RefCentre(canvas, rt);
            float sx = rt.rect.width / tex.width, sy = rt.rect.height / tex.height;
            float left = c.x - rt.rect.width / 2f, top = c.y - rt.rect.height / 2f;
            return (left + b.X0 * sx, top + b.Y0 * sy, left + (b.X1 + 1) * sx, top + (b.Y1 + 1) * sy);
        }

        /// <summary>Bounding box (sprite px, y from TOP) of every texel the predicate keeps.</summary>
        private static (int X0, int Y0, int X1, int Y1) InkBox(Texture2D tex, System.Func<Color32, bool> keep)
        {
            var px = tex.GetPixels32();                        // row 0 = BOTTOM
            int w = tex.width, h = tex.height;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (keep(px[y * w + x]))
                    {
                        int yt = h - 1 - y;
                        if (x < x0) x0 = x; if (x > x1) x1 = x;
                        if (yt < y0) y0 = yt; if (yt > y1) y1 = yt;
                    }
            Assert.Less(x0, x1, "предикат вообще что-то нашёл на спрайте");
            return (x0, y0, x1, y1);
        }

        private static bool IsInk(Color32 p) => p.a > 200 && Mathf.Max(p.r, Mathf.Max(p.g, p.b)) < 90;
        private static bool IsRedFill(Color32 p) => p.a > 200 && p.r > 190 && p.g < 80 && p.b < 80;
        private static bool IsGreenFill(Color32 p) => p.a > 200 && p.g > 150 && p.r < 160 && p.b < 120;
        private static bool IsSkin(Color32 p)
            => p.a > 200 && p.r > 180 && p.g > 120 && p.g < 215 && p.b > 90 && p.b < 190;

        /// <summary>
        /// The health bar's two end decorations (череп и сердце) by PIXELS: dark texels that the track's own
        /// red/green fill ENCLOSES on their row. That rule picks up exactly the two drawn icons and never the
        /// track frame or the plate outline (those lie outside the fill span).
        /// </summary>
        private static List<(int X0, int Y0, int X1, int Y1)> HealthEndIcons(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height, mid = w / 2;
            int[] lx = { int.MaxValue, int.MaxValue }, rx = { int.MinValue, int.MinValue };
            int[] ty = { int.MaxValue, int.MaxValue }, by = { int.MinValue, int.MinValue };
            for (int y = 0; y < h; y++)
            {
                int fillL = int.MaxValue, fillR = int.MinValue;
                for (int x = 0; x < w; x++)
                {
                    var p = px[y * w + x];
                    if (!IsRedFill(p) && !IsGreenFill(p)) continue;
                    if (x < fillL) fillL = x; if (x > fillR) fillR = x;
                }
                if (fillR - fillL < 100) continue;                    // not a row of the track
                for (int x = fillL + 1; x < fillR; x++)
                {
                    if (!IsInk(px[y * w + x])) continue;
                    int i = x < mid ? 0 : 1, yt = h - 1 - y;
                    if (x < lx[i]) lx[i] = x; if (x > rx[i]) rx[i] = x;
                    if (yt < ty[i]) ty[i] = yt; if (yt > by[i]) by[i] = yt;
                }
            }
            var icons = new List<(int, int, int, int)>();
            for (int i = 0; i < 2; i++)
            {
                Assert.Less(lx[i], rx[i], "нашли торцевое украшение здоровья #" + i);
                icons.Add((lx[i], ty[i], rx[i], by[i]));
            }
            return icons;
        }

        /// <summary>
        /// The relationships bar's two end decorations (лица) by PIXELS: the skin/hair colours ERODED once
        /// (the anti-aliased seam along the plate's own pink rim passes a raw skin test and would smear the
        /// box across the whole bar), then grown back through the icons' own ink OUTLINE. The growth is
        /// BOUNDED because the faces sit ON the track — an unbounded flood would run away along the track
        /// frame; bounded is conservative, the box can only come out bigger than the face.
        /// </summary>
        private static List<(int X0, int Y0, int X1, int Y1)> RelEndIcons(Texture2D tex, int grow = 8)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height, mid = w / 2;
            var skin = new bool[w * h];
            for (int i = 0; i < skin.Length; i++) skin[i] = IsSkin(px[i]);
            var mask = new bool[w * h];
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    mask[i] = skin[i] && skin[i - 1] && skin[i + 1] && skin[i - w] && skin[i + w];
                }
            for (int step = 0; step < grow; step++)
            {
                var next = (bool[])mask.Clone();
                for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = y * w + x;
                        if (mask[i] || !(IsInk(px[i]) || skin[i])) continue;
                        if (mask[i - 1] || mask[i + 1] || mask[i - w] || mask[i + w]) next[i] = true;
                    }
                mask = next;
            }
            int[] lx = { int.MaxValue, int.MaxValue }, rx = { int.MinValue, int.MinValue };
            int[] ty = { int.MaxValue, int.MaxValue }, by = { int.MinValue, int.MinValue };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!mask[y * w + x]) continue;
                    int i = x < mid ? 0 : 1, yt = h - 1 - y;
                    if (x < lx[i]) lx[i] = x; if (x > rx[i]) rx[i] = x;
                    if (yt < ty[i]) ty[i] = yt; if (yt > by[i]) by[i] = yt;
                }
            var icons = new List<(int, int, int, int)>();
            for (int i = 0; i < 2; i++)
            {
                Assert.Less(lx[i], rx[i], "нашли торцевое украшение отношений #" + i);
                icons.Add((lx[i], ty[i], rx[i], by[i]));
            }
            return icons;
        }

        /// <summary>Clear air between two reference-px boxes (negative = they overlap).</summary>
        private static float Clearance((float L, float T, float R, float B) a,
                                       (float L, float T, float R, float B) b)
            => Mathf.Max(Mathf.Max(a.L - b.R, b.L - a.R), Mathf.Max(a.T - b.B, b.T - a.B));

        [UnityTest]
        public IEnumerator BarMarkers_ClearTheDrawnEndDecorations_AndStayOnThePlate()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            var relBar = driver.RelBarImage.rectTransform;
            var hpBar = driver.HealthBarImage.rectTransform;
            var relMarker = (RectTransform)driver.BalancerMarker.transform;
            var hpMarker = (RectTransform)driver.HealthMarker.transform;

            var relTex = LoadArtPng("rel-bar-v2.png");
            var hpTex = LoadArtPng("health-bar-v2.png");

            // Markers are alpha-tight PNGs (cut from the bars), so the RECT is the drawn picture — asserted,
            // not assumed: a padded re-export would make every box below a lie.
            foreach (var (file, rt, what) in new[]
            {
                ("rel-marker-heart-v2.png", relMarker, "сердце-маркер"),
                ("health-marker-v2.png", hpMarker, "человечек-маркер"),
            })
            {
                var mt = LoadArtPng(file);
                var box = InkBox(mt, p => p.a > 8);
                Assert.AreEqual(0, box.X0, what + ": спрайт обрезан по альфе слева");
                Assert.AreEqual(0, box.Y0, what + ": спрайт обрезан по альфе сверху");
                Assert.AreEqual(mt.width - 1, box.X1, what + ": спрайт обрезан по альфе справа");
                Assert.AreEqual(mt.height - 1, box.Y1, what + ": спрайт обрезан по альфе снизу");
                Object.Destroy(mt);
            }

            // The plate each marker must stay on = the bar sprite's own alpha silhouette.
            var relPlate = SpriteBoxToRef(canvas, relBar, relTex, InkBox(relTex, p => p.a > 8));
            var hpPlate = SpriteBoxToRef(canvas, hpBar, hpTex, InkBox(hpTex, p => p.a > 8));
            var relIcons = RelEndIcons(relTex).Select(b => SpriteBoxToRef(canvas, relBar, relTex, b)).ToList();
            var hpIcons = HealthEndIcons(hpTex).Select(b => SpriteBoxToRef(canvas, hpBar, hpTex, b)).ToList();
            Object.Destroy(relTex); Object.Destroy(hpTex);

            // The measured icon edges are what GameDriver's travel window is built on — pixels vs constants.
            Assert.AreEqual(GameDriver.HealthSkullRight, hpIcons[0].R, 3f, "череп: правый край по пикселям");
            Assert.AreEqual(GameDriver.HealthHeartLeft, hpIcons[1].L, 3f, "сердце-торец: левый край по пикселям");
            // The faces' constants are the edge measured on the RENDERED frame; this projection out of the
            // sprite lands 2…4 px inside it (the bar draws at 0.46× and the resampled ink edge spreads), so
            // the check is «same edge, and the constant is the CONSERVATIVE one».
            Assert.AreEqual(GameDriver.RelBoyFaceRight, relIcons[0].R, 6f, "лицо мальчика: правый край по пикселям");
            Assert.GreaterOrEqual(GameDriver.RelBoyFaceRight, relIcons[0].R, "…и константа не уже пикселей");
            Assert.AreEqual(GameDriver.RelGirlFaceLeft, relIcons[1].L, 6f, "лицо девочки: левый край по пикселям");
            Assert.LessOrEqual(GameDriver.RelGirlFaceLeft, relIcons[1].L, "…и константа не уже пикселей");

            // Both bars, across the WHOLE scale (the ends are where it broke, the sweep proves the middle
            // never wanders onto a decoration either).
            const float MinGap = 4f;                            // design gate's zазор
            foreach (float v in new[] { 0f, 1f, 10f, 20f, 40f, 50f, 75f, 90f, 99f, 100f })
            {
                driver.DebugReflectScales(100f, v, v);
                yield return null;

                foreach (var (marker, plate, icons, what) in new[]
                {
                    (relMarker, relPlate, relIcons, "сердце-маркер отношений"),
                    (hpMarker, hpPlate, hpIcons, "человечек-маркер здоровья"),
                })
                {
                    var box = RefBox(canvas, marker);
                    string at = what + " на значении " + v;
                    for (int i = 0; i < icons.Count; i++)
                        Assert.GreaterOrEqual(Clearance(box, icons[i]), MinGap,
                            at + ": зазор до нарисованного торцевого украшения #" + i + " ≥ " + MinGap + " px");
                    Assert.GreaterOrEqual(box.L, plate.L, at + ": не вылезает за плашку слева");
                    Assert.LessOrEqual(box.R, plate.R, at + ": не вылезает за плашку справа");
                    Assert.GreaterOrEqual(box.T, plate.T, at + ": не вылезает за плашку сверху");
                    Assert.LessOrEqual(box.B, plate.B, at + ": не вылезает за плашку снизу (ступни на контуре)");
                }
            }

            Object.Destroy(go);
            yield return null;
        }

        // Founder canon §12-2/§12-4: the ART PACK is the palette — the explainers are truth for LAYOUT, not
        // colour (they were rendered from earlier sprite versions). So every sprite draws UNTINTED, and the
        // one colour we do set from tokens, INK, is the build-spec hex — not a pixel sampled off a PNG.
        [UnityTest]
        public IEnumerator Palette_ComesFromTheArtPack_AndTheInkToken()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);

            foreach (var (img, what) in new[]
            {
                (driver.BatteryImage, "батарея"), (driver.EnergyBolt, "молния"),
                (driver.RelBarImage, "бар отношений"), (driver.HealthBarImage, "бар здоровья"),
                (driver.MoneyJarImage, "банка"), (driver.MoneyCoin, "монета"),
                (driver.AgeBadgeImage, "бейдж"), (driver.CardFrameImage, "карточка"),
                (driver.BackgroundImage, "лучи"),
                (driver.HealthMarker.GetComponent<Image>(), "человечек-маркер"),
                (driver.HostBubble.GetComponent<Image>(), "облачко Ведущего"),
            })
                Assert.AreEqual(Color.white, img.color,
                    what + " рисуется СВОИМИ цветами из спрайта (никакой тонировки под эталон)");

            // …и это правило держится ИМЕННО В СПОКОЙНОМ ходе. §4-тревога — единственное исключение, и она
            // красит ТОЛЬКО свои слои: банка/монета тонируются RED_BRIGHT, батарея получает отдельную
            // красную копию, а ПЛАШКИ БАРОВ остаются нетронутыми (их тревога — кант вокруг).
            driver.enabled = false;                       // дальше время подаём вручную
            driver.DebugApplyAgeGates(34f);
            driver.Game.Scales.Energy = 10;
            driver.Game.Scales.Health = 10;
            driver.Game.Scales.Relationships = 20;
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(AlarmScale.Energy), "энергия ушла в тревогу");
            Assert.AreEqual(Color.white, driver.BatteryImage.color,
                "спокойная батарея НЕ тонируется даже в тревоге — красное несёт отдельный слой");
            Assert.IsTrue(driver.BatteryAlarmImage.gameObject.activeSelf, "…а он на экране");
            Assert.AreEqual(Color.white, driver.HealthBarImage.color, "плашка бара здоровья не тонируется");
            Assert.AreEqual(Color.white, driver.RelBarImage.color, "плашка бара отношений не тонируется");
            Assert.AreEqual(Color.white, driver.EnergyBolt.color, "молния не тонируется (читаемость §4)");

            // INK = #0B0F1A from the tokens (asset-map §12-4), and it is what the dark copy actually uses.
            Assert.AreEqual(11f / 255f, GameDriver.InkToken.r, 0.002f, "INK.r = 0x0B");
            Assert.AreEqual(15f / 255f, GameDriver.InkToken.g, 0.002f, "INK.g = 0x0F");
            Assert.AreEqual(26f / 255f, GameDriver.InkToken.b, 0.002f, "INK.b = 0x1A");
            Assert.AreEqual(GameDriver.InkToken, driver.AgeText.color, "цифра возраста — INK");
            Assert.AreEqual(GameDriver.InkToken, driver.MoneyText.color, "сумма в банке — INK");
            Assert.AreEqual(GameDriver.InkToken,
                driver.CardRect.Find("CardText").GetComponent<Text>().color, "вопрос — INK");

            Object.Destroy(go);
            yield return null;
        }

        // The relationships marker is the ONLY thing that flags the >75 % «красная зона» — the drawn bar
        // keeps its own colours (tinting the art muddied it). Carried over from the old balancer canon.
        [UnityTest]
        public IEnumerator RelMarker_TintsRed_InTheRedZone_BarKeepsItsOwnColours()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var markerImg = driver.BalancerMarker.GetComponent<Image>();

            driver.DebugReflectScales(100f, 100f, 60f, relRedZone: false);
            Assert.AreEqual(Color.white, markerImg.color, "вне красной зоны маркер белый (без тонировки)");
            driver.DebugReflectScales(100f, 100f, 90f, relRedZone: true);
            Assert.Greater(markerImg.color.r, markerImg.color.g + 0.3f, "в красной зоне маркер краснеет");
            Assert.AreEqual(Color.white, driver.RelBarImage.color,
                "сам бар всегда рисуется своими цветами (не тонируется)");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ digits/sums are DRAWN, in their boxes

        [UnityTest]
        public IEnumerator AgeDigits_And_MoneySum_AreDrawnInsideTheirArtBoxes()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);

            // Age: digits only — the «ВОЗРАСТ» caption is gone with the art pack (closes the overlap debt).
            var badgeTexts = driver.AgeBadge.GetComponentsInChildren<Text>(includeInactive: true);
            Assert.AreEqual(1, badgeTexts.Length, "бейдж возраста несёт ТОЛЬКО цифру, без подписи «ВОЗРАСТ»");
            Assert.AreEqual(112, driver.AgeText.resizeTextMaxSize,
                "кегль цифры возраста — 112 px Arimo Bold (asset-map §11-8: cap-height 80 на эталоне)");

            // NOTE: no `yield` past this point — the driver's Update rewrites both labels from the live game
            // every frame, so the posed strings must be measured on the SAME frame they are set.
            driver.AgeText.text = "33";
            AssertGeneratedInPill(driver.AgeText, driver.AgeBadgeImage, 30f, "цифра возраста");

            // Money: the sum is drawn INSIDE the jar's own cream label, compact when long (§11-6).
            driver.MoneyText.text = GameDriver.FormatMoneyJar(12500);
            Assert.AreEqual("₽12.5к", driver.MoneyText.text, "пятизначная сумма — компакт-формат");
            AssertGeneratedInPill(driver.MoneyText, driver.MoneyJarImage, 20f, "сумма в лейбле банки");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ layout invariants

        /// <summary>Спековый бокс купола (build-spec §2 / revisions §5a): x, yTop, w, h.</summary>
        private const float DomeBoxX = 760f, DomeBoxTop = 0f, DomeBoxW = 400f, DomeBoxH = 130f;
        /// <summary>Допуск на бокс купола — крупная композиция, ±15 px (решение основательницы 2026-07-31).</summary>
        private const float DomeTol = 15f;
        /// <summary>Минимальная видимая полоса дуги НАД барами, px (гейт «дугу видно»).</summary>
        private const float DomeVisibleBandMin = 25f;

        [UnityTest]
        public IEnumerator DomeTimer_IsTheBigCentredDome_DrawnUnderTheBars()
        {
            // Инкремент «купол», решение основательницы 2026-07-31: круглое кольцо (252,590) снято, таймер —
            // КРУПНАЯ полусфера по центру экрана, свисающая с верхнего края, и она лежит СЛОЕМ ПОД барами —
            // «как наклейки на афише»: бары дорисованы поверх купола, а дуга читается в просветах (полоса
            // над барами + коридор между ними). Поэтому здесь НЕ гейт «ничего не перекрывает» (он и загнал
            // купол в 116-пиксельный коридор), а три вещи: (1) бокс = спековый 760,0,400,130 ±15;
            // (2) z-порядок — купол НИЖЕ баров и ВЫШЕ фона-лучей; (3) просветы, в которых дугу реально видно.
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            // ---- (1) бокс купола = спек §2 -------------------------------------------------------------
            var dome = RefBox(canvas, driver.TimerDomeOutline.rectTransform);
            Assert.AreEqual(DomeBoxW, dome.R - dome.L, DomeTol, "ширина купола = бокс §2 (400)");
            Assert.AreEqual(DomeBoxH, dome.B - dome.T, DomeTol, "глубина купола = бокс §2 (130)");
            Assert.AreEqual(DomeBoxX + DomeBoxW / 2f, (dome.L + dome.R) / 2f, DomeTol,
                "купол по центру экрана (X 960)");
            Assert.AreEqual(960f, GameDriver.DomeCx, 0.001f, "центр купола — центр экрана (канон §5a)");
            Assert.AreEqual(DomeBoxTop, dome.T, 1.5f, "плоская сторона купола лежит на верхнем крае экрана");

            // ---- (2) z-порядок: купол ПОД барами, но НАД фоном-лучами ----------------------------------
            // Сравниваются siblings одного родителя — поэтому от бара поднимаемся до его предка, который
            // сам является ребёнком игровой панели (бары живут в HudRow).
            Transform PanelChild(Transform t)
            {
                while (t.parent != driver.GamePanel.transform)
                {
                    t = t.parent;
                    Assert.IsNotNull(t, "элемент живёт внутри игровой панели");
                }
                return t;
            }

            var domeChild = PanelChild(driver.TimerDome.transform);
            foreach (var (img, what) in new[]
            {
                (driver.RelBarImage, "бар отношений"),
                (driver.HealthBarImage, "бар здоровья"),
            })
            {
                var barChild = PanelChild(img.transform);
                Assert.AreNotSame(domeChild, barChild, "купол и «" + what + "» — разные ветки панели");
                Assert.Less(domeChild.GetSiblingIndex(), barChild.GetSiblingIndex(),
                    "купол рисуется НИЖЕ, чем «" + what + "» — бар полностью поверх купола");
            }
            // …и вся игровая панель (а с ней купол) — поверх фона-лучей.
            Assert.AreSame(driver.BackgroundImage.transform.parent, driver.GamePanel.transform.parent,
                "фон-лучи и игровая панель — siblings канваса");
            Assert.Less(driver.BackgroundImage.transform.GetSiblingIndex(),
                driver.GamePanel.transform.GetSiblingIndex(),
                "купол рисуется ВЫШЕ фона-лучей");

            // ---- (3) просветы, в которых дуга реально видна --------------------------------------------
            var relDrawn = DrawnRefBox(canvas, driver.RelBarImage.rectTransform, RelBar);
            var healthDrawn = DrawnRefBox(canvas, driver.HealthBarImage.rectTransform, HealthBar);

            // (3a) полоса НАД барами: от верхнего края экрана до верхней кромки ближайшего бара.
            float barTop = Mathf.Min(relDrawn.T, healthDrawn.T);
            float band = barTop - dome.T;
            Assert.GreaterOrEqual(band, DomeVisibleBandMin,
                $"над барами торчит читаемая полоса купола (замер {band:0.0} px)");
            Assert.Less(band, dome.B - dome.T, "полоса — это ЧАСТЬ купола, а не весь купол над HUD");

            // (3b) коридор МЕЖДУ барами: купол перекрывает его целиком и уходит ниже верхней кромки баров,
            //      т.е. в коридоре видна дуга, а не пустой фон.
            Assert.Less(dome.L, relDrawn.R, "купол заходит левее правого края бара отношений");
            Assert.Greater(dome.R, healthDrawn.L, "купол заходит правее левого края бара здоровья");
            Assert.Greater(dome.B, barTop + DomeVisibleBandMin,
                "в коридоре между барами купол читается заметным куском, а не кромкой");

            // (3c) «наклейка на афише» ЯВНО: купол ДОЛЖЕН заходить под оба бара — если он снова уедет в
            //      116-пиксельный коридор, этот ассерт упадёт.
            Assert.IsTrue(RefOverlap(dome, relDrawn), "купол уходит ПОД бар отношений (канон-наклейка)");
            Assert.IsTrue(RefOverlap(dome, healthDrawn), "купол уходит ПОД бар здоровья (канон-наклейка)");

            // ---- (4) …и при этом не лезет на остальной HUD и на карточку -------------------------------
            foreach (var (rt, what) in new[]
            {
                (driver.MoneyJarImage.rectTransform, "money jar"),
                (driver.AgeBadgeImage.rectTransform, "age badge"),
                (driver.CardFrameImage.rectTransform, "card plate"),
                (driver.BatteryImage.rectTransform, "battery (энергия)"),
                (driver.MoneyCoin.rectTransform, "coin"),
            })
                Assert.IsFalse(RefOverlap(dome, RefBox(canvas, rt)),
                    "dome rect must be disjoint from the " + what + " rect");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GamePanel_Contains_Exactly_The_Expected_Active_Images()
        {
            // Exhaustive enumeration across the WHOLE game panel (HUD row + timer + card + plates), not just
            // the HUD row — a stray/placeholder/tofu Image anywhere in the game view would fail this.
            var driver = BootToAdult(out var go);
            yield return null;                               // Start wires input + subscriptions
            ((PlayFakeInputSource)driver.Input).Confirm();
            yield return null;                               // enter Playing, first card drawn
            // Reveal the full adult HUD and enumerate SYNCHRONOUSLY (the driver's per-frame ApplyAgeGates
            // re-gates off the live Age=0, so a yield here would re-hide the age-gated widgets).
            driver.DebugApplyAgeGates(34f);

            var actual = driver.GamePanel.GetComponentsInChildren<Image>(includeInactive: false)
                .Select(i => i.sprite != null ? i.sprite.name : "<null>")
                .OrderBy(s => s)
                .ToList();

            var expected = new List<string>
            {
                "age-badge-v2",                                      // возраст
                "money-jar-v2", "coin-v2",                           // деньги: банка + монета
                "energy-battery-v2", "<null>", "<null>", "battery-bolt-v2",  // батарея + заливка + молния
                "rel-bar-v2", "rel-marker-heart-v2",                 // отношения: бар + сердце-маркер
                "health-bar-v2", "health-marker-v2",                 // здоровье: бар + человечек-маркер
                "timer-dome", "timer-dome", "timer-dome",             // купол-таймер: обводка + трек + дуга
                "timer-dome-hand",                                   // …и стрелка-кромка по границе заливки
                "choice-plate-v2",                                   // карточка-вопрос
                "btn-yes", "btn-no",                                 // answer plates (baked art, §9)
            }.OrderBy(s => s).ToList();

            CollectionAssert.AreEqual(expected, actual,
                "the adult game panel renders exactly its expected sprites (no stray/placeholder Image)");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Key_Elements_Fully_Inside_The_16by9_Frame()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;
            float halfW = canvas.rect.width / 2f, halfH = canvas.rect.height / 2f;
            float tol = 1.5f;

            void Inside(RectTransform rt, string what)
            {
                var b = OwnBounds(canvas, rt);
                Assert.GreaterOrEqual(b.min.x, -halfW - tol, what + " not clipped by the left edge");
                Assert.LessOrEqual(b.max.x, halfW + tol, what + " not clipped by the right edge");
                Assert.GreaterOrEqual(b.min.y, -halfH - tol, what + " not clipped by the bottom edge");
                Assert.LessOrEqual(b.max.y, halfH + tol, what + " not clipped by the top edge");
            }

            Inside(driver.AgeBadgeImage.rectTransform, "age badge");
            Inside(driver.MoneyJarImage.rectTransform, "money jar");
            Inside(driver.MoneyCoin.rectTransform, "coin");
            Inside(driver.BatteryImage.rectTransform, "battery");
            Inside(driver.HealthBarImage.rectTransform, "health bar");
            Inside(driver.RelBarImage.rectTransform, "relationships bar");
            Inside(driver.TimerDomeOutline.rectTransform, "купол-таймер");
            Inside(driver.CardRect, "card plate");
            Inside(driver.YesPlateImage.rectTransform, "ДА plate");
            Inside(driver.NoPlateImage.rectTransform, "СПАСИБО НЕ НАДО plate");

            Object.Destroy(go);
            yield return null;
        }

        // §5b: трубка ребёнка сменила старую «вспышку кнопки-ребёнка» (halo + лампочка в правой колонке).
        // Обе позы сняты с эталонов, поэтому геометрию сторожим ИМЕННО по ним: покой — за левым краем,
        // звонок — выехал внутрь; и в ОБЕИХ позах трубка не наезжает на карточку-вопрос и на правую
        // колонку HUD (банка/бейдж/монета), т.е. читаемость доски сохраняется.
        [UnityTest]
        public IEnumerator ChildPhone_BothPoses_MatchTheExplainers_AndClearTheBoard()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;
            driver.ChildGroup.SetActive(true);   // revealed by MD02=ДА in play; posed here for the geometry
            var rt = driver.ChildPhoneImage.rectTransform;

            var neighbours = new[]
            {
                (DrawnRefBox(canvas, driver.AgeBadgeImage.rectTransform, AgeBadge), "бейдж возраста"),
                (DrawnRefBox(canvas, driver.MoneyJarImage.rectTransform, MoneyJar), "банка денег"),
                (DrawnRefBox(canvas, driver.MoneyCoin.rectTransform, Coin), "монета"),
                (DrawnRefBox(canvas, driver.CardFrameImage.rectTransform, CardPlate), "карточка-вопрос"),
                (RefAabb(canvas, driver.YesPlateImage.rectTransform), "кнопка ДА"),
            };

            void AssertPose(Vector4 want, float tilt, string what)
            {
                var c = RefCentre(canvas, rt);
                Assert.AreEqual(want.x, c.x, MapTol, what + ": центр по X = замер эталона");
                Assert.AreEqual(want.y, c.y, MapTol, what + ": центр по Y = замер эталона");
                Assert.AreEqual(want.z, rt.rect.width, MapTol, what + ": ширина ректа");
                Assert.AreEqual(want.w, rt.rect.height, MapTol, what + ": высота ректа");
                Assert.AreEqual(tilt, Mathf.DeltaAngle(0f, rt.localRotation.eulerAngles.z), 0.5f,
                    what + ": наклон");
                // Габарит НАРИСОВАННОГО корпуса (а не всего ректа: у спрайта звонка треть площади —
                // прозрачные поля вокруг дуг), повёрнутый вместе с ректом.
                var box = PhoneBodyBox(canvas, rt);
                foreach (var (n, who) in neighbours)
                    Assert.IsFalse(RefOverlap(box, n), what + ": трубка не наезжает на " + who);
            }

            // (1) Покой: спрайт без дуг, торчит из-за ЛЕВОГО края (asset-map §2 — центр 34,569).
            driver.DebugPreviewChildPhoneRest();
            Assert.AreEqual("phone-rest-v2", driver.ChildPhoneImage.sprite.name,
                "поза покоя — трубка БЕЗ красных дуг");
            AssertPose(GameDriver.PhoneRestRect, GameDriver.PhoneRestTilt, "покой");
            Assert.Less(RefCentre(canvas, rt).x - rt.rect.width / 2f, 0f,
                "покой: трубка реально уходит за левый край кадра");

            // (2) Звонок: спрайт с запечёнными дугами, выехал внутрь (asset-map §4.2 — корпус 14,351,258,384).
            driver.DebugPreviewChildCall();
            Assert.AreEqual("phone-ring-v2", driver.ChildPhoneImage.sprite.name,
                "поза звонка — трубка С запечёнными дугами");
            AssertPose(GameDriver.PhoneRingRect, GameDriver.PhoneRingTilt, "звонок");
            Assert.Greater(RefCentre(canvas, rt).x, GameDriver.PhoneRestRect.x,
                "звонок: трубка выехала ВНУТРЬ кадра относительно покоя");

            Object.Destroy(go);
            yield return null;
        }

        // MAJOR: the BLOCK$ banner and the price sub-line live INSIDE the cream field, in the band below the
        // question. With the full §8 safe box a long question printed straight through both — best-fit only
        // shrinks text to its RECT, and the rect overlapped them. Worst case: longest deck question + banner
        // + price at once, checked on the GENERATED GLYPHS (the pixels), not the rect.
        [UnityTest]
        public IEnumerator CardQuestion_ClearsTheBlockBannerAndPrice_WhenBothAreUp()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            driver.DebugPreviewBlocked();        // blocked BLOCK$ card: red banner + «цена 100 ₽», frozen
            var cardText = driver.CardRect.Find("CardText").GetComponent<Text>();
            // The deck's LONGEST question (67 chars, Resources/scenes.csv), verbatim.
            cardText.text = "Ваш ребёнок вырос и больше не нуждается в помощи. Помочь всё равно?";
            yield return null;                   // best-fit + layout settle
            yield return null;

            Assert.IsTrue(driver.BlockBanner.activeInHierarchy, "the block banner is really up for this pose");
            Assert.IsTrue(driver.CardPricePlate.gameObject.activeInHierarchy, "the price line is really up");

            // Measured in the CARD's own local px (= reference px) so a non-16:9 batch game view cannot
            // squash the card's internals and make this pass/fail for the wrong reason.
            var card = driver.CardRect;
            var bannerRt = (RectTransform)driver.BlockBanner.transform;
            var question = GlyphBoxAt(cardText, OnCardRefCentre(canvas, card, cardText.rectTransform));
            var banner = BoxAt(OnCardRefCentre(canvas, card, bannerRt), bannerRt);
            var pricePlate = BoxAt(OnCardRefCentre(canvas, card, driver.CardPricePlate.rectTransform),
                driver.CardPricePlate.rectTransform);
            var priceGlyphs = GlyphBoxAt(driver.CardPriceText,
                OnCardRefCentre(canvas, card, driver.CardPriceText.rectTransform));

            // The banner really does occupy the reserved band the fix reasons about (asset-map §8 field).
            Assert.AreEqual(GameDriver.CardBandTop, banner.T, MapTol,
                "BLOCK$-баннер стоит на верхней границе зарезервированной нижней полосы");

            Assert.IsFalse(RefOverlap(question, banner),
                "нарисованные глифы вопроса не заезжают на BLOCK$-баннер (" + question + " vs " + banner + ")");
            Assert.IsFalse(RefOverlap(question, pricePlate),
                "нарисованные глифы вопроса не заезжают на плашку цены");
            Assert.IsFalse(RefOverlap(question, priceGlyphs),
                "нарисованные глифы вопроса не заезжают на глифы строки цены");
            Assert.Less(question.B, GameDriver.CardBandTop,
                "низ вопроса поднят ВЫШЕ зарезервированной нижней полосы карточки");

            // …and nothing left the plate's cream field while doing it (asset-map §8: 467,286,984,606).
            AssertInsideCreamField(question, "длинный вопрос при баннере и цене");
            AssertInsideCreamField(banner, "BLOCK$-баннер");
            AssertInsideCreamField(pricePlate, "плашка цены");
            AssertInsideCreamField(priceGlyphs, "строка цены");

            Object.Destroy(go);
            yield return null;
        }

        // Exhaustive sprite/text enumeration sorts its lists, so it cannot see LAYER order — and every one of
        // these stacks is «green test, wrong picture» if the order flips (baked 75.1 % level showing through
        // the live fill, the banner hidden behind the card frame, the halo eclipsing the age digits).
        [UnityTest]
        public IEnumerator LayerOrder_OfTheStackedWidgets_IsExplicit()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            driver.ChildGroup.SetActive(true);

            void Below(Transform lower, Transform upper, string what)
            {
                Assert.AreSame(lower.parent, upper.parent, what + ": сравниваются siblings одного родителя");
                Assert.Less(lower.GetSiblingIndex(), upper.GetSiblingIndex(), what);
            }

            // (1) battery: baked sprite → §4 alarm repaint → cream «empty» mask → yellow top-up → §4 charge
            // band (все накладки ВЫШЕ арта), а молния — последней.
            Below(driver.BatteryImage.transform, driver.BatteryAlarmImage.transform,
                "§4: красная копия батареи рисуется ПОВЕРХ спокойной (иначе тревоги не видно)");
            Below(driver.BatteryAlarmImage.transform, driver.EnergyEmpty.transform,
                "кремовая маска рисуется ПОВЕРХ спрайта батареи (иначе виден запечённый уровень)");
            Below(driver.EnergyEmpty.transform, driver.EnergyTopUp.transform,
                "жёлтый добор рисуется поверх кремовой маски");
            Below(driver.EnergyTopUp.transform, driver.EnergyCharge.transform,
                "§4: тревожная полоса остатка заряда — поверх заливки, иначе из-под красного торчала бы "
                + "жёлтая запечённая заливка спрайта");
            Below(driver.EnergyCharge.transform, driver.EnergyBolt.transform,
                "молния рисуется ПОСЛЕДНЕЙ — целиком, на любом уровне заливки (канон §12-3), "
                + "и поэтому читается на красной батарее");

            // (1b) §4 канты баров лежат ПОЗАДИ своих плашек — тревога торчит рамкой ВОКРУГ виджета,
            // а не тонирует бар (иначе она читалась бы как «маркер в нарисованной красной зоне»).
            Below(driver.HealthAlarmKantInk.transform, driver.HealthAlarmKant.transform,
                "§4: чёрная обводка канта здоровья — ПОД красным (видна кольцом снаружи)");
            Below(driver.HealthAlarmKant.transform, driver.HealthBarImage.transform,
                "§4: кант тревоги здоровья — под баром");
            Below(driver.RelAlarmKantInk.transform, driver.RelAlarmKant.transform,
                "§4: чёрная обводка канта отношений — ПОД красным");
            Below(driver.RelAlarmKant.transform, driver.RelBarImage.transform,
                "§4: кант тревоги отношений — под баром");

            // (2) card: frame → question → BLOCK$ banner → price plate → price text.
            var card = driver.CardRect;
            Below(card.Find("CardFrame"), card.Find("CardText"), "вопрос рисуется поверх плашки карточки");
            Below(card.Find("CardText"), card.Find("BlockBanner"), "BLOCK$-баннер рисуется поверх вопроса");
            Below(card.Find("BlockBanner"), card.Find("CardPricePlate"), "плашка цены — поверх баннера");
            Below(card.Find("CardPricePlate"), card.Find("CardPrice"), "текст цены — поверх своей плашки");

            // (3) §5b: звонящая трубка — слой 5 «оверлеи» (build-spec §1.3), т.е. ВЫШЕ ряда HUD и выше
            // карточки-вопроса; иначе выехавшая трубка ныряла бы под плашку и звонок читался бы как баг.
            Below(driver.HudRow.transform, driver.ChildGroup.transform,
                "трубка ребёнка рисуется ПОВЕРХ ряда HUD");
            Below(driver.CardRect.transform, driver.ChildGroup.transform,
                "трубка ребёнка рисуется ПОВЕРХ карточки-вопроса (слой оверлеев)");

            Object.Destroy(go);
            yield return null;
        }

        // Assert the actual GENERATED glyph mesh (best-fit honoured) of `t` sits inside `plate`'s rect shrunk
        // by `pill` on all four sides — reads the drawn verts, so a vertical spill past the plate fails even
        // while a rect-corner check stays green. (Local copy of the ScreensConformanceTests helper.)
        private static void AssertGeneratedInPill(Text t, Graphic plate, float pill, string what)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.characterCountVisible, 0, what + " renders glyphs (not empty/tofu-collapsed)");

            float upp = 1f / t.pixelsPerUnit;
            float lMinX = float.MaxValue, lMaxX = float.MinValue, lMinY = float.MaxValue, lMaxY = float.MinValue;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position;
                float x = p.x * upp, y = p.y * upp;
                if (x < lMinX) lMinX = x; if (x > lMaxX) lMaxX = x;
                if (y < lMinY) lMinY = y; if (y > lMaxY) lMaxY = y;
            }

            var tr = t.rectTransform;
            var plateRt = plate.rectTransform;
            var corners = new[]
            {
                new Vector3(lMinX, lMinY, 0f), new Vector3(lMinX, lMaxY, 0f),
                new Vector3(lMaxX, lMinY, 0f), new Vector3(lMaxX, lMaxY, 0f),
            };
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in corners)
            {
                var pl = plateRt.InverseTransformPoint(tr.TransformPoint(c));
                if (pl.x < minX) minX = pl.x; if (pl.x > maxX) maxX = pl.x;
                if (pl.y < minY) minY = pl.y; if (pl.y > maxY) maxY = pl.y;
            }
            var pr = plateRt.rect;
            const float tol = 1f;
            Assert.GreaterOrEqual(minX, pr.xMin + pill - tol, what + " drawn glyphs within the plate (left)");
            Assert.LessOrEqual(maxX, pr.xMax - pill + tol, what + " drawn glyphs within the plate (right)");
            Assert.GreaterOrEqual(minY, pr.yMin + pill - tol, what + " drawn glyphs within the plate (bottom)");
            Assert.LessOrEqual(maxY, pr.yMax - pill + tol, what + " drawn glyphs within the plate (top)");
        }

        // The deck's LONGEST question (67 chars) must best-fit ENTIRELY inside the plate's cream field — the
        // safe text box of asset-map §8 is inset ~190 px horizontally and ~113 px vertically from the rect.
        [UnityTest]
        public IEnumerator CardPlate_LongestQuestion_FitsInsideTheCreamField()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);

            var cardText = driver.CardRect.Find("CardText").GetComponent<Text>();
            // The two longest real deck questions are 67/66 chars; use the longest verbatim.
            cardText.text = "Ваш ребёнок вырос и больше не нуждается в помощи. Помочь всё равно?";
            yield return null;                               // best-fit + layout settle
            yield return null;

            AssertGeneratedInPill(cardText, driver.CardFrameImage, 110f, "longest card question");
            // Ink on cream (the reference draws flat black letters on the plate, never light-on-dark).
            Assert.Less(cardText.color.r + cardText.color.g + cardText.color.b, 1.2f,
                "текст вопроса — тёмный INK на кремовом поле карточки");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ §9 · buttons swapped (visual-foundation)

        /// Signed tilt of a rect in degrees, CCW-positive (Unity z), normalised to (−180,180].
        private static float TiltZ(RectTransform rt) => Mathf.DeltaAngle(0f, rt.localEulerAngles.z);

        // The art-pack plates carry the words BAKED INTO the picture, and the PNGs have a transparent margin:
        // the drawn (alpha-tight) art covers this fraction of the Image rect — measured on the source files
        // btn-no.png 1422×685 → 1377×657 and btn-yes.png 907×594 → 876×577.
        private const float NoArtFracX = 0.9684f, NoArtFracY = 0.9591f;
        private const float YesArtFracX = 0.9658f, YesArtFracY = 0.9714f;
        // The same plates measured on the reference screen «Экран спокойный обычный.png».
        private const float NoArtRefW = 544.7f, NoArtRefH = 259.9f;
        private const float YesArtRefW = 377.1f, YesArtRefH = 248.4f;
        private const float ArtSizeTol = 6f;

        // §9 canon: on EVERY ordinary choice screen the RED «СПАСИБО, НЕ НАДО» is the LEFT plate and the GREEN
        // «ДА» is the RIGHT one — matching the cabinet levers. Both are the ART-PACK sprites with the lettering
        // baked in, so the dynamic label overlay must be HIDDEN. Tilts are the reference's asymmetric pair.
        [UnityTest]
        public IEnumerator AnswerPlates_RedLeft_GreenRight_BakedArt_ReferenceTiltsAndSizes()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            var no = OwnBounds(canvas, driver.NoPlateImage.rectTransform);
            var yes = OwnBounds(canvas, driver.YesPlateImage.rectTransform);

            // Sides: canvas-local x = 0 is the screen centre (the 960 reference column).
            Assert.Less(no.center.x, 0f, "the RED «СПАСИБО, НЕ НАДО» plate is LEFT of screen centre");
            Assert.Greater(yes.center.x, 0f, "the GREEN «ДА» plate is RIGHT of screen centre");
            Assert.Less(no.center.x, yes.center.x, "red is left of green (never the old green-left layout)");
            Assert.IsFalse(Overlap(no, yes), "the two plates never overlap each other");

            Assert.AreEqual("btn-no", driver.NoPlateImage.sprite.name,
                "the left plate is the baked RED «СПАСИБО НЕ НАДО» art (btn-no), not a code plate");
            Assert.AreEqual("btn-yes", driver.YesPlateImage.sprite.name,
                "the right plate is the baked GREEN «ДА» art (btn-yes), not a code plate");
            Assert.AreEqual(Image.Type.Simple, driver.NoPlateImage.type, "baked art draws Simple (not 9-sliced)");
            Assert.AreEqual(Image.Type.Simple, driver.YesPlateImage.type, "baked art draws Simple (not 9-sliced)");

            // The words come from the PICTURE: the dynamic overlay label is hidden in ordinary play.
            Assert.IsFalse(driver.YesPlateText.gameObject.activeInHierarchy,
                "«ДА» is baked into the art — the dynamic label must be hidden (no doubled text)");
            Assert.IsFalse(driver.NoPlateText.gameObject.activeInHierarchy,
                "«СПАСИБО, НЕ НАДО» is baked into the art — the dynamic label must be hidden");
            Assert.IsTrue(driver.YesPlateImage.color.r > 0.9f && driver.YesPlateImage.color.g > 0.9f
                && driver.YesPlateImage.color.b > 0.9f, "the baked art is drawn untinted (white) in normal play");

            // Tilts per the reference — ASYMMETRIC and signed (a mirrored sign is a visible defect).
            Assert.AreEqual(-9f, TiltZ(driver.NoPlateImage.rectTransform), 0.5f,
                "the red LEFT plate leans down-to-the-right (Unity z = −9, reference −9.05)");
            Assert.AreEqual(13f, TiltZ(driver.YesPlateImage.rectTransform), 0.5f,
                "the green RIGHT plate leans up-to-the-right (Unity z = +13, reference +13.15)");

            // Visible (drawn) size == the reference plate, ±6 px — rotation-independent (own rect × art frac).
            var noRect = driver.NoPlateImage.rectTransform.rect;
            var yesRect = driver.YesPlateImage.rectTransform.rect;
            Assert.AreEqual(NoArtRefW, noRect.width * NoArtFracX, ArtSizeTol, "drawn width of the red plate ≈ reference");
            Assert.AreEqual(NoArtRefH, noRect.height * NoArtFracY, ArtSizeTol, "drawn height of the red plate ≈ reference");
            Assert.AreEqual(YesArtRefW, yesRect.width * YesArtFracX, ArtSizeTol, "drawn width of the green plate ≈ reference");
            Assert.AreEqual(YesArtRefH, yesRect.height * YesArtFracY, ArtSizeTol, "drawn height of the green plate ≈ reference");

            Object.Destroy(go);
            yield return null;
        }

        // §8 typography: «основные надписи» (headlines, the card question, the plate labels, the HUD digits)
        // render in Arimo Bold — and that font really carries the Cyrillic + ₽ glyphs they draw (anti-tofu:
        // HasCharacter, not a glyph count, which a replacement box would satisfy).
        [UnityTest]
        public IEnumerator DisplayFont_IsArimoBold_AndCarriesCyrillicAndRouble()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);

            var display = driver.YesPlateText.font;
            Assert.IsNotNull(display, "the display font is loaded (never a null font → all tofu)");
            StringAssert.Contains("Arimo", display.name,
                "«основные надписи» use Arimo Bold (meeting-revisions §8), not Russo One");

            Assert.AreSame(display, driver.NoPlateText.font, "the decline label uses the display font");
            Assert.AreSame(display, driver.CardRect.Find("CardText").GetComponent<Text>().font,
                "the card question uses the display font");
            Assert.AreSame(display, driver.AgeText.font, "the age DIGITS use the display font");
            Assert.AreSame(display, driver.MoneyText.font, "the jar's sum uses the display font");

            const string needed = "ДЯ₽СПАБОЕНХкм0123456789";
            if (display.dynamic) display.RequestCharactersInTexture(needed, 64, FontStyle.Normal);
            foreach (char c in needed)
                Assert.IsTrue(display.HasCharacter(c),
                    "display font «" + display.name + "» has a real glyph for '" + c
                        + "' (U+" + ((int)c).ToString("X4") + ") — not a tofu box");

            // Body copy stays Rubik (a different font asset) — §8 keeps the two roles apart. The host's own
            // replies run on the BOLD cut of the same family (design gate round 3: the variable Rubik.ttf
            // rasterises at its default wght 300, half the explainer's weight), so «Rubik» is the family
            // check and the two assets are the same face at two weights.
            var body = driver.CardPriceText.font;
            var hostBody = driver.HostBubbleText.font;
            Assert.IsNotNull(body, "the body font is loaded");
            StringAssert.Contains("Rubik", body.name, "мелкий текст — Rubik (§8 «комментарии»)");
            StringAssert.Contains("Rubik", hostBody.name, "облачко Ведущего stays Rubik (§8 «комментарии»)");
            Assert.AreNotSame(display, body, "the body face is a different asset from the display face");
            Assert.AreNotSame(display, hostBody, "the host's reply face is not the display face either");

            Object.Destroy(go);
            yield return null;
        }

        // §7 background: the art-pack rays are oversized enough to cover the 1920×1080 diagonal at ANY
        // rotation angle (никогда не оголяет углы), and they spin CLOCKWISE at 6°/сек (1 turn / 60 s).
        [UnityTest]
        public IEnumerator Background_CoversTheRotationDiagonal_AndSpinsClockwise()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            var bg = driver.BackgroundImage.rectTransform;
            float diagonal = Mathf.Sqrt(1920f * 1920f + 1080f * 1080f);   // ≈2202.9 reference px
            Assert.GreaterOrEqual(Mathf.Min(bg.rect.width, bg.rect.height), diagonal,
                "the rays cover the frame diagonal — corners stay filled at every spin angle (asset-map §7)");
            // Aspect preserved: `sunburst-bg-v3` is a SQUARE synthesis, drawn 1:1 — any stretch would both
            // bend the rays and resample the texture (the crispness the design gate measured).
            Assert.AreEqual(1f, bg.rect.width / bg.rect.height, 0.01f,
                "the rays keep the synthesised texture's 1:1 aspect (no stretched rays)");
            Assert.AreEqual(bg.rect.width, driver.BackgroundImage.sprite.rect.width, 1f,
                "the rays draw 1:1 with their texture — no rescale, no resampling blur");

            // The spin centre = the screen centre; the hub IS the sprite centre, so the pivot is plain centre.
            Assert.AreEqual(0.5f, bg.anchorMin.x, 0.001f, "the rays are anchored to the screen CENTRE (x)");
            Assert.AreEqual(0.5f, bg.anchorMin.y, 0.001f, "the rays are anchored to the screen CENTRE (y)");
            Assert.AreEqual(0.5f, bg.pivot.x, 0.001f, "the ray hub is the sprite's own centre (pivot x)");
            Assert.AreEqual(0.5f, bg.pivot.y, 0.001f, "the ray hub is the sprite's own centre (pivot y)");
            Assert.AreEqual(Vector2.zero, bg.anchoredPosition, "the pivot sits exactly on the screen centre");
            var pivotOnCanvas = canvas.InverseTransformPoint(bg.position);
            Assert.AreEqual(0f, pivotOnCanvas.x, 1f, "the rotation centre is the screen centre (x)");
            Assert.AreEqual(0f, pivotOnCanvas.y, 1f, "the rotation centre is the screen centre (y)");

            // Real coverage guard: EVERY edge is at least the frame's half-diagonal away from that pivot.
            float halfDiag = 0.5f * Mathf.Sqrt(canvas.rect.width * canvas.rect.width
                                             + canvas.rect.height * canvas.rect.height);
            var p = bg.pivot;
            float w2 = bg.rect.width, h2 = bg.rect.height;
            foreach (var (dist, edge) in new[]
            {
                (p.x * w2, "left"), ((1f - p.x) * w2, "right"),
                (p.y * h2, "bottom"), ((1f - p.y) * h2, "top"),
            })
                Assert.GreaterOrEqual(dist, halfDiag,
                    "the rays reach past the frame corner in every direction (" + edge + " edge)");

            // (a) live wiring: consecutive Update frames must actually MOVE the rays, clockwise.
            float spin0 = driver.BackgroundSpinDegrees;
            float z0 = bg.localEulerAngles.z;
            yield return null;
            yield return null;
            yield return null;
            float z1 = bg.localEulerAngles.z;
            Assert.Greater(driver.BackgroundSpinDegrees, spin0, "the rays keep spinning frame to frame (Update-driven)");
            Assert.Less(Mathf.DeltaAngle(z0, z1), 0f,
                "the rendered rays turn CLOCKWISE (Unity z decreases) — не против часовой");

            // (b) exact rate through the very same code path, on a deterministic dt: 6°/сек = 1 turn / 60 s.
            float zBefore = bg.localEulerAngles.z;
            driver.DebugSpinBackground(10f);
            Assert.AreEqual(-60f, Mathf.DeltaAngle(zBefore, bg.localEulerAngles.z), 0.05f,
                "10 s of spin = 60° clockwise → 6°/сек, one revolution per 60 s (meeting-revisions §7)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
