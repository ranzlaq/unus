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

    private bool _disposed;

    public ObservableCollection<DiscoveredDevice> Devices { get; }
        = new();

    public ObservableCollection<string> Events { get; }
        = new();

    public string PublisherStatus { get; private set; } = "Created";
    public string WatcherStatus { get; private set; } = "Created";

    public WiFiDirectDiscoveryProbe(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        _publisher = new WiFiDirectAdvertisementPublisher();

        // Make this laptop discoverable.
        _publisher.Advertisement.ListenStateDiscoverability =
            WiFiDirectAdvertisementListenStateDiscoverability.Normal;

        string selector = WiFiDirectDevice.GetDeviceSelector(
            WiFiDirectDeviceSelectorType.AssociationEndpoint);

        AddEvent($"Device selector: {selector}");

        _watcher = DeviceInformation.CreateWatcher(
            selector,
            Array.Empty<string>(),
            DeviceInformationKind.AssociationEndpoint);

        _publisher.StatusChanged += OnPublisherStatusChanged;

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
            AddEvent($"Publisher Start() called. Status: {PublisherStatus}");

            _watcher.Start();

            WatcherStatus = _watcher.Status.ToString();
            AddEvent($"Watcher Start() called. Status: {WatcherStatus}");
        }
        catch (Exception ex)
        {
            AddEvent($"START ERROR: {ex}");
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
        });
    }

    private void OnDeviceUpdated(
        DeviceWatcher sender,
        DeviceInformationUpdate update)
    {
        RunOnUI(() =>
        {
            AddEvent($"DEVICE UPDATED: {update.Id}");
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
        });
    }

    private void OnWatcherStopped(
        DeviceWatcher sender,
        object args)
    {
        RunOnUI(() =>
        {
            WatcherStatus = sender.Status.ToString();

            AddEvent($"WATCHER STOPPED: {WatcherStatus}");
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

        _disposed = true;
    }
}