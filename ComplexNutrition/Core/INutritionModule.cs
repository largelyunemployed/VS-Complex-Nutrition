using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// A feature that plugs into the core. Modules only talk to the core, never to each other,
    /// so any one can be switched off without breaking the rest. Everything a module does, including
    /// what must happen while it is off, lives in the module.
    /// </summary>
    public interface INutritionModule
    {
        /// <summary>Id used in the config file, for example "fooddata" (see <see cref="ModuleIds"/>).</summary>
        string Id { get; }

        /// <summary>Ids of modules that must be enabled for this one to run.</summary>
        string[] Requires { get; }

        bool EnabledByDefault { get; }

        bool IsEnabled { get; }

        /// <summary>
        /// Called once on each side when assets are final, before any module is enabled, whether this one runs or not:
        /// for what must work while it is off, such as its commands or putting players back on vanilla values.
        /// </summary>
        void Start(ICoreAPI api, ComplexNutritionModSystem core);

        /// <summary>Called once assets are final, on each side the module runs on, and when it is switched on.</summary>
        void Enable(ICoreAPI api, ComplexNutritionModSystem core);

        /// <summary>Stops the module: when it is switched off, and when the game shuts down.</summary>
        void Disable(ICoreAPI api);

        /// <summary>Called after <see cref="Disable"/> when an admin switched the module off while the game runs (not at shutdown).</summary>
        void OnSwitchedOff(ICoreAPI api);
    }
}
