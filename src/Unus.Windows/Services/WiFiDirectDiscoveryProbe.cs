using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFiDirect;
using Unus.Windows.Models;

namespace Unus.Windows.Services;

internal sealed class WiFiDirectDiscoveryProbe : IDisposable
{
    private readonly WiFiDirectAdvertisementPublisher _publisher;
    private readonly DeviceWatcher _watcher;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Random _random = new();

    private bool _disposed;
    private bool _isPairing;
    private bool _refreshPending;

    public ObservableCollection<DiscoveredDevice> Devices { get; }
        = new();

    public ObservableCollection<string> Events { get; }
        = new();

    public string PublisherStatus { get; private set; } = "Created";
    public string WatcherStatus { get; private set; } = "Created";

    public bool IsPairing => _isPairing;

    public WiFiDirectDiscoveryProbe(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        // Create the Wi-Fi Direct advertisement publisher.
        _publisher = new WiFiDirectAdvertisementPublisher();

        // Keep the laptop discoverable in foreground and background.
        _publisher.Advertisement.ListenStateDiscoverability =
            WiFiDirectAdvertisementListenStateDiscoverability.Intensive;

        // Create the device watcher.
        string selector = WiFiDirectDevice.GetDeviceSelector(
            WiFiDirectDeviceSelectorType.AssociationEndpoint);

        AddEvent($"Device selector: {selector}");

        _watcher = DeviceInformation.CreateWatcher(
            selector,
            Array.Empty<string>(),
            DeviceInformationKind.AssociationEndpoint);

        // Refresh discovery during Active Pairing.
        _refreshTimer = new DispatcherTimer();

        _refreshTimer.Tick += OnRefreshTimerTick;

        // Publisher events.
        _publisher.StatusChanged += OnPublisherStatusChanged;

        // Watcher events.
        _watcher.Added += OnDeviceAdded;
        _watcher.Updated += OnDeviceUpdated;
        _watcher.Removed += OnDeviceRemoved;
        _watcher.EnumerationCompleted += OnEnumerationCompleted;
        _watcher.Stopped += OnWatcherStopped;

        AddEvent("Discovery probe initialized. Waiting for user.");
    }

    // ---------------------------------------------------------
    // ACTIVE PAIRING
    // ---------------------------------------------------------

    public void StartPairing()
    {
        if (_disposed || _isPairing)
            return;

        _isPairing = true;
        _refreshPending = false;

        // Begin a fresh pairing session.
        Devices.Clear();

        AddEvent("========================================");
        AddEvent("ACTIVE PAIRING STARTED.");
        AddEvent("Starting Wi-Fi Direct advertisement...");

        try
        {
            _publisher.Start();

            PublisherStatus = _publisher.Status.ToString();

            AddEvent(
                $"Publisher Start() called. Status: {PublisherStatus}");
        }
        catch (Exception ex)
        {
            AddEvent($"PUBLISHER START ERROR: {ex}");
        }

        // Start scanning independently of publisher startup.
        StartWatcher();

        // Begin the refresh interval.
        _refreshTimer.Interval = GetRandomRefreshInterval();
        _refreshTimer.Start();

        AddEvent(
            $"Automatic watcher refresh enabled. Next refresh in {_refreshTimer.Interval.TotalSeconds:F2} seconds.");
    }

    public void StopPairing()
    {
        if (_disposed || !_isPairing)
            return;

        AddEvent("STOPPING ACTIVE PAIRING...");

        _isPairing = false;
        _refreshPending = false;

        // Stop automatic refresh.
        _refreshTimer.Stop();

        // Stop the device watcher.
        if (_watcher.Status == DeviceWatcherStatus.Started ||
            _watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
        {
            try
            {
                AddEvent("Stopping device watcher...");
                _watcher.Stop();
            }
            catch (Exception ex)
            {
                AddEvent($"WATCHER STOP ERROR: {ex}");
            }
        }

        // Stop advertising.
        try
        {
            AddEvent("Stopping Wi-Fi Direct advertisement...");
            _publisher.Stop();

            PublisherStatus = _publisher.Status.ToString();

            AddEvent(
                $"Publisher Stop() called. Status: {PublisherStatus}");
        }
        catch (Exception ex)
        {
            AddEvent($"PUBLISHER STOP ERROR: {ex}");
        }

        AddEvent("ACTIVE PAIRING STOPPED.");
        AddEvent("Discovery is now idle.");
    }

    // ---------------------------------------------------------
    // DEVICE WATCHER
    // ---------------------------------------------------------

    private void StartWatcher()
    {
        if (_disposed || !_isPairing)
            return;

        DeviceWatcherStatus status = _watcher.Status;

        if (status != DeviceWatcherStatus.Created &&
            status != DeviceWatcherStatus.Stopped &&
            status != DeviceWatcherStatus.Aborted)
        {
            AddEvent(
                $"WATCHER START SKIPPED. Current status: {status}");

            return;
        }

        try
        {
            AddEvent("Starting device watcher...");

            _watcher.Start();

            WatcherStatus = _watcher.Status.ToString();

            AddEvent(
                $"Watcher Start() called. Status: {WatcherStatus}");
        }
        catch (Exception ex)
        {
            AddEvent($"WATCHER START ERROR: {ex}");
        }
    }

    private void OnDeviceAdded(
        DeviceWatcher sender,
        DeviceInformation deviceInfo)
    {
        RunOnUI(() =>
        {
            AddEvent(
                $"DEVICE ADDED EVENT RECEIVED. Watcher: {_watcher.Status}");

            AddEvent($"DEVICE ADDED: {deviceInfo.Name}");
            AddEvent($"Device ID: {deviceInfo.Id}");

            var existing = Devices.FirstOrDefault(
                d => d.DeviceId == deviceInfo.Id);

            if (existing == null)
            {
                Devices.Add(new DiscoveredDevice
                {
                    Name = deviceInfo.Name,
                    DeviceId = deviceInfo.Id
                });
            }
            else
            {
                AddEvent(
                    "Device already exists; keeping existing entry.");
            }
        });
    }

    private void OnDeviceUpdated(
        DeviceWatcher sender,
        DeviceInformationUpdate update)
    {
        RunOnUI(() =>
        {
            AddEvent(
                $"DEVICE UPDATED: {update.Id} | Watcher: {_watcher.Status}");
        });
    }

    private void OnDeviceRemoved(
        DeviceWatcher sender,
        DeviceInformationUpdate update)
    {
        RunOnUI(() =>
        {
            AddEvent($"DEVICE REMOVED: {update.Id}");

            var existing = Devices.FirstOrDefault(
                d => d.DeviceId == update.Id);

            if (existing != null)
                Devices.Remove(existing);
        });
    }

    private void OnEnumerationCompleted(
        DeviceWatcher sender,
        object args)
    {
        RunOnUI(() =>
        {
            WatcherStatus = sender.Status.ToString();

            AddEvent(
                $"INITIAL ENUMERATION COMPLETED. Watcher: {WatcherStatus}");

            AddEvent(
                "Watcher remains active and monitors device changes.");
        });
    }

    // ---------------------------------------------------------
    // AUTOMATIC AND MANUAL REFRESH
    // ---------------------------------------------------------

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        // Randomize the next automatic refresh interval.
        _refreshTimer.Interval = GetRandomRefreshInterval();

        AddEvent(
            $"Next automatic refresh scheduled in {_refreshTimer.Interval.TotalSeconds:F2} seconds.");

        RefreshDiscovery("AUTOMATIC");
    }

