using AliantWebS.Pages.MonitoringPages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class SampleCollector
{
    private IEnumerable<string> _ports;
    private AccelerometerOutputDataRate _odr;
    private AccelerometerRange _range;
    private int _sampleCount;
    private int _timeout;

    public SampleCollector(IEnumerable<string> serialPorts,
                           AccelerometerOutputDataRate odr,
                           AccelerometerRange range,
                           int sampleCount,
                           int timeout = 60)
    {
        _ports = serialPorts;
        _odr = odr;
        _range = range;
        _timeout = timeout;
        _sampleCount = sampleCount;
    }

    private async Task<Dictionary<int, List<(long, int, int, int)>>> ProcessRunAsync(string port)
    {
        // Create sensor and reader
        var accel = new Accelerometer(port);
        var reader = new AccelerometerReader(accel);

        // Setup sensor and reader
        accel.Reset();
        accel.ODR = _odr;
        accel.Range = _range;
        reader.SampleLimit = _sampleCount;

        // Synchronize sensor clock
        accel.SyncClock();

        // Run the reader
        reader.Run(_timeout);

        // Store result
        var result = new Dictionary<int, List<(long, int, int, int)>>
        {
            { accel.Address, reader.Samples }
        };

        return result;
    }

    public async Task<Dictionary<int, List<(long, int, int, int)>>> RunAsync()
    {
        var tasks = new List<Task<Dictionary<int, List<(long, int, int, int)>>>>();
        foreach (var port in _ports)
        {
            tasks.Add(ProcessRunAsync(port));
        }

        var results = await Task.WhenAll(tasks);

        // Combine results from all tasks
        var finalResult = new Dictionary<int, List<(long, int, int, int)>>();
        foreach (var result in results)
        {
            foreach (var pair in result)
            {
                finalResult.Add(pair.Key, pair.Value);
            }
        }

        return finalResult;
    }
}
