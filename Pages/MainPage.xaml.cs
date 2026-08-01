using System.Globalization;
using Microsoft.Extensions.Logging;
using SocShared;
using Uninstaller.Helpers;
using Uninstaller.Models;
using Uninstaller.Services;

namespace Uninstaller.Pages;

public partial class MainPage : ContentPage
{
    private readonly ILocalizationService _l;
    private readonly ISettingsService _settings;
    private readonly IAppInventoryService _inventory;
    private readonly IToastService _toast;
    private readonly UpdateService _update;
    private readonly ILogger<MainPage> _logger;

    private List<InstalledApp> _apps = new();
    private bool _isBusy;
    private bool _loadedOnce;
    private bool _suppressToggle;

    public MainPage()
    {
        InitializeComponent();

        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _inventory = ServiceHelper.GetRequiredService<IAppInventoryService>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();
        _update = ServiceHelper.GetRequiredService<UpdateService>();
        _logger = ServiceHelper.GetRequiredService<ILogger<MainPage>>();

        _l.LanguageChanged += (_, _) => ApplyTexts();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _suppressToggle = true;
        ShowSystemSwitch.IsToggled = _settings.ShowSystemApps;
        _suppressToggle = false;

        ApplyTexts();

        if (!_loadedOnce)
        {
            _loadedOnce = true;
            await LoadAppsAsync();
        }

        // Comprobacion de version al arrancar (constitucion 15): no bloqueante.
        _ = _update.CheckAndPromptAsync(this);
    }

    private void ApplyTexts()
    {
        Title = _l["AppName"];
        TitleLabel.Text = _l["AppsTitle"];
        RefreshButton.Text = _l["Refresh"];
        ShowSystemLabel.Text = _l["ShowSystemApps"];
        SelectAllButton.Text = _l["SelectAll"];
        ClearButton.Text = _l["DeselectAll"];
        EmptyLabel.Text = _l["EmptyList"];
        EmptyHintLabel.Text = _l["EmptyListHint"];
        LoadingLabel.Text = _l["Loading"];
        ApplyDetails();
        UpdateCounts();
    }

    private async Task LoadAppsAsync()
    {
        if (_isBusy)
            return;

        _isBusy = true;
        LoadingOverlay.IsVisible = true;

        try
        {
            var apps = await _inventory.GetInstalledAppsAsync(_settings.ShowSystemApps);
            _apps = apps.ToList();
            ApplyDetails();
            ApplySort();
            UpdateCounts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the installed apps");
            await ModernDialog.AlertAsync(this, _l["Error"], string.Format(_l.CurrentCulture, _l["ErrorLoad"], ex.Message), _l["Ok"]);
        }
        finally
        {
            LoadingOverlay.IsVisible = false;
            ListRefresh.IsRefreshing = false;
            _isBusy = false;
        }
    }

    // Criterios de orden disponibles, en el mismo orden en que se ofrecen al usuario.
    private static readonly string[] SortModes = { "install", "updated", "size", "name" };

    // Nombre visible del criterio, ya traducido (seccion 8: ningun texto fijo en el codigo).
    private string SortModeName(string mode) => mode switch
    {
        "name"    => _l["SortName"],
        "updated" => _l["SortUpdated"],
        "size"    => _l["SortSize"],
        _         => _l["SortInstall"],
    };

