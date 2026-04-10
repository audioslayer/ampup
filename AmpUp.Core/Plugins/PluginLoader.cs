using System.IO;
using System.Reflection;

namespace AmpUp.Core.Plugins;

/// <summary>Scans a plugins/ folder for DLLs and loads IPlugin implementations.</summary>
public class PluginLoader
{
    private readonly List<IPlugin> _plugins = new();

    public IReadOnlyList<IPlugin> Plugins => _plugins;
    public IEnumerable<IKnobPlugin> KnobPlugins => _plugins.OfType<IKnobPlugin>();
    public IEnumerable<IButtonPlugin> ButtonPlugins => _plugins.OfType<IButtonPlugin>();
    public IEnumerable<ILightPlugin> LightPlugins => _plugins.OfType<ILightPlugin>();

    /// <summary>Scan the plugins directory and load all plugin DLLs.</summary>
    public void LoadPlugins(string pluginsDir, IPluginHost host)
    {
        if (!Directory.Exists(pluginsDir))
        {
            Logger.Log($"Plugins directory not found: {pluginsDir}");
            return;
        }

        foreach (var dll in Directory.GetFiles(pluginsDir, "*.dll"))
        {
            try
            {
                LoadPluginAssembly(dll, host);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load plugin {dll}: {ex.Message}");
            }
        }

        Logger.Log($"Loaded {_plugins.Count} plugin(s)");
    }

    private void LoadPluginAssembly(string dllPath, IPluginHost host)
    {
        var assembly = Assembly.LoadFrom(dllPath);
        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var type in pluginTypes)
        {
            try
            {
                var plugin = (IPlugin)Activator.CreateInstance(type)!;
                plugin.Initialize(host);
                _plugins.Add(plugin);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to initialize plugin {type.FullName}: {ex.Message}");
            }
        }
    }

    /// <summary>Shutdown all plugins gracefully.</summary>
    public void UnloadAll()
    {
        foreach (var plugin in _plugins)
        {
            try
            {
                plugin.Shutdown();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error shutting down plugin {plugin.Name}: {ex.Message}");
            }
        }
        _plugins.Clear();
    }
}
