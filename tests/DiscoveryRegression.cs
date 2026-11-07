using System;
using PeripheralBattery;

internal static class DiscoveryRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    private static int Main()
    {
        try
        {
            var bluetooth = new HidDevice {
                Path = @"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#device",
                Vendor = 0x054C, Product = 0x0CE6, Page = 1, Usage = 5,
                InputSize = 78, Transport = DeviceTransport.Bluetooth
            };
            Check(HidProvider.IncludeInterface(bluetooth), "Bluetooth service UUID path is discovered");
            Check(new DualSenseReader().Supports(bluetooth), "Discovered Bluetooth DualSense reaches its reader");
            Check(HidProvider.IncludeInterface(new HidDevice { Path = @"\\?\hid#vid_373b&pid_1216", Vendor = 0x373B, Transport = DeviceTransport.UsbWired }), "USB receiver interfaces remain discoverable");
            Check(!HidProvider.IncludeInterface(new HidDevice { Path = @"\\?\hid#ven_internal", Vendor = 1, Transport = DeviceTransport.Unknown }), "Internal unidentified interface remains filtered");
            var row = new BatteryDevice();
            var enhanced = new byte[78]; enhanced[0] = 0x31; enhanced[54] = 0x08;
            DualSenseReader.Parse(enhanced, enhanced.Length, row);
            Check(row.Percent == 85, "Enhanced Bluetooth battery decoded");
            row = new BatteryDevice();
            var simple = new byte[78]; simple[0] = 1;
            DualSenseReader.Parse(simple, simple.Length, row);
            Check(!row.Percent.HasValue, "Padded simple Bluetooth report is not mistaken for USB battery");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
