using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    public class BinArrayComparer : IComparer<byte[]>
    {
        private BinArrayComparer() { }
        static readonly public BinArrayComparer StarDict = new();

        static byte AsciiToLower(byte c) => ('A' <= c && c <= 'Z') ? (byte)(c - 'A' + 'a') : c;
        static int CompareImpl(byte[] left, byte[] right, bool caseInsensitive)
        {
            foreach (var (lc, rc) in left.Zip(right))
            {
                byte c1 = lc, c2 = rc;
                if (caseInsensitive)
                {
                    c1 = AsciiToLower(lc);
                    c2 = AsciiToLower(rc);
                }
                var d = c1 - c2;
                if (d != 0)
                    return d > 0 ? 1 : -1;
            }
            if (left.Length > right.Length)
                return 1;
            if (left.Length < right.Length)
                return -1;
            return 0;
        }
        public int Compare(byte[] left, byte[] right)
        {
            if (left == null)
                throw new ArgumentNullException(nameof(left));
            if (right == null)
                throw new ArgumentNullException(nameof(right));
            var d = CompareImpl(left, right, true);
            if (d == 0)
                return CompareImpl(left, right, false);
            else
                return d;
        }
    }
}
