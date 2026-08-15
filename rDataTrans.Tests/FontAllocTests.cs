using rDataTrans;
using Xunit;

namespace rDataTrans.Tests;

public class FontAllocTests
{
    [Fact]
    public void OldFormula_NullAlloc_Becomes0x20_AndReproducesDumpAddress()
    {
        uint brokenBase = FontAlloc.BrokenAddrFromAlloc(IntPtr.Zero);
        Assert.Equal(0x20u, brokenBase);
        Assert.Equal(
            FontAlloc.ObservedCrashAddress,
            FontAlloc.UnpackReadAddr(brokenBase, FontAlloc.CrashSjisIndex));
    }

    [Fact]
    public void NewFormula_NullAlloc_IsRejected()
    {
        Assert.False(FontAlloc.TryFromAlloc(IntPtr.Zero, out uint addr));
        Assert.Equal(0u, addr);
    }

    [Fact]
    public void FromAllocOrThrow_NullAlloc_NamesTheCrashAddress()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => FontAlloc.FromAllocOrThrow(IntPtr.Zero, 192, 133834752));
        Assert.Contains("1D8820", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("font192", ex.Message);
    }

    [Fact]
    public void NewFormula_ValidAlloc_AddsHeaderPad()
    {
        var alloc = new IntPtr(0x10000);
        Assert.True(FontAlloc.TryFromAlloc(alloc, out uint addr));
        Assert.Equal(0x10020u, addr);
    }

    [Fact]
    public void HighLaaAddress_SignExtended_IsAccepted()
    {
        var alloc = new IntPtr(unchecked((int)0x87FA0000));
        Assert.Equal(0x87FA0000u, FontAlloc.ToUInt32Addr(alloc));
        Assert.True(FontAlloc.TryFromAlloc(alloc, out uint addr));
        Assert.Equal(0x87FA0020u, addr);
    }

    [Fact]
    public void HighLaaAddress_ZeroExtended_IsAccepted()
    {
        var alloc = new IntPtr(0x87FA0000);
        Assert.True(FontAlloc.TryFromAlloc(alloc, out uint addr));
        Assert.Equal(0x87FA0020u, addr);
    }

    [Fact]
    public void AlignUp_Uses64kGranularity()
    {
        Assert.Equal(0x10000u, FontAlloc.AlignUp(0x10000, FontAlloc.AllocGranularity));
        Assert.Equal(0x20000u, FontAlloc.AlignUp(0x10001, FontAlloc.AllocGranularity));
    }
}
