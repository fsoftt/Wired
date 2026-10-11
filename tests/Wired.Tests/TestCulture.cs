using System.Runtime.CompilerServices;
using Wired.Core.Localization;

namespace Wired.Tests;

/// <summary>
/// The tests that check texts expect the neutral language (Spanish), whatever the machine's language is.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseSpanish() => L.Apply("es");
}
