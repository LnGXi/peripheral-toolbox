using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBattery
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct DevInfo { public int Size; public Guid Class; public uint Instance; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct DevInterface { public int Size; public Guid Class; public uint Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey
        {
            public Guid Format; public uint Id;
            public PropertyKey(string guid, uint id) { Format = new Guid(guid); Id = id; }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
        [StructLayout(LayoutKind.Sequential)] internal struct Attributes { public int Size; public ushort Vendor, Product, Version; }
        [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern IntPtr SetupDiGetClassDevs(IntPtr guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError=true)] internal static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DevInfo data);
        [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DevInfo data, StringBuilder id, int size, out int required);
        [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DevInfo data, ref PropertyKey key, out uint type, byte[] buffer, uint length, out uint required, uint flags);
        [DllImport("setupapi.dll", SetLastError=true)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref DevInterface data);
        [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref DevInterface data, IntPtr detail, uint size, out uint required, ref DevInfo info);
        [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] internal static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attrs);
        [DllImport("hid.dll")] internal static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] text, int length);
        [DllImport("hid.dll")] internal static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
        [DllImport("hid.dll")] internal static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data, IntPtr caps);
        [DllImport("hid.dll")] internal static extern int HidP_GetValueCaps(int reportType, IntPtr caps, ref ushort count, IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetUsageValue(int reportType, ushort page, ushort link, ushort usage, out uint value, IntPtr data, byte[] report, uint length);
        [DllImport("hid.dll")] internal static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int length);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, int size, out int count, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, int size, out int count, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out int count, bool wait);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint instance, uint flags);
        [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint instance, StringBuilder id, uint length, uint flags);

        internal static DeviceTransport GetTransport(uint instance)
        {
            for (int depth = 0; depth < 16; depth++)
            {
                var id = new StringBuilder(1024);
                if (CM_Get_Device_IDW(instance, id, (uint)id.Capacity, 0) != 0) break;
                string value = id.ToString();
                if (value.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) return DeviceTransport.Bluetooth;
                if (value.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) return DeviceTransport.UsbWired;
                uint parent;
                if (CM_Get_Parent(out parent, instance, 0) != 0) break;
                instance = parent;
            }
            return DeviceTransport.Unknown;
        }

        internal static byte[] Property(IntPtr set, ref DevInfo info, string guid, uint id, out uint type)
        {
            var key = new PropertyKey(guid, id);
            uint required;
            var bytes = new byte[4096];
            if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out type, bytes, (uint)bytes.Length, out required, 0)) return null;
            Array.Resize(ref bytes, (int)required);
            return bytes;
        }
        internal static string TextProperty(IntPtr set, ref DevInfo info, string guid, uint id)
        {
            uint type;
            byte[] value = Property(set, ref info, guid, id, out type);
            return value != null && type == 18 ? Encoding.Unicode.GetString(value).TrimEnd('\0') : "";
        }
        internal static string Container(IntPtr set, ref DevInfo info)
        {
            uint type;
            byte[] value = Property(set, ref info, "8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2, out type);
            return value != null && value.Length == 16 ? new Guid(value).ToString() : "";
        }
        // Drain a cancelled operation before freeing its native buffer or event.
        internal static int Transfer(SafeFileHandle file, byte[] bytes, bool write, uint timeout)
        {
            using (var signal = new EventWaitHandle(false, EventResetMode.ManualReset))
            {
                IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
                IntPtr operation = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlapped)));
                try
                {
                    Marshal.Copy(bytes, 0, buffer, bytes.Length);
                    Marshal.StructureToPtr(new Overlapped { Event = signal.SafeWaitHandle.DangerousGetHandle() }, operation, false);
                    int count;
                    bool done = write ? WriteFile(file, buffer, bytes.Length, out count, operation) : ReadFile(file, buffer, bytes.Length, out count, operation);
                    if (!done)
                    {
                        if (Marshal.GetLastWin32Error() != 997) return 0;
                        if (WaitForSingleObject(signal.SafeWaitHandle.DangerousGetHandle(), timeout) != 0)
                        {
                            CancelIoEx(file, operation);
                            GetOverlappedResult(file, operation, out count, true);
                            return 0;
                        }
                        if (!GetOverlappedResult(file, operation, out count, false)) return 0;
                    }
                    if (!write && count > 0) Marshal.Copy(buffer, bytes, 0, count);
                    return count;
                }
                finally { Marshal.FreeHGlobal(operation); Marshal.FreeHGlobal(buffer); }
            }
        }
    }
}
