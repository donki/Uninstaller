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
            AppsList.ItemsSource = _apps;
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

    private void UpdateCounts()
    {
        var total = _apps.Count;
        var selected = _apps.Count(a => a.IsSelected);

        var totalText = total == 1
            ? _l["OneApp"]
            : string.Format(_l.CurrentCulture, _l["AppsCount"], total);

        CountLabel.Text = selected > 0
            ? $"{totalText} · {string.Format(_l.CurrentCulture, _l["SelectedCount"], selected)}"
            : totalText;

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
