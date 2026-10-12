using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace PowerCutCrew.Net
{
    /// <summary>UnityTransport that counts payload bytes (Send override, OnTransportEvent). Host loopback does not use the transport.</summary>
    public class MeteredUnityTransport : UnityTransport
    {
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            NetMetrics.AddSent(payload.Count);
            base.Send(clientId, payload, networkDelivery);
        }

        private void OnEnable()
        {
            OnTransportEvent += CountReceived;
        }

        private void OnDisable()
        {
            OnTransportEvent -= CountReceived;
        }

        private static void CountReceived(NetworkEvent e, ulong clientId, ArraySegment<byte> payload, float receiveTime)
        {
            if (e == NetworkEvent.Data) { NetMetrics.AddRecv(payload.Count); }
        }
    }
}
