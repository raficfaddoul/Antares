using System;
using System.Collections.Generic;
using System.Management; // Add reference to System.Management

public static class SensorUtilities
{
    public static IEnumerable<string> GetSensorPorts()
    {
        const int SeeedstudioVid = 0x2886;
        var ports = new List<string>();

        using (var searcher = new ManagementObjectSearcher
            ("SELECT * FROM WIN32_SerialPort"))
        {
            foreach (var port in searcher.Get())
            {
                var deviceId = port.GetPropertyValue("DeviceID").ToString();
                var pnpDeviceId = port.GetPropertyValue("PNPDeviceID").ToString();

                if (pnpDeviceId.Contains($"VID_{SeeedstudioVid:X4}"))
                {
                    ports.Add(deviceId);
                }
            }
        }

        return ports;
    }
}
