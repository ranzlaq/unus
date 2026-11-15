using System;
using System.Collections.Specialized;
using System.Windows;
using Unus.Windows.Services;

namespace Unus.Windows;

public partial class MainWindow : Window
{
    private readonly WiFiDirectDiscoveryProbe _discoveryProbe;

    public MainWindow()
    {
        InitializeComponent();

        LocalDeviceName.Text = Environment.MachineName;

        _discoveryProbe = new WiFiDirectDiscoveryProbe(Dispatcher);

        DiscoveredDevicesList.ItemsSource = _discoveryProbe.Devices;
        DiscoveryLogList.ItemsSource = _discoveryProbe.Events;

        // Start in Idle. Discovery begins only when requested.
        DiscoveryStatusText.Text = "Discovery off";

        NoDevicesPanel.Visibility = Visibility.Visible;

        _discoveryProbe.Devices.CollectionChanged += OnDevicesChanged;
        _discoveryProbe.Events.CollectionChanged += OnEventsChanged;

        Closed += (_, _) => _discoveryProbe.Dispose();
    }

    private void AddDeviceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _discoveryProbe.StartPairing();

        DiscoveryStatusText.Text = "Active Pairing";
        AddDeviceButton.IsEnabled = false;
        RefreshButton.IsEnabled = true;
        StopPairingButton.IsEnabled = true;
    }

    private void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _discoveryProbe.RefreshDiscoveryManually();
    }

    private void StopPairingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _discoveryProbe.StopPairing();

        DiscoveryStatusText.Text = "Discovery off";
        AddDeviceButton.IsEnabled = true;
        RefreshButton.IsEnabled = false;
        StopPairingButton.IsEnabled = false;
    }

    private void OnDevicesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        NoDevicesPanel.Visibility =
            _discoveryProbe.Devices.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (_discoveryProbe.IsPairing)
        {
            DiscoveryStatusText.Text =
                $"Active Pairing • {_discoveryProbe.Devices.Count} device(s)";
        }
        else
        {
            DiscoveryStatusText.Text = "Discovery off";
        }
    }

    private void OnEventsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (DiscoveryLogList.Items.Count > 0)
        {
            DiscoveryLogList.ScrollIntoView(
                DiscoveryLogList.Items[
                    DiscoveryLogList.Items.Count - 1]);
        }
    }
}