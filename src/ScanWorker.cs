using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PeripheralBattery
{
    internal sealed class ScanSnapshot
    {
        internal readonly List<BatteryDevice> Devices;
        internal readonly List<string> Errors;
        internal ScanSnapshot(List<BatteryDevice> devices, List<string> errors) { Devices = devices; Errors = errors; }
    }

    internal static class ScanProtocol
    {
        private const int Signature = 0x50425431;
        internal static void Write(Stream stream, ScanSnapshot snapshot)
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Signature);
                writer.Write(snapshot.Devices.Count);
                foreach (var device in snapshot.Devices)
                {
                    writer.Write(device.Id ?? ""); writer.Write(device.Name ?? ""); writer.Write(device.Detail ?? "");
                    writer.Write(device.Level ?? ""); writer.Write(device.Address ?? ""); writer.Write((byte)device.Transport);
                    writer.Write(device.Percent.HasValue);
                    if (device.Percent.HasValue) writer.Write((byte)device.Percent.Value);
                }
                writer.Write(snapshot.Errors.Count);
                foreach (var error in snapshot.Errors) writer.Write(error);
            }
        }
        private static int Count(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 4096) throw new InvalidDataException("设备结果数量异常");
            return count;
        }
        internal static ScanSnapshot Read(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadInt32() != Signature) throw new InvalidDataException("设备读取结果无效");
                int count = Count(reader);
                var devices = new List<BatteryDevice>(count);
                for (int i = 0; i < count; i++)
                {
                    var device = new BatteryDevice { Id = reader.ReadString(), Name = reader.ReadString(), Detail = reader.ReadString(), Level = reader.ReadString(), Address = reader.ReadString(), Transport = (DeviceTransport)reader.ReadByte() };
                    if (device.Level.Length == 0) device.Level = null;
                    if (!Enum.IsDefined(typeof(DeviceTransport), device.Transport)) throw new InvalidDataException("连接类型无效");
                    if (reader.ReadBoolean())
                    {
                        byte percent = reader.ReadByte();
                        if (percent > 100) throw new InvalidDataException("电量结果无效");
                        device.Percent = percent;
                    }
                    devices.Add(device);
                }
                int errors = Count(reader);
                var messages = new List<string>(errors);
                for (int i = 0; i < errors; i++) messages.Add(reader.ReadString());
                return new ScanSnapshot(devices, messages);
            }
        }
    }

    // The same EXE performs one short scan and exits. Bluetooth runtime modules
    // are loaded in that process, keeping them out of the idle tray process.
    internal sealed class ScanWorker : IDisposable
    {
        private readonly object gate = new object();
        private Process process;
        private bool disposed;
        internal static void Run()
        {
            List<string> errors;
            var devices = Scanner.Scan(out errors);
            ScanProtocol.Write(Console.OpenStandardOutput(), new ScanSnapshot(devices, errors));
        }
        internal ScanSnapshot Scan()
        {
            using (var child = new Process())
            {
                child.StartInfo = new ProcessStartInfo(typeof(ScanWorker).Assembly.Location, "--scan-worker")
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true
                };
                lock (gate)
                {
                    if (disposed) throw new ObjectDisposedException("ScanWorker");
                    if (process != null) throw new InvalidOperationException("设备读取正在进行");
                    child.Start();
                    process = child;
                }
                try
                {
                    using (var output = new MemoryStream())
                    {
                        var copy = child.StandardOutput.BaseStream.CopyToAsync(output);
                        if (!child.WaitForExit(20000))
                        {
                            child.Kill();
                            child.WaitForExit();
                            copy.GetAwaiter().GetResult();
                            throw new TimeoutException("设备读取超时");
                        }
                        copy.GetAwaiter().GetResult();
                        if (child.ExitCode != 0) throw new IOException("设备读取未完成");
                        output.Position = 0;
                        return ScanProtocol.Read(output);
                    }
                }
                finally { lock (gate) { process = null; } }
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                try { if (process != null && !process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
