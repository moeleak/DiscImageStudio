using System.Globalization;
using System.Runtime.Versioning;

namespace DiscImageStudio.Burning;

/// <summary>
/// Optical drives from the IOKit registry (IOCompactDiscServices, IODVDServices and IOBDServices
/// nubs, one per drive by its capability), named from their "Device Characteristics", and opened
/// through the MMC authoring user client with exclusive access. Device ids are registry entry
/// ids, which stay valid while the drive is attached.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacOpticalDeviceProvider : IMmcDeviceProvider
{
    private const string IdPrefix = "iokit:";
    private static readonly string[] ServiceClasses = ["IOBDServices", "IODVDServices", "IOCompactDiscServices"];

    public IReadOnlyList<MmcDeviceInfo> EnumerateDevices()
    {
        Dictionary<ulong, MmcDeviceInfo> devices = [];
        foreach (string serviceClass in ServiceClasses)
        {
            if (MacNative.IOServiceGetMatchingServices(0, MacNative.IOServiceMatching(serviceClass), out uint iterator)
                != MacNative.Success)
            {
                continue;
            }

            try
            {
                for (uint service = MacNative.IOIteratorNext(iterator); service != 0; service = MacNative.IOIteratorNext(iterator))
                {
                    try
                    {
                        if (MacNative.IORegistryEntryGetRegistryEntryID(service, out ulong entryId) != MacNative.Success
                            || devices.ContainsKey(entryId))
                        {
                            continue;
                        }

                        string name = ReadModel(service);
                        devices[entryId] = new MmcDeviceInfo(IdPrefix + entryId.ToString("X", CultureInfo.InvariantCulture),
                            name.Length == 0 ? $"光驱 {devices.Count + 1}" : name, []);
                    }
                    finally
                    {
                        _ = MacNative.IOObjectRelease(service);
                    }
                }
            }
            finally
            {
                _ = MacNative.IOObjectRelease(iterator);
            }
        }

        return devices.Values.OrderBy(device => device.DisplayName, StringComparer.Ordinal).ToArray();
    }

    public IMmcDevice Open(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!deviceId.StartsWith(IdPrefix, StringComparison.Ordinal)
            || !ulong.TryParse(deviceId.AsSpan(IdPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong entryId))
        {
            throw new InvalidOperationException($"找不到光驱 {deviceId}；请刷新刻录机列表。");
        }

        uint service = MacNative.IOServiceGetMatchingService(0, MacNative.IORegistryEntryIDMatching(entryId));
        if (service == 0) throw new InvalidOperationException("所选光驱已不可用（可能已断开）；请刷新刻录机列表。");
        string name = ReadModel(service);
        return new MacMmcDevice(MacScsiTaskTransport.Open(name.Length == 0 ? "光驱" : name, service));
    }

    private static string ReadModel(uint service)
    {
        IntPtr key = MacNative.CreateString("Device Characteristics");
        IntPtr characteristics = IntPtr.Zero;
        try
        {
            characteristics = MacNative.IORegistryEntrySearchCFProperty(service, "IOService", key, IntPtr.Zero,
                MacNative.IterateRecursively | MacNative.IterateParents);
            if (characteristics == IntPtr.Zero || MacNative.CFGetTypeID(characteristics) != MacNative.CFDictionaryGetTypeID())
                return string.Empty;
            string vendor = Read(characteristics, "Vendor Name");
            string product = Read(characteristics, "Product Name");
            return string.Join(" ", new[] { vendor, product }.Where(text => text.Length > 0));
        }
        finally
        {
            if (characteristics != IntPtr.Zero) MacNative.CFRelease(characteristics);
            MacNative.CFRelease(key);
        }

        static string Read(IntPtr dictionary, string name)
        {
            IntPtr key = MacNative.CreateString(name);
            try { return MacNative.ReadString(MacNative.CFDictionaryGetValue(dictionary, key))?.Trim() ?? string.Empty; }
            finally { MacNative.CFRelease(key); }
        }
    }

    private sealed class MacMmcDevice(MacScsiTaskTransport transport) : IMmcDevice
    {
        public IMmcCommandTransport Transport => transport;

        public void Dispose() => transport.Dispose();
    }
}
