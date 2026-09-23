using System;
using System.Globalization;
using System.IO;

namespace CoreEngine.Sim.Avr
{
    /// <summary>Parser for Intel HEX files as produced by avr-objcopy (docs/05 §8.4).</summary>
    public static class IntelHex
    {
        /// <summary>
        /// Parses Intel HEX text into a flash image of <paramref name="capacity"/> bytes.
        /// Unwritten bytes are 0xFF (erased flash). <paramref name="usedBytes"/> is the highest
        /// written address + 1.
        /// </summary>
        public static byte[] Parse(string text, int capacity, out int usedBytes)
        {
            var image = new byte[capacity];
            for (int i = 0; i < image.Length; i++) image[i] = 0xFF;
            usedBytes = 0;
            int upperAddress = 0;
            int lineNumber = 0;

            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                line = line.Trim();
                if (line.Length == 0) continue;
                if (line[0] != ':' || line.Length < 11 || (line.Length - 1) % 2 != 0)
                    throw new FormatException($"Line {lineNumber}: not an Intel HEX record.");

                var bytes = new byte[(line.Length - 1) / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (!byte.TryParse(line.Substring(1 + 2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                        throw new FormatException($"Line {lineNumber}: invalid hex digits.");
                }

                int count = bytes[0];
                if (bytes.Length != count + 5)
                    throw new FormatException($"Line {lineNumber}: length does not match the byte count.");

                int sum = 0;
                foreach (byte b in bytes) sum += b;
                if ((sum & 0xFF) != 0)
                    throw new FormatException($"Line {lineNumber}: checksum error.");

                int address = (bytes[1] << 8) | bytes[2];
                int type = bytes[3];
                switch (type)
                {
                    case 0x00:
                        for (int i = 0; i < count; i++)
                        {
                            int target = upperAddress + address + i;
                            if (target >= capacity)
                                throw new FormatException($"Line {lineNumber}: address 0x{target:X} is outside the {capacity}-byte flash.");
                            image[target] = bytes[4 + i];
                            if (target + 1 > usedBytes) usedBytes = target + 1;
                        }
                        break;
                    case 0x01:
                        return image;
                    case 0x02:
                        upperAddress = ((bytes[4] << 8) | bytes[5]) << 4;
                        break;
                    case 0x04:
                        upperAddress = ((bytes[4] << 8) | bytes[5]) << 16;
                        break;
                    case 0x03:
                    case 0x05:
                        break; // start addresses are irrelevant for AVR
                    default:
                        throw new FormatException($"Line {lineNumber}: unknown record type {type}.");
                }
            }

            return image;
        }

        public static byte[] Parse(string text, int capacity) => Parse(text, capacity, out _);
    }
}
