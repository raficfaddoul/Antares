using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AliantWebS.Pages.MonitoringPages;
using CsvHelper;

class AccelerometersReader
{

        public static readonly Dictionary<AccelerometerOutputDataRate, double> SamplingsFrequencies = new Dictionary<AccelerometerOutputDataRate, double>
    {
        { AccelerometerOutputDataRate.HZ_4000, 4000 },
        { AccelerometerOutputDataRate.HZ_2000, 2000 },
        { AccelerometerOutputDataRate.HZ_1000, 1000 },
        { AccelerometerOutputDataRate.HZ_500, 500 },
        { AccelerometerOutputDataRate.HZ_250, 250 },
        { AccelerometerOutputDataRate.HZ_125, 125 },
        { AccelerometerOutputDataRate.HZ_62_5, 62.5 },
        { AccelerometerOutputDataRate.HZ_31_25, 31.25 },
        { AccelerometerOutputDataRate.HZ_15_625, 15.625 },
        { AccelerometerOutputDataRate.HZ_7_813, 7.813 },
        { AccelerometerOutputDataRate.HZ_3_906, 3.906 }
    };


    //py server.py -f 250 -r 4

    // --port 

    static async Task Main(string[] args)
    {
        // Argument parsing
        var outputPath = args.Length > 0 ? args[0] : throw new ArgumentException("Output path is required");
        var portArgIndex = Array.IndexOf(args, "--port");
        var frequencyArgIndex = Array.IndexOf(args, "--frequency");
        var rangeArgIndex = Array.IndexOf(args, "--range");
        var durationArgIndex = Array.IndexOf(args, "--duration");

        var ports = portArgIndex != -1 ? args[portArgIndex + 1].Split(',') : SensorUtilities.GetSensorPorts();
        var frequency = frequencyArgIndex != -1 ? args[frequencyArgIndex + 1] : "250";
        var range = rangeArgIndex != -1 ? args[rangeArgIndex + 1] : "4";
        var duration = durationArgIndex != -1 ? float.Parse(args[durationArgIndex + 1], CultureInfo.InvariantCulture) : 10f;

        if (duration <= 0)
            throw new ArgumentException("Sampling duration must be greater than zero");

        // Convert frequency and range to enum
        if (!Enum.TryParse<AccelerometerOutputDataRate>(frequency, out var samplingRate))
            throw new ArgumentException("Unsupported sampling rate");

        if (!Enum.TryParse<AccelerometerRange>(range, out var sampleRange))
            throw new ArgumentException("Unsupported sample range");

        // Run collector
        var collector = new SampleCollector(ports, samplingRate, sampleRange, (int)(duration * SamplingsFrequencies[samplingRate]));
        var samples = await collector.RunAsync();

        // Save samples to CSV
        SaveSamplesToCsv(outputPath, samples);
    }

    private static void SaveSamplesToCsv(string path, Dictionary<int, List<(long, int, int, int)>> samples)
    {
        using (var writer = new StreamWriter(path))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var sensorSamples in samples)
            {
                csv.WriteField($"Sensor {sensorSamples.Key}");
                csv.NextRecord();

                foreach (var sample in sensorSamples.Value)
                {
                    csv.WriteRecord(sample);
                    csv.NextRecord();
                }
            }
        }
    }

    static async public Task FonctionAAppele()
    {
        string[] str = new string[4];
        str[0] = "-f";
        str[1] = "250";
        str[2] = "-r";
        str[3] = "4";

        await Main(str);
    }
    


}


