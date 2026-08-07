using System.Runtime.CompilerServices;

namespace Jellyfin.Plugin.Community.Tests;

internal static class SqliteTestInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SQLitePCL.Batteries_V2.Init();
    }
}
