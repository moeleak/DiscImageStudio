using DiscImageStudio.Imaging;

/// <summary>Portable PNG fixtures for tests (replaces the former WPF encoder/decoder helpers).</summary>
internal static class TestImages
{
    internal static void SaveGray8Png(string path, int width, int height, byte[] pixels)
        => ImageCodec.SavePng(path, new Gray8Image(width, height, pixels));

    internal static void SaveBgraPng(string path, int width, int height, byte[] pixels)
        => ImageCodec.SavePng(path, new Bgra32Image(width, height, pixels));

    internal static byte[] ReadBgra(string path) => ImageCodec.Decode(path).Pixels;

    /// <summary>Gray value of each pixel of a neutral (R = G = B) image.</summary>
    internal static byte[] ReadGray(Bgra32Image image)
    {
        byte[] gray = new byte[image.Width * image.Height];
        for (int index = 0; index < gray.Length; index++) gray[index] = image.Pixels[index * 4 + 1];
        return gray;
    }

    internal static byte[] ReadGray(Gray8Image image) => image.Pixels;
}
