﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Grace.Utilities
{
    /// <summary>
    /// Specify which part of the guid you want to be sequential
    /// </summary>
    public enum SequentialGuidType
    {
        /// <summary>
        /// Store as a string, useful for MySql
        /// </summary>
        SequentialAsString,

        /// <summary>
        /// Store as a binary useful for MongoDB and any other DB that stores it UUID as a binary value
        /// </summary>
        SequentialAsBinary,

        /// <summary>
        /// Store uniqueness at end useful for MSSQL as it uses the last 6 digits of the guid first
        /// </summary>
        SequentialAtEnd
    }

    /// <summary>
    /// This is a utility class for creating sequential guids down to the millisecond 
    /// </summary>
    public static class SequentialGuid
    {
        private static Random _globalRandom = new Random();

        [ThreadStatic]
        private static Random _random;

        [ThreadStatic]
        private static ushort _count;

        /// <summary>
        /// Default Type of Guid to generate
        /// </summary>
        public static SequentialGuidType DefaultType = SequentialGuidType.SequentialAsBinary;

        /// <summary>
        /// Gets the next sequential guid, depending on where you are storing will dictate which type you want
        /// Note: When called within the same millisecond there is no guarntee it will be in sequence but it will be unique
        /// </summary>
        /// <param name="guidType"></param>
        /// <returns></returns>
        public static Guid Next(SequentialGuidType? guidType = null)
        {
            if (_random == null)
            {
                _random = new Random(_globalRandom.Next());
                _count = (ushort)_random.Next();
            }

            byte[] countBytes = BitConverter.GetBytes(_count++);

            byte[] randomBytes = new byte[10 - countBytes.Length];

            _random.NextBytes(randomBytes);

            long timestamp = DateTime.UtcNow.Ticks / 10000L;
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(timestampBytes);
            }

            byte[] guidBytes = new byte[16];

            if (!guidType.HasValue)
            {
                guidType = DefaultType;
            }

            switch (guidType)
            {
                case SequentialGuidType.SequentialAsString:
                case SequentialGuidType.SequentialAsBinary:
                    Buffer.BlockCopy(timestampBytes, 2, guidBytes, 0, 6);
                    Buffer.BlockCopy(countBytes, 0, guidBytes, 6, countBytes.Length);
                    Buffer.BlockCopy(randomBytes, 0, guidBytes, 6 + countBytes.Length, randomBytes.Length);

                    // If formatting as a string, we have to reverse the order
                    // of the Data1 and Data2 blocks on little-endian systems.
                    if (guidType == SequentialGuidType.SequentialAsString && BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(guidBytes, 0, 4);
                        Array.Reverse(guidBytes, 4, 2);
                    }
                    break;

                case SequentialGuidType.SequentialAtEnd:
                    Buffer.BlockCopy(countBytes, 0, guidBytes, 0, countBytes.Length);
                    Buffer.BlockCopy(randomBytes, 0, guidBytes, countBytes.Length, randomBytes.Length);
                    Buffer.BlockCopy(timestampBytes, 2, guidBytes, 10, 6);
                    break;
            }

            return new Guid(guidBytes);
        }
    }
}
