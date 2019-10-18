﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NewSF64Toolkit.DataStructures;
using NewSF64Toolkit.Settings;

namespace NewSF64Toolkit
{
    public struct ROMInfo
    {
        public string Title;

        public string GameID;

        public byte Version;

        public uint CRC1;

        public uint CRC2;

        public uint DMATableOffset;

        public ROMInfo(string title, string gameID, byte version, uint crc1, uint crc2, uint dmaTableOffset)
        {
            Title = title;
            GameID = gameID;
            Version = version;
            CRC1 = crc1;
            CRC2 = crc2;
            DMATableOffset = dmaTableOffset;
        }
    }

    public struct MIO0Header
    {
        public uint ID;			// always "MIO0"
        public uint OutputSize;	// decompressed data size
        public uint CompLoc;		// compressed data loc
        public uint RawLoc;		// uncompressed data loc
    }

    public static class StarFoxRomInfo
    {
        private static ROMInfo[] VALID_GAME_VERSIONS =
        {
            new ROMInfo("A","NFXJ", 0, 0xFFCAA7C1, 0x68858537, 0x0E93C0),
            new ROMInfo("Star Fox 64 (U) [v1.0]",	"NFXE", 0, 0xA7D015F8, 0x2289AA43, 0x0D9A90),
            new ROMInfo("Star Fox 64 (U) [v1.1]",	"NFXE", 1, 0xBA780BA0, 0x0F21DB34, 0x0DE480),
            new ROMInfo("Lylat Wars (E)",			"NFXP", 0, 0xF4CBE92C, 0xB392ED12, 0x0E0570),
            new ROMInfo("Lylat Wars (A)",			"NFXU", 0, 0x2483F22B, 0x136E025E, 0x0E0470)
        };



        public static uint[] EndiannessMarkers = { 0x80371240, 0x40123780, 0x37804012, 0x12408037 };

        public static int[] LevelIndexAndDMAs = { 18, 19, 26, 29, 29, 35,
                                                    30, 36, 37, 47, 53, 39, 20, 28, 34, 38, 
                                                    33, 27, 31, 12 };

        public static int[] LevelIndexAndAdvItemIndex = { 0x3D9E8, 0x2F3AC, 0x320D0, 0x27F50, 0x289FC, 0x32E10,
                                                            0x1B1D8, 0x0, 0x2AAC0, 0x37E3C, 0x9B34, 0x381D8, 0x0, 0x308B8, 0x0, 0x0,
                                                            0x0, 0x0, 0x0, 0x0 };

        public static string[] LevelNamesByIndex = new string[]
        {
            "Corneria",
            "Meteo",
            "Sector X",
            "Area 6",
            "N/A",
            "Sector Y",
            "Venom 1",
            "Solar",
            "Zoness",
            "Venom 2",
            "Training Mode",
            "Macbeth",
            "Titania",
            "Aquas",
            "Fortuna",
            "N/A",
            "Katina",
            "Bolse",
            "Sector Z",
            "Venom (Star Wolf)",
            "Corneria (Multi)"
        };

        public static int DMATableToLevelIndex(int dmaTable)
        {
            for (int i = 0; i < LevelIndexAndDMAs.Length; i++)
            {
                if (LevelIndexAndDMAs[i] == dmaTable)
                    return i;
            }

            return -1;
        }

        public static uint GetDMATableOffset(string gameID, byte version)
        {
            //Do more work here, check against the CRC, etc.
            ROMInfo matchingInfo = VALID_GAME_VERSIONS.SingleOrDefault(v => v.GameID == gameID && v.Version == version);

            if (matchingInfo.GameID == gameID)
                return matchingInfo.DMATableOffset;

            return 0x0;
        }

        public static bool IsValidVersion(string gameID, byte version)
        {
            //Do more work here, check against the CRC, etc.
            return VALID_GAME_VERSIONS.Count(v => v.GameID == gameID && v.Version == version) > 0;
        }

        public static Endianness GetEndianness(uint endianBytes)
        {
            return EndiannessMarkers.Contains(endianBytes) ? (Endianness)EndiannessMarkers.ToList().IndexOf(endianBytes) : Endianness.BigEndian;
        }

        public static uint GetEndianness(Endianness endian)
        {
            return ((int)endian) >= 0 && ((int)endian) < EndiannessMarkers.Length ? EndiannessMarkers[(int)endian] : EndiannessMarkers[0];
        }

        // MIO0 decompression code by HyperHacker (adapted from SF64Toolkit)
        public static bool DecompressMIO0(byte[] data, out byte[] outputData)
        {
            MIO0Header Header;
            byte MapByte = 0x80, CurMapByte, Length;
            ushort SData, Dist;
            uint NumBytesOutput = 0;
            uint MapLoc = 0;	// current compression map position
            uint CompLoc = 0;	// current compressed data position
            uint RawLoc = 0;	// current raw data position
            uint OutLoc = 0;	// current output position

            outputData = null;

            int i;

            Header.ID = ByteHelper.ReadUInt(data, 0);
            Header.OutputSize = ByteHelper.ReadUInt(data, 4);
            Header.CompLoc = ByteHelper.ReadUInt(data, 8);
            Header.RawLoc = ByteHelper.ReadUInt(data, 12);

            // "MIO0"
            if (Header.ID != 0x4D494F30)
            {
                return false;
            }

            byte[] MIO0Buffer = new byte[Header.OutputSize];

            MapLoc = 0x10;
            CompLoc = Header.CompLoc;
            RawLoc = Header.RawLoc;

            CurMapByte = data[MapLoc];

            while (NumBytesOutput < Header.OutputSize)
            {
                // raw
                if ((CurMapByte & MapByte) != 0x0)
                {
                    MIO0Buffer[OutLoc] = data[RawLoc]; // copy a byte to output.
                    OutLoc++;
                    RawLoc++;
                    NumBytesOutput++;
                }

                // compressed
                else
                {
                    SData = ByteHelper.ReadUShort(data, CompLoc); // get compressed data
                    Length = (byte)((SData >> 12) + 3);
                    Dist = (ushort)((SData & 0xFFF) + 1);

                    // sanity check: can't copy from before first byte
                    if (((int)OutLoc - Dist) < 0)
                    {
                        return false;
                    }

                    // copy from output
                    for (i = 0; i < Length; i++)
                    {
                        MIO0Buffer[OutLoc] = MIO0Buffer[OutLoc - Dist];
                        OutLoc++;
                        NumBytesOutput++;
                        if (NumBytesOutput >= Header.OutputSize) break;
                    }
                    CompLoc += 2;
                }

                MapByte >>= 1; // next map bit

                // if we did them all, get the next map byte
                if (MapByte == 0x0)
                {
                    MapByte = 0x80;
                    MapLoc++;
                    CurMapByte = data[MapLoc];

                    // sanity check: map pointer should never reach this
                    int Check = (int)CompLoc;
                    if (RawLoc < CompLoc) Check = (int)RawLoc;

                    if (MapLoc > Check)
                    {
                        return false;
                    }
                }
            }

            outputData = MIO0Buffer;

            return true;
        }

    }
}
