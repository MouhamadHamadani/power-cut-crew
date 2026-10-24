using NUnit.Framework;
using PowerCutCrew.Localization;
using RTLTMPro;

namespace PowerCutCrew.Tests.EditMode
{
    /// <summary>The Arabic number formatting runs the real RTLTMPro fixer, so the test sees what the screen would draw (the fixer's output is the left-to-right drawing order).</summary>
    public class ArabicNumbersTests
    {
        private const string Uptime = "نسبة تشغيل الكهرباء: ";
        private const string Amps = " أمبير";

        /// <summary>What the player sees, left to right, with the digits written as ASCII so the test does not depend on the digit set the fixer picks.</summary>
        private static string Drawn(string logical)
        {
            var output = new FastStringBuilder(RTLSupport.DefaultBufferSize);
            RTLSupport.FixRTL(logical, output, true, true, false);
            string s = output.ToString();
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= '٠' && chars[i] <= '٩') { chars[i] = (char)('0' + (chars[i] - '٠')); }
                else if (chars[i] >= '۰' && chars[i] <= '۹') { chars[i] = (char)('0' + (chars[i] - '۰')); }
            }
            return new string(chars);
        }

        private static string Number(string text) => Drawn(ArabicNumbers.ForRtl(text));

        [Test]
        public void ThePlainFixer_ReversesAPercentage_WhichIsTheBugThisFixes()
        {
            Assert.AreEqual("8%.19", Drawn(Uptime + "19.8%").Substring(0, 5), "the uptime line of the old summary screenshot");
        }

        [Test]
        public void ADecimalPercentage_IsDrawnWithTheArabicSeparatorAndSignInOrder()
        {
            Assert.IsTrue(Number(Uptime + "19.8%").StartsWith("19٫8٪"), Number(Uptime + "19.8%"));
            Assert.IsTrue(Number(Uptime + "100%").StartsWith("100٪"));
            Assert.IsTrue(Number(Uptime + "0.5%").StartsWith("0٫5٪"));
        }

        [Test]
        public void ADecimalNumber_KeepsItsDigitsInOrder()
        {
            Assert.IsTrue(Number("9.5 / 10" + Amps).Contains("10 / 9٫5") || Number("9.5 / 10" + Amps).Contains("9٫5"), Number("9.5 / 10" + Amps));
            string load = Number("12.25" + Amps);
            Assert.IsTrue(load.EndsWith("12٫25"), load);   // the number comes first in the logical string, so it is on the right
        }

        [Test]
        public void AClockTime_KeepsItsDigitsInOrder()
        {
            string t = Number("متبقي 9:05");
            Assert.IsTrue(t.StartsWith("9:05"), t);
            string longer = Number("متبقي 10:30");
            Assert.IsTrue(longer.StartsWith("10:30"), longer);
        }

        [Test]
        public void WholeNumbers_AndLettersAreLeftAlone()
        {
            Assert.AreEqual("مبانٍ غاضبة: 12", ArabicNumbers.ForRtl("مبانٍ غاضبة: 12"));
            Assert.AreEqual("Uptime: 19.8%", ArabicNumbers.ForRtl("Uptime: 19.8%"), "no Arabic letter: not a right-to-left string");
            Assert.AreEqual("", ArabicNumbers.ForRtl(""));
            Assert.AreEqual(null, ArabicNumbers.ForRtl(null));
        }

        [Test]
        public void ANumberFollowedByAFullStop_IsNotADecimal()
        {
            Assert.AreEqual("كان العدد 5.", ArabicNumbers.ForRtl("كان العدد 5."));
        }

        [Test]
        public void TheLogicalString_PutsTheGroupsLastFirst_AndThePercentSignBeforeTheNumber()
        {
            Assert.AreEqual("أ ٪8٫19", ArabicNumbers.ForRtl("أ 19.8%"));
            Assert.AreEqual("أ 10:2", ArabicNumbers.ForRtl("أ 2:10"));
            Assert.AreEqual("أ ٪50 و 7", ArabicNumbers.ForRtl("أ 50% و 7"));
        }

        [Test]
        public void WithoutArabicLetters_TheDigitsGetTheArabicDecimalSeparatorAndPercentSign()
        {
            Assert.AreEqual("١٩٫٨٪", ArabicDigits.Convert("19.8%"));
            Assert.AreEqual("٢:١٠", ArabicDigits.Convert("2:10"));
            Assert.AreEqual("٥٫٥ A", ArabicDigits.Convert("5.5 A"));
            Assert.AreEqual("A.B", ArabicDigits.Convert("A.B"));
        }
    }
}
