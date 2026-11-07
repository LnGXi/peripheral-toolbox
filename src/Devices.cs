using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PeripheralBattery
{
    internal enum DeviceTransport { Unknown, Bluetooth, UsbReceiver, UsbWired, Wireless }
    internal sealed class BatteryDevice
    {
        public string Id, Name, Detail, Level, Address;
        public DeviceTransport Transport;
        public int? Percent;
        public string Connection
        {
            get
            {
                switch (Transport)
                {
                    case DeviceTransport.Bluetooth: return "蓝牙";
                    case DeviceTransport.UsbReceiver: return "USB 接收器";
                    case DeviceTransport.UsbWired: return "USB 直连";
                    case DeviceTransport.Wireless: return "无线";
                    default: return "连接未知";
                }
            }
        }
        public bool IsWireless { get { return Transport == DeviceTransport.Bluetooth || Transport == DeviceTransport.UsbReceiver || Transport == DeviceTransport.Wireless; } }
        public string Display { get { return Percent.HasValue ? Percent + "%" : (Level ?? "暂不支持读取"); } }
    }
    internal interface IBatteryProvider
    {
        string Name { get; }
        List<BatteryDevice> Scan();
    }
    internal sealed class DeviceNode
    {
        public string Id, Name, Container;
        public bool? Connected;
        public int? Percent;
    }
    internal sealed class WindowsProvider : IBatteryProvider
    {
        public string Name { get { return "Windows 蓝牙"; } }
        public List<BatteryDevice> Scan()
        {
            var nodes = new List<DeviceNode>();
            IntPtr set = Native.SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 6);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            try
            {
                for (uint index = 0; ; index++)
                {
                    var info = new Native.DevInfo { Size = Marshal.SizeOf(typeof(Native.DevInfo)) };
                    if (!Native.SetupDiEnumDeviceInfo(set, index, ref info)) break;
                    var id = new StringBuilder(1024); int required;
                    if (!Native.SetupDiGetDeviceInstanceId(set, ref info, id, id.Capacity, out required)) continue;
                    string instance = id.ToString();
                    if (!instance.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) continue;
                    uint type;
                    byte[] battery = Native.Property(set, ref info, "104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2, out type);
                    int? percent = battery != null && type == 3 && battery.Length == 1 && battery[0] <= 100 ? (int?)battery[0] : null;
                    byte[] state = Native.Property(set, ref info, "83DA6326-97A6-4088-9453-A1923F573B29", 15, out type);
                    bool? connected = state != null && type == 17 && state.Length == 1 ? (bool?)(state[0] != 0) : null;
                    string name = Native.TextProperty(set, ref info, "A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);
                    nodes.Add(new DeviceNode { Id = instance, Name = name, Container = Native.Container(set, ref info), Percent = percent, Connected = connected });
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            var result = new List<BatteryDevice>();
            foreach (var root in nodes.Where(n => n.Id.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) || n.Id.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase)))
            {
                // Windows retains paired nodes and battery values while off.
                if (root.Connected != true) continue;
                var level = root.Percent;
                if (!level.HasValue && root.Container != "") level = nodes.Where(n => n.Container == root.Container).Select(n => n.Percent).FirstOrDefault(p => p.HasValue);
                result.Add(new BatteryDevice { Id = root.Container == "" ? root.Id : root.Container, Name = root.Name, Transport = DeviceTransport.Bluetooth, Percent = level, Detail = level.HasValue ? "Windows 最近上报的电量" : "已连接 · 系统未上报电量" });
            }
            return result;
        }
    }
    internal sealed class HidDevice
    {
        internal string Path, Name, Container;
        internal DeviceTransport Transport;
        internal ushort Vendor, Product, Page, Usage, InputSize, OutputSize, FeatureSize;
        internal List<BatteryFeature> BatteryFeatures = new List<BatteryFeature>();
    }
    internal sealed class BatteryFeature
    {
        internal byte ReportId;
        internal ushort Link;
        internal int Maximum;
    }
    internal interface IHidBatteryReader
    {
        bool Supports(HidDevice device);
        void Read(HidDevice device, BatteryDevice result);
    }
    internal sealed class StandardHidReader : IHidBatteryReader
    {
        public bool Supports(HidDevice device) { return device.BatteryFeatures.Count > 0 && device.FeatureSize > 0; }
        internal static List<BatteryFeature> Discover(IntPtr preparsed, ushort count)
        {
            var features = new List<BatteryFeature>();
            if (count == 0) return features;
            IntPtr buffer = Marshal.AllocHGlobal(count * 72);
            try
            {
                if (Native.HidP_GetValueCaps(2, buffer, ref count, preparsed) != 0x110000) return features;
                for (int i = 0; i < count; i++)
                {
                    IntPtr cap = IntPtr.Add(buffer, i * 72);
                    ushort page = (ushort)Marshal.ReadInt16(cap, 0);
                    ushort first = (ushort)Marshal.ReadInt16(cap, 56);
                    ushort last = Marshal.ReadByte(cap, 12) != 0 ? (ushort)Marshal.ReadInt16(cap, 58) : first;
                    int minimum = Marshal.ReadInt32(cap, 40), maximum = Marshal.ReadInt32(cap, 44);
                    if (page == 6 && first <= 0x20 && last >= 0x20 && minimum == 0 && maximum > 0)
                        features.Add(new BatteryFeature { ReportId = Marshal.ReadByte(cap, 2), Link = (ushort)Marshal.ReadInt16(cap, 6), Maximum = maximum });
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return features;
        }
        public void Read(HidDevice device, BatteryDevice result)
        {
            using (var file = Native.CreateFile(device.Path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (file.IsInvalid) return;
                IntPtr preparsed;
                if (!Native.HidD_GetPreparsedData(file, out preparsed)) return;
                try
                {
                    foreach (var feature in device.BatteryFeatures)
                    {
                        var report = new byte[device.FeatureSize]; report[0] = feature.ReportId;
                        uint value;
                        if (Native.HidD_GetFeature(file, report, report.Length) && Native.HidP_GetUsageValue(2, 6, feature.Link, 0x20, out value, preparsed, report, (uint)report.Length) == 0x110000 && value <= feature.Maximum)
                        {
                            result.Percent = (int)Math.Round(value * 100.0 / feature.Maximum);
                            result.Detail = "标准 HID Battery Strength";
                            return;
                        }
                    }
                }
                finally { Native.HidD_FreePreparsedData(preparsed); }
            }
        }
    }
    internal sealed class AtkReader : IHidBatteryReader
    {
        public bool Supports(HidDevice d) { return d.Vendor == 0x373B && d.Product == 0x1216 && d.Page == 0xFF05; }
        public void Read(HidDevice d, BatteryDevice result)
        {
            result.Transport = DeviceTransport.UsbReceiver;
            using (var file = Native.CreateFile(d.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
            {
                if (file.IsInvalid) { result.Detail = "接收器已发现 · 暂时无法访问"; return; }
                if (d.OutputSize < 64 || d.InputSize < 10) return;
                byte[] query = new byte[d.OutputSize];
                query[0] = 8; query[2] = 0x72; query[5] = 1; query[6] = 7; query[7] = 1;
                if (Native.Transfer(file, query, true, 300) == 0) return;
                var deadline = DateTime.UtcNow.AddMilliseconds(500);
                while (DateTime.UtcNow < deadline)
                {
                    byte[] reply = new byte[d.InputSize];
                    int count = Native.Transfer(file, reply, false, 150);
                    if (count >= 10 && reply[0] == 8 && reply[1] == 0x72 && reply[5] == 7 && reply[6] == 1 && reply[7] <= 100)
                    {
                        result.Percent = reply[7];
                        result.Detail = reply[3] == 0x3B ? "充电中 · ATK 协议" : "ATK 接收器上报";
                        return;
                    }
                }
                result.Detail = "接收器已连接 · 鼠标休眠或未返回电量";
            }
        }
    }
    internal sealed class DualSenseReader : IHidBatteryReader
    {
        public bool Supports(HidDevice d) { return d.Vendor == 0x054C && (d.Product == 0x0CE6 || d.Product == 0x0DF2) && d.Page == 1 && d.Usage == 5; }
        internal static void Parse(byte[] data, int count, BatteryDevice result)
        {
            int offset = count >= 78 && data[0] == 0x31 ? 54 : (count >= 64 && data[0] == 1 ? 53 : -1);
            if (offset < 0) return;
            int status = data[offset] >> 4, level = data[offset] & 15;
            if (level > 10 || status > 2) { result.Detail = "手柄电池状态未知"; return; }
            result.Percent = status == 2 ? 100 : Math.Min(100, level * 10 + 5);
            result.Detail = (status == 1 ? "充电中" : status == 2 ? "已充满" : "使用电池") + " · 约值，10% 档位";
        }
        public void Read(HidDevice d, BatteryDevice result)
        {
            using (var file = Native.CreateFile(d.Path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
            {
                if (file.IsInvalid) { result.Detail = "手柄被其他程序独占或已断开"; return; }
                if (d.InputSize == 78) { var feature = new byte[41]; feature[0] = 5; Native.HidD_GetFeature(file, feature, feature.Length); }
                byte[] data = new byte[Math.Max(78, (int)d.InputSize)];
                int count = Native.Transfer(file, data, false, 500);
                Parse(data, count, result);
                if (!result.Percent.HasValue) result.Detail = "已发现 · 未收到完整电量报告";
            }
        }
    }
    internal sealed class HidProvider : IBatteryProvider
    {
        public string Name { get { return "USB / HID"; } }
        private readonly IHidBatteryReader[] readers = { new AtkReader(), new FlydigiReader(), new DualSenseReader(), new StandardHidReader() };
        public List<BatteryDevice> Scan()
        {
            var devices = new List<HidDevice>(); Guid guid; Native.HidD_GetHidGuid(out guid);
            IntPtr guidPtr = Marshal.AllocHGlobal(16); Marshal.StructureToPtr(guid, guidPtr, false);
            IntPtr set = Native.SetupDiGetClassDevs(guidPtr, null, IntPtr.Zero, 18);
            Marshal.FreeHGlobal(guidPtr);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            try
            {
                for (uint i = 0; ; i++)
                {
                    var face = new Native.DevInterface { Size = Marshal.SizeOf(typeof(Native.DevInterface)) };
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref face)) break;
                    var info = new Native.DevInfo { Size = Marshal.SizeOf(typeof(Native.DevInfo)) }; uint size;
                    Native.SetupDiGetDeviceInterfaceDetailW(set, ref face, IntPtr.Zero, 0, out size, ref info);
                    if (size < 8) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref face, detail, size, out size, ref info)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        using (var file = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                        {
                            if (file.IsInvalid) continue;
                            var attr = new Native.Attributes { Size = Marshal.SizeOf(typeof(Native.Attributes)) };
                            if (!Native.HidD_GetAttributes(file, ref attr)) continue;
                            IntPtr preparsed;
                            if (!Native.HidD_GetPreparsedData(file, out preparsed)) continue;
                            IntPtr caps = Marshal.AllocHGlobal(64);
                            try
                            {
                                if (Native.HidP_GetCaps(preparsed, caps) != 0x110000) continue;
                                var text = new byte[256]; Native.HidD_GetProductString(file, text, text.Length);
                                devices.Add(new HidDevice { Path = path, Vendor = attr.Vendor, Product = attr.Product, Name = Encoding.Unicode.GetString(text).TrimEnd('\0'), Container = Native.Container(set, ref info), Transport = Native.GetTransport(info.Instance), Usage = (ushort)Marshal.ReadInt16(caps, 0), Page = (ushort)Marshal.ReadInt16(caps, 2), InputSize = (ushort)Marshal.ReadInt16(caps, 4), OutputSize = (ushort)Marshal.ReadInt16(caps, 6), FeatureSize = (ushort)Marshal.ReadInt16(caps, 8), BatteryFeatures = StandardHidReader.Discover(preparsed, (ushort)Marshal.ReadInt16(caps, 60)) });
                            }
                            finally { Marshal.FreeHGlobal(caps); Native.HidD_FreePreparsedData(preparsed); }
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            var result = new List<BatteryDevice>();
            foreach (var group in devices.Where(d => d.Vendor != 0 && d.Path.StartsWith(@"\\?\hid#vid_", StringComparison.OrdinalIgnoreCase)).GroupBy(d => d.Container == "" ? d.Path : d.Container))
            {
                HidDevice selected = readers.Select(candidateReader => group.FirstOrDefault(candidateReader.Supports)).FirstOrDefault(device => device != null);
                if (selected == null) selected = group.FirstOrDefault(d => d.Page == 1 && (d.Usage == 2 || d.Usage == 4 || d.Usage == 5 || d.Usage == 6));
                if (selected == null) continue;
                var row = new BatteryDevice { Id = group.Key, Name = string.IsNullOrWhiteSpace(selected.Name) ? string.Format("HID 外设 {0:X4}:{1:X4}", selected.Vendor, selected.Product) : selected.Name, Transport = selected.Transport, Detail = "设备接口已发现 · 电量协议待适配" };
                IHidBatteryReader reader = readers.FirstOrDefault(r => r.Supports(selected));
                // USB enumeration alone cannot distinguish a receiver from a cable.
                // Known receiver protocols may establish the link type by querying it.
                if (!row.IsWireless && !(reader is AtkReader) && !(reader is FlydigiReader)) continue;
                if (reader != null)
                {
                    try { reader.Read(selected, row); }
                    catch (Exception error) { row.Detail = "读取失败 · " + error.Message; }
                }
                result.Add(row);
            }
            return result;
        }
    }
    internal sealed class XInputProvider : IBatteryProvider
    {
        [StructLayout(LayoutKind.Sequential)] private struct Battery { public byte Type, Level; }
        [StructLayout(LayoutKind.Sequential)] private struct Capabilities
        {
            public byte Type, SubType;
            public ushort Flags, Buttons;
            public byte LeftTrigger, RightTrigger;
            public short LeftX, LeftY, RightX, RightY;
            public ushort LeftMotor, RightMotor;
        }
        [DllImport("xinput1_4.dll")] private static extern uint XInputGetBatteryInformation(uint index, byte type, out Battery battery);
        [DllImport("xinput1_4.dll")] private static extern uint XInputGetCapabilities(uint index, uint flags, out Capabilities capabilities);
        public string Name { get { return "XInput 手柄"; } }
        public List<BatteryDevice> Scan()
        {
            var result = new List<BatteryDevice>();
            for (uint i = 0; i < 4; i++)
            {
                Battery battery;
                if (XInputGetBatteryInformation(i, 0, out battery) != 0 || battery.Type == 0) continue;
                Capabilities capabilities;
                if (battery.Type == 1 || XInputGetCapabilities(i, 0, out capabilities) != 0 || (capabilities.Flags & 2) == 0) continue;
                bool known = (battery.Type == 2 || battery.Type == 3) && battery.Level <= 3;
                result.Add(new BatteryDevice { Id = "xinput-" + i, Name = "XInput 手柄 " + (i + 1), Transport = DeviceTransport.Wireless, Level = known ? new[] { "电量耗尽", "低电量", "中电量", "电量充足" }[battery.Level] : null, Detail = "驱动确认无线连接，未区分蓝牙或接收器 · " + (known ? "驱动上报档位" : "驱动未提供有效电量") });
            }
            return result;
        }
    }
    internal static class Scanner
    {
        internal static readonly IBatteryProvider[] Providers = { new BluetoothProvider(), new WindowsProvider(), new HidProvider(), new XInputProvider() };
        internal static List<BatteryDevice> Scan(out List<string> errors)
        {
            errors = new List<string>(); var rows = new List<BatteryDevice>();
            foreach (var provider in Providers)
            {
                try
                {
                    foreach (var row in provider.Scan())
                    {
                        var existing = rows.FirstOrDefault(r => r.Id == row.Id);
                        if (existing == null) rows.Add(row);
                        else
                        {
                            // A device-specific wired result takes precedence over
                            // cached Bluetooth properties sharing the same container.
                            if (row.Transport == DeviceTransport.UsbWired) existing.Transport = row.Transport;
                            if (row.Percent.HasValue && !existing.Percent.HasValue) { existing.Percent = row.Percent; existing.Detail = row.Detail; }
                        }
                    }
                }
                catch (Exception error) { errors.Add(provider.Name + " · " + error.Message); }
            }
            return rows.Where(r => r.IsWireless).OrderByDescending(r => r.Percent.HasValue).ThenBy(r => r.Name).ToList();
        }
    }
}
