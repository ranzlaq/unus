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

        DiscoveryStatusText.Text = "Starting discovery...";

        _discoveryProbe.Devices.CollectionChanged += OnDevicesChanged;
        _discoveryProbe.Events.CollectionChanged += OnEventsChanged;

        Closed += (_, _) => _discoveryProbe.Dispose();

        _discoveryProbe.Start();
    }

    private void OnDevicesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        NoDevicesPanel.Visibility =
            _discoveryProbe.Devices.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        DiscoveryStatusText.Text =
            $"{_discoveryProbe.Devices.Count} device(s) discovered";
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