using System;
using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// The pure host-reaction picker: NAMED line beats the tone pool, timeout ignores named lines
    /// (silence = «пропуск»), the tone heuristic (FATAL/skip/positive/risky/absurd/cautious), and the
    /// pool-bubble frequency lands in the ~30–40% band for a seeded stream.
    /// </summary>
    public class HostVoiceTests
    {
        private static Card Card(string id = "X", string hostYes = null, string hostNo = null,
            IReadOnlyList<ScaleDelta> yes = null, IReadOnlyList<ScaleDelta> no = null,
            bool fatal = false, bool rond = false, string tone = null)
        {
            return new Card
            {
                Id = id,
                HostYes = hostYes, HostNo = hostNo,
                YesDeltas = yes ?? Array.Empty<ScaleDelta>(),
                NoDeltas = no ?? Array.Empty<ScaleDelta>(),
                YesIsFatal = fatal, IsRond = rond, Tone = tone,
                Flags = Array.Empty<string>(),
            };
        }

        private static ScaleDelta Add(Scale s, int v) => new ScaleDelta(s, DeltaKind.Add, v);
        private static ScaleDelta Rand(Scale s, int v) => new ScaleDelta(s, DeltaKind.RandomPlusMinus, v);

        // A voice whose RNG always says «show» (0.0 < PoolChance) and picks pool index 0.
        private static HostVoice AlwaysShow() => new HostVoice(() => 0.0);

        // ---- tone classifier ----

        [Test]
        public void Tone_Fatal_OnYesIntoFatal()
        {
            var v = AlwaysShow();
            Assert.AreEqual(HostTone.Fatal, v.Classify(Card(fatal: true), AnswerSide.Yes));
            // …but choosing НЕТ on the same card is not fatal.
            Assert.AreNotEqual(HostTone.Fatal, v.Classify(Card(fatal: true), AnswerSide.No));
        }

        [Test]
        public void Tone_Skip_OnTimeout_Always()
        {
            var v = AlwaysShow();
            Assert.AreEqual(HostTone.Skip, v.Classify(Card(fatal: true), AnswerSide.Timeout));
            Assert.AreEqual(HostTone.Skip, v.Classify(Card(yes: new[] { Add(Scale.Health, 9) }), AnswerSide.Timeout));
        }

        [Test]
        public void Tone_Positive_OnBigNetPositive()
        {
            var v = AlwaysShow();
            var c = Card(yes: new[] { Add(Scale.Relationships, 3) });
            Assert.AreEqual(HostTone.Positive, v.Classify(c, AnswerSide.Yes));
        }

        [Test]
        public void Tone_Risky_OnMoneyLoss_HealthHit_OrGamble()
        {
            var v = AlwaysShow();
            Assert.AreEqual(HostTone.Risky, v.Classify(Card(yes: new[] { Add(Scale.Money, -3) }), AnswerSide.Yes),
                "money loss reads risky");
            Assert.AreEqual(HostTone.Risky, v.Classify(Card(yes: new[] { Add(Scale.Health, -2) }), AnswerSide.Yes),
                "health hit reads risky");
            Assert.AreEqual(HostTone.Risky, v.Classify(Card(yes: new[] { Rand(Scale.Money, 3) }), AnswerSide.Yes),
                "±N gamble reads risky");
        }

        [Test]
        public void Tone_Absurd_OnRondFlavour()
        {
            var v = AlwaysShow();
            Assert.AreEqual(HostTone.Absurd, v.Classify(Card(rond: true, yes: new[] { Add(Scale.Energy, 1) }), AnswerSide.Yes));
        }

        [Test]
        public void Tone_Cautious_OnLowImpactNo()
        {
            var v = AlwaysShow();
            Assert.AreEqual(HostTone.Cautious, v.Classify(Card(no: Array.Empty<ScaleDelta>()), AnswerSide.No));
        }

        // ---- explicit «Тон» tag (canon col 14): tag > heuristic, but fatal/skip still win ----

        [Test]
        public void ExplicitTone_BeatsHeuristic_WhenSet()
        {
            var v = AlwaysShow();
            // Δ would read «risky» (money loss), but the design tagged this card «absurd» (кек-flavour).
            var c = Card(yes: new[] { Add(Scale.Money, -3) }, tone: "absurd");
            Assert.AreEqual(HostTone.Absurd, v.Classify(c, AnswerSide.Yes),
                "explicit «Тон» tag overrides the Δ-heuristic that would say risky");
            // A «risky» tag on a card whose Δ is net-positive (heuristic would say Positive).
            var c2 = Card(yes: new[] { Add(Scale.Money, 3) }, tone: "risky");
            Assert.AreEqual(HostTone.Risky, v.Classify(c2, AnswerSide.Yes),
                "explicit «Тон» tag overrides a positive-reading Δ");
        }

        [Test]
        public void ExplicitTone_ParsesAllFourTags_CaseInsensitive()
        {
            var v = AlwaysShow();
            Assert.AreEqual(HostTone.Positive, v.Classify(Card(tone: "positive"), AnswerSide.Yes));
            Assert.AreEqual(HostTone.Risky, v.Classify(Card(tone: "RISKY"), AnswerSide.Yes));
            Assert.AreEqual(HostTone.Absurd, v.Classify(Card(tone: " Absurd "), AnswerSide.Yes));
            Assert.AreEqual(HostTone.Cautious, v.Classify(Card(tone: "cautious"), AnswerSide.No));
        }

        [Test]
        public void ExplicitTone_UnknownOrBlank_FallsThroughToHeuristic()
        {
            var v = AlwaysShow();
            // Blank / unknown tag → heuristic (money loss reads risky).
            Assert.AreEqual(HostTone.Risky, v.Classify(Card(yes: new[] { Add(Scale.Money, -3) }, tone: ""), AnswerSide.Yes));
            Assert.AreEqual(HostTone.Risky, v.Classify(Card(yes: new[] { Add(Scale.Money, -3) }, tone: null), AnswerSide.Yes));
            Assert.AreEqual(HostTone.Risky, v.Classify(Card(yes: new[] { Add(Scale.Money, -3) }, tone: "весёлый"), AnswerSide.Yes),
                "an unrecognised tag is ignored, not adopted");
        }

        [Test]
        public void ExplicitTone_DoesNotOverride_FatalOrSkip()
        {
            var v = AlwaysShow();
            // FATAL still wins over a «positive» tag on the ДА side.
            Assert.AreEqual(HostTone.Fatal, v.Classify(Card(fatal: true, tone: "positive"), AnswerSide.Yes),
                "fatal precedence beats the explicit tag");
            // Timeout still reads Skip regardless of the tag.
            Assert.AreEqual(HostTone.Skip, v.Classify(Card(tone: "positive"), AnswerSide.Timeout),
                "skip (timeout) precedence beats the explicit tag");
        }

        [Test]
        public void Named_BeatsExplicitTone_AndPool()
        {
            var v = AlwaysShow();   // rng would otherwise emit a pool line
            // Named ДА line must win even when an explicit «Тон» tag is present.
            var c = Card(hostYes: "Умница!", tone: "absurd", yes: new[] { Add(Scale.Money, -9) });
            Assert.AreEqual("Умница!", v.Pick(c, AnswerSide.Yes),
                "named line beats explicit tone (priority: named > tag > heuristic)");
        }

        // ---- canon spot-check: the real snapshot's tags flow through parse → classify ----

        [Test]
        public void CanonSnapshot_TaggedCards_ClassifyByTag_UntaggedByHeuristic()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes present");
            var v = AlwaysShow();
            var byId = new Dictionary<string, Card>();
            foreach (var card in CardLoader.ParseAll(asset.text)) byId[card.Id] = card;

            // KEK01 tagged «absurd», RND01 tagged «risky» (DELAY+FATAL → not an immediate fatal on ДА).
            Assert.AreEqual("absurd", byId["KEK01"].Tone, "KEK01 carries the absurd tag from col 14");
            Assert.AreEqual(HostTone.Absurd, v.Classify(byId["KEK01"], AnswerSide.Yes), "KEK01 pool = absurd");
            Assert.AreEqual("risky", byId["RND01"].Tone, "RND01 carries the risky tag from col 14");
            Assert.IsFalse(byId["RND01"].YesIsFatal, "RND01 is a delayed fatal, so the fatal branch is skipped on ДА");
            Assert.AreEqual(HostTone.Risky, v.Classify(byId["RND01"], AnswerSide.Yes), "RND01 pool = risky");

            // A card with no «Тон» tag still classifies purely by the heuristic (tag is null → fall through).
            Assert.IsNull(byId["I02"].Tone, "I02 has no tone tag");
        }

        // ---- source priority: named beats pool ----

        [Test]
        public void Named_Yes_BeatsPool_ExactText_RegardlessOfRng()
        {
            var v = AlwaysShow();   // rng would otherwise emit a pool line
            var c = Card(hostYes: "Умница!", hostNo: "Бунтарь, обожаю!", yes: new[] { Add(Scale.Money, -9) });
            Assert.AreEqual("Умница!", v.Pick(c, AnswerSide.Yes), "named ДА line wins even over a risky-tone pool");
            Assert.AreEqual("Бунтарь, обожаю!", v.Pick(c, AnswerSide.No), "named НЕТ line for the НЕТ side");
        }

        [Test]
        public void Named_NotUsed_OnTimeout_SkipPoolInstead()
        {
            var v = AlwaysShow();   // 0.0 < PoolChance → shows, index 0 → the first skip line
            var c = Card(hostYes: "Умница!");
            var line = v.Pick(c, AnswerSide.Timeout);
            Assert.AreNotEqual("Умница!", line, "silence must not surface the named ДА line");
            CollectionAssert.Contains(HostContent.Pool[HostTone.Skip], line, "timeout uses the skip pool");
        }

        [Test]
        public void NoNamed_NoShow_ReturnsNull()
        {
            // rng says «don't show» (1.0 >= PoolChance) → no bubble.
            var v = new HostVoice(() => 0.999999);
            Assert.IsNull(v.Pick(Card(no: Array.Empty<ScaleDelta>()), AnswerSide.No));
        }

        // ---- overall frequency band ----

        [Test]
        public void PoolBubble_Frequency_Is_30to40pct_ForNonNamedChoices()
        {
            var rng = new System.Random(12345);
            var v = new HostVoice(rng.NextDouble) { PoolChance = 0.35 };
            var c = Card(no: Array.Empty<ScaleDelta>());   // low-impact НЕТ, no named line → pool-gated

            const int N = 4000;
            int shown = 0;
            for (int i = 0; i < N; i++)
                if (v.Pick(c, AnswerSide.No) != null) shown++;

            double freq = (double)shown / N;
            Assert.That(freq, Is.InRange(0.30, 0.40),
                $"pool-bubble frequency {freq:P1} should sit in the 30–40% band (PoolChance 0.35)");
        }
    }
}
