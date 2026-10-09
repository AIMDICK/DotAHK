using System.Runtime.CompilerServices;

namespace DotAHK;

/// <summary>
/// Applies the UI language as early as possible - a module initializer runs before the
/// generated entry point (and therefore before <c>Application.Start</c>), so the XAML
/// framework's resource context honors the override for <c>x:Uid</c>. Applying it later
/// (e.g. in OnLaunched) is too late: the framework has already captured the OS language.
/// </summary>
internal static class LocalizationBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            Services.LocalizationService.ApplyEarly();
        }
        catch
        {
            // Localization must never prevent the app from starting.
        }
    }
}
