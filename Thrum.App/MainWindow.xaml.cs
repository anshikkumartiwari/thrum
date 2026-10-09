using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Thrum.App.ViewModels;

namespace Thrum.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void OnTabZonesClick(object sender, RoutedEventArgs e)
    {
        ZonesTabContent.Visibility = Visibility.Visible;
        SettingsTabContent.Visibility = Visibility.Collapsed;
        _viewModel.SelectedTabIndex = 0;
    }

    private void OnTabSettingsClick(object sender, RoutedEventArgs e)
    {
        ZonesTabContent.Visibility = Visibility.Collapsed;
        SettingsTabContent.Visibility = Visibility.Visible;
        _viewModel.SelectedTabIndex = 1;
    }

    private void OnColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hexColor && _viewModel.SelectedZone != null)
        {
            _viewModel.SelectedZone.Color = hexColor;
        }
    }

    private void OnBrowseAppClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe|All Files (*.*)|*.*",
            Title = "Select Application to Launch"
        };

        if (dialog.ShowDialog() == true && _viewModel.SelectedZone != null)
        {
            _viewModel.SelectedZone.TargetPath = dialog.FileName;
        }
    }

    private void OnCloseTrainingDialogClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowTrainingResultDialog = false;
        _viewModel.StartListening();
    }

    private void OnTrayIconClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowVisibility();
    }

    private void OnOpenWindowClick(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void OnQuitAppClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
        TrayIcon.Dispose();
        Application.Current.Shutdown();
    }

    private void ToggleWindowVisibility()
    {
        if (Visibility == Visibility.Visible && WindowState != WindowState.Minimized)
        {
            Hide();
        }
        else
        {
            ShowWindow();
        }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Minimize to tray on close
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}