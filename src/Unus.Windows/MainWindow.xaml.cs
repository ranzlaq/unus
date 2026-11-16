using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Unus.Windows.Models;
using Unus.Windows.Services;

namespace Unus.Windows;

public partial class MainWindow : Window
{
    private readonly WiFiDirectDiscoveryProbe _discoveryProbe;
    private readonly WiFiDirectConnectionService _connectionService;

    public MainWindow()
    {
        InitializeComponent();

        LocalDeviceName.Text = Environment.MachineName;

        _discoveryProbe = new WiFiDirectDiscoveryProbe(Dispatcher);
        _connectionService = new WiFiDirectConnectionService(Dispatcher);

        DiscoveredDevicesList.ItemsSource = _discoveryProbe.Devices;
        DiscoveryLogList.ItemsSource = _discoveryProbe.Events;

        DiscoveryStatusText.Text = "Discovery off";
        NoDevicesPanel.Visibility = Visibility.Visible;

        ConnectionStatusText.Text = "Not connected";
        ConnectionEndpointText.Text = string.Empty;
        DisconnectButton.IsEnabled = false;

        _discoveryProbe.Devices.CollectionChanged += OnDevicesChanged;
        _discoveryProbe.Events.CollectionChanged += OnEventsChanged;

        _connectionService.StatusChanged += OnConnectionStatusChanged;
        _connectionService.IncomingConnectionApprovalRequested +=
            OnIncomingConnectionApprovalRequested;

        // Listen for incoming connection requests while UNUS is running.
        _connectionService.StartListening();

        Closed += (_, _) =>
        {
            _connectionService.Dispose();
            _discoveryProbe.Dispose();
        };
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

    private async void ConnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not DiscoveredDevice device)
        {
            return;
        }

        if (_connectionService.IsConnected)
        {
            MessageBox.Show(
                "UNUS already has an active connection.",
                "Connection unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        button.IsEnabled = false;

        ConnectionStatusText.Text = $"Connecting to {device.Name}...";
        ConnectionEndpointText.Text = string.Empty;

        string result = await _connectionService.ConnectAsync(device);

        ConnectionStatusText.Text = result;

        UpdateConnectionUI();

        if (!_connectionService.IsConnected)
            button.IsEnabled = true;
    }

    private void DisconnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _connectionService.Disconnect();
        UpdateConnectionUI();
    }

    private bool OnIncomingConnectionApprovalRequested(string deviceName)
    {
        MessageBoxResult result = MessageBox.Show(
            $"Device \"{deviceName}\" wants to connect to this computer.\n\n" +
            "Do you want to allow this connection?",
            "Incoming UNUS connection",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    private void OnConnectionStatusChanged(string message)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ConnectionStatusText.Text = message;
            UpdateConnectionUI();
        }));
    }

    private void UpdateConnectionUI()
    {
        bool connected = _connectionService.IsConnected;

        DisconnectButton.IsEnabled = connected;

        if (connected)
        {
            ConnectionStatusText.Text =
                $"Connected to {_connectionService.ConnectedDeviceName}";

            ConnectionEndpointText.Text =
                $"Local endpoint: {_connectionService.LocalAddress}\n" +
                $"Remote endpoint: {_connectionService.RemoteAddress}";
        }
        else if (_connectionService.ConnectedDeviceName == null)
        {
            ConnectionEndpointText.Text = string.Empty;
        }
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