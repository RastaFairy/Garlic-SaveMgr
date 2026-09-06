using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace GarlicSaveMgr.Infrastructure;

/// <summary>
/// Descubre módulos ejecutables independientes dentro de Modules/**.
/// Cada módulo encapsula su vista XAML y su comportamiento y expone únicamente
/// el contrato estable ModuleEntry.CreateModule(IModuleHostContext).
/// Un módulo ausente, inválido o incompatible nunca bloquea el arranque.
/// </summary>
public sealed class ModuleManager : IDisposable
{
    private readonly Dictionary<string, ModuleInstance> _modules = new(StringComparer.OrdinalIgnoreCase);
    private TabControl? _hostTabs;

    public IReadOnlyCollection<string> LoadedModuleIds => _modules.Keys.ToArray();
    public IReadOnlyList<string> MissingRecommendedModules =>
        RecommendedModules.Keys.Where(id => !_modules.ContainsKey(id)).ToArray();
    public IReadOnlyList<string> InvalidModules { get; private set; } = [];

    private static readonly IReadOnlyDictionary<string, string> RecommendedModules =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["security"] = "SEGURIDAD",
            ["trash"] = "PAPELERA",
            ["salud"] = "SALUD 2.2",
            ["updater"] = "ACTUALIZADOR"
        };

    public void Load(TabControl tabs, IModuleHostContext context)
    {
        _hostTabs = tabs;
        UnloadDynamicTabs();
        _modules.Clear();
        InvalidModules = [];

        Directory.CreateDirectory(AppPaths.ModulesDirectory);
        var invalid = new List<string>();
        var moduleFiles = Directory.EnumerateFiles(AppPaths.ModulesDirectory, "*Module.dll", SearchOption.AllDirectories)
            .OrderBy(GetSortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var assemblyPath in moduleFiles)
        {
            try
            {
                var normalizedModulePath = assemblyPath.Replace('\\', '/');
                if (normalizedModulePath.Contains("/Modules/Motor/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(assemblyPath), "GarlicSaveMgr.MotorModule.dll", StringComparison.OrdinalIgnoreCase))
                {
                    ApplicationTelemetryService.RegisterModule("motor-legacy", "Ignored", "MOTOR sustituido por SALUD desde v6.8.7.31");
                    LogService.Write($"Módulo MOTOR histórico ignorado: {Path.GetFileName(assemblyPath)}. Use Modules/Salud.", "INFO");
                    continue;
                }
                LoadExecutableModuleAssembly(assemblyPath, tabs, context);
            }
            catch (Exception ex)
            {
                invalid.Add(Path.GetRelativePath(AppPaths.ModulesDirectory, assemblyPath));
                ApplicationTelemetryService.RegisterModule(Path.GetFileNameWithoutExtension(assemblyPath), "Failed", ex.Message);
                LogService.Write($"Módulo ejecutable omitido: {Path.GetFileName(assemblyPath)} — {ex.Message}", "WARN");
            }
        }

        InvalidModules = invalid.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var missing in MissingRecommendedModules)
            ApplicationTelemetryService.RegisterModule(missing, "Missing", "Módulo recomendado no disponible");
        if (MissingRecommendedModules.Count > 0)
            LogService.Write($"Módulos detallados no disponibles: {string.Join(", ", MissingRecommendedModules)}.", "WARN");
    }

    public void NotifyStateChanged(string state)
    {
        foreach (var module in _modules.Values.ToArray())
        {
            try
            {
                module.Runtime.OnHostStateChanged(state);
            }
            catch (Exception ex)
            {
                LogService.Write($"WARN módulo {module.Id}: {ex.Message}", "WARN");
            }
        }
    }

    public bool TryGetCapability<T>(string moduleId, out T? capability) where T : class, IModuleCapability
    {
        capability = null;
        if (!_modules.TryGetValue(moduleId, out var module)) return false;
        capability = module.Runtime as T;
        return capability is not null;
    }

    private void LoadExecutableModuleAssembly(string assemblyPath, TabControl tabs, IModuleHostContext context)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        var entryTypes = assembly.GetExportedTypes()
            .Where(t => t.Name == "ModuleEntry" && t.IsClass && t.IsAbstract && t.IsSealed)
            .ToArray();
        if (entryTypes.Length != 1)
            throw new InvalidDataException("El ensamblado debe contener exactamente un ModuleEntry público estático.");

        var method = entryTypes[0].GetMethod("CreateModule", BindingFlags.Public | BindingFlags.Static);
        if (method is null) throw new InvalidDataException("ModuleEntry no expone CreateModule.");
        var parameters = method.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(IModuleHostContext) ||
            !typeof(IExecutableModule).IsAssignableFrom(method.ReturnType))
            throw new InvalidDataException("CreateModule debe aceptar IModuleHostContext y devolver IExecutableModule.");

        var runtime = method.Invoke(null, [context]) as IExecutableModule
            ?? throw new InvalidDataException("El módulo no devolvió IExecutableModule.");
        if (string.IsNullOrWhiteSpace(runtime.Id) || string.IsNullOrWhiteSpace(runtime.Header))
        {
            runtime.Dispose();
            throw new InvalidDataException("El módulo no expone Id/Header válidos.");
        }

        if (_modules.ContainsKey(runtime.Id))
        {
            runtime.Dispose();
            throw new InvalidDataException($"ID de módulo duplicado: '{runtime.Id}'.");
        }

        var tab = new TabItem
        {
            Header = runtime.Header,
            Tag = runtime.Id,
            Content = runtime.View,
            Visibility = Visibility.Visible
        };
        _modules[runtime.Id] = new ModuleInstance(runtime.Id, runtime.Header, assemblyPath, runtime.View, tab, runtime);
        ApplicationTelemetryService.RegisterModule(runtime.Id, "Loaded", runtime.Header);
        tabs.Items.Add(tab);
        LogService.Write($"Módulo ejecutable cargado: {runtime.Id} — {runtime.Header}.", "INFO");
    }

    public bool IsAvailable(string id) => _modules.ContainsKey(id);
    public bool IsSelected(TabControl tabs, string id) => _modules.TryGetValue(id, out var m) && ReferenceEquals(tabs.SelectedItem, m.Tab);
    public TabItem? GetTab(string id) => _modules.TryGetValue(id, out var m) ? m.Tab : null;
    public void SetVisibility(string id, Visibility visibility) { if (_modules.TryGetValue(id, out var m)) m.Tab.Visibility = visibility; }
    public bool IsVisible(string id) => _modules.TryGetValue(id, out var m) && m.Tab.Visibility == Visibility.Visible;
    public static string GetRecommendedHeader(string id) => RecommendedModules.TryGetValue(id, out var h) ? h : id.ToUpperInvariant();

    public void UnloadDynamicTabs()
    {
        if (_hostTabs is null) return;
        foreach (var module in _modules.Values.ToArray())
        {
            _hostTabs.Items.Remove(module.Tab);
            try { module.Runtime.Dispose(); } catch { }
            ApplicationTelemetryService.RegisterModule(module.Id, "Unloaded", null);
        }
    }

    public void Dispose()
    {
        UnloadDynamicTabs();
        _modules.Clear();
    }

    private static string GetSortKey(string path)
    {
        var p = path.Replace('\\', '/').ToLowerInvariant();
        if (p.Contains("/security/")) return "00-security-" + p;
        if (p.Contains("/trash/")) return "01-trash-" + p;
        if (p.Contains("/salud/")) return "02-salud-" + p;
        if (p.Contains("/updater/")) return "03-updater-" + p;
        return "10-" + p;
    }

    private sealed record ModuleInstance(string Id, string Header, string File, UserControl View, TabItem Tab, IExecutableModule Runtime);
}
