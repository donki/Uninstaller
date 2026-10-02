using Microsoft.Extensions.Logging;
using Uninstaller.Helpers;
using Uninstaller.Models;
using Uninstaller.Services;
using Uninstaller.ViewModels;

namespace Uninstaller.Pages;

/// <summary>Lista de aplicaciones: enlace fino con <see cref="MainViewModel"/>, que tiene la logica.</summary>
public partial class MainPage : ContentPage, IMainView
{
    private readonly ILocalizationService _l;
    private readonly ISettingsService _settings;
    private readonly UpdateService _update;
    private readonly IDialogService _dialogs;
    private readonly MainViewModel _vm;

    public MainPage()
    {
        InitializeComponent();

        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _update = ServiceHelper.GetRequiredService<UpdateService>();
        _dialogs = new PageDialogService(this);
        _vm = new MainViewModel(_l, _settings,
            ServiceHelper.GetRequiredService<IAppInventoryService>(),
            ServiceHelper.GetRequiredService<IToastService>(),
            _dialogs, this,
            ServiceHelper.GetRequiredService<ILogger<MainPage>>(),
            isWindows: DeviceInfo.Platform == DevicePlatform.WinUI);
#if WINDOWS
        // Espacio en disco: solo en Windows (en Android haria falta el permiso de todo el almacenamiento).
        HeaderButtons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        DiskButton.IsVisible = true;
        // El «toast» de Windows lo pinta la propia pagina: una franja que se esconde a los tres segundos.
        Platforms.Windows.ToastService.ToastRequested += async message =>
        {
            ToastLabel.Text = message;
            ToastStrip.IsVisible = true;
            await Task.Delay(3000);
            ToastStrip.IsVisible = false;
        };
#endif
        _vm.Changed += (_, _) => Render();
        _l.LanguageChanged += (_, _) => ApplyTexts();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyTexts();
        await _vm.AppearingAsync();

        // Comprobacion de version al arrancar (constitucion 15): no bloqueante.
        _ = _update.CheckAndPromptAsync(_dialogs);
    }

    private void ApplyTexts()
    {
        Title = _l["AppName"];
        // Los botones de la cabecera son solo icono: el texto va a la ayuda contextual.
        foreach (var (name, key) in MainViewModel.Descriptions)
            SemanticProperties.SetDescription((BindableObject)FindByName(name), _l[key]);
        ToolTipProperties.SetText(DiskButton, _l["DiskTitle"]);
        SearchEntry.Placeholder = _l[MainViewModel.StaticTexts["SearchEntry"]];
        EmptyLabel.Text = _l[MainViewModel.StaticTexts["EmptyLabel"]];
        EmptyHintLabel.Text = _l[MainViewModel.StaticTexts["EmptyHintLabel"]];
        _vm.RefreshTexts();
    }

    /// <summary>Vuelca el estado de la logica en los controles.</summary>
    private void Render()
    {
        if (!ReferenceEquals(AppsList.ItemsSource, _vm.Visible))
            AppsList.ItemsSource = _vm.Visible;
        CountLabel.Text = _vm.CountText;
        UninstallButton.Text = _vm.UninstallText;
        UninstallButton.IsEnabled = _vm.UninstallEnabled;
        LoadingLabel.Text = _vm.LoadingText;
        LoadingOverlay.IsVisible = _vm.LoadingVisible;
        UninstallProgress.IsVisible = UninstallDetail.IsVisible = _vm.ProgressVisible;
        UninstallDetail.Text = _vm.ProgressDetail;
        if (!_vm.IsBusy)
            ListRefresh.IsRefreshing = false;
        ShowSystemButton.BackgroundColor = _vm.ShowSystemApps ? (Color)Application.Current!.Resources["Primary"] : Colors.Transparent;
        ShowSystemButton.ImageSource = _vm.ShowSystemApps ? "ic_system_w.png" : "ic_system.png";
        SearchRow.IsVisible = _vm.SearchVisible;
        if (_vm.Search.Length == 0 && !string.IsNullOrEmpty(SearchEntry.Text))
            SearchEntry.Text = string.Empty;
    }

    // ============ IMainView ============

    public void FocusSearch() => SearchEntry.Focus();

    public void UnfocusSearch() => SearchEntry.Unfocus();

    public Task AnimateProgressAsync(double value) => UninstallProgress.ProgressTo(value, 150, Easing.Linear);

    /// <summary>Atras (Mobile 7): lo que hay abierto encima se cierra antes de salir.</summary>
    protected override bool OnBackButtonPressed() => _vm.HandleBack() || base.OnBackButtonPressed();

    // ============ Toques ============

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => _vm.SetSearch(e.NewTextValue);

    private async void OnSortClicked(object? sender, EventArgs e) => await _vm.ChooseSortAsync();

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        // El CheckBox refleja el cambio por binding y dispara OnItemCheckedChanged.
        if (sender is Element { BindingContext: InstalledApp app })
            _vm.ToggleRow(app);
    }

    private void OnItemCheckedChanged(object? sender, CheckedChangedEventArgs e) => _vm.UpdateCounts();

    private void OnSelectAllClicked(object? sender, EventArgs e) => _vm.SelectAllVisible();

    private void OnClearClicked(object? sender, EventArgs e) => _vm.ClearSelection();

    private async void OnShowSystemClicked(object? sender, EventArgs e) => await _vm.ToggleShowSystemAsync();

    private void OnSearchClicked(object? sender, EventArgs e) => _vm.ToggleSearch();

    private async void OnRefreshClicked(object? sender, EventArgs e) => await _vm.LoadAppsAsync();

    private async void OnDiskClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("//DiskUsagePage");

    private async void OnRefreshing(object? sender, EventArgs e) => await _vm.LoadAppsAsync();

    private async void OnUninstallSelectedClicked(object? sender, EventArgs e) => await _vm.UninstallSelectedAsync();
}
