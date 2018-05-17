using System;
using System.Collections.Generic;
using System.Text;

namespace SpeechWebSocketProtocol
{
    public abstract class Message
    {
        public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

        public string Path
        {
            get
            {
                return Headers["Path"];
            }
            set
            {
                Headers["Path"] = value;
            }
        }

        public Guid RequestId
        {
            get
            {
                return Guid.Parse(Headers["X-RequestId"]);
            }
            set
            {
                Headers["X-RequestId"] = value.ToString("N");
            }
        }
    }

    public abstract class Message<T> : Message
    {
        public T Body { get; set; }
    }

    public sealed class TextMessage : Message<string>
    {
    }

    public sealed class BinaryMessage : Message<byte[]>
    {
    }

    public abstract class MessageSerializer<TMessage> where TMessage : Message
    {
        protected readonly string Delimiter = "\r\n";
        protected readonly string KeyValueDelimiter = ":";

        public abstract TMessage Deserialize(ReadOnlySpan<byte> source);

        public abstract int Serialize(TMessage message, Span<byte> destination);
    }

    public sealed class TextMessageSerializer : MessageSerializer<TextMessage>
    {
        private readonly static Encoding s_encoding = Encoding.UTF8;

        public override TextMessage Deserialize(ReadOnlySpan<byte> source)
        {
            var message = new TextMessage();

            string[] values = s_encoding.GetString(source).Split(Delimiter + Delimiter, StringSplitOptions.RemoveEmptyEntries);

            foreach (string header in values[0].Split(Delimiter, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] keyValue = header.Split(KeyValueDelimiter, StringSplitOptions.RemoveEmptyEntries);

                message.Headers.Add(keyValue[0], keyValue[1]);
            }

            message.Body = values[1];

            return message;
        }

        public override int Serialize(TextMessage message, Span<byte> destination)
        {
            int index = 0;

            foreach (var header in message.Headers)
            {
                index += s_encoding.GetBytes(header.Key, destination.Slice(index));
                index += s_encoding.GetBytes(KeyValueDelimiter, destination.Slice(index));
                index += s_encoding.GetBytes(header.Value, destination.Slice(index));
                index += s_encoding.GetBytes(Delimiter, destination.Slice(index));
            }

            index += s_encoding.GetBytes(Delimiter, destination.Slice(index));

            index += s_encoding.GetBytes(message.Body ?? string.Empty, destination.Slice(index));

            return index;
        }
    }

    public sealed class BinaryMessageSerializer : MessageSerializer<BinaryMessage>
    {
        private readonly static Encoding s_encoding = Encoding.ASCII;

        public override BinaryMessage Deserialize(ReadOnlySpan<byte> source)
        {
            var message = new BinaryMessage();

            int headersLength = source[0] << 8 | source[1];

            string headers = s_encoding.GetString(source.Slice(2, headersLength));

            foreach (string header in headers.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] keyValue = header.Split(KeyValueDelimiter, StringSplitOptions.RemoveEmptyEntries);

                message.Headers.Add(keyValue[0], keyValue[1]);
            }

            message.Body = source.Slice(2 + headersLength).ToArray();

            return message;
        }

        public override int Serialize(BinaryMessage message, Span<byte> destination)
        {
            int index = 2;

            foreach (var header in message.Headers)
            {
                index += s_encoding.GetBytes(header.Key, destination.Slice(index));
                index += s_encoding.GetBytes(KeyValueDelimiter, destination.Slice(index));
                index += s_encoding.GetBytes(header.Value, destination.Slice(index));
                index += s_encoding.GetBytes(Delimiter, destination.Slice(index));
            }

            int headersLength = index - 2;

            destination[0] = (byte)((headersLength & 0xFF00) >> 8);
            destination[1] = (byte)(headersLength & 0x00FF);

            message.Body?.AsSpan().CopyTo(destination.Slice(index));

            return index + message.Body.Length;
        }
    }
}
