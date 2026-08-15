using System.Runtime.InteropServices;

namespace rDataTrans;

/// <summary>
/// 4K crash was: VirtualAllocEx failed, old code still did
/// <c>(uint)(alloc + 0x20)</c> → font base 0x20, then unpack read
/// 0xD2 * 0x2400 + 0x20 = 0x1D8820.
/// </summary>
public static class FontAlloc
{
    public const uint HeaderPad = 0x20;
    public const uint GlyphStride192 = 0x2400;
    public const byte CrashSjisIndex = 0xD2;
    public const uint ObservedCrashAddress = 0x001D8820;
    public const uint AllocGranularity = 0x10000;

    /// <summary>CJK blobs that do not fit in the engine's original buffers.</summary>
    public static readonly uint[] HdPixels = [192, 160, 144, 128, 96, 80, 72];

    public static uint AlignUp(uint addr, uint align)
    {
        if (align == 0 || (align & (align - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(align));
        return (addr + align - 1) & ~(align - 1);
    }

    /// <summary>The old rdata.exe formula. Null alloc becomes 0x20.</summary>
    public static uint BrokenAddrFromAlloc(IntPtr alloc)
    {
        return (uint)(alloc + (int)HeaderPad);
    }

    public static uint UnpackReadAddr(uint fontBase, byte sjisIndex)
    {
        return fontBase + sjisIndex * GlyphStride192;
    }

    /// <summary>
    /// 32-bit LAA addresses at or above 0x80000000 are valid.
    /// <see cref="IntPtr.ToInt64"/> sign-extends them to 0xFFFFFFFFXXXXXXXX.
    /// </summary>
    public static uint ToUInt32Addr(IntPtr alloc)
    {
        return unchecked((uint)(alloc.ToInt64() & 0xFFFFFFFF));
    }

    public static bool TryFromAlloc(IntPtr alloc, out uint payloadAddr)
    {
        payloadAddr = 0;
        if (alloc == IntPtr.Zero)
            return false;

        uint raw = ToUInt32Addr(alloc);
        if (raw == 0 || raw > uint.MaxValue - HeaderPad)
            return false;

        payloadAddr = raw + HeaderPad;
        return true;
    }

    public static uint FromAllocOrThrow(IntPtr alloc, uint fontPx, int bytes)
    {
        if (!TryFromAlloc(alloc, out var addr))
        {
            throw new InvalidOperationException(
                $"font{fontPx} VirtualAlloc returned 0x{ToUInt32Addr(alloc):X}, size={bytes}. " +
                $"Refusing to write base 0x{BrokenAddrFromAlloc(alloc):X} (old formula crashes at " +
                $"0x{UnpackReadAddr(BrokenAddrFromAlloc(alloc), CrashSjisIndex):X}).");
        }

        return addr;
    }
}
