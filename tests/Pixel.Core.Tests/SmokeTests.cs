namespace Pixel.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads()
    {
        Assert.Equal("Pixel.Core", typeof(Pixel.Core.AssemblyMarker).Assembly.GetName().Name);
    }
}
