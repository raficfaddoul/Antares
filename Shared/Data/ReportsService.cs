using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AliantWebS.Models;
using Microsoft.EntityFrameworkCore;
using static TorchSharp.torch;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Index.HPRtree;
using Microsoft.Extensions.Caching.Memory;

namespace AliantWebS.Data
{

    public class Report
    {
        public string report_type { get; set; }
        public int report_version { get; set; }
        public long report_datetime { get; set; }
        public List<ReportData> report_data { get; set; }
    }

    public class ReportData
    {
        public string name { get; set; }
        public string type { get; set; }
        public Dictionary<string, double> values { get; set; }
        public string error { get; set; }
    }

    public class ReportsService
    {
        private readonly IMemoryCache _memoryCache;
      
        //   private readonly HttpClient _httpClient;
        private long _latestTimestamp = 0;

        public static event Func<List<Report>, Task>? OnReportsReceived;


        public ReportsService(HttpClient httpClient, IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;      
            //      _httpClient = httpClient;

        }



        static public async Task<List<Report>> GetReportsAsync(long fromTimestamp, HttpClient _httpClient)
        {
            try
            {
                string url = $"/api/v1/reports?from={fromTimestamp}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var reports = await response.Content.ReadFromJsonAsync<List<Report>>();
                    return reports ?? new List<Report>();
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return new List<Report>(); // No new reports
                }
                else
                {
                    throw new Exception($"Error {response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching reports: {ex.Message}");
                return new List<Report>();
            }
        }

        static public async Task StartContinuousFetch(Action<List<Report>> onNewReports, CancellationToken cancellationToken, IServiceScopeFactory scopeFactory,
    int pollingIntervalSeconds
)
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AliantWebContext>();

            using var scope1 = scopeFactory.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();


            var _latestTimestamp = await InfluxService.QueryLatestTimestampFromInfluxAsync();

            // We will run a loop that keeps fetching until canceled.
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 1) Attempt to fetch new reports from the remote aggregator
                    var newReports = await ReportsService.GetReportsAsync(_latestTimestamp,httpClient);

