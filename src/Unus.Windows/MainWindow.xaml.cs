using System;
using System.Windows;
using Unus.Windows.Services;

namespace Unus.Windows;

public partial class MainWindow : Window
{
    private WiFiDirectDiscoveryProbe? _discoveryProbe;

    public MainWindow()
    {
        InitializeComponent();

        LocalDeviceName.Text = Environment.MachineName;

        _discoveryProbe = new WiFiDirectDiscoveryProbe(message =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(
                    this,
                    message,
                    "Unus - Wi-Fi Direct Discovery",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }));
        });

        _discoveryProbe.Start();

        Closed += (_, _) => _discoveryProbe?.Dispose();
    }
}