using AliantWebS.Pages.MonitoringPages;
using System;
using System.Collections.Generic;
using System.Threading;

public class AccelerometerReader
{
    private Accelerometer _sensor;
    private int _sampleLimit;
    private int _sampleCount;
    private List<(long, int, int, int)> _samples;

    public AccelerometerReader(Accelerometer sensor)
    {
        _sensor = sensor;
        _sampleLimit = 0;
        _sampleCount = 0;
        _samples = new List<(long, int, int, int)>();
    }

    public List<(long, int, int, int)> Samples => _samples;

    public int SampleCount => _sampleCount;

    public int SampleLimit
    {
        get => _sampleLimit;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Only positive values are allowed");
            _sampleLimit = value;
        }
    }

    public void Clear()
    {
        _sampleCount = 0;
        _samples.Clear();
    }

    private void StoreSample(long t, int x, int y, int z)
    {
        if (_sampleCount < _sampleLimit)
        {
            _samples.Add((t, x, y, z));
            _sampleCount++;
        }
    }

    public void Run(int timeoutInSeconds = 60)
    {
        // Hook callback and start measurements
        Action<long, int, int, int> sampleHandler = StoreSample;
        _sensor.AddListener(sampleHandler);
        _sensor.StartMeasurements();

        // Wait for sample count to be reached or timeout
        var timeout = DateTime.Now.AddSeconds(timeoutInSeconds);
        while (_sampleCount < _sampleLimit && DateTime.Now < timeout)
        {
            _sensor.HandleEvents();
            Thread.Sleep(10); // Avoid busy waiting
        }

        // Stop measurements and unhook callback
        _sensor.StopMeasurements();
        _sensor.RemoveListener(sampleHandler);
    }
}