    // Ordena la lista segun el criterio guardado y refresca el binding.
    private void ApplySort()
    {
        IEnumerable<InstalledApp> sorted = _settings.SortMode switch
        {
            "name"    => _apps.OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase),
            "updated" => _apps.OrderByDescending(a => a.UpdatedDate),
            "size"    => _apps.OrderByDescending(a => a.SizeBytes),
            _         => _apps.OrderByDescending(a => a.InstallDate), // "install" (defecto)
        };
        _apps = sorted.ToList();
        AppsList.ItemsSource = _apps;
    }

    // Compone la linea de detalle de cada fila: fecha de instalacion, ultima actualizacion y
    // tamano. Se rehace al cambiar de idioma porque el formato depende de la cultura.
    private void ApplyDetails()
    {
        var culture = _l.CurrentCulture;
        // Ano de dos cifras: la linea entera tiene que caber en el ancho de la fila.
        var dateFormat = culture.DateTimeFormat.ShortDatePattern.Replace("yyyy", "yy");

        foreach (var app in _apps)
        {
            var size = FormatSize(app.SizeBytes, culture);
            var installed = app.InstallDate == DateTime.MinValue ? "—" : app.InstallDate.ToString(dateFormat, culture);

            // La mayoria de apps nunca se actualizan: repetir la misma fecha solo gasta espacio.
            if (app.UpdatedDate == DateTime.MinValue || app.UpdatedDate.Date == app.InstallDate.Date)
            {
                app.Details = string.Format(culture, _l["AppDetailsNoUpdate"], installed, size);
                continue;
            }

            app.Details = string.Format(
                culture,
                _l["AppDetails"],
                installed,
                app.UpdatedDate.ToString(dateFormat, culture),
                size);
        }
    }

    private static string FormatSize(long bytes, CultureInfo culture)
    {
        const long Kb = 1024;
        const long Mb = Kb * 1024;
        const long Gb = Mb * 1024;

        return bytes switch
        {
            <= 0    => "—",
            >= Gb   => $"{(bytes / (double)Gb).ToString("0.#", culture)} GB",
            >= Mb   => $"{(bytes / (double)Mb).ToString("0.#", culture)} MB",
            >= Kb   => $"{(bytes / (double)Kb).ToString("0.#", culture)} kB",
            _       => $"{bytes.ToString(culture)} B",
        };
    }

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        // El criterio activo se marca con ✓ para que se vea cual esta aplicado.
        var current = _settings.SortMode;
        var options = SortModes
            .Select(m => m == current ? $"✓ {SortModeName(m)}" : SortModeName(m))
            .ToArray();

        var choice = await ModernDialog.ActionSheetAsync(this, _l["SortBy"], _l["Cancel"], options);
        if (string.IsNullOrEmpty(choice))
            return;

        var index = Array.IndexOf(options, choice);
        if (index < 0)
            return;

        _settings.SortMode = SortModes[index];
        ApplySort();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        var total = _apps.Count;
        var selected = _apps.Count(a => a.IsSelected);

        var totalText = total == 1
            ? _l["OneApp"]
            : string.Format(_l.CurrentCulture, _l["AppsCount"], total);

        // El criterio de orden se muestra siempre junto al contador: era invisible y no se
        // adivinaba que el boton de la derecha ordenaba (nota de autor del 2026-08-01).
        var sortText = string.Format(_l.CurrentCulture, _l["SortedBy"], SortModeName(_settings.SortMode));

        CountLabel.Text = selected > 0
            ? $"{totalText} · {string.Format(_l.CurrentCulture, _l["SelectedCount"], selected)} · {sortText}"
            : $"{totalText} · {sortText}";

        UninstallButton.Text = selected > 0
            ? $"{_l["UninstallSelected"]} ({selected})"
            : _l["UninstallSelected"];
        UninstallButton.IsEnabled = selected > 0 && !_isBusy;
    }

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Element { BindingContext: InstalledApp app })
            app.IsSelected = !app.IsSelected;
        // El CheckBox refleja el cambio por binding y dispara OnItemCheckedChanged.
    }

    private void OnItemCheckedChanged(object? sender, CheckedChangedEventArgs e) => UpdateCounts();

    private void OnSelectAllClicked(object? sender, EventArgs e)
    {
        foreach (var app in _apps)
            app.IsSelected = true;
        UpdateCounts();
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        foreach (var app in _apps)
            app.IsSelected = false;
        UpdateCounts();
    }

    private async void OnShowSystemToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressToggle)
            return;

        _settings.ShowSystemApps = e.Value;
        await LoadAppsAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAppsAsync();

    private async void OnRefreshing(object? sender, EventArgs e) => await LoadAppsAsync();

    private async void OnUninstallSelectedClicked(object? sender, EventArgs e)
    {
        var selected = _apps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await ModernDialog.AlertAsync(this, _l["ConfirmUninstallTitle"], _l["NothingSelected"], _l["Ok"]);
            return;
        }

        var confirm = await ModernDialog.AlertAsync(
            this,
            _l["ConfirmUninstallTitle"],
            string.Format(_l.CurrentCulture, _l["ConfirmUninstallMany"], selected.Count),
            _l["Continue"], _l["Cancel"]);

        if (!confirm)
            return;

        var done = 0;
        foreach (var app in selected)
        {
            try
            {
                // Android exige la confirmacion del usuario por cada app (sin borrado masivo silencioso).
                if (await _inventory.UninstallAsync(app.PackageName))
                    done++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not uninstall {Package}", app.PackageName);
                await ModernDialog.AlertAsync(
                    this,
                    _l["Error"],
                    string.Format(_l.CurrentCulture, _l["ErrorUninstall"], app.Label, ex.Message),
                    _l["Ok"]);
            }
        }

        // Se refresca la lista al volver para reflejar lo que realmente quedo instalado.
        await LoadAppsAsync();
        _toast.Show(string.Format(_l.CurrentCulture, _l["UninstallDone"], done, selected.Count));
    }
}
