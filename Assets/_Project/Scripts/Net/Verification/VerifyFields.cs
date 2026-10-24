using System.Collections.Generic;
using System.Text;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// What one host-side comparison of a client report found, field by field (plain C#, unit tested). A field the host is still changing is skipped on its own;
    /// every other field of the report is compared, so a busy street no longer throws a whole report away.
    /// </summary>
    public sealed class VerifyFields
    {
        private readonly StringBuilder _diff = new StringBuilder();
        private readonly List<string> _skippedNames = new List<string>();

        public int Compared { get; private set; }
        public int Skipped => _skippedNames.Count;
        public IReadOnlyList<string> SkippedNames => _skippedNames;
        public bool Differs => _diff.Length > 0;

        /// <summary>The differences found, or null when everything compared matched.</summary>
        public string Diff => _diff.Length == 0 ? null : _diff.ToString();

        /// <summary>Compare one field. <paramref name="changing"/>: the host value moved lately, so there is nothing to compare yet.</summary>
        public void Field(string name, bool changing, bool matches, string detail = null)
        {
            if (changing) { _skippedNames.Add(name); return; }
            Compared++;
            if (!matches) { _diff.Append(detail ?? name).Append("; "); }
        }

        /// <summary>A difference found by a checker that does its own comparing (counts as one compared field).</summary>
        public void Fail(string detail)
        {
            Compared++;
            _diff.Append(detail);
        }

        public string SkippedSummary => _skippedNames.Count == 0 ? "-" : string.Join(",", _skippedNames);
    }

    /// <summary>
    /// Host: when each named value last changed, so a report is only compared for the fields that have been quiet for a moment (plain C#, unit tested).
    /// Feed it every frame with <see cref="Observe"/>; ask <see cref="Changing"/> when a report comes in.
    /// </summary>
    public sealed class ChangeClock
    {
        private readonly Dictionary<string, string> _value = new Dictionary<string, string>();
        private readonly Dictionary<string, float> _changedAt = new Dictionary<string, float>();

        public void Observe(string key, string value, float now)
        {
            if (_value.TryGetValue(key, out string old) && old == value) { return; }
            _value[key] = value;
            _changedAt[key] = now;
        }

        /// <summary>True when the value changed less than <paramref name="quietSeconds"/> ago. A value that was never observed counts as quiet.</summary>
        public bool Changing(string key, float now, float quietSeconds)
        {
            return _changedAt.TryGetValue(key, out float at) && now - at < quietSeconds;
        }

        public void Clear()
        {
            _value.Clear();
            _changedAt.Clear();
        }
    }
}
