using NUnit.Framework;
using PowerCutCrew.Localization;

namespace PowerCutCrew.Tests.EditMode
{
    public class RtlBindingTests
    {
        private const string Arabic = "التقط";

        [Test]
        public void English_PutsTheKeyFirst()
        {
            Assert.AreEqual("[E] Pick up", RtlBinding.Compose("E", "Pick up", false));
            Assert.AreEqual("H: controls", RtlBinding.Compose("H", "controls", false, BindingStyle.Colon));
        }

        [Test]
        public void Arabic_PutsTheArabicTextFirst_SoTheStringStartsWithAnArabicLetter()
        {
            string bracket = RtlBinding.Compose("E", Arabic, true);
            Assert.AreEqual(Arabic + " [E]", bracket);
            Assert.AreEqual(Arabic + ": H", RtlBinding.Compose("H", Arabic, true, BindingStyle.Colon));
            Assert.IsTrue(bracket[0] >= '؀' && bracket[0] <= 'ۿ', "RTLTMPro shapes a string only when its first letter is Arabic");
        }

        [Test]
        public void Arabic_WritesTheSlashSeparatedPartsOfABindingInReverse_SoTheyReadAsIntended()
        {
            Assert.AreEqual("RT / LMB", RtlBinding.ReverseParts("LMB / RT"));
            Assert.AreEqual("E", RtlBinding.ReverseParts("E"));
            Assert.AreEqual("C / B / A", RtlBinding.ReverseParts("A / B / C"));
            Assert.AreEqual(Arabic + " [RT / LMB]", RtlBinding.Compose("LMB / RT", Arabic, true));
            Assert.AreEqual("[LMB / RT] text", RtlBinding.Compose("LMB / RT", "text", false), "English keeps the order");
        }

        [Test]
        public void NoBinding_LeavesTheTextAlone()
        {
            Assert.AreEqual(Arabic, RtlBinding.Compose("", Arabic, true));
            Assert.AreEqual("Pick up", RtlBinding.Compose(null, "Pick up", false));
        }

        [Test]
        public void ArabicDigits_ConvertEveryAsciiDigit_AndNothingElse()
        {
            Assert.AreEqual("٢:١٠", ArabicDigits.Convert("2:10"));
            Assert.AreEqual("٣٤ $", ArabicDigits.Convert("34 $"));
            Assert.AreEqual("٥٫٥ A", ArabicDigits.Convert("5.5 A"));
            Assert.AreEqual("Space / A", ArabicDigits.Convert("Space / A"));
            Assert.AreEqual("10 \u0623\u0645\u0628\u064A\u0631", ArabicDigits.Convert("10 \u0623\u0645\u0628\u064A\u0631"), "a string with Arabic letters is left to RTLTMPro, which keeps numbers in reading order");
            Assert.AreEqual("", ArabicDigits.Convert(""));
            Assert.AreEqual("٠١٢٣٤٥٦٧٨٩", ArabicDigits.Convert("0123456789"));
        }

        [Test]
        public void GamepadNamesAreShort()
        {
            Assert.AreEqual("L3", InputNames.ShortGamepad("Left Stick Press"));
            Assert.AreEqual("R3", InputNames.ShortGamepad("Right Stick Press"));
            Assert.AreEqual("LS", InputNames.ShortGamepad("Left Stick"));
            Assert.AreEqual("RS", InputNames.ShortGamepad("Right Stick"));
            Assert.AreEqual("LT", InputNames.ShortGamepad("Left Trigger"));
            Assert.AreEqual("RT", InputNames.ShortGamepad("Right Trigger"));
            Assert.AreEqual("A", InputNames.ShortGamepad("Button South"));
            Assert.AreEqual("Select", InputNames.ShortGamepad("View"));
            Assert.AreEqual("Select", InputNames.ShortGamepad("Select"));
            Assert.AreEqual("X", InputNames.ShortGamepad("X"));
        }
    }
}
