using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechWebSocketProtocol
{
    public static class Program
    {
        private static readonly TextMessageSerializer s_textMessageSerializer = new TextMessageSerializer();
        private static readonly BinaryMessageSerializer s_binaryMessageSerializer = new BinaryMessageSerializer();

        public static async Task Main(string[] args)
        {
            string authorizationToken = await GetAuthorizationTokenAsync();

            using (var webSocket = new ClientWebSocket())
            {
                webSocket.Options.SetRequestHeader("X-ConnectionId", Guid.NewGuid().ToString("N"));
                webSocket.Options.SetRequestHeader("Authorization", $"Bearer {authorizationToken}");

                await webSocket.ConnectAsync(new Uri("{STT_URL}"), default);

                var cts = new CancellationTokenSource();

                await Task.WhenAll(Task.Run(() => SendAsync(webSocket, cts.Token)), Task.Run(() => ReceiveAsync(webSocket, cts.Token)));

                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cts.Token);
            }
        }

        private static async Task<string> GetAuthorizationTokenAsync()
        {
            using (var client = new HttpClient())
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, "https://westus.api.cognitive.microsoft.com/sts/v1.0/issueToken"))
                {
                    request.Headers.Add("Ocp-Apim-Subscription-Key", "{STT_KEY}");

                    using (var response = await client.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();

                        return await response.Content.ReadAsStringAsync();
                    }
                }
            }
        }

        private static async Task SendAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            Memory<byte> buffer = new byte[1024 * 1024];

            //
            // speech.config
            //
            {
                var message = new TextMessage();

                message.Path = "speech.config";
                message.RequestId = Guid.NewGuid();
                message.Headers.Add("Content-Type", "application/json;charset=utf-8");

                message.Body = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "speech.config.json"))));

                int totalLength = s_textMessageSerializer.Serialize(message, buffer.Span);

                await webSocket.SendAsync(buffer.Slice(0, totalLength), WebSocketMessageType.Text, true, cancellationToken);
            }

            //
            // send audio file
            //
            {
                var message = new BinaryMessage();

                message.Path = "audio";
                message.RequestId = Guid.NewGuid();
                message.Headers.Add("Content-Type", "audio/x-wav");

                int bodyLength = 0;
                using (var stream = File.OpenRead(Path.Combine(Directory.GetCurrentDirectory(), "data", "b0050.wav")))
                {
                    bodyLength += await stream.ReadAsync(buffer, cancellationToken);
                }

                // Rewrite wav header.
                WavHeaderWriter.TryWritePcmWavHeader(buffer.Span, 1, 16000, 16, 0);

                message.Body = buffer.Slice(0, bodyLength).ToArray();

                int totalLength = s_binaryMessageSerializer.Serialize(message, buffer.Span);

                await webSocket.SendAsync(buffer.Slice(0, totalLength), WebSocketMessageType.Binary, true, cancellationToken);
            }
        }

        private static async Task ReceiveAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            const int ReceiveBufferSize = 4 * 1024;

            Memory<byte> buffer = new byte[ReceiveBufferSize];

            while (true)
            {
                var result = await webSocket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    Console.WriteLine("---------------------------------------------------------------");

                    Console.WriteLine(Encoding.UTF8.GetString(buffer.Span.Slice(0, result.Count)));

                    Console.WriteLine("---------------------------------------------------------------");

                    Console.WriteLine();

                    var message = s_textMessageSerializer.Deserialize(buffer.Span.Slice(0, result.Count));

                    if (message.Path == "turn.end" || message.Path == "speech.endDetected")
                    {
                        var ackMessage = new TextMessage();
                        ackMessage.Path = "telemetry";
                        ackMessage.RequestId = Guid.NewGuid();

                        int count = s_textMessageSerializer.Serialize(ackMessage, buffer.Span);

                        await webSocket.SendAsync(buffer.Slice(0, count), WebSocketMessageType.Text, true, default);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // TODO
                }
                else
                {
                    throw new IndexOutOfRangeException($"Unrecognized WebSocketMessageType: {result.MessageType}.");
                }
            }
        }
    }
}
