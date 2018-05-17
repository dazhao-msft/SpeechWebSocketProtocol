using System;
using System.Text;

namespace SpeechWebSocketProtocol
{
    public static class WavHeaderWriter
    {
        /// <summary>
        /// https://web.archive.org/web/20140327141505/https://ccrma.stanford.edu/courses/422/projects/WaveFormat/
        /// </summary>
        public static bool TryWritePcmWavHeader(Span<byte> destination, int numberOfChannels, int sampleRate, int bitsPerSample, int dataSize)
        {
            if (destination.Length < 44)
            {
                return false;
            }

            // chunk ID
            Encoding.ASCII.GetBytes("RIFF", destination.Slice(0, 4));

            // chunk size
            BitConverter.TryWriteBytes(destination.Slice(4, 4), dataSize + 36);

            // format
            Encoding.ASCII.GetBytes("WAVE", destination.Slice(8, 4));

            // fmt chunk ID
            Encoding.ASCII.GetBytes("fmt ", destination.Slice(12, 4));

            // fmt chunk size: 16 for PCM
            BitConverter.TryWriteBytes(destination.Slice(16, 4), 16);

            // audio format: 1 for PCM
            BitConverter.TryWriteBytes(destination.Slice(20, 2), (short)1);

            // number of channels
            BitConverter.TryWriteBytes(destination.Slice(22, 2), (short)numberOfChannels);

            // sample rate
            BitConverter.TryWriteBytes(destination.Slice(24, 4), sampleRate);

            // byte rate
            BitConverter.TryWriteBytes(destination.Slice(28, 4), sampleRate * numberOfChannels * bitsPerSample / 8);

            // block align
            BitConverter.TryWriteBytes(destination.Slice(32, 2), (short)(numberOfChannels * bitsPerSample / 8));

            // bits per sample
            BitConverter.TryWriteBytes(destination.Slice(34, 2), (short)bitsPerSample);

            // data chunk ID
            Encoding.ASCII.GetBytes("data", destination.Slice(36, 4));

            // data chunk size
            BitConverter.TryWriteBytes(destination.Slice(40, 4), dataSize);

            return true;
        }
    }
}
