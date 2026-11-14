using System;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFiDirect;

namespace Unus.Windows.Services;

internal sealed class WiFiDirectDiscoveryProbe : IDisposable
{
    private readonly WiFiDirectAdvertisementPublisher _publisher;
    private readonly DeviceWatcher _watcher;
    private readonly Action<string> _report;

    public WiFiDirectDiscoveryProbe(Action<string> report)
    {
        _report = report;

        _publisher = new WiFiDirectAdvertisementPublisher();

        string selector = WiFiDirectDevice.GetDeviceSelector(
            WiFiDirectDeviceSelectorType.AssociationEndpoint);

        _watcher = DeviceInformation.CreateWatcher(
            selector,
            Array.Empty<string>(),
            DeviceInformationKind.AssociationEndpoint);

        _publisher.StatusChanged += OnPublisherStatusChanged;
        _watcher.Added += OnDeviceAdded;
        _watcher.EnumerationCompleted += OnEnumerationCompleted;
    }

    public void Start()
    {
        _report("Starting Wi-Fi Direct advertisement and discovery...");

        _publisher.Start();
        _watcher.Start();
    }

    private void OnPublisherStatusChanged(
        WiFiDirectAdvertisementPublisher sender,
        WiFiDirectAdvertisementPublisherStatusChangedEventArgs args)
    {
        _report(
            $"Advertisement status: {args.Status}\n" +
            $"Error: {args.Error}");
    }

    private void OnDeviceAdded(
        DeviceWatcher sender,
        DeviceInformation deviceInfo)
    {
        _report(
            "Wi-Fi Direct device discovered!\n\n" +
            $"Name: {deviceInfo.Name}\n\n" +
            $"Device ID: {deviceInfo.Id}");
    }

    private void OnEnumerationCompleted(
        DeviceWatcher sender,
        object args)
    {
        _report("Initial device enumeration completed. " +
                "Discovery remains active.");
    }

    public void Dispose()
    {
        if (_watcher.Status == DeviceWatcherStatus.Started ||
            _watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
        {
            _watcher.Stop();
        }

        _publisher.Stop();

        _publisher.StatusChanged -= OnPublisherStatusChanged;
        _watcher.Added -= OnDeviceAdded;
        _watcher.EnumerationCompleted -= OnEnumerationCompleted;
    }
}