    private TimeSpan GetRandomRefreshInterval()
    {
        // Random interval between 4 and 6 seconds.
        int milliseconds = _random.Next(4000, 6001);

        return TimeSpan.FromMilliseconds(milliseconds);
    }
    public void RefreshDiscoveryManually()
    {
        RefreshDiscovery("MANUAL");
    }

    private void RefreshDiscovery(string refreshType)
    {
        if (_disposed || !_isPairing)
        {
            AddEvent(
                $"{refreshType} REFRESH IGNORED. Active Pairing is not running.");

            return;
        }

        if (_refreshPending)
        {
            AddEvent(
                $"{refreshType} REFRESH IGNORED. A refresh is already in progress.");

            return;
        }

        if (_watcher.Status != DeviceWatcherStatus.Started &&
            _watcher.Status != DeviceWatcherStatus.EnumerationCompleted)
        {
            AddEvent(
                $"{refreshType} REFRESH ABORTED. Watcher status: {_watcher.Status}");

            return;
        }

        AddEvent($"{refreshType} WATCHER REFRESH REQUESTED.");

        _refreshPending = true;

        try
        {
            AddEvent("Stopping watcher before refresh...");
            _watcher.Stop();
        }
        catch (Exception ex)
        {
            _refreshPending = false;

            AddEvent($"WATCHER REFRESH STOP ERROR: {ex}");
        }
    }

    private void OnWatcherStopped(
        DeviceWatcher sender,
        object args)
    {
        RunOnUI(() =>
        {
            WatcherStatus = sender.Status.ToString();

            AddEvent($"WATCHER STOPPED: {WatcherStatus}");

            // Do not restart if the user has stopped pairing.
            if (_disposed || !_isPairing || !_refreshPending)
                return;

            _refreshPending = false;

            AddEvent("WATCHER REFRESH: restarting device watcher...");

            StartWatcher();
        });
    }

    // ---------------------------------------------------------
    // PUBLISHER EVENTS
    // ---------------------------------------------------------

    private void OnPublisherStatusChanged(
        WiFiDirectAdvertisementPublisher sender,
        WiFiDirectAdvertisementPublisherStatusChangedEventArgs args)
    {
        RunOnUI(() =>
        {
            PublisherStatus = args.Status.ToString();

            AddEvent(
                $"PUBLISHER STATUS: {args.Status} | Error: {args.Error}");
        });
    }

    // ---------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------

    private void RunOnUI(Action action)
    {
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed)
                action();
        }));
    }

    private void AddEvent(string message)
    {
        Events.Add(
            $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    // ---------------------------------------------------------
    // DISPOSAL
    // ---------------------------------------------------------

    public void Dispose()
    {
        if (_disposed)
            return;

        AddEvent("Disposing discovery probe...");

        _disposed = true;
        _isPairing = false;
        _refreshPending = false;

        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;

        _publisher.StatusChanged -= OnPublisherStatusChanged;

        _watcher.Added -= OnDeviceAdded;
        _watcher.Updated -= OnDeviceUpdated;
        _watcher.Removed -= OnDeviceRemoved;
        _watcher.EnumerationCompleted -= OnEnumerationCompleted;
        _watcher.Stopped -= OnWatcherStopped;

        if (_watcher.Status == DeviceWatcherStatus.Started ||
            _watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
        {
            _watcher.Stop();
        }

        _publisher.Stop();
    }
}