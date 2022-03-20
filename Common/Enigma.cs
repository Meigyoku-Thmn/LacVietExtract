using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    internal class Enigma
    {
        readonly byte[] _seed;

        #region Keys
        public uint key1 = 0x13579BDF;
        public uint key2 = 0x2468ACE0;
        public uint key3 = 0xFDB97531;
        public uint key4 = 0x80000062;
        public uint key5 = 0x40000020;
        public uint key6 = 0x10000002;
        public uint key7 = 0x7FFFFFFF;
        public uint key8 = 0x3FFFFFFF;
        public uint key9 = 0xFFFFFFF;
        public uint key10 = 0x80000000;
        public uint key11 = 0xC0000000;
        public uint key12 = 0xF0000000;
        #endregion

        public Enigma(string seed)
        {
            _seed = Encoding.Latin1.GetBytes(seed);
            for (var _seedIdx = 0; _seedIdx < 4; ++_seedIdx)
            {
                key1 <<= 8;
                key1 |= _seed[_seedIdx];
                key2 <<= 8;
                key2 |= _seed[_seedIdx + 4];
                key3 <<= 8;
                key3 |= _seed[_seedIdx + 8];
            }
            if (key1 == 0)
                key1 = 0x13579BDF;
            if (key2 == 0)
                key2 = 0x2468ACE0;
            if (key3 == 0)
                key3 = 0xFDB97531;
        }

        public byte Advance()
        {
            uint v2; byte v3; uint v4; uint v5; uint v6; uint v7; uint v8; uint v9;
            uint v10; uint v11; uint v12; uint v13; uint v14; uint v15; uint v16; uint v17; uint v18;
            uint v19; byte key; byte v21; byte v22; byte v23; byte v24; byte v25; byte v26;

            v2 = key1;
            v3 = (byte)(key2 & 1);
            v21 = 0;
            v25 = v3;
            v26 = (byte)(key3 & 1);
            v4 = 2;

            while (true)
            {
                if ((v2 & 1) != 0)
                {
                    v5 = v2 ^ (key4 >> 1);
                    v6 = key2;
                    v7 = key10 | v5;
                    if ((v6 & 1) != 0)
                    {
                        key2 = key11 | (v6 ^ (key5 >> 1));
                        v3 = 1;
                        v25 = 1;
                    }
                    else
                    {
                        v3 = 0;
                        key2 = key8 & (v6 >> 1);
                        v25 = 0;
                    }
                }
                else
                {
                    v7 = key7 & (v2 >> 1);
                    v8 = key3;
                    if ((v8 & 1) != 0)
                    {
                        v26 = 1;
                        key3 = key12 | (v8 ^ (key6 >> 1));
                    }
                    else
                    {
                        v26 = 0;
                        key3 = key9 & (v8 >> 1);
                    }
                }
                v22 = (byte)((byte)(2 * v21) | (v26 ^ v3));
                if ((v7 & 1) != 0)
                {
                    v9 = v7 ^ (key4 >> 1);
                    v10 = key2;
                    v11 = key10 | v9;
                    if ((v10 & 1) != 0)
                    {
                        v25 = 1;
                        key2 = key11 | (v10 ^ (key5 >> 1));
                    }
                    else
                    {
                        v25 = 0;
                        key2 = key8 & (v10 >> 1);
                    }
                }
                else
                {
                    v11 = key7 & (v7 >> 1);
                    v12 = key3;
                    if ((v12 & 1) != 0)
                    {
                        v26 = 1;
                        key3 = key12 | (v12 ^ (key6 >> 1));
                    }
                    else
                    {
                        v26 = 0;
                        key3 = key9 & (v12 >> 1);
                    }
                }
                v23 = (byte)((byte)(2 * v22) | (v26 ^ v25));
                if ((v11 & 1) != 0)
                {
                    v13 = v11 ^ (key4 >> 1);
                    v14 = key2;
                    v15 = key10 | v13;
                    if ((v14 & 1) != 0)
                    {
                        v25 = 1;
                        key2 = key11 | (v14 ^ (key5 >> 1));
                    }
                    else
                    {
                        v25 = 0;
                        key2 = key8 & (v14 >> 1);
                    }
                }
                else
                {
                    v15 = key7 & (v11 >> 1);
                    v16 = key3;
                    if ((v16 & 1) != 0)
                    {
                        v26 = 1;
                        key3 = key12 | (v16 ^ (key6 >> 1));
                    }
                    else
                    {
                        v26 = 0;
                        key3 = key9 & (v16 >> 1);
                    }
                }
                v24 = (byte)((byte)(2 * v23) | (v26 ^ v25));
                if ((v15 & 1) != 0)
                {
                    v17 = v15 ^ (key4 >> 1);
                    v18 = key2;
                    v2 = key10 | v17;
                    if ((v18 & 1) != 0)
                    {
                        v25 = 1;
                        key2 = key11 | (v18 ^ (key5 >> 1));
                    }
                    else
                    {
                        v25 = 0;
                        key2 = key8 & (v18 >> 1);
                    }
                }
                else
                {
                    v2 = key7 & (v15 >> 1);
                    v19 = key3;
                    if ((v19 & 1) != 0)
                    {
                        v26 = 1;
                        key3 = key12 | (v19 ^ (key6 >> 1));
                    }
                    else
                    {
                        v26 = 0;
                        key3 = key9 & (v19 >> 1);
                    }
                }
                key = (byte)((byte)(2 * v24) | (v26 ^ v25));
                --v4;
                v21 = key;
                if (v4 == 0)
                    break;
                v3 = v25;
            }
            key1 = v2;

            return key;
        }
    };
}
