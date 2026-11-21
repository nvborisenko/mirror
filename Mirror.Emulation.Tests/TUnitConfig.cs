using System.Runtime.CompilerServices;

internal static class TUnitConfig
{
    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable("TUNIT_OTEL_MAX_EXTERNAL_SPANS", "10000");
    }
}