                    // 2) If we got any new reports, write them to Influx
                    if (newReports.Any())
                    {
                        // Build list of Influx points
                        var pointsList = new List<PointData>();
                        foreach (var report in newReports)
                        {
                            // Convert your `Report` object to one or more `PointData`.
                            // Usually each report has multiple sensor fields => one measurement line.
                            var ps = await ConvertReportToPointData(report, dbContext);
                            pointsList.AddRange(ps);
                        }

                        // Attempt to write to Influx
                        await InfluxService.WriteData(pointsList);

                        // 3) Update _latestTimestamp so we don’t fetch these again
                        long maxReportTime = newReports.Max(r => r.report_datetime);
                        if (maxReportTime > _latestTimestamp)
                        {
                            _latestTimestamp = maxReportTime;
                        }

                        // 4) Notify the UI / caller
                        onNewReports?.Invoke(newReports);
                        //OnReportsReceived?.Invoke(newReports);
                        if (OnReportsReceived != null)
                        {
                            var tasks = OnReportsReceived
                                .GetInvocationList()
                                .Cast<Func<List<Report>, Task>>()
                                .Select(handler => handler(newReports));
                            await Task.WhenAll(tasks);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If there's a network error, server error, etc.
                    // Log or handle the exception as needed.
                    Console.WriteLine($"[StartContinuousFetch] Error: {ex.Message}");
                }

                // 5) Wait before polling again, unless canceled
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // If Task.Delay was canceled, we are shutting down
                    break;
                }
            }
        }

        static private async Task<List<PointData>> ConvertReportToPointData(Report report, AliantWebContext _appDBContext)
        {

            /*                var point = PointData.Measurement("SHM")
        .Tag("sensor", "5cdd75fd-fb15-ee11-8da6-5820b17cefc4")
        .Field("c08e6d09-fc15-ee11-8da6-5820b17cefc4", 21.1)
        .Field("e8c8f514-fc15-ee11-8da6-5820b17cefc4", 35.9)
        .Field("b1df4339-fc15-ee11-8da6-5820b17cefc4", 0)
        .Timestamp(DateTimeOffset.FromUnixTimeSeconds(1741024000), WritePrecision.S);*/


            // This is just a simplified example.
            // Each "report" might represent multiple sensors, but you can also choose 
            // to write them all as fields on a single measurement.
            // Let’s call the measurement "SHM".

            List<PointData> points = new List<PointData>();

            foreach (var rd in report.report_data)
            {
                if (string.IsNullOrEmpty(rd.error))
                {

                    //var sensor = await _memoryCache.GetOrCreateAsync(rd.name, async entry =>
                    //{
                    //    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10); // Cache for 10 minutes
                    //    return await _appDBContext.Elements
                    //        .Include(e => e.ElementType)
                    //            .ThenInclude(et => et.ChannelDataTypeElementTypes)
                    //                .ThenInclude(cdte => cdte.ChannelDataType)
                    //        .Include(e => e.ElementType)
                    //            .ThenInclude(et => et.AttributeTypeElementTypes)
                    //                .ThenInclude(atte => atte.AttributeType)
                    //        .Where(e => e.Name == rd.name)
                    //        .FirstOrDefaultAsync();
                    //});


                    var sensor = await _appDBContext.Elements
                    .Include(e => e.ElementType) // Include ElementType
                        .ThenInclude(et => et.ChannelDataTypeElementTypes) // Include ChannelDataTypeElementTypes within ElementType
                            .ThenInclude(cdte => cdte.ChannelDataType) // Include ChannelDataType within ChannelDataTypeElementTypes
                     .Include(e => e.ElementType)
                    .ThenInclude(et => et.AttributeTypeElementTypes)
                        .ThenInclude(atte => atte.AttributeType)
                        .ThenInclude(at => at.AttributeElements)
                            .Where(e => e.Name == rd.name)
                    .FirstOrDefaultAsync(); // Fetch the first result asynchronously

                    if (sensor != null)
                    {

                        var p = PointData.Measurement("SHM")
                       .Tag("sensor", sensor.ElementId.ToString())
                        .Timestamp(DateTimeOffset.FromUnixTimeSeconds(report.report_datetime), WritePrecision.Us);

                        Dictionary<string, double> thresholdValues = new Dictionary<string, double>();
                        if (sensor.ElementType?.AttributeTypeElementTypes.Count > 0)
                        {
                            foreach (var at in sensor.ElementType.AttributeTypeElementTypes)
                            {
                                if (at.AttributeType.Name == "Seuil Alarme")
                                {
                                    var alarmValue = sensor.AttributeElements.Where(x => x.AttributeTypeId == at.AttributeTypeId).Select(x => x.Value).First();

                                    string[]? realValues = alarmValue?.Split(';');
                                    foreach (var v in realValues)
                                    {
                                        string[]? realValue = v.Split(':');
                                        if (realValue.Length == 2)
                                        {
                                            thresholdValues.Add(realValue[0], double.Parse(realValue[1]));

                                        }
                                    }
                                }

                            }
                        }

                        foreach (var v in rd.values)
                        {
                            var ChannelDataTypeID = sensor.ElementType.ChannelDataTypeElementTypes.Where(x => x.ChannelDataType.Name == v.Key).Select(x => x.ChannelDataTypeId).First();
                            p = p.Field(ChannelDataTypeID.ToString(), v.Value);

                            if (thresholdValues.ContainsKey(v.Key))
                            {
                                if (v.Value > thresholdValues[v.Key])
                                {
                                    // Alarm triggered
                                    AppData.IsAlarmActive = true;


                                }
                            }


                        }
                        if (p.HasFields())
                        {
                            points.Add(p);
                        }
                    }

                }
                else
                {
                    // If there's an error, you can log it, or store it as a field if you want
                    // point.Field($"{sensorReading.name}.error", sensorReading.error);
                }

            }

     
  

            return points;
        }


    }
}
