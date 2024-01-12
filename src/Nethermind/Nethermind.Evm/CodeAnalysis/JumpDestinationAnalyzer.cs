// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nethermind.Evm.CodeAnalysis
{
    public sealed class JumpDestinationAnalyzer
    {
        private const int PUSH1 = 0x60;
        private const int PUSH32 = 0x7f;
        private const int JUMPDEST = 0x5b;
        private const int BEGINSUB = 0x5c;
        private const int BitShiftPerInt32 = 5;

        private int[]? _codeBitmap;
        public byte[] MachineCode { get; set; }

        public JumpDestinationAnalyzer(byte[] code)
        {
            MachineCode = code;
        }

        public bool ValidateJump(int destination, bool isSubroutine)
        {
            // Take array ref to local so Jit knows its size won't change in the method.
            byte[] machineCode = MachineCode;
            _codeBitmap ??= CreateJumpDestinationBitmap(machineCode);

            var result = false;
            // Cast to uint to change negative numbers to very high numbers
            // Then do length check, this both reduces check by 1 and eliminates the bounds
            // check from accessing the array.
            if ((uint)destination < (uint)machineCode.Length &&
                IsJumpDestination(_codeBitmap, destination))
            {
                // Store byte to int, as less expensive operations at word size
                int codeByte = machineCode[destination];
                if (isSubroutine)
                {
                    result = codeByte == BEGINSUB;
                }
                else
                {
                    result = codeByte == JUMPDEST;
                }
            }

            return result;
        }

        /// <summary>
        /// Used for conversion between different representations of bit array.
        /// Returns (n + (32 - 1)) / 32, rearranged to avoid arithmetic overflow.
        /// For example, in the bit to int case, the straightforward calc would
        /// be (n + 31) / 32, but that would cause overflow. So instead it's
        /// rearranged to ((n - 1) / 32) + 1.
        /// Due to sign extension, we don't need to special case for n == 0, if we use
        /// bitwise operations (since ((n - 1) >> 5) + 1 = 0).
        /// This doesn't hold true for ((n - 1) / 32) + 1, which equals 1.
        ///
        /// Usage:
        /// GetInt32ArrayLengthFromBitLength(77): returns how many ints must be
        /// allocated to store 77 bits.
        /// </summary>
        /// <param name="n"></param>
        /// <returns>how many ints are required to store n bytes</returns>
        private static int GetInt32ArrayLengthFromBitLength(int n)
        {
            return (int)((uint)(n - 1 + (1 << BitShiftPerInt32)) >> BitShiftPerInt32);
        }

        /// <summary>
        /// Collects data locations in code.
        /// An unset bit means the byte is an opcode, a set bit means it's data.
        /// </summary>
        private static int[] CreateJumpDestinationBitmap(byte[] code)
        {
            int[] bitvec = new int[GetInt32ArrayLengthFromBitLength(code.Length)];

            int pc = 0;
            while (true)
            {
                // Since we are using a non-standard for loop here
                // Changing to while(true) plus below if check elides
                // the bounds check from the array access
                if ((uint)pc >= (uint)code.Length) break;
                int instruction = code[pc];

                if (instruction >= PUSH1 && instruction <= PUSH32)
                {
                    pc += instruction - PUSH1 + 2;
                }
                else if (instruction == JUMPDEST || instruction == BEGINSUB)
                {
                    Set(bitvec, pc);
                    pc++;
                }
                else
                {
                    pc++;
                }
            }

            return bitvec;
        }

        /// <summary>
        /// Checks if the position is in a code segment.
        /// </summary>
        private static bool IsJumpDestination(int[] bitvec, int pos)
        {
            int vecIndex = pos >> BitShiftPerInt32;
            // Check if in bounds, Jit will add slightly more expensive exception throwing check if we don't
            if ((uint)vecIndex >= (uint)bitvec.Length) return false;

            return (bitvec[vecIndex] & (1 << pos)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Set(int[] bitvec, int pos)
        {
            int vecIndex = pos >> BitShiftPerInt32;
            Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(bitvec), vecIndex)
                |= 1 << pos;
        }
    }
}