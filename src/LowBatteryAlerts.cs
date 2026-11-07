using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace PeripheralBattery
{
    internal sealed class LowBatteryAlerts
    {
        private readonly HashSet<string> notified;
        private readonly Action<string[]> save;
        internal LowBatteryAlerts(IEnumerable<string> previous, Action<string[]> save)
        {
            notified = new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase);
            this.save = save;
        }
        internal static LowBatteryAlerts Load()
        {
            string[] previous;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\PeripheralBattery"))
                previous = key == null ? null : key.GetValue("LowBatteryAlerted") as string[];
            return new LowBatteryAlerts(previous ?? new string[0], delegate(string[] ids)
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\PeripheralBattery"))
                    key.SetValue("LowBatteryAlerted", ids, RegistryValueKind.MultiString);
            });
        }
        internal List<BatteryDevice> Pending(IEnumerable<BatteryDevice> devices)
        {
            var pending = new List<BatteryDevice>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (var device in devices)
            {
                if (!device.IsWireless || !device.Percent.HasValue || string.IsNullOrEmpty(device.Id)) continue;
                if (device.Percent.Value >= 20)
                {
                    changed |= notified.Remove(device.Id);
                    continue;
                }
                if (!notified.Contains(device.Id) && seen.Add(device.Id)) pending.Add(device);
            }
            if (changed) save(notified.ToArray());
            return pending;
        }
        internal void MarkSent(IEnumerable<BatteryDevice> devices)
        {
            bool changed = false;
            foreach (var device in devices) changed |= notified.Add(device.Id);
            if (changed) save(notified.ToArray());
        }
        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "外设";
            if (name.Length <= 32) return name;
            int length = char.IsHighSurrogate(name[31]) ? 31 : 32;
            return name.Substring(0, length) + "…";
        }
        internal static string Message(List<BatteryDevice> devices)
        {
            string message = string.Join("\n", devices.Take(3).Select(device => ShortName(device.Name) + " · " + device.Percent.Value + "%"));
            if (devices.Count > 3) message += "\n另有 " + (devices.Count - 3) + " 台设备低电量";
            return message + "\n请及时充电。";
        }
    }
}
