using Microsoft.Extensions.Logging;
using Uninstaller.Models;
using Uninstaller.Services;

namespace Uninstaller.ViewModels;

/// <summary>Lo que la lista de aplicaciones hace con sus propios controles y la logica no puede hacer.</summary>
public interface IMainView
{
    void FocusSearch();

    void UnfocusSearch();

    /// <summary>Lleva la barra de progreso de la desinstalacion a ese valor (0..1), con animacion.</summary>
    Task AnimateProgressAsync(double value);
}

/// <summary>
/// Logica de la lista de aplicaciones instaladas: cargar, ordenar, buscar, marcar y desinstalar.
/// La pagina vuelca este estado en sus controles (<see cref="Changed"/>) y le pasa los toques
/// (General 8.6: la logica se prueba sin interfaz).
/// </summary>
public class MainViewModel
{
    /// <summary>Criterios de orden disponibles, en el mismo orden en que se ofrecen al usuario.</summary>
    public static readonly string[] SortModes = { "install", "updated", "size", "name" };

    /// <summary>Botones de solo icono (x:Name) y clave de su descripcion para la ayuda contextual.</summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["SortButton"] = "SortBy",
        ["RefreshButton"] = "Refresh",
        ["SearchButton"] = "SearchPlaceholder",
        ["SelectAllButton"] = "SelectAll",
        ["ClearButton"] = "DeselectAll",
        ["ShowSystemButton"] = "ShowSystemApps",
        ["DiskButton"] = "DiskTitle",
    };

    /// <summary>Controles con texto fijo (x:Name) y su clave.</summary>
    public static readonly IReadOnlyDictionary<string, string> StaticTexts = new Dictionary<string, string>
    {
        ["SearchEntry"] = "SearchPlaceholder",
        ["EmptyLabel"] = "EmptyList",
        ["EmptyHintLabel"] = "EmptyListHint",
    };

    private readonly ILocalizationService _l;
    private readonly ISettingsService _settings;
    private readonly IAppInventoryService _inventory;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IMainView _view;
    private readonly ILogger _logger;
    private readonly bool _isWindows;

    private List<InstalledApp> _apps = new();
    private string _search = string.Empty;

    public MainViewModel(ILocalizationService localization, ISettingsService settings, IAppInventoryService inventory,
        IToastService toast, IDialogService dialogs, IMainView view, ILogger logger, bool isWindows)
    {
        _l = localization;
        _settings = settings;
        _inventory = inventory;
        _toast = toast;
        _dialogs = dialogs;
        _view = view;
        _logger = logger;
        _isWindows = isWindows;
        LoadingText = _l["Loading"];
    }

    public event EventHandler? Changed;

    /// <summary>Todas las aplicaciones cargadas, ya ordenadas.</summary>
    public IReadOnlyList<InstalledApp> Apps => _apps;

    /// <summary>Lo que se esta viendo: <see cref="Apps"/> pasado por el buscador (objeto nuevo en cada cambio).</summary>
    public List<InstalledApp> Visible { get; private set; } = new();

    public bool IsBusy { get; private set; }

    public bool LoadedOnce { get; private set; }

    public string Search => _search;

    public bool SearchVisible { get; private set; }

    public bool LoadingVisible { get; private set; }

    public string LoadingText { get; private set; }

    public bool ProgressVisible { get; private set; }

    public string ProgressDetail { get; private set; } = string.Empty;

    public string CountText { get; private set; } = string.Empty;

    public string UninstallText { get; private set; } = string.Empty;

    public bool UninstallEnabled { get; private set; }

    /// <summary>El conmutador de apps del sistema se pinta relleno cuando esta activo.</summary>
    public bool ShowSystemApps => _settings.ShowSystemApps;

    public int SelectedCount => _apps.Count(a => a.IsSelected);

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Al aparecer: textos y, la primera vez, la lista.</summary>
    public async Task AppearingAsync()
    {
        RefreshTexts();
        if (!LoadedOnce)
        {
            LoadedOnce = true;
            await LoadAppsAsync();
        }
    }

    /// <summary>Textos que dependen del idioma: detalles de cada fila y contadores.</summary>
    public void RefreshTexts()
    {
        LoadingText = _l["Loading"];
        ApplyDetails();
        UpdateCounts();
    }

    public async Task LoadAppsAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        LoadingText = _l["Loading"];
        LoadingVisible = true;
        Notify();

        try
        {
            var apps = await _inventory.GetInstalledAppsAsync(_settings.ShowSystemApps);
            _apps = apps.ToList();
            ApplyDetails();
            ApplySort();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the installed apps");
            await _dialogs.AlertAsync(_l["Error"], string.Format(_l.CurrentCulture, _l["ErrorLoad"], ex.Message), _l["Ok"]);
        }
        finally
        {
            LoadingVisible = false;
            IsBusy = false;
            UpdateCounts();
        }
    }

    /// <summary>Nombre visible del criterio de orden, ya traducido.</summary>
    public string SortModeName(string? mode) => mode switch
    {
        "name" => _l["SortName"],
        "updated" => _l["SortUpdated"],
        "size" => _l["SortSize"],
        _ => _l["SortInstall"],
    };

    private void ApplySort()
    {
        _apps = ListRules.Sort(_apps, _settings.SortMode);
        ApplyFilter();
    }

    /// <summary>Por nombre visible o por paquete: quien busca «whatsapp» y «com.whatsapp» quiere lo mismo.</summary>
    private void ApplyFilter() => Visible = ListRules.Filter(_apps, _search);

    public void SetSearch(string? text)
    {
        _search = text ?? string.Empty;
        ApplyFilter();
        UpdateCounts();
    }

    /// <summary>Linea de detalle de cada fila: instalacion, ultima actualizacion y tamano.</summary>
    private void ApplyDetails()
    {
        var culture = _l.CurrentCulture;
        // Ano de dos cifras: la linea entera tiene que caber en el ancho de la fila.
        var dateFormat = ListRules.CompactDatePattern(culture);

        foreach (var app in _apps)
        {
            var size = ListRules.AppSize(app.SizeBytes, culture);
            var installed = app.InstallDate == DateTime.MinValue ? "—" : app.InstallDate.ToString(dateFormat, culture);

            // La mayoria de apps nunca se actualizan: repetir la misma fecha solo gasta espacio.
            app.Details = app.UpdatedDate == DateTime.MinValue || app.UpdatedDate.Date == app.InstallDate.Date
                ? string.Format(culture, _l["AppDetailsNoUpdate"], installed, size)
                : string.Format(culture, _l["AppDetails"], installed, app.UpdatedDate.ToString(dateFormat, culture), size);
        }
    }

    public async Task ChooseSortAsync()
    {
        // El criterio activo se marca con ✓ para que se vea cual esta aplicado.
        var current = _settings.SortMode;
        var options = SortModes.Select(m => m == current ? $"✓ {SortModeName(m)}" : SortModeName(m)).ToArray();

        var choice = await _dialogs.ActionSheetAsync(_l["SortBy"], _l["Cancel"], options);
        var index = string.IsNullOrEmpty(choice) ? -1 : Array.IndexOf(options, choice);
        if (index < 0)
            return;

        _settings.SortMode = SortModes[index];
        ApplySort();
        UpdateCounts();
    }

    /// <summary>
    /// Contador de lo que se ve, las marcadas (de todas: se desinstalan aunque el buscador las
    /// esconda) y el criterio de orden; y el boton de desinstalar.
    /// </summary>
    public void UpdateCounts()
    {
        var total = Visible.Count;
        var selected = SelectedCount;
        var totalText = total == 1 ? _l["OneInstalledApp"] : string.Format(_l.CurrentCulture, _l["InstalledAppsCount"], total);
        var sortText = SortModeName(_settings.SortMode);

        CountText = selected > 0
            ? $"{totalText} · {string.Format(_l.CurrentCulture, _l["SelectedCount"], selected)} · {sortText}"
            : $"{totalText} · {sortText}";
        UninstallText = selected > 0 ? $"{_l["UninstallSelected"]} ({selected})" : _l["UninstallSelected"];
        UninstallEnabled = selected > 0 && !IsBusy;
        Notify();
    }

    public void ToggleRow(InstalledApp app) => app.IsSelected = !app.IsSelected;

    /// <summary>Marca solo lo que se esta viendo: marcar lo que el buscador esconde seria una trampa.</summary>
    public void SelectAllVisible()
    {
        foreach (var app in Visible)
            app.IsSelected = true;
        UpdateCounts();
    }

    public void ClearSelection()
    {
        foreach (var app in _apps)
            app.IsSelected = false;
        UpdateCounts();
    }

    public async Task ToggleShowSystemAsync()
    {
        _settings.ShowSystemApps = !_settings.ShowSystemApps;
        Notify();
        await LoadAppsAsync();
    }

    /// <summary>La lupa despliega el buscador; al plegarlo se vacia el filtro.</summary>
    public void ToggleSearch()
    {
        SearchVisible = !SearchVisible;
        if (SearchVisible)
        {
            Notify();
            _view.FocusSearch();
            return;
        }

        _view.UnfocusSearch();
        SetSearch(string.Empty);
    }

    /// <summary>Atras (Mobile 7): primero se pliega el buscador, despues se quita la seleccion. True si lo atendio.</summary>
    public bool HandleBack()
    {
        if (SearchVisible)
        {
            ToggleSearch();
            return true;
        }

        if (_apps.Any(a => a.IsSelected))
        {
            ClearSelection();
            return true;
        }

        return false;
    }

    public async Task UninstallSelectedAsync()
    {
        var selected = _apps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await _dialogs.AlertAsync(_l["ConfirmUninstallTitle"], _l["NothingSelected"], _l["Ok"]);
            return;
        }

        var confirm = await _dialogs.AlertAsync(
            _l["ConfirmUninstallTitle"],
            string.Format(_l.CurrentCulture, _l[_isWindows ? "ConfirmUninstallManyWindows" : "ConfirmUninstallMany"], selected.Count),
            _l["Continue"], _l["Cancel"]);
        if (!confirm)
            return;

        // Windows: desatendido donde el instalador lo admite, o con el asistente de cada uno. Se
        // pregunta solo si alguno de los marcados lo admite; en Android no hay opcion.
        var unattended = false;
        var quiet = selected.Count(a => a.SupportsUnattended);
        if (_isWindows && quiet > 0)
        {
            unattended = await _dialogs.AlertAsync(
                _l["UnattendedTitle"],
                string.Format(_l.CurrentCulture, _l["UnattendedBody"], quiet, selected.Count),
                _l["Unattended"], _l["WithWizard"]);
        }

        // Progreso a la vista mientras dura: cual va (n de N), su nombre y la barra.
        LoadingText = _l["Uninstalling"];
        ProgressVisible = true;
        LoadingVisible = true;
        Notify();
        await _view.AnimateProgressAsync(0);

        var done = 0;
        for (var index = 1; index <= selected.Count; index++)
        {
            var app = selected[index - 1];
            ProgressDetail = string.Format(_l.CurrentCulture, _l["UninstallingItem"], index, selected.Count, app.Label);
            Notify();
            await _view.AnimateProgressAsync((index - 1) / (double)selected.Count);
            try
            {
                // Android exige la confirmacion del usuario por cada app; en Windows cada programa
                // abre su propio desinstalador, tambien uno detras de otro.
                if (await _inventory.UninstallAsync(app.PackageName, unattended))
                    done++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not uninstall {Package}", app.PackageName);
                await _dialogs.AlertAsync(_l["Error"], string.Format(_l.CurrentCulture, _l["ErrorUninstall"], app.Label, ex.Message), _l["Ok"]);
            }
        }

        await _view.AnimateProgressAsync(1);
        ProgressVisible = false;
        LoadingVisible = false;
        Notify();

        // Se refresca la lista para reflejar lo que realmente quedo instalado.
        await LoadAppsAsync();
        _toast.Show(string.Format(_l.CurrentCulture, _l["UninstallDone"], done, selected.Count));
    }
}
