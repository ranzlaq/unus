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

    private bool _disposed;
    private bool _refreshAttempted;
    private bool _refreshPending;

    public ObservableCollection<DiscoveredDevice> Devices { get; }
        = new();

    public ObservableCollection<string> Events { get; }
        = new();

    public string PublisherStatus { get; private set; } = "Created";
    public string WatcherStatus { get; private set; } = "Created";

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

        // Configure a one-time discovery refresh 30 seconds
        // after initial enumeration completes.
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };

        _refreshTimer.Tick += OnRefreshTimerTick;

        // Publisher events.
        _publisher.StatusChanged += OnPublisherStatusChanged;

        // Watcher events.
        _watcher.Added += OnDeviceAdded;
        _watcher.Updated += OnDeviceUpdated;
        _watcher.Removed += OnDeviceRemoved;
        _watcher.EnumerationCompleted += OnEnumerationCompleted;
        _watcher.Stopped += OnWatcherStopped;

        AddEvent("Discovery probe initialized.");
    }

    public void Start()
    {
        AddEvent("Starting Wi-Fi Direct discovery...");

        try
        {
            _publisher.Start();

            PublisherStatus = _publisher.Status.ToString();

            AddEvent(
                $"Publisher Start() called. Status: {PublisherStatus}");

            StartWatcher();
        }
        catch (Exception ex)
        {
            AddEvent($"START ERROR: {ex}");
        }
    }

    private void StartWatcher()
    {
        if (_disposed)
            return;

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
                AddEvent("Device already exists in collection; keeping existing entry.");
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
            {
                Devices.Remove(existing);
            }
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
                "WATCHER IS STILL EXPECTED TO MONITOR FOR DEVICE CHANGES.");

            // Schedule exactly one refresh for this experiment.
            if (!_refreshAttempted && !_refreshPending)
            {
                AddEvent(
                    "WATCHER REFRESH scheduled in 30 seconds.");

                _refreshTimer.Start();
            }
        });
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();

        if (_disposed || _refreshAttempted)
            return;

        _refreshAttempted = true;

        if (_watcher.Status != DeviceWatcherStatus.Started &&
            _watcher.Status != DeviceWatcherStatus.EnumerationCompleted)
        {
            AddEvent(
                $"WATCHER REFRESH ABORTED. Current status: {_watcher.Status}");

            return;
        }

        AddEvent("WATCHER REFRESH: stopping watcher...");

        _refreshPending = true;

        try
        {
            _watcher.Stop();
        }
        catch (Exception ex)
        {
            _refreshPending = false;

            AddEvent($"WATCHER STOP ERROR: {ex}");
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

            if (_disposed || !_refreshPending)
                return;

            _refreshPending = false;

            AddEvent(
                "WATCHER REFRESH: stopped successfully. Restarting watcher...");

            StartWatcher();
        });
    }

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

    public void Dispose()
    {
        if (_disposed)
            return;

        AddEvent("Stopping discovery...");

        _disposed = true;

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