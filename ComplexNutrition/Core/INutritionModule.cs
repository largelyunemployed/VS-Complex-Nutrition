using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// A feature that plugs into the core. Modules only talk to the core, never to each other,
    /// so any one can be switched off without breaking the rest.
    /// </summary>
    public interface INutritionModule
    {
        /// <summary>Id used in the config file, for example "fooddata".</summary>
        string Id { get; }

        /// <summary>Ids of modules that must be enabled for this one to run.</summary>
        string[] Requires { get; }

        bool EnabledByDefault { get; }

        bool IsEnabled { get; }

        /// <summary>Called once assets are final, on each side the module runs on.</summary>
        void Enable(ICoreAPI api, ComplexNutritionModSystem core);

        void Disable(ICoreAPI api);
    }
}
