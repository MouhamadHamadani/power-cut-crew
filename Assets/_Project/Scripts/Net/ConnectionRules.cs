namespace PowerCutCrew.Net
{
    public readonly struct ConnectionDecision
    {
        public readonly bool Approved;

        /// <summary>UI string table key explaining the rejection; empty when approved.</summary>
        public readonly string ReasonKey;

        public ConnectionDecision(bool approved, string reasonKey)
        {
            Approved = approved;
            ReasonKey = reasonKey;
        }
    }

    /// <summary>Who may join. Plain C#, unit tested.</summary>
    public static class ConnectionRules
    {
        public const string ReasonFull = "net.error.full";
        public const string ReasonVersion = "net.error.version";

        public static ConnectionDecision Evaluate(int currentPlayers, int maxPlayers, string hostVersion, string clientVersion)
        {
            if (currentPlayers >= maxPlayers)
            {
                return new ConnectionDecision(false, ReasonFull);
            }
            if (hostVersion != clientVersion)
            {
                return new ConnectionDecision(false, ReasonVersion);
            }
            return new ConnectionDecision(true, string.Empty);
        }
    }
}
