using System.Runtime.InteropServices.ComTypes;

namespace DiscImageStudio.Burning;

internal interface ICdRawSession
{
    bool MediaPhysicallyBlank { get; }

    long LastPossibleStartOfLeadout { get; }

    IReadOnlyList<int> SupportedSectorTypes { get; }

    int CurrentWriteSpeed { get; }

    bool CurrentRotationTypeIsPureCav { get; }

    void PrepareMedia();

    void SetRequestedSectorType(int sectorType);

    void SetWriteSpeed(int sectorsPerSecond, bool rotationTypeIsPureCav);

    void WriteMedia(IStream content);

    void CancelWrite();

    void ReleaseMedia();
}

internal interface ICdRawImageSession : IDisposable
{
    long StartOfLeadout { get; }

    IStream CreateImage(IStream content);
}
