// A QR Code encoder for Capri Remote's pairing code: byte mode, versions 1 to 40, all four error correction levels, and the
// mask with the lowest penalty, as the standard (ISO/IEC 18004) picks it.
//
// Ported to C# 5 from Project Nayuki's QR Code generator library, https://www.nayuki.io/page/qr-code-generator-library
// Copyright (c) Project Nayuki. (MIT License)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom
// the Software is furnished to do so, subject to the following conditions:
// - The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
//   Software.
// - The Software is provided "as is", without warranty of any kind, express or implied, including but not limited to the
//   warranties of merchantability, fitness for a particular purpose and noninfringement. In no event shall the authors or
//   copyright holders be liable for any claim, damages or other liability, whether in an action of contract, tort or
//   otherwise, arising from, out of or in connection with the Software or the use or other dealings in the Software.

using System;
using System.Collections.Generic;
using System.Text;

namespace CapriDesktop
{
    internal sealed class RemoteQr
    {
        // Error correction levels from low to high, with their two format bits.
        internal enum Level { Low = 0, Medium = 1, Quartile = 2, High = 3 }
        private static readonly int[] FormatBits = { 1, 0, 3, 2 };
        private const int PenaltyN1 = 3, PenaltyN2 = 3, PenaltyN3 = 40, PenaltyN4 = 10;

        internal readonly int Version, Size, Mask;
        internal readonly Level Correction;
        // [y, x], true for dark.
        private readonly bool[,] modules;
        private bool[,] function;

        internal bool Dark(int x, int y) { return x >= 0 && y >= 0 && x < Size && y < Size && modules[y, x]; }

        // The smallest version that holds the text's UTF-8 at least at this level, then the highest level that still fits.
        internal static RemoteQr Encode(string text, Level least = Level.Medium)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? "");
            int version, used = 0;
            for (version = 1; ; version++)
            {
                used = Bits(data.Length, version);
                if (used >= 0 && used <= DataCodewords(version, least) * 8) break;
                if (version >= 40) throw new ArgumentException("That is too long for a QR code.");
            }
            Level level = least;
            for (Level better = Level.Low; better <= Level.High; better++)
                if (better > level && used <= DataCodewords(version, better) * 8) level = better;
            List<bool> bits = new List<bool>();
            Append(bits, 0x4, 4);
            Append(bits, data.Length, version <= 9 ? 8 : 16);
            foreach (byte b in data) Append(bits, b, 8);
            int capacity = DataCodewords(version, level) * 8;
            Append(bits, 0, Math.Min(4, capacity - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);
            byte[] codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            return new RemoteQr(version, level, codewords);
        }

        // A byte segment's bits at this version, or -1 when its length doesn't fit the count field.
        private static int Bits(int length, int version)
        {
            int count = version <= 9 ? 8 : 16;
            return length >= 1 << count ? -1 : 4 + count + length * 8;
        }

