using System.Runtime.InteropServices;
using HsWin.App.Audio;

namespace HsWin.App.Tests;

public sealed class NativeAudioDeviceControllerTests
{
    [Theory]
    [InlineData(unchecked((int)0x80070490))] // E_NOTFOUND: no default device / unknown id
    [InlineData(unchecked((int)0x80070057))] // E_INVALIDARG: malformed id
    public void MissingDeviceErrorsAreRecognized(int hresult)
    {
        Assert.True(NativeAudioDeviceController.IsDeviceMissing(new COMException("missing", hresult)));
    }

    [Fact]
    public void OtherComFailuresAreNotTreatedAsAMissingDevice()
    {
        Assert.False(NativeAudioDeviceController.IsDeviceMissing(new COMException("denied", unchecked((int)0x80070005))));
    }
}
