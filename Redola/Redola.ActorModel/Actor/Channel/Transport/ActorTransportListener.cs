﻿using System;
using System.Collections.Concurrent;
using System.Net;
using Cowboy.Sockets;
using Logrila.Logging;
using Redola.ActorModel.Extensions;

namespace Redola.ActorModel
{
    public class ActorTransportListener
    {
        private ILog _log = Logger.Get<ActorTransportListener>();
        private TcpSocketServer _server;
        private ConcurrentDictionary<string, ActorTransportSession> _sessions
            = new ConcurrentDictionary<string, ActorTransportSession>(); // sessionKey -> session

        public ActorTransportListener(IPEndPoint listenedEndPoint)
        {
            if (listenedEndPoint == null)
                throw new ArgumentNullException("listenedEndPoint");

            this.ListenedEndPoint = listenedEndPoint;
        }

        public IPEndPoint ListenedEndPoint { get; private set; }
        public bool IsListening { get { return _server == null ? false : _server.IsListening; } }

        public void Start()
        {
            if (IsListening)
                return;
            if (this.ListenedEndPoint.Address.Equals(IPAddress.None)
                || this.ListenedEndPoint.Address.Equals(IPAddress.IPv6None))
                return;

            try
            {
                var configuration = new TcpSocketServerConfiguration()
                {
                    SendTimeout = TimeSpan.FromSeconds(15),
                    ReceiveTimeout = TimeSpan.Zero,
                };
                _server = new TcpSocketServer(this.ListenedEndPoint, configuration);
                _server.ClientConnected += OnClientConnected;
                _server.ClientDisconnected += OnClientDisconnected;
                _server.ClientDataReceived += OnClientDataReceived;

                _log.InfoFormat("TCP server is listening to [{0}].", this.ListenedEndPoint);
                _server.Listen();
            }
            catch
            {
                _server.ClientConnected -= OnClientConnected;
                _server.ClientDisconnected -= OnClientDisconnected;
                _server.ClientDataReceived -= OnClientDataReceived;
                _server.Shutdown();
                _server = null;

                throw;
            }
        }

        public void Stop()
        {
            if (!IsListening)
                return;
            if (this.ListenedEndPoint.Address.Equals(IPAddress.None)
                || this.ListenedEndPoint.Address.Equals(IPAddress.IPv6None))
                return;

            try
            {
                _server.ClientConnected -= OnClientConnected;
                _server.ClientDisconnected -= OnClientDisconnected;
                _server.ClientDataReceived -= OnClientDataReceived;
                _server.Shutdown();
                _server = null;
            }
            catch { }
        }

        public void CloseSession(string sessionKey)
        {
            _server.CloseSession(sessionKey);
        }

        private void OnClientConnected(object sender, TcpClientConnectedEventArgs e)
        {
            _log.InfoFormat("TCP client [{0}] has connected.", e.Session.RemoteEndPoint);
            var session = new ActorTransportSession(e.Session);
            _sessions.Add(e.Session.SessionKey, session);

            if (Connected != null)
            {
                Connected(this, new ActorTransportSessionConnectedEventArgs(session));
            }
        }

        private void OnClientDisconnected(object sender, TcpClientDisconnectedEventArgs e)
        {
            _log.InfoFormat("TCP client [{0}] has disconnected.", e.Session.RemoteEndPoint);
            ActorTransportSession session = null;
            if (_sessions.TryRemove(e.Session.SessionKey, out session))
            {
                if (Disconnected != null)
                {
                    Disconnected(this, new ActorTransportSessionDisconnectedEventArgs(session));
                }
            }
        }

        private void OnClientDataReceived(object sender, TcpClientDataReceivedEventArgs e)
        {
            ActorTransportSession session = null;
            if (_sessions.TryGetValue(e.Session.SessionKey, out session))
            {
                if (DataReceived != null)
                {
                    DataReceived(this, new ActorTransportSessionDataReceivedEventArgs(session, e.Data, e.DataOffset, e.DataLength));
                }
            }
        }

        public void SendTo(string sessionKey, byte[] data)
        {
            SendTo(sessionKey, data, 0, data.Length);
        }

        public void SendTo(string sessionKey, byte[] data, int offset, int count)
        {
            if (!IsListening)
                throw new InvalidOperationException("The server has stopped to listen.");

            ActorTransportSession session = null;
            if (_sessions.TryGetValue(sessionKey, out session))
            {
                session.Send(data, offset, count);
            }
            else
            {
                _log.WarnFormat("SendTo, cannot find target session [{0}].", sessionKey);
            }
        }

        public void BeginSendTo(string sessionKey, byte[] data)
        {
            BeginSendTo(sessionKey, data, 0, data.Length);
        }

        public void BeginSendTo(string sessionKey, byte[] data, int offset, int count)
        {
            if (!IsListening)
                throw new InvalidOperationException("The server has stopped to listen.");

            ActorTransportSession session = null;
            if (_sessions.TryGetValue(sessionKey, out session))
            {
                session.BeginSend(data, offset, count);
            }
            else
            {
                _log.WarnFormat("BeginSendTo, cannot find target session [{0}].", sessionKey);
            }
        }

        public IAsyncResult BeginSendTo(string sessionKey, byte[] data, AsyncCallback callback, object state)
        {
            return BeginSendTo(sessionKey, data, 0, data.Length, callback, state);
        }

        public IAsyncResult BeginSendTo(string sessionKey, byte[] data, int offset, int count, AsyncCallback callback, object state)
        {
            if (!IsListening)
                throw new InvalidOperationException("The server has stopped to listen.");

            ActorTransportSession session = null;
            if (_sessions.TryGetValue(sessionKey, out session))
            {
                return session.BeginSend(data, offset, count, callback, state);
            }
            else
            {
                _log.WarnFormat("BeginSendTo, cannot find target session [{0}].", sessionKey);
            }

            return null;
        }

        public void EndSendTo(string sessionKey, IAsyncResult asyncResult)
        {
            ActorTransportSession session = null;
            if (_sessions.TryGetValue(sessionKey, out session))
            {
                session.EndSend(asyncResult);
            }
            else
            {
                _log.WarnFormat("EndSendTo, cannot find target session [{0}].", sessionKey);
            }
        }

        public void Broadcast(byte[] data)
        {
            if (!IsListening)
                throw new InvalidOperationException("The server has stopped to listen.");

            _server.Broadcast(data);
        }

        public void BeginBroadcast(byte[] data)
        {
            if (!IsListening)
                throw new InvalidOperationException("The server has stopped to listen.");

            _server.BeginBroadcast(data);
        }

        public event EventHandler<ActorTransportSessionConnectedEventArgs> Connected;
        public event EventHandler<ActorTransportSessionDisconnectedEventArgs> Disconnected;
        public event EventHandler<ActorTransportSessionDataReceivedEventArgs> DataReceived;
    }
}
