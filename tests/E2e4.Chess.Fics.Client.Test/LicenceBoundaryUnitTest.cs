using System.Reflection;
using Xunit;

namespace E2e4.Chess.Fics.Client.Test;

/// <summary>
/// The library is MIT. The timeseal cipher (E2e4.Chess.OpenSeal) is GPL v2 or later, so it must reach the client only through
/// <see cref="ITimesealCodec"/>, supplied by the application, never as a dependency of this package.
/// </summary>
public class LicenceBoundaryUnitTest
{
    [Fact]
    public void TheLibraryDoesNotReferenceTheGplOpenSealAssembly()
    {
        AssemblyName[] references = typeof(FicsClient).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, r => r.Name != null && r.Name.Contains("OpenSeal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheTestsDoNotLoadTheGplOpenSealAssemblyEither()
    {
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "E2e4.Chess.OpenSeal");
    }
}
