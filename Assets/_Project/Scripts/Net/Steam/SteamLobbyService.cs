using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace PowerCutCrew.Net
{
    public struct LobbyInfo
    {
        public ulong LobbyId;
        public string Version;
        public int Members;
        public int MaxMembers;
    }

    /// <summary>
    /// Steam lobbies through Steamworks.NET: create (friends-only by default), join by id, browse (filtered by
    /// game=powercutcrew), Steam overlay invites and the join-game rich presence. Steam is only initialised when used.
    /// </summary>
    public class SteamLobbyService : MonoBehaviour
    {
        public const string GameKey = "game";
        public const string GameValue = "powercutcrew";
        public const string VersionKey = "version";
        public const string ConnectKey = "connect";

        public bool Ready { get; private set; }
        public ulong LobbyId { get; private set; }
        public ulong HostSteamId { get; private set; }

        /// <summary>True when the last join failed because Steam reported the lobby as full.</summary>
        public bool JoinFailedFull { get; private set; }
        public string PersonaName => Ready ? SteamFriends.GetPersonaName() : string.Empty;

        /// <summary>Raised when the player accepts an invite or "join game" from the Steam overlay / friends list.</summary>
        public event Action<ulong> JoinRequested;

        private CallResult<LobbyCreated_t> _created;
        private CallResult<LobbyEnter_t> _entered;
        private CallResult<LobbyMatchList_t> _listed;
        private Callback<GameLobbyJoinRequested_t> _joinRequested;
        private Callback<GameRichPresenceJoinRequested_t> _richJoinRequested;
        private Action<bool> _done;
        private Action<List<LobbyInfo>> _listDone;

        /// <summary>Initialise the Steam API once. Returns false when Steam is not running or init fails.
        /// Deliberately never calls RestartAppIfNecessary (App ID 480 is the test app).</summary>
        public bool Init()
        {
            if (Ready) { return true; }
            try
            {
                Ready = SteamAPI.Init();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Steam] init exception: " + e.Message);
                Ready = false;
            }
            if (!Ready)
            {
                Debug.LogWarning("[Steam] SteamAPI.Init failed: is Steam running and logged in?");
                return false;
            }
            _created = CallResult<LobbyCreated_t>.Create(OnCreated);
            _entered = CallResult<LobbyEnter_t>.Create(OnEntered);
            _listed = CallResult<LobbyMatchList_t>.Create(OnListed);
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => JoinRequested?.Invoke(r.m_steamIDLobby.m_SteamID));
            _richJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichJoin);
            Debug.Log($"[Steam] initialized for {SteamFriends.GetPersonaName()} (id {SteamUser.GetSteamID().m_SteamID})");
            return true;
        }

        /// <summary>Create a lobby for up to maxMembers players with the game and version data set.</summary>
        public void CreateLobby(int maxMembers, string version, bool friendsOnly, Action<bool> done)
        {
            _done = done;
            ELobbyType type = friendsOnly ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic;
            _created.Set(SteamMatchmaking.CreateLobby(type, maxMembers));
            _pendingVersion = version;
        }

        private string _pendingVersion;

        public void JoinLobby(ulong lobbyId, Action<bool> done)
        {
            _done = done;
            _entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)));
        }

        /// <summary>List open lobbies of this game (filter game=powercutcrew).</summary>
        public void RequestLobbyList(Action<List<LobbyInfo>> done)
        {
            _listDone = done;
            SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameValue, ELobbyComparison.k_ELobbyComparisonEqual);
            _listed.Set(SteamMatchmaking.RequestLobbyList());
        }

        public void Leave()
        {
            if (!Ready || LobbyId == 0) { return; }
            SteamMatchmaking.LeaveLobby(new CSteamID(LobbyId));
            SteamFriends.ClearRichPresence();
            LobbyId = 0;
        }

        public string GetLobbyData(string key)
        {
            return Ready && LobbyId != 0 ? SteamMatchmaking.GetLobbyData(new CSteamID(LobbyId), key) : string.Empty;
        }

        private void OnCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogWarning($"[Steam] lobby creation failed: {r.m_eResult}");
                _done?.Invoke(false);
                return;
            }
            var id = new CSteamID(r.m_ulSteamIDLobby);
            LobbyId = id.m_SteamID;
            HostSteamId = SteamUser.GetSteamID().m_SteamID;
            SteamMatchmaking.SetLobbyData(id, GameKey, GameValue);
            SteamMatchmaking.SetLobbyData(id, VersionKey, _pendingVersion ?? string.Empty);
            // join-game rich presence: lets friends join from the Steam friends list
            SteamFriends.SetRichPresence(ConnectKey, "+connect_lobby " + LobbyId);
            Debug.Log($"[Steam] lobby created: id={LobbyId} {GameKey}={SteamMatchmaking.GetLobbyData(id, GameKey)} {VersionKey}={_pendingVersion} appid={SteamUtils.GetAppID()}");
            LogToFile($"lobby created: id={LobbyId} {GameKey}={GameValue} {VersionKey}={_pendingVersion}");
            _done?.Invoke(true);
        }

        private void OnEntered(LobbyEnter_t r, bool ioFailure)
        {
            var id = new CSteamID(r.m_ulSteamIDLobby);
            JoinFailedFull = r.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseFull;
            bool ok = !ioFailure && r.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess
                && SteamMatchmaking.GetLobbyData(id, GameKey) == GameValue;
            if (ok)
            {
                LobbyId = id.m_SteamID;
                HostSteamId = SteamMatchmaking.GetLobbyOwner(id).m_SteamID;
            }
            Debug.Log($"[Steam] lobby join {(ok ? "ok" : "failed")}: id={id.m_SteamID} response={r.m_EChatRoomEnterResponse}");
            _done?.Invoke(ok);
        }

        private void OnListed(LobbyMatchList_t r, bool ioFailure)
        {
            var list = new List<LobbyInfo>();
            if (!ioFailure)
            {
                for (int i = 0; i < r.m_nLobbiesMatching; i++)
                {
                    CSteamID id = SteamMatchmaking.GetLobbyByIndex(i);
                    list.Add(new LobbyInfo
                    {
                        LobbyId = id.m_SteamID,
                        Version = SteamMatchmaking.GetLobbyData(id, VersionKey),
                        Members = SteamMatchmaking.GetNumLobbyMembers(id),
                        MaxMembers = SteamMatchmaking.GetLobbyMemberLimit(id),
                    });
                }
            }
            _listDone?.Invoke(list);
        }

        /// <summary>Lobby ids go to Logs/steam-lobby.txt next to the game so a friend can be told the id.</summary>
        private static void LogToFile(string line)
        {
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "steam-lobby.txt"), DateTime.Now.ToString("s") + " " + line + Environment.NewLine);
        }

        private void OnRichJoin(GameRichPresenceJoinRequested_t r)
        {
            // connect string looks like "+connect_lobby <id>"
            string[] parts = r.m_rgchConnect.Split(' ');
            if (parts.Length == 2 && parts[0] == "+connect_lobby" && ulong.TryParse(parts[1], out ulong id))
            {
                JoinRequested?.Invoke(id);
            }
        }

        private void Update()
        {
            if (Ready) { SteamAPI.RunCallbacks(); }
        }

        private void OnDestroy()
        {
            // SteamAPI.Shutdown() is deliberately not called: the transport shuts down in its own OnDestroy and needs the API alive.
            if (Ready && LobbyId != 0) { SteamMatchmaking.LeaveLobby(new CSteamID(LobbyId)); }
        }
    }
}
