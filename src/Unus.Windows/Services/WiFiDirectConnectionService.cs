using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using Unus.Windows.Models;
using Windows.Devices.WiFiDirect;

namespace Unus.Windows.Services;

public sealed class WiFiDirectConnectionService : IDisposable
{
    private readonly Dispatcher _dispatcher;

    private WiFiDirectConnectionListener? _listener;
    private WiFiDirectDevice? _device;

    private bool _isConnecting;
    private bool _disposed;

    public bool IsConnected =>
        _device?.ConnectionStatus == WiFiDirectConnectionStatus.Connected;

    public string? ConnectedDeviceName { get; private set; }

    public string? LocalAddress { get; private set; }

    public string? RemoteAddress { get; private set; }

    public event Action<string>? StatusChanged;

    // The UI must explicitly approve incoming connection requests.
    public event Func<string, bool>? IncomingConnectionApprovalRequested;

    public WiFiDirectConnectionService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void StartListening()
    {
        if (_disposed || _listener != null)
            return;

        try
        {
            _listener = new WiFiDirectConnectionListener();
            _listener.ConnectionRequested += OnConnectionRequested;

            StatusChanged?.Invoke("Incoming connection listener started.");
        }
        catch (Exception ex)
        {
            _listener = null;

            StatusChanged?.Invoke(
                $"Could not start connection listener: {ex.Message} " +
                $"(HRESULT: 0x{ex.HResult:X8})");
        }
    }

    public void StopListening()
    {
        if (_listener == null)
            return;

        _listener.ConnectionRequested -= OnConnectionRequested;
        _listener = null;

        StatusChanged?.Invoke("Incoming connection listener stopped.");
    }

    public async Task<string> ConnectAsync(DiscoveredDevice device)
    {
        if (_disposed)
            return "Connection service has been disposed.";

        if (device == null)
            return "No device was selected.";

        if (string.IsNullOrWhiteSpace(device.DeviceId))
            return "The selected device has no valid Device ID.";

        if (_device != null || _isConnecting)
            return "A connection already exists or is in progress.";

        _isConnecting = true;

        WiFiDirectDevice? candidate = null;

        try
        {
            StatusChanged?.Invoke($"Connecting to {device.Name}...");

            // Call from the UI thread.
            candidate = await WiFiDirectDevice.FromIdAsync(device.DeviceId);

            if (candidate == null)
                return "Wi-Fi Direct did not return a connection device.";

            var endpoints = candidate.GetConnectionEndpointPairs();

            if (endpoints.Count == 0)
                return "Connection returned no network endpoints.";

            candidate.ConnectionStatusChanged += OnConnectionStatusChanged;

            _device = candidate;
            candidate = null;

            ConnectedDeviceName = device.Name;
            LocalAddress = endpoints[0].LocalHostName.ToString();
            RemoteAddress = endpoints[0].RemoteHostName.ToString();

            string result =
                $"Connected to {ConnectedDeviceName}.\n" +
                $"Local endpoint: {LocalAddress}\n" +
                $"Remote endpoint: {RemoteAddress}";

            StatusChanged?.Invoke(result);

            return result;
        }
        catch (Exception ex)
        {
            string error =
                $"Outgoing connection failed: {ex.Message}\n" +
                $"HRESULT: 0x{ex.HResult:X8}";

            StatusChanged?.Invoke(error);

            return error;
        }
        finally
        {
            if (candidate != null)
            {
                candidate.Dispose();
            }

            _isConnecting = false;
        }
    }

    private async void OnConnectionRequested(
        WiFiDirectConnectionListener sender,
        WiFiDirectConnectionRequestedEventArgs args)
    {
        WiFiDirectConnectionRequest? request = null;

        try
        {
            request = args.GetConnectionRequest();

            // Handle the request on the WPF UI thread.
            await _dispatcher.InvokeAsync(
                () => HandleIncomingRequestAsync(request)).Task.Unwrap();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                $"Incoming request handling failed: {ex.Message} " +
                $"(HRESULT: 0x{ex.HResult:X8})");
        }
        finally
        {
            request?.Dispose();
        }
    }

    private async Task HandleIncomingRequestAsync(
        WiFiDirectConnectionRequest request)
    {
        if (_disposed)
            return;

        string deviceName = request.DeviceInformation.Name;
        string deviceId = request.DeviceInformation.Id;

        if (_device != null || _isConnecting)
        {
            StatusChanged?.Invoke(
                $"Declined request from {deviceName}: another connection is active.");

            return;
        }

        bool approved =
            IncomingConnectionApprovalRequested?.Invoke(deviceName) ?? false;

        if (!approved)
        {
            StatusChanged?.Invoke(
                $"Incoming connection request from {deviceName} declined.");

            return;
        }

        _isConnecting = true;

        WiFiDirectDevice? candidate = null;

        try
        {
            StatusChanged?.Invoke($"Accepting connection from {deviceName}...");

            // Microsoft specifies that FromIdAsync should be called
            // from the UI thread.
            candidate = await WiFiDirectDevice.FromIdAsync(deviceId);

            if (candidate == null)
            {
                StatusChanged?.Invoke(
                    $"Could not establish connection with {deviceName}.");

                return;
            }

            var endpoints = candidate.GetConnectionEndpointPairs();

            if (endpoints.Count == 0)
            {
                StatusChanged?.Invoke(
                    $"No network endpoints returned for {deviceName}.");

                return;
            }

            candidate.ConnectionStatusChanged += OnConnectionStatusChanged;

            _device = candidate;
            candidate = null;

            ConnectedDeviceName = deviceName;
            LocalAddress = endpoints[0].LocalHostName.ToString();
            RemoteAddress = endpoints[0].RemoteHostName.ToString();

            StatusChanged?.Invoke(
                $"Connected to {ConnectedDeviceName}.\n" +
                $"Local endpoint: {LocalAddress}\n" +
                $"Remote endpoint: {RemoteAddress}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                $"Incoming connection failed: {ex.Message} " +
                $"(HRESULT: 0x{ex.HResult:X8})");
        }
        finally
        {
            candidate?.Dispose();
            _isConnecting = false;
        }
    }

    private void OnConnectionStatusChanged(
        WiFiDirectDevice sender,
        object args)
    {
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_device, sender))
                return;

            if (sender.ConnectionStatus ==
                WiFiDirectConnectionStatus.Connected)
            {
                StatusChanged?.Invoke("Wi-Fi Direct connection established.");
            }
            else
            {
                StatusChanged?.Invoke("Wi-Fi Direct connection disconnected.");
                Disconnect();
            }
        }));
    }

    public void Disconnect()
    {
        var device = _device;

        if (device == null)
            return;

        _device = null;

        device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        device.Dispose();

        ConnectedDeviceName = null;
        LocalAddress = null;
        RemoteAddress = null;

        StatusChanged?.Invoke("Connection closed.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopListening();
        Disconnect();

        _disposed = true;
    }
}