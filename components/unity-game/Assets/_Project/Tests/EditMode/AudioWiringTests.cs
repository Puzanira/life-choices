using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// РАЗВОДКА ПОЛНА: у каждого события манифеста есть точка вызова в драйвере. Это ГРУБАЯ СЕТЬ
    /// ПОЛНОТЫ по исходнику <c>GameDriver.cs</c>, и она честно называет себя сетью: что событие
    /// дёргает ВЕРНЫЙ клип В ВЕРНЫЙ МОМЕНТ, проверяет поведение в PlayMode
    /// (<c>AudioDriverWiringTests</c> / <c>AudioBehaviourCoverageTests</c> — там же лежит явный
    /// список «это событие проверено ТОЛЬКО сканом» с причиной на каждое).
    ///
    /// ⚠ СКАН ИДЁТ ПО КОДУ, А НЕ ПО ТЕКСТУ ФАЙЛА (находка Codex 2026-08-08, MINOR). До этого гард
    /// искал строку «SoundEvent.X» во всём файле — и засчитывал упоминание В КОММЕНТАРИИ. Драйвер
    /// комментирован плотно, каждое событие в нём так или иначе названо словами, поэтому «разводка»
    /// могла быть выпилена целиком, а гард остался бы зелёным. Комментарии и строковые литералы
    /// вырезаются ДО поиска — теперь засчитывается только настоящий код.
    /// </summary>
    public class AudioWiringTests
    {
        private static string DriverSource()
        {
            var p = Path.Combine(Application.dataPath, "_Project", "Scripts", "GameDriver.cs");
            Assert.IsTrue(File.Exists(p), "исходник драйвера на месте: " + p);
            return StripCommentsAndStrings(File.ReadAllText(p));
        }

        /// <summary>
        /// Вырезать из C#-исходника комментарии (<c>//</c>, <c>/* */</c>) и строковые литералы
        /// (обычные, дословные <c>@""</c> и символьные), оставив ровно исполняемый код. Простой
        /// конечный автомат — препроцессора и интерполяции в драйвере нет, гадать не о чем.
        /// </summary>
        private static string StripCommentsAndStrings(string src)
        {
            var sb = new System.Text.StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char next = i + 1 < src.Length ? src[i + 1] : '\0';

                if (c == '/' && next == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;                       // встали на '/'; цикл дошагает
                    sb.Append(' ');
                    continue;
                }
                if (c == '@' && next == '"')   // дословная строка: закрывается одиночной кавычкой ("" — экран)
                {
                    i += 2;
                    while (i < src.Length)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '"') { i += 2; continue; }
                            break;
                        }
                        i++;
                    }
                    sb.Append("\"\"");
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < src.Length && src[i] != quote)
                    {
                        if (src[i] == '\\') i++;   // экранированная кавычка строку не закрывает
                        i++;
                    }
                    sb.Append(quote).Append(quote);
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Сам автомат тоже под гардом — иначе «скан по коду» тихо выродится в скан по тексту.</summary>
        [Test]
        public void TheScanner_IgnoresCommentsAndStrings()
        {
            const string sample = "a(); // Audio.Play(SoundEvent.Ghost);\n"
                                + "/* Audio.Play(SoundEvent.Ghost2); */\n"
                                + "Log(\"Audio.Play(SoundEvent.Ghost3)\");\n"
                                + "Audio.Play(SoundEvent.Real);\n";
            var code = StripCommentsAndStrings(sample);
            StringAssert.DoesNotContain("Ghost", code, "упоминание в комментарии/строке — не разводка");
            StringAssert.Contains("SoundEvent.Real", code, "настоящий вызов обязан уцелеть");
        }

        /// <summary>Три семейства уходят в драйвер не по имени, а через маппер каталога.</summary>
        private static readonly SoundEvent[] ViaHostTone =
        {
            SoundEvent.HostPositive, SoundEvent.HostRisky, SoundEvent.HostAbsurd,
            SoundEvent.HostCautious, SoundEvent.HostFatal, SoundEvent.HostSkip
        };
        private static readonly SoundEvent[] ViaAlarm =
        {
            SoundEvent.AlarmMoney, SoundEvent.AlarmEnergy, SoundEvent.AlarmHealth
        };
        private static readonly SoundEvent[] ViaFinale =
        {
            SoundEvent.FinaleOldAge, SoundEvent.FinaleBurnout, SoundEvent.FinaleFatal
        };

        private static HashSet<SoundEvent> WiredEvents()
        {
            var src = DriverSource();
            var wired = new HashSet<SoundEvent>();
            foreach (SoundEvent e in System.Enum.GetValues(typeof(SoundEvent)))
                if (src.Contains("SoundEvent." + e)) wired.Add(e);

            if (src.Contains("ForHostTone")) foreach (var e in ViaHostTone) wired.Add(e);
            if (src.Contains("ForAlarm")) foreach (var e in ViaAlarm) wired.Add(e);
            if (src.Contains("FinaleSound.EventFor")) foreach (var e in ViaFinale) wired.Add(e);
            return wired;
        }

        [Test]
        public void EverySoundEvent_IsWiredInTheDriver()
        {
            var wired = WiredEvents();
            var orphans = System.Enum.GetValues(typeof(SoundEvent)).Cast<SoundEvent>()
                                     .Where(e => !wired.Contains(e))
                                     .OrderBy(e => e.ToString()).ToList();
            CollectionAssert.IsEmpty(orphans,
                "событие манифеста есть в каталоге, но его никто не дёргает: "
                + string.Join(", ", orphans));
        }

        [Test]
        public void MapperFamilies_AreActuallyCalled()
        {
            // Отдельно и явно: если маппер выпилят, предыдущий тест покажет 6/3/3 сироты сразу —
            // а этот скажет ПОЧЕМУ.
            var src = DriverSource();
            StringAssert.Contains("ForHostTone", src, "стингеры Ведущего разводятся через ForHostTone");
            StringAssert.Contains("ForAlarm", src, "семейство тревог разводится через ForAlarm");
            StringAssert.Contains("FinaleSound.EventFor", src, "финал разводится по типу конца");
        }

        [Test]
        public void DriverPlaysOnlyThroughTheLayer()
        {
            // Единая точка: прямых PlayOneShot/AudioSource в драйвере быть не должно, иначе фильтр
            // депрессии накроет не весь микс, а то, что вспомнили.
            var src = DriverSource();
            StringAssert.DoesNotContain("PlayOneShot", src, "проигрывание идёт только через AudioLayer");
            StringAssert.DoesNotContain("AudioSource", src, "драйвер не держит собственных AudioSource");
        }

        [Test]
        public void DepressionFilter_IsDrivenFromTheDriver()
        {
            var src = DriverSource();
            StringAssert.Contains("EnterDepression", src, "вход в депрессию глушит микс");
            StringAssert.Contains("SetDepressionHits", src, "попадание возвращает ступень");
            StringAssert.Contains("ExitDepression", src, "выход снимает фильтр");
            StringAssert.Contains("ResetAll", src, "рестарт сбрасывает слой — иначе «застрявшая вата»");
        }

        /// <summary>
        /// Строки манифеста, у которых В КОДЕ НЕТ СОБЫТИЯ и которые поэтому ловятся ЛАТЧЕМ по
        /// состоянию. Тест фиксирует сам факт латча: уберут — красное, и никто не потеряет звук молча.
        /// </summary>
        [Test]
        public void StatePolledMoments_HaveLatches()
        {
            var src = DriverSource();
            StringAssert.Contains("TickAudioLatches", src, "покадровые латчи звука на месте");
            StringAssert.Contains("_audioDomeAlarmed", src, "последняя секунда купола — латч (события нет)");
            StringAssert.Contains("_audioBurnout", src, "выход из выгорания — латч (события нет)");
            StringAssert.Contains("_audioDepPulsing", src, "пульс депрессии — латч (события нет)");
            StringAssert.Contains("_audioBlitzFails", src, "провал блица — латч по счётчику (события нет)");
        }

        [Test]
        public void PauseDoesNotQueueSounds()
        {
            // Гарантия done contract §4 держится ранним возвратом в латчах, а не фильтрацией в слое.
            var src = DriverSource();
            var i = src.IndexOf("private void TickAudioLatches", System.StringComparison.Ordinal);
            Assert.Greater(i, 0, "метод латчей найден");
            var body = src.Substring(i, System.Math.Min(1400, src.Length - i));
            StringAssert.Contains("_game.Paused", body,
                "латчи обязаны стоять на паузе — иначе снятие паузы выстрелит пачкой фронтов");
        }
    }
}
