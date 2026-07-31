using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// The NON-LINEAR value→track maps of the art-pack HUD (asset-map §11-2/§11-4) and the jar's compact
    /// money format (§11-6), as pure functions. The maps exist because the ART draws its zone borders in
    /// different places than the MECHANIC's thresholds (canon, untouched): the marker must be visually
    /// honest — a mechanically-green value has to sit in drawn green. These tests pin the knots EXACTLY,
    /// so a "simplification" back to a linear map is red immediately.
    /// </summary>
    public class HudMappingTests
    {
        // ---- relationships: 0→0, 40→20.7 %, 75→81.5 %, 100→100 % of the drawn track ----

        [Test]
        public void Relations_Knots_LandOnTheDrawnZoneBorders()
        {
            Assert.AreEqual(0.207f, GameDriver.RelationsTrackFraction(40f), 1e-5f,
                "mechanical 40 % (hold-zone floor) lands on the drawn red/green border at 20.7 %");
            Assert.AreEqual(0.815f, GameDriver.RelationsTrackFraction(75f), 1e-5f,
                "mechanical 75 % (red-zone ceiling) lands on the drawn green/red border at 81.5 %");
        }

        // ---- the END knots are the marker's TRAVEL WINDOW, not the bare ends of the rail ----
        // The bars draw a decoration at each end of their own track (skull/heart, the two faces) and a marker
        // parked on one reads as a blob — design gate round 3. So 0 and 100 stop in front of them, with the
        // gate's ≥4 px of clear air, and the pixel guard in HudConformanceTests
        // (BarMarkers_ClearTheDrawnEndDecorations_AndStayOnThePlate) is what proves the window is right.

        [Test]
        public void EndKnots_ParkTheMarkers_ClearOfTheDrawnEndDecorations()
        {
            float relLo = GameDriver.RelTrackX + GameDriver.RelationsTrackFraction(0f) * GameDriver.RelTrackW;
            float relHi = GameDriver.RelTrackX + GameDriver.RelationsTrackFraction(100f) * GameDriver.RelTrackW;
            Assert.GreaterOrEqual(relLo - GameDriver.RelMarkerW / 2f, GameDriver.RelBoyFaceRight + 4f,
                "0 отношений: левый край сердца-маркера ≥4 px правее лица мальчика");
            Assert.LessOrEqual(relHi + GameDriver.RelMarkerW / 2f, GameDriver.RelGirlFaceLeft - 4f,
                "100 отношений: правый край сердца-маркера ≥4 px левее лица девочки");

            float hpLo = GameDriver.HealthTrackX + GameDriver.HealthTrackFraction(0f) * GameDriver.HealthTrackW;
            float hpHi = GameDriver.HealthTrackX + GameDriver.HealthTrackFraction(100f) * GameDriver.HealthTrackW;
            Assert.GreaterOrEqual(hpLo - GameDriver.HealthMarkerW / 2f, GameDriver.HealthSkullRight + 4f,
                "0 здоровья: человечек ≥4 px правее черепа");
            Assert.LessOrEqual(hpHi + GameDriver.HealthMarkerW / 2f, GameDriver.HealthHeartLeft - 4f,
                "100 здоровья: человечек ≥4 px левее сердца-торца");
        }

        [Test]
        public void EndKnots_StillReadInTheRightDrawnZone()
        {
            // Cutting the travel must not cost the reading: a dead scale still sits in the drawn RED, a full
            // one in the drawn GREEN, and 100 relationships still past the drawn green/red border (red zone).
            Assert.Less(GameDriver.HealthTrackFraction(0f), 0.52f, "0 здоровья — в нарисованном красном");
            Assert.Greater(GameDriver.HealthTrackFraction(100f), 0.52f, "100 здоровья — в нарисованном зелёном");
            Assert.Less(GameDriver.RelationsTrackFraction(0f), 0.207f, "0 отношений — в левом красном");
            Assert.Greater(GameDriver.RelationsTrackFraction(100f), 0.815f, "100 отношений — в правом красном");
            // …and the scale keeps its resolution: no flat top/bottom where different values read alike.
            Assert.Greater(GameDriver.HealthTrackFraction(100f) - GameDriver.HealthTrackFraction(60f), 0.05f,
                "60 и 100 здоровья стоят в РАЗНЫХ местах (клэмп не съел верх шкалы)");
            // Relationships lose the most rail (the girl's face sits ON the track's right end), so the top
            // segment is asserted in PIXELS: 75→100 still has to MOVE the marker visibly.
            Assert.Greater((GameDriver.RelationsTrackFraction(100f) - 0.815f) * GameDriver.RelTrackW, 5f,
                "75…100 отношений двигают маркер минимум на 5 px (верх шкалы не схлопнут)");
        }

        [Test]
        public void Relations_IsPiecewiseLinear_BetweenKnots()
        {
            // Midpoint of each segment == midpoint of the segment's fractions (linear inside a segment).
            float lo = GameDriver.RelationsTrackFraction(0f), hi = GameDriver.RelationsTrackFraction(100f);
            Assert.AreEqual((lo + 0.207f) / 2f, GameDriver.RelationsTrackFraction(20f), 1e-5f,
                "0…40 segment is linear");
            Assert.AreEqual(0.511f, GameDriver.RelationsTrackFraction(57.5f), 1e-5f, "40…75 segment is linear");
            Assert.AreEqual((0.815f + hi) / 2f, GameDriver.RelationsTrackFraction(87.5f), 1e-5f,
                "75…100 segment is linear");
        }

        [Test]
        public void Relations_IsNotLinear_TheWholeWay()
        {
            // The whole point of the map: 40 must NOT read as 0.40, and 75 must NOT read as 0.75.
            Assert.Greater(Mathf.Abs(GameDriver.RelationsTrackFraction(40f) - 0.40f), 0.05f,
                "40 must not read linearly (it sits at the DRAWN border, 20.7 %)");
            Assert.Greater(Mathf.Abs(GameDriver.RelationsTrackFraction(75f) - 0.75f), 0.05f,
                "75 must not read linearly (it sits at the DRAWN border, 81.5 %)");
            Assert.Greater(Mathf.Abs(GameDriver.HealthTrackFraction(20f) - 0.20f), 0.05f,
                "health 20 must not read linearly (it sits at the DRAWN border, 52 %)");
        }

        [Test]
        public void Relations_ClampsOutsideTheScale()
        {
            Assert.AreEqual(GameDriver.RelationsTrackFraction(0f),
                GameDriver.RelationsTrackFraction(-40f), 1e-5f);
            Assert.AreEqual(GameDriver.RelationsTrackFraction(100f),
                GameDriver.RelationsTrackFraction(180f), 1e-5f);
        }

        // ---- health: 20→52 % (the drawn red/green border); the ends = the travel window ----

        [Test]
        public void Health_Knots_LandOnTheDrawnZoneBorder()
        {
            Assert.AreEqual(0.52f, GameDriver.HealthTrackFraction(20f), 1e-5f,
                "the alarm threshold 20 lands exactly on the drawn red/green border at 52 %");
        }

        [Test]
        public void Health_IsPiecewiseLinear_BetweenKnots()
        {
            float lo = GameDriver.HealthTrackFraction(0f), hi = GameDriver.HealthTrackFraction(100f);
            Assert.AreEqual((lo + 0.52f) / 2f, GameDriver.HealthTrackFraction(10f), 1e-5f,
                "0…20 segment is linear");
            Assert.AreEqual((0.52f + hi) / 2f, GameDriver.HealthTrackFraction(60f), 1e-5f,
                "20…100 segment is linear");
        }

        [Test]
        public void Health_ClampsOutsideTheScale()
        {
            Assert.AreEqual(GameDriver.HealthTrackFraction(0f), GameDriver.HealthTrackFraction(-5f), 1e-5f);
            Assert.AreEqual(GameDriver.HealthTrackFraction(100f), GameDriver.HealthTrackFraction(140f), 1e-5f);
        }

        // ---- both maps must be MONOTONE: a rising scale can never move the marker backwards ----

        [Test]
        public void BothMaps_AreMonotoneNonDecreasing_AcrossTheWholeScale()
        {
            float prevRel = -1f, prevHealth = -1f;
            for (int i = 0; i <= 1000; i++)
            {
                float v = i * 0.1f;
                float rel = GameDriver.RelationsTrackFraction(v);
                float hp = GameDriver.HealthTrackFraction(v);
                Assert.GreaterOrEqual(rel, prevRel, "relationships map never steps back at value " + v);
                Assert.GreaterOrEqual(hp, prevHealth, "health map never steps back at value " + v);
                Assert.IsTrue(rel >= 0f && rel <= 1f, "relationships fraction stays on the track at " + v);
                Assert.IsTrue(hp >= 0f && hp <= 1f, "health fraction stays on the track at " + v);
                prevRel = rel; prevHealth = hp;
            }
        }

        // ---- the jar's cream label is only 92×36 px (asset-map §8) → compact format from 5 digits up ----

        [Test]
        public void MoneyJar_ShortSums_StayLiteral()
        {
            Assert.AreEqual("₽0", GameDriver.FormatMoneyJar(0));
            Assert.AreEqual("₽7", GameDriver.FormatMoneyJar(7.9));          // floored, canon
            Assert.AreEqual("₽1240", GameDriver.FormatMoneyJar(1240));
            Assert.AreEqual("₽9999", GameDriver.FormatMoneyJar(9999));
        }

        [Test]
        public void MoneyJar_FiveDigitsAndUp_UseTheCompactThousandsForm()
        {
            Assert.AreEqual("₽12.5к", GameDriver.FormatMoneyJar(12500), "the §11-6 canon example");
            Assert.AreEqual("₽10к", GameDriver.FormatMoneyJar(10000), "no stray «.0» tail");
            Assert.AreEqual("₽999.9к", GameDriver.FormatMoneyJar(999_900));
            Assert.AreEqual("₽12.5м", GameDriver.FormatMoneyJar(12_500_000), "millions keep it short too");
        }

        [Test]
        public void MoneyJar_NegativeSums_KeepTheirSign()
        {
            Assert.AreEqual("₽-250", GameDriver.FormatMoneyJar(-250), "«в минус» is canon");
            Assert.AreEqual("-₽12.5к", GameDriver.FormatMoneyJar(-12500));
        }

        [Test]
        public void MoneyJar_EveryFormattedSum_FitsTheLabelBudget()
        {
            // The label is tiny; the compact form exists so the string never grows past ~7 glyphs (best-fit
            // then only has to shrink a little rather than collapse to an unreadable size).
            foreach (var v in new double[] { 0, 12, 999, 1240, 9999, 10_000, 12_500, 987_654, 12_500_000 })
                Assert.LessOrEqual(GameDriver.FormatMoneyJar(v).Length, 7,
                    "«" + GameDriver.FormatMoneyJar(v) + "» fits the jar label budget");
        }
    }
}