        private static void Append(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        private RemoteQr(int version, Level level, byte[] data)
        {
            Version = version;
            Size = version * 4 + 17;
            Correction = level;
            modules = new bool[Size, Size];
            function = new bool[Size, Size];
            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(data));
            int best = 0, lowest = int.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                ApplyMask(i);
                DrawFormatBits(i);
                int penalty = Penalty();
                if (penalty < lowest) { best = i; lowest = penalty; }
                ApplyMask(i);
            }
            Mask = best;
            ApplyMask(best);
            DrawFormatBits(best);
            function = null;
        }

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);
            int[] align = AlignmentPositions();
            int count = align.Length;
            for (int i = 0; i < count; i++)
                for (int j = 0; j < count; j++)
                    if (!(i == 0 && j == 0 || i == 0 && j == count - 1 || i == count - 1 && j == 0)) DrawAlignment(align[i], align[j]);
            DrawFormatBits(0);
            DrawVersion();
        }

        private void DrawFormatBits(int mask)
        {
            int data = FormatBits[(int)Correction] << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
            SetFunction(8, 7, Bit(bits, 6));
            SetFunction(8, 8, Bit(bits, 7));
            SetFunction(7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));
            for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));
            SetFunction(8, Size - 8, true);
        }

        private void DrawVersion()
        {
            if (Version < 7) return;
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = Version << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = Bit(bits, i);
                int a = Size - 11 + i % 3, b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        private void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) SetFunction(xx, yy, distance != 2 && distance != 4);
                }
        }

        private void DrawAlignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++) SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        private void SetFunction(int x, int y, bool dark)
        {
            modules[y, x] = dark;
            function[y, x] = true;
        }

        private byte[] AddEccAndInterleave(byte[] data)
        {
            int level = (int)Correction;
            int blocks = EccBlocks[level, Version], eccLength = EccPerBlock[level, Version];
            int raw = RawDataModules(Version) / 8;
            int shortBlocks = blocks - raw % blocks, shortLength = raw / blocks;
            byte[][] split = new byte[blocks][];
            byte[] divisor = Divisor(eccLength);
            for (int i = 0, k = 0; i < blocks; i++)
            {
                int length = shortLength - eccLength + (i < shortBlocks ? 0 : 1);
                byte[] part = new byte[length];
                Buffer.BlockCopy(data, k, part, 0, length);
                k += length;
                byte[] block = new byte[shortLength + 1];
                Buffer.BlockCopy(part, 0, block, 0, length);
                byte[] ecc = Remainder(part, divisor);
                Buffer.BlockCopy(ecc, 0, block, block.Length - eccLength, ecc.Length);
                split[i] = block;
            }
            byte[] result = new byte[raw];
            for (int i = 0, k = 0; i < split[0].Length; i++)
                for (int j = 0; j < split.Length; j++)
                    if (i != shortLength - eccLength || j >= shortBlocks) result[k++] = split[j][i];
            return result;
        }

        private void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vertical = 0; vertical < Size; vertical++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vertical : vertical;
                        if (!function[y, x] && i < data.Length * 8)
                        {
                            modules[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert && !function[y, x]) modules[y, x] = !modules[y, x];
                }
        }

        private int Penalty()
        {
            int result = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < Size; a++)
                {
                    bool colour = false;
                    int run = 0;
                    int[] history = new int[7];
                    for (int b = 0; b < Size; b++)
                    {
                        bool here = pass == 0 ? modules[a, b] : modules[b, a];
                        if (here == colour)
                        {
                            run++;
                            if (run == 5) result += PenaltyN1;
                            else if (run > 5) result++;
                        }
                        else
                        {
                            AddHistory(run, history);
                            if (!colour) result += CountFinders(history) * PenaltyN3;
                            colour = here;
                            run = 1;
                        }
                    }
                    result += TerminateAndCount(colour, run, history) * PenaltyN3;
                }
            for (int y = 0; y < Size - 1; y++)
                for (int x = 0; x < Size - 1; x++)
                {
                    bool colour = modules[y, x];
                    if (colour == modules[y, x + 1] && colour == modules[y + 1, x] && colour == modules[y + 1, x + 1]) result += PenaltyN2;
                }
            int dark = 0;
            foreach (bool module in modules) if (module) dark++;
            int total = Size * Size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return result + k * PenaltyN4;
        }

        private int CountFinders(int[] history)
        {
            int n = history[1];
            bool core = n > 0 && history[2] == n && history[3] == n * 3 && history[4] == n && history[5] == n;
            return (core && history[0] >= n * 4 && history[6] >= n ? 1 : 0) + (core && history[6] >= n * 4 && history[0] >= n ? 1 : 0);
        }

        private int TerminateAndCount(bool colour, int run, int[] history)
        {
            if (colour)
            {
                AddHistory(run, history);
                run = 0;
            }
            run += Size;
            AddHistory(run, history);
            return CountFinders(history);
        }

        private void AddHistory(int run, int[] history)
        {
            if (history[0] == 0) run += Size;
            Array.Copy(history, 0, history, 1, history.Length - 1);
            history[0] = run;
        }

        private int[] AlignmentPositions()
        {
            if (Version == 1) return new int[0];
            int count = Version / 7 + 2;
            int step = (Version * 8 + count * 3 + 5) / (count * 4 - 4) * 2;
            int[] result = new int[count];
            result[0] = 6;
            for (int i = result.Length - 1, position = Size - 7; i >= 1; i--, position -= step) result[i] = position;
            return result;
        }

        private static int RawDataModules(int version)
        {
            int result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                int count = version / 7 + 2;
                result -= (25 * count - 10) * count - 55;
                if (version >= 7) result -= 36;
            }
            return result;
        }

        internal static int DataCodewords(int version, Level level) { return RawDataModules(version) / 8 - EccPerBlock[(int)level, version] * EccBlocks[(int)level, version]; }

        // Reed-Solomon over GF(2^8 / 0x11D) with generator 2.
        private static byte[] Divisor(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] = (byte)Multiply(result[j], root);
                    if (j + 1 < result.Length) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        private static byte[] Remainder(byte[] data, byte[] divisor)
        {
            byte[] result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
            }
            return result;
        }

        private static int Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        private static bool Bit(int value, int i) { return ((value >> i) & 1) != 0; }

        // Rows are Low, Medium, Quartile and High; columns are versions, with 0 unused.
        private static readonly int[,] EccPerBlock =
        {
            { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
            { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }
        };

        private static readonly int[,] EccBlocks =
        {
            { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
            { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
            { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
            { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 }
        };
    }
}
