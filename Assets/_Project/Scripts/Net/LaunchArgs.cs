using System;
using System.Globalization;

namespace PowerCutCrew.Net
{
    /// <summary>Command-line arguments: -host, -client ip, -steamhost, -steamclient id, -bot, -duration s, -netsim profile, -seed n.</summary>
    public static class LaunchArgs
    {
        private static readonly string[] _args = Environment.GetCommandLineArgs();

        public static bool Has(string name) => Array.IndexOf(_args, name) >= 0;

        public static string Get(string name, string fallback = null)
        {
            int i = Array.IndexOf(_args, name);
            return i >= 0 && i + 1 < _args.Length ? _args[i + 1] : fallback;
        }

        public static float GetFloat(string name, float fallback)
        {
            return float.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        public static int GetInt(string name, int fallback) => int.TryParse(Get(name), out int v) ? v : fallback;

        public static bool Host => Has("-host");
        public static bool Client => Has("-client");
        public static bool SteamHost => Has("-steamhost");
        public static bool SteamClient => Has("-steamclient");
        public static bool Bot => Has("-bot");
        public static bool Verify => Has("-verify");
        public static float Duration => GetFloat("-duration", 0f);
        public static string NetSim => Get("-netsim", "none");
        public static int Seed => GetInt("-seed", 1);
        public static string Role => Get("-role", Host || SteamHost ? "host" : "client");
        public static bool AutoStart => Host || Client || SteamHost || SteamClient;
    }
}
