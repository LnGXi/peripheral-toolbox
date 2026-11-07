using System;
using System.Collections.Generic;

namespace PeripheralBattery
{
    internal enum DeviceKind { Other, Headset, Mouse, Controller, Keyboard }

    internal static class DevicePresentation
    {
        internal static DeviceKind Classify(string description)
        {
            string text = (description ?? "").ToLowerInvariant();
            if (text.Contains("headset") || text.Contains("headphone") || text.Contains("耳机")) return DeviceKind.Headset;
            if (text.Contains("gaming") || text.Contains("controller") || text.Contains("gamepad") || text.Contains("dualsense") || text.Contains("vader") || text.Contains("手柄")) return DeviceKind.Controller;
            if (text.Contains("mouse") || text.Contains("鼠标")) return DeviceKind.Mouse;
            if (text.Contains("keyboard") || text.Contains("键盘")) return DeviceKind.Keyboard;
            return DeviceKind.Other;
        }
        internal static string Symbol(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.Headset: return "🎧\uFE0E";
                case DeviceKind.Mouse: return "🖱\uFE0E";
                case DeviceKind.Controller: return "🎮\uFE0E";
                case DeviceKind.Keyboard: return "⌨\uFE0E";
                default: return "◇";
            }
        }
        internal static string Level(BatteryDevice device)
        {
            if (device.Percent.HasValue) return device.Percent.Value + "%";
            switch (device.Level)
            {
                case "电量耗尽": return "空";
                case "低电量": return "低";
                case "中电量": return "中";
                case "电量充足": return "满";
                default: return "—";
            }
        }
        internal static string Tooltip(List<BatteryDevice> devices)
        {
            if (devices.Count == 0) return "外设工具箱 · 暂无设备";
            string text = "";
            for (int i = 0; i < devices.Count; i++)
            {
                BatteryDevice device = devices[i];
                DeviceKind kind = device.Kind == DeviceKind.Other ? Classify(device.Name) : device.Kind;
                string line = Symbol(kind) + " " + Level(device);
                string candidate = text.Length == 0 ? line : text + "\n" + line;
                string suffix = i < devices.Count - 1 ? "\n+" + (devices.Count - i - 1) + " 台" : "";
                if (candidate.Length + suffix.Length > 63) return text + "\n+" + (devices.Count - i) + " 台";
                text = candidate;
            }
            return text;
        }
    }
}
