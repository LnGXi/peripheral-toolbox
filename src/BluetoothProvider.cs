using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace PeripheralBattery
{
    internal static class BluetoothOperation
    {
        internal static T Wait<T>(IAsyncOperation<T> operation, int timeout)
        {
            try
            {
                var watch = Stopwatch.StartNew();
                while (operation.Status == AsyncStatus.Started)
                {
                    if (watch.ElapsedMilliseconds >= timeout) throw new TimeoutException("蓝牙查询超时");
                    Thread.Sleep(15);
                }
                return operation.GetResults();
            }
            finally
            {
                if (operation.Status == AsyncStatus.Started) operation.Cancel();
                else operation.Close();
            }
        }
        internal static void Wait(IAsyncAction operation, int timeout)
        {
            try
            {
                var watch = Stopwatch.StartNew();
                while (operation.Status == AsyncStatus.Started)
                {
                    if (watch.ElapsedMilliseconds >= timeout) throw new TimeoutException("蓝牙连接超时");
                    Thread.Sleep(15);
                }
                operation.GetResults();
            }
            finally
            {
                if (operation.Status == AsyncStatus.Started) operation.Cancel();
                else operation.Close();
            }
        }
    }

    internal sealed class BluetoothProvider : IBatteryProvider
    {
        public string Name { get { return "蓝牙直读"; } }
        public List<BatteryDevice> Scan()
        {
            string classic = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            string lowEnergy = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            var properties = new[] { "System.Devices.Aep.IsConnected", "System.Devices.Aep.ContainerId", "System.Devices.Aep.DeviceAddress", "System.Devices.BatteryLife" };
            var endpoints = BluetoothOperation.Wait(DeviceInformation.FindAllAsync("(" + classic + ") OR (" + lowEnergy + ")", properties, DeviceInformationKind.AssociationEndpoint), 3000);
            var rows = new List<BatteryDevice>();
            foreach (var endpoint in endpoints)
            {
                object connected;
                if (!endpoint.Properties.TryGetValue("System.Devices.Aep.IsConnected", out connected) || !true.Equals(connected)) continue;
                object addressValue, containerValue, batteryValue;
                endpoint.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out addressValue);
                endpoint.Properties.TryGetValue("System.Devices.Aep.ContainerId", out containerValue);
                endpoint.Properties.TryGetValue("System.Devices.BatteryLife", out batteryValue);
                string address = (addressValue as string ?? "").Replace(":", "").Replace("-", "").ToUpperInvariant();
                Guid container;
                string id = containerValue != null && Guid.TryParse(containerValue.ToString(), out container) && container != Guid.Empty ? container.ToString() : endpoint.Id;
                var row = new BatteryDevice { Id = id, Address = address, Name = endpoint.Name, Kind = DevicePresentation.Classify(endpoint.Name), Transport = DeviceTransport.Bluetooth, Detail = "当前读取方式未获取到电量" };
                int percent;
                if (batteryValue != null && int.TryParse(batteryValue.ToString(), out percent) && percent >= 0 && percent <= 100)
                {
                    row.Percent = percent;
                    row.Detail = "Windows 蓝牙实时属性";
                }
                if (endpoint.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase))
                {
                    try { ReadStandardBattery(row); }
                    catch (Exception error) { if (!row.Percent.HasValue) row.Detail = "标准蓝牙读取失败 · " + error.Message; }
                }
                rows.Add(row);
            }
            EdifierBattery.Read(rows);
            return rows;
        }
        private static void ReadStandardBattery(BatteryDevice row)
        {
            ulong address;
            if (!ulong.TryParse(row.Address, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out address)) return;
            using (var device = BluetoothOperation.Wait(BluetoothLEDevice.FromBluetoothAddressAsync(address), 1500))
            {
                if (device == null || device.ConnectionStatus != BluetoothConnectionStatus.Connected) return;
                var services = BluetoothOperation.Wait(device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached), 2000);
                try
                {
                    if (services.Status != GattCommunicationStatus.Success) return;
                    foreach (var service in services.Services)
                    {
                        var characteristics = BluetoothOperation.Wait(service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached), 1500);
                        if (characteristics.Status != GattCommunicationStatus.Success) continue;
                        foreach (var characteristic in characteristics.Characteristics)
                        {
                            var value = BluetoothOperation.Wait(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached), 1500);
                            if (value.Status != GattCommunicationStatus.Success || value.Value == null || value.Value.Length != 1) continue;
                            using (var reader = DataReader.FromBuffer(value.Value))
                            {
                                byte level = reader.ReadByte();
                                if (level > 100) continue;
                                row.Percent = level;
                                row.Detail = "标准蓝牙电池服务直读";
                                return;
                            }
                        }
                    }
                }
                finally { foreach (var service in services.Services) service.Dispose(); }
            }
        }
    }

    internal static class EdifierBattery
    {
        private static readonly Guid Service = new Guid("EDF00000-EDFE-DFED-FEDF-EDFEDFEDFEDF");
        internal static int? Parse(IList<byte> frame)
        {
            if (frame.Count != 6 || frame[0] != 0xBB || frame[1] != 2 || frame[2] != 0xD0 || frame[3] > 100) return null;
            int checksum = (0x2019 + frame[0] + frame[1] + frame[2] + frame[3]) & 0xFFFF;
            return frame[4] == checksum >> 8 && frame[5] == (checksum & 255) ? (int?)frame[3] : null;
        }
        internal static void Read(List<BatteryDevice> rows)
        {
            // The vendor service UUID identifies compatible devices. Match its
            // physical Bluetooth address to a connected row before sending a query.
            if (rows.Count == 0) return;
            DeviceInformationCollection endpoints;
            try { endpoints = BluetoothOperation.Wait(DeviceInformation.FindAllAsync(RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(Service))), 2000); }
            catch (Exception) { return; }
            foreach (var endpoint in endpoints)
            {
                BatteryDevice row = null;
                try
                {
                    using (var service = BluetoothOperation.Wait(RfcommDeviceService.FromIdAsync(endpoint.Id), 1500))
                    {
                        if (service == null) continue;
                        row = rows.FirstOrDefault(r => r.Address == service.Device.BluetoothAddress.ToString("X12"));
                        if (row == null) continue;
                        using (var socket = new StreamSocket())
                        {
                            BluetoothOperation.Wait(socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName), 5000);
                            using (var writer = new DataWriter(socket.OutputStream))
                            using (var reader = new DataReader(socket.InputStream))
                            {
                                reader.InputStreamOptions = InputStreamOptions.Partial;
                                writer.WriteBytes(new byte[] { 0xAA, 1, 0xD0, 0x21, 0x94 });
                                BluetoothOperation.Wait(writer.StoreAsync(), 1000);
                                var buffer = new List<byte>();
                                var watch = Stopwatch.StartNew();
                                while (watch.ElapsedMilliseconds < 1600)
                                {
                                    uint count = BluetoothOperation.Wait(reader.LoadAsync(256), Math.Max(1, 1600 - (int)watch.ElapsedMilliseconds));
                                    if (count == 0) break;
                                    var bytes = new byte[count]; reader.ReadBytes(bytes); buffer.AddRange(bytes);
                                    while (buffer.Count >= 2)
                                    {
                                        if (buffer[0] != 0xBB && buffer[0] != 0xCC) { buffer.RemoveAt(0); continue; }
                                        int length = buffer[1] + 4;
                                        if (buffer.Count < length) break;
                                        int? battery = Parse(buffer.GetRange(0, length));
                                        buffer.RemoveRange(0, length);
                                        if (!battery.HasValue) continue;
                                        row.Percent = battery.Value;
                                        row.Detail = "漫步者蓝牙协议直读";
                                        break;
                                    }
                                    if (row.Detail == "漫步者蓝牙协议直读") break;
                                }
                            }
                        }
                    }
                }
                catch (Exception error) { if (row != null && !row.Percent.HasValue) row.Detail = "耳机直读失败 · " + error.Message; }
            }
        }
    }
}
