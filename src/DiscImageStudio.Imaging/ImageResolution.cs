using System.Buffers.Binary;

namespace DiscImageStudio.Imaging;

/// <summary>
/// Reads and writes the physical resolution stored in image headers. Skia does not expose
/// it, so PNG <c>pHYs</c>, JPEG JFIF density and BMP pixels-per-metre are parsed here.
/// Missing or aspect-ratio-only values report 96 DPI, matching WIC.
/// </summary>
public static class ImageResolution
{
    public const double DefaultDpi = 96.0;
    private const double MetresPerInch = 0.0254;
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static double Valid(double dpi) => double.IsFinite(dpi) && dpi > 0 ? dpi : DefaultDpi;

    public static (double DpiX, double DpiY) Read(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature))
        {
            return ReadPng(data);
        }

        if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return ReadJpeg(data);
        }

        if (data.Length >= 54 && data[0] == (byte)'B' && data[1] == (byte)'M')
        {
            int xPerMetre = BinaryPrimitives.ReadInt32LittleEndian(data[38..]);
            int yPerMetre = BinaryPrimitives.ReadInt32LittleEndian(data[42..]);
            return (FromPerMetre(xPerMetre), FromPerMetre(yPerMetre));
        }

        return (DefaultDpi, DefaultDpi);
    }

    /// <summary>Inserts (or replaces) a PNG <c>pHYs</c> chunk directly after <c>IHDR</c>.</summary>
    public static byte[] WithPngResolution(ReadOnlySpan<byte> png, double dpiX, double dpiY)
    {
        if (!png.StartsWith(PngSignature) || png.Length < 33)
        {
            throw new ArgumentException("The data is not a PNG stream.", nameof(png));
        }

        uint xPerMetre = (uint)Math.Round(Valid(dpiX) / MetresPerInch);
        uint yPerMetre = (uint)Math.Round(Valid(dpiY) / MetresPerInch);
        Span<byte> chunk = stackalloc byte[21];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, 9);
        "pHYs"u8.CopyTo(chunk[4..]);
        BinaryPrimitives.WriteUInt32BigEndian(chunk[8..], xPerMetre);
        BinaryPrimitives.WriteUInt32BigEndian(chunk[12..], yPerMetre);
        chunk[16] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(chunk[17..], PngCrc(chunk[4..17]));

        using MemoryStream output = new(png.Length + chunk.Length);
        int ihdrEnd = PngSignature.Length + 8 + 13 + 4;
        output.Write(png[..ihdrEnd]);
        output.Write(chunk);
        int position = ihdrEnd;
        while (position + 8 <= png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[position..]));
            int total = checked(12 + length);
            ReadOnlySpan<byte> type = png.Slice(position + 4, 4);
            if (!type.SequenceEqual("pHYs"u8))
            {
                output.Write(png.Slice(position, total));
            }

            position += total;
        }

        return output.ToArray();
    }

    private static (double, double) ReadPng(ReadOnlySpan<byte> data)
    {
        int position = PngSignature.Length;
        while (position + 8 <= data.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            ReadOnlySpan<byte> type = data.Slice(position + 4, 4);
            if (type.SequenceEqual("IDAT"u8) || length > int.MaxValue)
            {
                break;
            }

            if (type.SequenceEqual("pHYs"u8) && length == 9 && position + 17 <= data.Length)
            {
                uint x = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 8)..]);
                uint y = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 12)..]);
                return data[position + 16] == 1
                    ? (FromPerMetre(x), FromPerMetre(y))
                    : (DefaultDpi, DefaultDpi);
            }

            position = checked(position + 12 + (int)length);
        }

        return (DefaultDpi, DefaultDpi);
    }

    private static (double, double) ReadJpeg(ReadOnlySpan<byte> data)
    {
        int position = 2;
        while (position + 4 <= data.Length && data[position] == 0xFF)
        {
            byte marker = data[position + 1];
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
            if (marker == 0xE0 && length >= 16 && position + 2 + length <= data.Length
                && data.Slice(position + 4, 5).SequenceEqual("JFIF\0"u8))
            {
                byte units = data[position + 11];
                int x = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 12)..]);
                int y = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 14)..]);
                return units switch
                {
                    1 => (Valid(x), Valid(y)),
                    2 => (Valid(x * 2.54), Valid(y * 2.54)),
                    _ => (DefaultDpi, DefaultDpi),
                };
            }

            if (marker is 0xDA or 0xD9)
            {
                break;
            }

            position += 2 + length;
        }

        return (DefaultDpi, DefaultDpi);
    }

    private static double FromPerMetre(double perMetre) => perMetre > 0 ? perMetre * MetresPerInch : DefaultDpi;

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint PngCrc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
