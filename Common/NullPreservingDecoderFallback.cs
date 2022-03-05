using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    /// <summary>
    /// Fallback used for multi-byte encodings except UTF-X encodings
    /// </summary>
    public class NullPreservingDecoderFallback : DecoderFallback
    {
        public override int MaxCharCount => 2;

        public override DecoderFallbackBuffer CreateFallbackBuffer() => new NullPreservingDecoderFallbackBuffer();
    }

    class NullPreservingDecoderFallbackBuffer : DecoderFallbackBuffer
    {
        int fallbackCount = 2;
        int fallbackIndex = -1;

        public override bool Fallback(byte[] bytesUnknown, int _)
        {
            fallbackIndex = -1;
            fallbackCount = bytesUnknown.Last() == 0 ? 2 : 1;
            return true;
        }

        public override char GetNextChar()
        {
            fallbackCount--;
            fallbackIndex++;

            if (fallbackCount < 0)
                return '\0';
            if (fallbackCount == int.MaxValue)
            {
                fallbackCount = -1;
                return '\0';
            }

            if (fallbackIndex != 1)
                return '�';
            else
                return '␀';
        }

        public override bool MovePrevious()
        {
            if (fallbackCount >= -1 && fallbackIndex >= 0)
            {
                fallbackIndex--;
                fallbackCount++;
                return true;
            }
            return false;
        }

        public override int Remaining => (fallbackCount < 0) ? 0 : fallbackCount;
    }
}
