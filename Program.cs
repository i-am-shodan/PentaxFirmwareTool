using FirmwareTools.Properties;
using NDesk.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FirmwareTools
{
    /// <summary>
    /// Main class
    /// </summary>
    class Program
    {
        /// <summary>
        /// Struct which contains data for specific camera versions
        /// </summary>
        struct CameraInfo
        {
            public string Name;
            public int DebugID;
            // strings in the firmware we can shorten to adjust the checksum
            // mode,address (address+4) == end of string
            public Dictionary<int, int> SafeFirmwareChecksumLocations;
        }

        /// <summary>
        /// Returns a CameraInfo struct when given a camera model
        /// Currently only supports the K-30
        /// </summary>
        /// <param name="name">Camera name, such as K-30</param>
        /// <returns>CameraInfo</returns>
        static CameraInfo GetCameraInfo(string name)
        {
            CameraInfo ci = new CameraInfo();
            ci.Name = "Pentax K-30";
            ci.DebugID = 524;
            ci.SafeFirmwareChecksumLocations = new Dictionary<int, int>();
            ci.SafeFirmwareChecksumLocations.Add(0, 0x2C58E8); // pentax unused (C) - 0xA02C58E8
            ci.SafeFirmwareChecksumLocations.Add(1, 0x8E5BF8); // create recovery fw msg - 0xA08E5BF8

            return ci;
        }

        /// <summary>
        /// Main function
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            bool checksum = false;
            bool fixChecksum = false;
            bool encrypt = false;
            bool decrypt = false;
            bool usage = false;
            string inFile = string.Empty;
            string outFile = string.Empty;

            var p = new OptionSet() {
                { "c|checksum", "Checks the firmware for validity", v => checksum = true },
                { "f|fix", "Fix the checksum of a modified firmware",  var => fixChecksum = true },
                { "d|decrypt", "Decrypt a firmware image", v => decrypt = true },
                { "e|encrypt",  "Encrypt a firmware image", v => encrypt = true },
                { "h|help", "Show help message", v => usage = true },
                { "i|in=", "Input file", v => inFile = v },
                { "o|out=", "Output file", v => outFile = v },
            };

            List<string> extra;
            try
            {
                extra = p.Parse(args);

                int modeCount = 0;

                // ensure only a single mode is set
                if (checksum) { ++modeCount; }
                if (fixChecksum) { ++modeCount; }
                if (decrypt) { ++modeCount; }
                if (encrypt) { ++modeCount; }
                if (usage) { ++modeCount; }

                if (modeCount > 1)
                {
                    throw new OptionException("More than one mode set", "");
                }
            }
            catch (OptionException e)
            {
                Console.Write("pftool: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `pftool --help' for more information.");
                return;
            }

            if (usage)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("checksum: pftool -c in=fwdc215b.bin");
                Console.WriteLine("fix checksum: pftool -f in=fwdc215b.bin out=fixed.bin");
                Console.WriteLine("decrypt fw: pftool -d in=fwdc215b.bin out=decryted.bin");
                Console.WriteLine("encrypt fw: pftool -e in=fwdc215b.bin out=encrypted.bin");
                Console.WriteLine("help: pftool -h");
                return;
            }

            var inFI = new FileInfo(inFile);
            var firmware = File.ReadAllBytes(inFI.FullName);
            var camera = GetCameraInfo("K-30");

            if (checksum) 
            {
                var valueA = Checksum(firmware, 0, camera.DebugID);
                var valueB = Checksum(firmware, 1, camera.DebugID);
                if (valueA == 0 && valueB == 0)
                {
                    Console.WriteLine("Checksum correct");
                }
                else
                {
                    Console.WriteLine("Checksum invalid");
                    Environment.ExitCode = 1;
                }
            }
            else
            {
                var outFI = new FileInfo(outFile);

                if (fixChecksum) 
                {
                    var valueA = Checksum(firmware, 0, camera.DebugID);
                    var valueB = Checksum(firmware, 1, camera.DebugID);

                    if (valueA == 0 && valueB == 0)
                    {
                        Console.WriteLine("Firmware checksums are already correct");
                    }
                    else
                    {
                        if (valueA != 0)
                        {
                            AdjustChecksumValue(ref firmware, 0, camera);
                        }

                        if (valueB != 0)
                        {
                            AdjustChecksumValue(ref firmware, 1, camera);
                        }

                        valueA = Checksum(firmware, 0, camera.DebugID);
                        valueB = Checksum(firmware, 1, camera.DebugID);

                        if (valueA != 0 || valueB != 0)
                        {
                            throw new InvalidDataException();
                        }
                    }

                    File.WriteAllBytes(outFI.FullName, firmware);

                }
                else if (decrypt) 
                { 
                    XORwithKey(inFI, outFI); 
                }
                else if (encrypt) 
                {
                    XORwithKey(inFI, outFI); 
                }
            }

        }

        /// <summary>
        /// Corrects the checksum of the firmware
        /// </summary>
        /// <param name="firmware"></param>
        /// <param name="index"></param>
        /// <param name="camera"></param>
        private static void AdjustChecksumValue(ref byte[] firmware, int index, CameraInfo camera)
        {
            var offsetToSafeStr = camera.SafeFirmwareChecksumLocations[index];
            var offsetToChecksumVar = offsetToSafeStr + 4;
            SetValue(ref firmware, offsetToSafeStr, 0); // terminate the string
            SetValue(ref firmware, offsetToChecksumVar, 0); // we now have 4 bytes to play with

            var newCheckSumValue = Checksum(firmware, 0, camera.DebugID);
            newCheckSumValue = UInt32.MaxValue - newCheckSumValue + 1;
            SetValue(ref firmware, offsetToChecksumVar, newCheckSumValue);
        }

        /// <summary>
        /// Sets a value in the firmware as big endian
        /// </summary>
        /// <param name="firmware"></param>
        /// <param name="offsetToChecksumVar"></param>
        /// <param name="newCheckSumValue"></param>
        private static void SetValue(ref byte[] firmware, int offsetToChecksumVar, UInt32 newCheckSumValue)
        {
            var newBytes = ReverseBytes(newCheckSumValue);
            var data = BitConverter.GetBytes(newBytes);
            Buffer.BlockCopy(data, 0, firmware, offsetToChecksumVar, data.Length);
        }

        /// <summary>
        /// reverse byte order of a UInt32 (32-bit)
        /// </summary>
        /// <param name="value">UInt32</param>
        /// <returns>UInt32</returns>
        public static UInt32 ReverseBytes(UInt32 value)
        {
            return (value & 0x000000FFU) << 24 | (value & 0x0000FF00U) << 8 |
                   (value & 0x00FF0000U) >> 8 | (value & 0xFF000000U) >> 24;
        }

        /// <summary>
        /// Gets a Little Endian into from a Big Endian byte[]
        /// </summary>
        /// <param name="firmware">buffer</param>
        /// <param name="offset">offset into buffer</param>
        /// <returns>UInt32</returns>
        public static UInt32 GetValueFromFirmware(ref byte[] firmware, int offset)
        {
            var tmp = BitConverter.ToUInt32(firmware, offset);
            return ReverseBytes(tmp);
        }

        /// <summary>
        /// Returns the checksum of the firmware
        /// </summary>
        /// <param name="firmware">the firmware</param>
        /// <param name="index">Firmware mode. Only 0 and 1 are currently tested</param>
        /// <param name="cameraDebugId">Camera Debug Id</param>
        /// <returns>firmware checksum, 0 == success</returns>
        public static UInt32 Checksum(byte[] firmware, int index, int cameraDebugId)
        {
            int[] cameraDebugIdLocations = { 0x788, 0x600088, 0x788, 0x4FFF0, 0 };

            var debugIdIndex = 2 * cameraDebugIdLocations[index];
            var cameraDebugIdValueInFirmware = GetValueFromFirmware(ref firmware, debugIdIndex);
            if (cameraDebugIdValueInFirmware == cameraDebugId)
            {
                var counter = 0;
                // Do some initial checks for magic values in the firmware
                var cameraMagicChecksumValue = 0x10001 * cameraDebugId;
                do
                {
                    int[] locationOfCheckSumMagicValueLocations = {0x7FC, 0x57FFFC, 0x5FFFFC, 
                                                0x600080, 0x600080, 0x61FFF8,
                                                0x7FC, 0x57FFFC, 0x5FFFFC, 
                                                0x4FFFA, 0x4FFFA, 0x4FFFA,
                                                0, 0, 0};
                    const UInt32 magicValue = 0xA55A5AA5;

                    var indexIntoFirmware = 2 * locationOfCheckSumMagicValueLocations[3 * index + counter];
                    var firmwareValue = GetValueFromFirmware(ref firmware, indexIntoFirmware);
                    bool valueInFirmwareIsValid = cameraMagicChecksumValue == firmwareValue;

                    if (valueInFirmwareIsValid)
                    {
                        var newIndex = (indexIntoFirmware + 4);
                        var newValue = GetValueFromFirmware(ref firmware, newIndex);
                        valueInFirmwareIsValid = newValue == magicValue;
                    }

                    if (!valueInFirmwareIsValid)
                    {
                        throw new InvalidDataException("Firmware does not appear valid");
                    }
                    ++counter;
                }
                while (counter < 3);

                // interesting early exit here that bypasses the firmware checksum!
                int[] locationOfChecksumOverrideBits = {
                                               0x7FC, 0x57FFFC, 0x5FFFFC, 
                                               0x600080, 0x600080, 0x61FFF8,
                                                0x7FC, 0x57FFFC, 0x5FFFFC, 
                                                0x4FFFA, 0x4FFFA, 0x4FFFA,
                                                0, 0, 0};

                int indexToMagicCrcOverrideByte = 2 * locationOfChecksumOverrideBits[index];
                var crcOverrideValue = GetValueFromFirmware(ref firmware, indexToMagicCrcOverrideByte);

                if (crcOverrideValue == UInt32.MaxValue)
                {
                    return 0;
                }

                // 0, 0x600000, 0, 0x10000, 0
                int[] checkSumStartAddresses = { 0, 0x600000, 0, 0x10000, 0 };

                // 0x300000, 0x10000, 0x300000, 0x20000, 0
                int[] totalAmountTOCheckSum = { 0x300000, 0x10000, 0x300000, 0x20000, 0 };

                UInt32 checkSumValue = 0;
                int addressToProcess = 2 * checkSumStartAddresses[index];
                int totalNumberOfDwordsToCheck = totalAmountTOCheckSum[index];
                // this looks like the main firmware checksum
                var count = 0;
                while (count < totalNumberOfDwordsToCheck)
                {
                    var valueAtOffset = GetValueFromFirmware(ref firmware, addressToProcess);
                    //totalNumberOfDwordsToCheck += 4;
                    addressToProcess += 4;
                    ++count;
                    checkSumValue += valueAtOffset;
                }

                if (count != totalNumberOfDwordsToCheck)
                {
                    throw new InvalidOperationException();
                }

                return checkSumValue;
            }

            throw new InvalidDataException("Firmware does not appear valid");
        }

        /// <summary>
        /// XORs a file with the in-built key
        /// </summary>
        /// <param name="infile"></param>
        /// <param name="outfile"></param>
        public static void XORwithKey(FileInfo infile, FileInfo outfile)
        {
            byte[] key = Resources.xor;

            byte[] inArray = File.ReadAllBytes(infile.FullName);
            byte[] outArray = new byte[key.Length];

            if (inArray.Length != key.Length)
            {
                throw new InvalidDataException("The size of the firmware and the size of the key are not the same");
            }

            for (var i = 0; i < outArray.Length; ++i)
            {
                outArray[i] = (byte)(inArray[i] ^ key[i]);
            }

            File.WriteAllBytes(outfile.FullName, outArray);
        }
    }
}

