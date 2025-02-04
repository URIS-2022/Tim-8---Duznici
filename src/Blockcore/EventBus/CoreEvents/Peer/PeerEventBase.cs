﻿using System.Net;


namespace Blockcore.EventBus.CoreEvents.Peer
{
    /// <summary>
    /// Base peer event.
    /// </summary>
    /// <seealso cref="EventBase" />
    public abstract class PeerEventBase : EventBase
    {
        /// <summary>
        /// Gets the peer end point.
        /// </summary>
        /// <value>
        /// The peer end point.
        /// </value>
        public IPEndPoint PeerEndPoint { get; }

        protected PeerEventBase(IPEndPoint peerEndPoint)
        {
            this.PeerEndPoint = peerEndPoint;
        }
    }
}