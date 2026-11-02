using BitMiracle.LibTiff.Classic;

namespace DiscImageStudio.Imaging;

/// <summary>TIFF fallback (Skia has no TIFF codec). Decodes the first directory to
/// straight-alpha BGRA. LibTiff's RGBA reader premultiplies any alpha, so it is undone here.</summary>
internal static class TiffImageDecoder
{
    private static readonly object HandlerGate = new();
    private static bool _handlerInstalled;

    internal static bool IsTiff(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && ((data[0] == (byte)'I' && data[1] == (byte)'I' && data[2] == 42 && data[3] == 0)
            || (data[0] == (byte)'M' && data[1] == (byte)'M' && data[2] == 0 && data[3] == 42));

    internal static (int Width, int Height) ReadSize(byte[] data)
    {
        using Tiff tiff = Open(data);
        return (Field(tiff, TiffTag.IMAGEWIDTH), Field(tiff, TiffTag.IMAGELENGTH));
    }

    internal static Bgra32Image Decode(byte[] data)
    {
        using Tiff tiff = Open(data);
        int width = Field(tiff, TiffTag.IMAGEWIDTH);
        int height = Field(tiff, TiffTag.IMAGELENGTH);
        if (width <= 0 || height <= 0)
        {
            throw new NotSupportedException("The TIFF image has no pixels.");
        }

        int[] raster = new int[checked(width * height)];
        if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
        {
            throw new NotSupportedException("The TIFF image could not be decoded.");
        }

        byte[] pixels = new byte[checked(raster.Length * 4)];
        for (int index = 0, offset = 0; index < raster.Length; index++, offset += 4)
        {
            int value = raster[index];
            int alpha = Tiff.GetA(value);
            int red = Tiff.GetR(value);
            int green = Tiff.GetG(value);
            int blue = Tiff.GetB(value);
            if (alpha is > 0 and < 255)
            {
                red = Math.Min(255, ((red * 255) + (alpha / 2)) / alpha);
                green = Math.Min(255, ((green * 255) + (alpha / 2)) / alpha);
                blue = Math.Min(255, ((blue * 255) + (alpha / 2)) / alpha);
            }

            pixels[offset] = (byte)blue;
            pixels[offset + 1] = (byte)green;
            pixels[offset + 2] = (byte)red;
            pixels[offset + 3] = (byte)alpha;
        }

        (double dpiX, double dpiY) = ReadResolution(tiff);
        return new Bgra32Image(width, height, pixels, dpiX, dpiY);
    }

    private static Tiff Open(byte[] data)
    {
        lock (HandlerGate)
        {
            if (!_handlerInstalled)
            {
                Tiff.SetErrorHandler(new SilentHandler());
                _handlerInstalled = true;
            }
        }

        MemoryStream stream = new(data, writable: false);
        return Tiff.ClientOpen("image.tif", "r", stream, new TiffStream())
            ?? throw new NotSupportedException("The TIFF image could not be opened.");
    }

    private static int Field(Tiff tiff, TiffTag tag)
    {
        FieldValue[]? value = tiff.GetField(tag);
        return value is { Length: > 0 } ? value[0].ToInt() : 0;
    }

    private static (double, double) ReadResolution(Tiff tiff)
    {
        FieldValue[]? x = tiff.GetField(TiffTag.XRESOLUTION);
        FieldValue[]? y = tiff.GetField(TiffTag.YRESOLUTION);
        FieldValue[]? unit = tiff.GetField(TiffTag.RESOLUTIONUNIT);
        if (x is not { Length: > 0 } || y is not { Length: > 0 })
        {
            return (ImageResolution.DefaultDpi, ImageResolution.DefaultDpi);
        }

        ResUnit resolutionUnit = unit is { Length: > 0 } ? (ResUnit)unit[0].ToInt() : ResUnit.INCH;
        double factor = resolutionUnit switch
        {
            ResUnit.INCH => 1.0,
            ResUnit.CENTIMETER => 2.54,
            _ => double.NaN,
        };
        return (ImageResolution.Valid(x[0].ToFloat() * factor), ImageResolution.Valid(y[0].ToFloat() * factor));
    }

    private sealed class SilentHandler : TiffErrorHandler
    {
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format,
            params object[] args)
        {
        }

        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format,
            params object[] args)
        {
        }
    }
}
