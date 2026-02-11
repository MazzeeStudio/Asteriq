using Asteriq.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Asteriq.Services;

/// <summary>
/// Extension methods for configuring dependency injection services
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>
    /// Register all application services with the dependency injection container
    /// </summary>
    public static IServiceCollection AddAsteriqServices(this IServiceCollection services)
    {
        // Core services (Singleton - stateful, maintain state across application lifetime)
        services.AddSingleton<IInputService, InputService>();
        services.AddSingleton<IVJoyService, VJoyService>();
        services.AddSingleton<IMappingEngine, MappingEngine>();
        services.AddSingleton<IHidHideService, HidHideService>();
        services.AddSingleton<KeyboardService>();
        services.AddSingleton<DriverSetupManager>();

        // Profile management services (Singleton - maintain profile state)
        services.AddSingleton<IProfileRepository, ProfileRepository>();
        services.AddSingleton<IProfileManager, ProfileManager>();
        services.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>();
        services.AddSingleton<IUIThemeService, UIThemeService>();
        services.AddSingleton<IWindowStateManager, WindowStateManager>();

        // Settings migration service
        services.AddSingleton<SettingsMigrationService>();

        // Backward compatibility adapter for IProfileService
#pragma warning disable CS0618 // Intentional use of obsolete ProfileServiceAdapter for backward compatibility
        services.AddSingleton<IProfileService, ProfileServiceAdapter>();
#pragma warning restore CS0618

        // HTTP client factory for driver downloads
        services.AddHttpClient("Asteriq", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Asteriq/1.0");
            client.Timeout = TimeSpan.FromMinutes(10); // Long timeout for large driver downloads
        });

        // Star Citizen integration services (Singleton - cache state)
        services.AddSingleton<ISCInstallationService, SCInstallationService>();
        services.AddSingleton<SCProfileCacheService>();
        services.AddSingleton<SCSchemaService>();
        services.AddSingleton<SCXmlExportService>();
        services.AddSingleton<SCExportProfileService>();

        // Device services (Singleton - maintain device state)
        services.AddSingleton<HidDeviceService>();
        services.AddSingleton<DeviceMatchingService>();

        // Utility services (Transient - stateless, created per request)
        // Note: CryXmlService and SCCategoryMapper are static classes and don't need DI registration
        services.AddTransient<P4kExtractorService>();
        services.AddTransient<InputDetectionService>();

        // DirectInput services (Singleton - manage device state)
        services.AddSingleton<DirectInput.DirectInputService>();
        services.AddSingleton<DirectInput.DirectInputReader>();

        return services;
    }
}
