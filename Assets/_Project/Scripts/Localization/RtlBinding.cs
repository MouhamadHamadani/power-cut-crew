using UnityEngine.Localization.Settings;

namespace PowerCutCrew.Localization
{
    public enum BindingStyle
    {
        /// <summary>"[E] Pick up" and, in Arabic, "التقط [E]".</summary>
        Bracket,
        /// <summary>"H: controls" and, in Arabic, "التحكم: H".</summary>
        Colon,
    }

    /// <summary>
    /// Puts a key name next to a localized text. RTLTMPro only shapes a string whose first letter is Arabic, so in Arabic the text comes first in the logical order and the key
    /// last, which draws it on the left end of the line, where the reader's eye ends up. Use it for every "key + text" label instead of reordering single strings.
    /// </summary>
    public static class RtlBinding
    {
        public static bool IsArabic => LocalizationSettings.SelectedLocale != null && LocalizationSettings.SelectedLocale.Identifier.Code == "ar";

        public static string Compose(string binding, string text, bool arabic, BindingStyle style = BindingStyle.Bracket)
        {
            if (string.IsNullOrEmpty(binding)) { return text; }
            if (arabic) { binding = ReverseParts(binding); }
            if (!arabic) { return style == BindingStyle.Bracket ? "[" + binding + "] " + text : binding + ": " + text; }
            return style == BindingStyle.Bracket ? text + " [" + binding + "]" : text + ": " + binding;
        }

        public static string Compose(string binding, string text, BindingStyle style = BindingStyle.Bracket) => Compose(binding, text, IsArabic, style);

        /// <summary>
        /// In an Arabic line each Latin word is drawn as a left-to-right run and the runs are laid out right to left, so "LMB / RT" would read "RT / LMB".
        /// The parts of a binding written in that order read the way they were meant.
        /// </summary>
        public static string ReverseParts(string binding)
        {
            const string separator = " / ";
            if (string.IsNullOrEmpty(binding) || !binding.Contains(separator)) { return binding; }
            string[] parts = binding.Split(new[] { separator }, System.StringSplitOptions.None);
            System.Array.Reverse(parts);
            return string.Join(separator, parts);
        }
    }

    /// <summary>
    /// The digits of the shown language: Arabic-Indic in Arabic, so the time, the money, the load and the summary agree with the amps. A string with Arabic letters is left
    /// alone: RTLTMPro turns its digits into Arabic-Indic ones itself and keeps each number in reading order (pre-converted digits would be reversed like letters).
    /// </summary>
    public static class ArabicDigits
    {
        public static string Convert(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            bool any = false;
            foreach (char c in text)
            {
                if (c >= '\u0621' && c <= '\u064A') { return text; }   // an Arabic letter: RTLTMPro handles this string
                if (c >= '0' && c <= '9') { any = true; }
            }
            if (!any) { return text; }
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= '0' && chars[i] <= '9') { chars[i] = (char)('٠' + (chars[i] - '0')); }
            }
            return new string(chars);
        }
    }

    /// <summary>Short names for the controls card: the Input System spells gamepad controls out ("Left Stick Press").</summary>
    public static class InputNames
    {
        public static string ShortGamepad(string display)
        {
            if (string.IsNullOrEmpty(display)) { return display; }
            switch (display)
            {
                case "Left Stick Press": return "L3";
                case "Right Stick Press": return "R3";
                case "Left Stick": return "LS";
                case "Right Stick": return "RS";
                case "Left Trigger": return "LT";
                case "Right Trigger": return "RT";
                case "Left Shoulder": return "LB";
                case "Right Shoulder": return "RB";
                case "Button South": return "A";
                case "Button East": return "B";
                case "Button West": return "X";
                case "Button North": return "Y";
                case "View": return "Select";
                default: return display;
            }
        }
    }
}
