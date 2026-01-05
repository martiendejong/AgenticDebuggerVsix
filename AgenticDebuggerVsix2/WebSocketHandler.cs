using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AgenticDebuggerVsix
{
    internal sealed class WebSocketHandler
    {
        private readonly List<WebSocketConnection> _connections = new();
        private readonly object _lock = new();
        private readonly MetricsCollector _metrics;

        public WebSocketHandler(MetricsCollector metrics)
        {
            _metrics = metrics;
        }

        public async Task HandleWebSocketRequest(HttpListenerContext context)
        {
            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            HttpListenerWebSocketContext wsContext = null;
            try
            {
                wsContext = await context.AcceptWebSocketAsync(null);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                context.Response.StatusDescription = ex.Message;
                context.Response.Close();
                return;
            }

            var connection = new WebSocketConnection
            {
                Id = Guid.NewGuid().ToString("N"),
                WebSocket = wsContext.WebSocket,
                ConnectedAt = DateTime.UtcNow
            };

            lock (_lock)
            {
                _connections.Add(connection);
                _metrics.IncrementWebSocketConnections();
            }

            try
            {
                // Send welcome message
                await SendToConnection(connection, new
                {
                    type = "connected",
                    connectionId = connection.Id,
                    timestamp = DateTime.UtcNow
                });

                // Keep connection alive and handle incoming messages
                await ReceiveLoop(connection);
            }
            catch (Exception)
            {
                // Connection closed or error
            }
            finally
            {
                lock (_lock)
                {
                    _connections.Remove(connection);
                    _metrics.DecrementWebSocketConnections();
                }

                if (connection.WebSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await connection.WebSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            CancellationToken.None
                        );
                    }
                    catch { }
                }

                connection.WebSocket.Dispose();
            }
        }

        private async Task ReceiveLoop(WebSocketConnection connection)
        {
            var buffer = new byte[1024 * 4];

            while (connection.WebSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await connection.WebSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None
                    );
                }
                catch
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                // For now, we just receive messages (ping/pong for keepalive)
                // Future: Could handle commands sent via WebSocket
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    // Echo ping messages for keepalive
                    if (message == "ping")
                    {
                        await SendToConnection(connection, new { type = "pong" });
                    }
                }
            }
        }

        public void BroadcastStateChange(DebuggerSnapshot snapshot)
        {
            BroadcastMessage(new
            {
                type = "stateChange",
                timestamp = DateTime.UtcNow,
                snapshot = snapshot
            });
        }

        public void BroadcastBuildEvent(string eventType, string message)
        {
            BroadcastMessage(new
            {
                type = "buildEvent",
                eventType = eventType,  // "started", "completed", "failed"
                message = message,
                timestamp = DateTime.UtcNow
            });
        }

        private void BroadcastMessage(object message)
        {
            WebSocketConnection[] connectionsCopy;
            lock (_lock)
            {
                connectionsCopy = _connections.ToArray();
            }

            var tasks = connectionsCopy.Select(conn => SendToConnection(conn, message));
            Task.WhenAll(tasks).ContinueWith(t =>
            {
                // Clean up failed connections
                if (t.IsFaulted || t.Exception != null)
                {
                    lock (_lock)
                    {
                        _connections.RemoveAll(c => c.WebSocket.State != WebSocketState.Open);
                    }
                }
            });
        }

        private async Task SendToConnection(WebSocketConnection connection, object message)
        {
            if (connection.WebSocket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                var json = JsonConvert.SerializeObject(message);
                var bytes = Encoding.UTF8.GetBytes(json);

                await connection.WebSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
            }
            catch (Exception)
            {
                // Connection failed, will be cleaned up
            }
        }

        public int GetConnectionCount()
        {
            lock (_lock)
            {
                return _connections.Count;
            }
        }

        public void CloseAll()
        {
            WebSocketConnection[] connectionsCopy;
            lock (_lock)
            {
                connectionsCopy = _connections.ToArray();
                _connections.Clear();
            }

            foreach (var conn in connectionsCopy)
            {
                try
                {
                    if (conn.WebSocket.State == WebSocketState.Open)
                    {
                        conn.WebSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Server shutting down",
                            CancellationToken.None
                        ).Wait(1000);
                    }
                    conn.WebSocket.Dispose();
                }
                catch { }
            }
        }
    }

    internal sealed class WebSocketConnection
    {
        public string Id { get; set; } = "";
        public WebSocket WebSocket { get; set; }
        public DateTime ConnectedAt { get; set; }
    }
}
