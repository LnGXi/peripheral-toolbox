using System;

namespace PeripheralBattery
{
    internal sealed class FlydigiReader : IHidBatteryReader
    {
        internal static DeviceTransport ConnectionType(byte mode)
        {
            switch (mode)
            {
                case 1: return DeviceTransport.UsbWired;
                case 2: return DeviceTransport.UsbReceiver;
                default: return DeviceTransport.Unknown;
            }
        }
        public bool Supports(HidDevice device)
        {
            return device.Vendor == 0x37D7 && (device.Product == 0x2401 || device.Product == 0x2501 || device.Product == 0x2502)
                && device.Page == 0xFFA0 && device.OutputSize >= 6 && device.InputSize >= 32;
        }
        public void Read(HidDevice device, BatteryDevice result)
        {
            using (var file = Native.CreateFile(device.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
            {
                if (file.IsInvalid) { result.Detail = "手柄接口暂时无法访问"; return; }
                var query = new byte[device.OutputSize];
                query[0] = device.Product == 0x2501 ? (byte)3 : (byte)0;
                query[1] = 0x5A; query[2] = 0xA5; query[3] = 1; query[4] = 2;
                if (Native.Transfer(file, query, true, 300) == 0) return;
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(600);
                while (DateTime.UtcNow < deadline)
                {
                    var reply = new byte[device.InputSize];
                    int count = Native.Transfer(file, reply, false, 150);
                    int offset = count >= 32 && reply[1] == 0x5A ? 1 : 0;
                    if (count - offset < 31 || reply[offset] != 0x5A || reply[offset + 1] != 0xA5 || reply[offset + 2] != 1) continue;
                    int status = reply[offset + 11] >> 4, level = reply[offset + 11] & 15;
                    int step = device.Product == 0x2502 ? 10 : 20;
                    if (status > 2 || level * step > 100) continue;
                    result.Percent = status == 2 ? 100 : level * step;
                    result.Transport = device.Transport == DeviceTransport.Bluetooth ? DeviceTransport.Bluetooth : ConnectionType(reply[offset + 6]);
                    result.Detail = (status == 1 ? "充电中" : status == 2 ? "已充满" : "使用电池") + " · " + step + "% 档位";
                    return;
                }
                result.Detail = "手柄未返回电量 · 可尝试关闭厂商设置程序后刷新";
            }
        }
    }
}
