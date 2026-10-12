using PowerCutCrew.Localization;
using PowerCutCrew.Net;
using TMPro;
using UnityEngine;

namespace PowerCutCrew.UI
{
    /// <summary>Temporary Play panel: Host (Local), Join (IP), Host (Steam), Back. Replaced by the real lobby UI later.</summary>
    public class NetPlayPanel : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private GameObject _mainButtons;
        [SerializeField] private TMP_InputField _address;
        [SerializeField] private LocalizedRtlText _message;

        private void Start()
        {
            _panel.SetActive(false);
            if (!string.IsNullOrEmpty(NetSession.PendingMenuMessageKey))
            {
                _panel.SetActive(true);
                _mainButtons.SetActive(false);
                _message.SetKey(NetSession.PendingMenuMessageKey);
                NetSession.PendingMenuMessageKey = null;
            }
            if (NetSession.Instance != null) { NetSession.Instance.ErrorRaised += ShowError; }
        }

        private void OnDestroy()
        {
            if (NetSession.Instance != null) { NetSession.Instance.ErrorRaised -= ShowError; }
        }

        public void Open()
        {
            _panel.SetActive(true);
            _mainButtons.SetActive(false);
            _message.SetKey(null);
        }

        public void Back()
        {
            _panel.SetActive(false);
            _mainButtons.SetActive(true);
            _message.SetKey(null);
        }

        public void HostLocal()
        {
            _message.SetKey("net.connecting");
            NetSession.Instance.StartHost(ConnectionMode.Local);
        }

        public void HostSteam()
        {
            _message.SetKey("net.connecting");
            NetSession.Instance.StartHost(ConnectionMode.Steam);
        }

        public void Join()
        {
            _message.SetKey("net.connecting");
            NetSession.Instance.StartClient(ConnectionMode.Local, string.IsNullOrWhiteSpace(_address.text) ? "127.0.0.1" : _address.text.Trim());
        }

        private void ShowError(string key)
        {
            _panel.SetActive(true);
            _mainButtons.SetActive(false);
            _message.SetKey(key);
        }
    }
}
