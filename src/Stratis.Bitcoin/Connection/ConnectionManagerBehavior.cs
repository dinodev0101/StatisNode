using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol.Behaviors;
using Stratis.Bitcoin.P2P.Protocol.Payloads;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.Extensions;
using TracerAttributes;

namespace Stratis.Bitcoin.Connection
{
    public interface IConnectionManagerBehavior : INetworkPeerBehavior
    {
        bool Whitelisted { get; }

        bool OneTry { get; }
    }

    public class ConnectionManagerBehavior : NetworkPeerBehavior, IConnectionManagerBehavior
    {
        /// <summary>Logger factory to create loggers.</summary>
        private readonly ILoggerFactory loggerFactory;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>
        /// Instance logger that we use for logging of INFO level messages that are visible on the console.
        /// <para>Unlike <see cref="logger"/>, this one is created without prefix for the nicer console output.</para>
        /// </summary>
        private readonly ILogger infoLogger;

        private readonly IConnectionManager connectionManager;

        public bool Whitelisted { get; internal set; }

        public bool OneTry { get; internal set; }

        public ConnectionManagerBehavior(IConnectionManager connectionManager, ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName, $"[{this.GetHashCode():x}] ");
            this.infoLogger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.loggerFactory = loggerFactory;

            this.connectionManager = connectionManager;
        }

        [NoTrace]
        public override object Clone()
        {
            return new ConnectionManagerBehavior(this.connectionManager, this.loggerFactory)
            {
                OneTry = this.OneTry,
                Whitelisted = this.Whitelisted,
            };
        }

        [NoTrace]
        protected override void AttachCore()
        {
            this.AttachedPeer.StateChanged.Register(this.OnStateChangedAsync);

            INetworkPeer peer = this.AttachedPeer;
            if (peer != null)
            {
                if (this.connectionManager.ConnectionSettings.Whitelist.Exists(e => e.MatchIpOnly(peer.PeerEndPoint)))
                {
                    this.Whitelisted = true;
                }
            }
        }

        private Task OnStateChangedAsync(INetworkPeer peer, NetworkPeerState oldState)
        {
            try
            {
                if (peer.State == NetworkPeerState.HandShaked)
                {
                    this.connectionManager.AddConnectedPeer(peer);
                    this.infoLogger.LogInformation("Peer '{0}' connected ({1}), agent '{2}', height {3}", peer.RemoteSocketEndpoint, peer.Inbound ? "inbound" : "outbound", peer.PeerVersion.UserAgent, peer.PeerVersion.StartHeight);

                    peer.SendMessage(new SendHeadersPayload());
                }

                if ((peer.State == NetworkPeerState.Failed) || (peer.State == NetworkPeerState.Offline))
                {
                    this.infoLogger.LogInformation("Peer '{0}' ({1}) offline, reason: '{2}.{3}'", peer.RemoteSocketEndpoint, peer.Inbound ? "inbound" : "outbound", peer.DisconnectReason?.Reason ?? "unknown", peer.DisconnectReason?.Exception?.Message != null ? string.Format(" {0}.", peer.DisconnectReason.Exception.Message) : string.Empty);

                    this.connectionManager.RemoveConnectedPeer(peer, "Peer offline");
                }
            }
            catch (OperationCanceledException)
            {
            }

            return Task.CompletedTask;
        }

        [NoTrace]
        protected override void DetachCore()
        {
            this.AttachedPeer.StateChanged.Unregister(this.OnStateChangedAsync);

            if (this.AttachedPeer.Connection != null)
                this.connectionManager.PeerDisconnected(this.AttachedPeer.Connection.Id);
        }
    }
}