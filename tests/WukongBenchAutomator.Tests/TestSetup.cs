using System.Globalization;
using System.Runtime.CompilerServices;

namespace WukongBenchAutomator.Tests;

internal static class TestSetup
{
    // как в Program: числа с точкой
    [ModuleInitializer]
    internal static void UseInvariantCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
