using System.IO;
using System.Text;
using DiscImageStudio.Iso;

/// <summary>Case-sensitive file systems (Linux, case-sensitive APFS) allow siblings whose names
/// differ only in case. ISO9660/Joliet readers compare case-insensitively, so both files must
/// receive distinct identifiers and keep their own contents.</summary>
internal static class CaseVariantNamesTests
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"case-variant-names-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Readme.txt"), "lower-case contents"u8.ToArray());
            if (File.Exists(Path.Combine(directory, "README.txt")))
            {
                Console.WriteLine("case-variant-names: skipped (case-insensitive file system)");
                return;
            }

            File.WriteAllBytes(Path.Combine(directory, "README.txt"), "upper-case contents"u8.ToArray());
            var plan = Iso9660ImageWriter.Plan(directory, 256, "CASE", DateTimeOffset.UnixEpoch);
            using MemoryStream iso = new();
            Iso9660ImageWriter.WriteSequentialPrefix(plan, iso, 256);
            byte[] bytes = iso.ToArray();
            Require(Contains(bytes, Encoding.BigEndianUnicode.GetBytes("README.txt;1")),
                "the ordinal-first sibling keeps its Joliet name");
            Require(Contains(bytes, Encoding.BigEndianUnicode.GetBytes("Readme.txt~2;1")),
                "the case-variant sibling receives a distinct Joliet name");
            Require(Contains(bytes, "lower-case contents"u8.ToArray()) && Contains(bytes, "upper-case contents"u8.ToArray()),
                "both case-variant files are stored");
            Console.WriteLine("case-variant-names: passed (distinct Joliet identifiers and contents)");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Case-variant names: " + message);
    }
}
