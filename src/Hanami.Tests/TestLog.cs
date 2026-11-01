using System.Runtime.CompilerServices;
using Hanami.Core;

namespace Hanami.Tests;

/// <summary>Keeps test runs from writing warnings (e.g. "settings.json unreadable") into the user's real log.</summary>
internal static class TestLog
{
    [ModuleInitializer]
    internal static void Redirect() =>
        Log.FilePath = Path.Combine(Path.GetTempPath(), "hanami-tests-log", "hanami.log");
}
