using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AliantWebS.Models;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Index.HPRtree;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.Text;
using System.Text.Json;

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
        public Dictionary<string, JsonElement> values { get; set; }  // Changed from double to JsonElement
        public string error { get; set; }
    }

    // Class to store alarm details
    public class AlarmDetail
    {
        public string SensorName { get; set; }
        public string ChannelName { get; set; }

        /// <summary>Structure the sensor belongs to, captured when the alarm latched.</summary>
        public string StructureName { get; set; }

        public double CurrentValue { get; set; }
        public double ThresholdValue { get; set; }
        public DateTime TriggeredTime { get; set; }

        /// <summary>
        /// Highest level reached since the alarm latched (1/2/3), NOT the level of the latest
        /// sample. An alarm stays latched until someone resets it, so letting the level fall back
        /// when the load eases would silence a siren nobody acknowledged.
        /// </summary>
        public int Level { get; set; } = 3;

        /// <summary>Latest value as a percentage of the threshold, for display.</summary>
        public double RatioPercent =>
            ThresholdValue == 0 ? 0 : CurrentValue / ThresholdValue * 100.0;
    }

    public class ReportsService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly bool _autoCreateChannelDataTypes = true; // Set this to enable/disable auto-creation
        
        //   private readonly HttpClient _httpClient;
        

        static public readonly string BucketName = "AntaresBucket";

        // Track active alarms with their details
        private static readonly Dictionary<string, AlarmDetail> _activeAlarms = new Dictionary<string, AlarmDetail>();
        private static readonly object _alarmLock = new object();
        private static DateTime _lastEmailSentTime = DateTime.MinValue;

        // ── Mute switches (administrator-controlled from /alarm-management) ────────────────────
        // Muting the siren stops the physical alarm being driven; muting e-mail stops the initial
        // and reminder notifications. Neither affects detection — alarms still latch and are listed.
        private static bool _sirenMuted;
        private static bool _emailMuted;

        public static bool SirenMuted { get { lock (_alarmLock) return _sirenMuted; } }
        public static bool EmailMuted { get { lock (_alarmLock) return _emailMuted; } }

        // ── Collection pause (administrators and developers, from /alarm-management) ───────────
        // Unlike the two mutes above, this one DOES stop detection: while paused, no report is read, so
        // nothing new can trigger. Alarms already latched keep their state, their reminders and the siren.
        // It exists because catching up on months of reports (the InfluxDB bucket's last point is the start
        // of the replay) floods the console and slows a development container until its circuits drop.
        //
        // The token is what stops a replay IN FLIGHT: a single fetch streams every report since the last
        // point, which can take hours, so a flag read at the top of the next cycle would come far too late.
        // Cancelling it ends the stream at the next report read; the reports already read are still
        // processed, so the replay resumes exactly where it stopped - nothing skipped, nothing counted twice.
        private static readonly object _fetchLock = new object();
        private static bool _fetchPaused;
        private static DateTime? _fetchPausedSinceUtc;
        private static CancellationTokenSource _fetchStop = new CancellationTokenSource();

        public static bool FetchPaused { get { lock (_fetchLock) return _fetchPaused; } }
        public static DateTime? FetchPausedSinceUtc { get { lock (_fetchLock) return _fetchPausedSinceUtc; } }

        private static CancellationToken FetchStopToken { get { lock (_fetchLock) return _fetchStop.Token; } }

        private static void ApplyFetchPaused(bool paused)
        {
            lock (_fetchLock)
            {
                if (_fetchPaused == paused) return;
                _fetchPaused = paused;

                if (paused)
                {
                    _fetchPausedSinceUtc = DateTime.UtcNow;
                    _fetchStop.Cancel();                              // ends a replay in flight
                }
                else
                {
                    _fetchPausedSinceUtc = null;
                    _fetchStop.Dispose();
                    _fetchStop = new CancellationTokenSource();       // the next fetch gets a live token
                }
            }
        }

        // Last state actually pushed to the on-site controller, so the siren is only driven on a
        // real change no matter which path (background loop, reset, mute) triggered it.
        private static bool _lastSentSirenState;

        // Dedicated client for the alarm endpoint: ResetAlarm/mute run outside the fetch loop and
        // so have no HttpClient of their own.
        private static readonly HttpClient _alarmHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // Alarm state is persisted so a container restart does not silently drop a live alarm.
        private static IServiceScopeFactory _scopeFactory;
        
        // Email resend interval in minutes (configurable)
        private static readonly int _emailResendIntervalMinutes = 30; // Change this value as needed

        // API configuration
        ////"http://139.59.155.110:8080";
        ///192.168.2.10:3001
        private static readonly string _apiBaseUrl = "http://192.168.2.10:3001";  // "http://host.docker.internal:8080"; // Access host machine from Docker container
        private static readonly int _maxRetryAttempts = 3;
        private static readonly int _retryDelayMilliseconds = 1000;

        public static event Func<List<Report>, Task>? OnReportsReceived;

        private static long? _timestampOverride = null;
        private static readonly object _timestampOverrideLock = new object();

        public static void SetTimestampOverride(long unixTimestamp)
        {
            lock (_timestampOverrideLock)
            {
                _timestampOverride = unixTimestamp;
            }
            Console.WriteLine($"[ReportsService] Transient timestamp override queued: {unixTimestamp}");
        }


        public ReportsService(HttpClient httpClient, IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;      
            //      _httpClient = httpClient;

        }



        /// <param name="stopToken">
/// Ends the stream at the next report read - the collection pause. What was already read is still handed
/// to <paramref name="onBatchReady"/>, and the returned MaxTimestamp covers exactly those reports, so the
/// next fetch starts right after the last one processed.
/// </param>
static public async Task<(int Count, long MaxTimestamp)> GetReportsAsync(
    long fromTimestamp,
    HttpClient _httpClient,
    Func<List<Report>, Task>? onBatchReady = null,
    int batchSize = 100,
    CancellationToken stopToken = default)
{
    int totalCount = 0;
    long maxTimestamp = 0;
    var currentBatch = new List<Report>();

    try
    {
        string url = $"{_apiBaseUrl}/api/v1/reports?from={fromTimestamp}";

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stopToken);

        if (response.IsSuccessStatusCode)
        {
            using var stream = await response.Content.ReadAsStreamAsync(stopToken);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            await foreach (var report in JsonSerializer.DeserializeAsyncEnumerable<Report>(stream, options, stopToken))
            {
                if (report != null)
                {
                    currentBatch.Add(report);
                    totalCount++;

                    if (report.report_datetime > maxTimestamp)
                        maxTimestamp = report.report_datetime;

                    if (currentBatch.Count >= batchSize)
                    {
                        if (onBatchReady != null)
                        {
                            await onBatchReady(currentBatch);
                            Console.WriteLine($"Processed batch of {currentBatch.Count} reports (total: {totalCount})");
                        }
                        currentBatch.Clear(); // ← reuse list, don't allocate new one
                    }
                }
            }

            if (currentBatch.Any() && onBatchReady != null)
            {
                await onBatchReady(currentBatch);
                Console.WriteLine($"Processed final batch of {currentBatch.Count} reports (total: {totalCount})");
            }

            return (totalCount, maxTimestamp);
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return (0, 0);
        }
        else
        {
            throw new Exception($"Error {response.StatusCode}: {response.ReasonPhrase}");
        }
    }
    catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
    {
        // Paused. Not an error: the reports read before the pause are processed like any others, which is
        // what makes MaxTimestamp - and so the resume - exact.
        if (currentBatch.Any() && onBatchReady != null)
            await onBatchReady(currentBatch);

        Console.WriteLine($"[Monitoring] Collecte suspendue après {totalCount} rapport(s) lu(s) dans ce cycle.");
        return (totalCount, maxTimestamp);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error fetching reports: {ex.Message}");

        if (currentBatch.Any() && onBatchReady != null)
        {
            await onBatchReady(currentBatch);
            Console.WriteLine($"Processed partial batch of {currentBatch.Count} reports before error");
        }

        return (totalCount, maxTimestamp);
    }
}


        /// <summary>
        /// Sends alarm state to the on-site alarm API
        /// </summary>
        /// <param name="state">"normal" or "danger"</param>
        /// <param name="httpClient">HttpClient instance</param>
        /// <returns>True if successful, false otherwise</returns>
        static public async Task<bool> SendAlarmStateToApi(string state, HttpClient httpClient)
        {
            if (string.IsNullOrEmpty(state) || (state != "normal" && state != "danger"))
            {
                Console.WriteLine($"Invalid alarm state: {state}. Must be 'normal' or 'danger'");
                return false;
            }

            try
            {
                // Base URL is configurable in AppSetting; falls back to the compiled-in address.
                string baseUrl = await AppSettings.GetStringAsync(AppSettings.AlarmApiBaseUrl, _apiBaseUrl);
                string url = $"{baseUrl}/api/v1/alarm?state={state}";
                
             
                if (state == "danger")
                {
                    for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
                    {
                        try
                        {
                            var response = await httpClient.PostAsync(url, null);
                            
                            if (response.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"Successfully set alarm to DANGER state");
                                return true;
                            }
                            else if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                            {
                                Console.WriteLine($"On-site controller not responding (attempt {attempt}/{_maxRetryAttempts})");
                                
                                if (attempt < _maxRetryAttempts)
                                {
                                    await Task.Delay(_retryDelayMilliseconds * attempt); // Exponential backoff
                                    continue;
                                }
                                else
                                {
                                    Console.WriteLine($"Failed to set alarm to DANGER after {_maxRetryAttempts} attempts");
                                    return false;
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Error setting alarm to DANGER: {response.StatusCode} - {response.ReasonPhrase}");
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Exception during alarm API call (attempt {attempt}/{_maxRetryAttempts}): {ex.Message}");
                            
                            if (attempt < _maxRetryAttempts)
                            {
                                await Task.Delay(_retryDelayMilliseconds * attempt);
                                continue;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                }
                else // state == "normal" - asynchronous, no retry needed
                {
                    var response = await httpClient.PostAsync(url, null);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Successfully set alarm to NORMAL state (asynchronous)");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Error setting alarm to NORMAL: {response.StatusCode} - {response.ReasonPhrase}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending alarm state to API: {ex.Message}");
                return false;
            }

            return false;
        }

        static public async Task StartContinuousFetch(Action<List<Report>> onNewReports, CancellationToken cancellationToken, IServiceScopeFactory scopeFactory,
    int pollingIntervalSeconds)
        {
            const int BATCH_SIZE = 100;
            const int SENSOR_REFRESH_CYCLES = 30; // Refresh sensors every 30 cycles (~5 min at 10s interval)


            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AliantWebContext>();
      
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            var _latestTimestamp = await InfluxService.QueryLatestTimestampFromInfluxAsync(BucketName);

            if (_latestTimestamp < 1766620800)  // Before Dec 25, 2025... not really i was uopdates to 5 january 2026
            {
                _latestTimestamp = 1766620800;  // Start from Dec 25, 2025
            }



            bool previousAlarmState = false;
            bool hasNewAlarms = false;
            int cycleCount = 0;

            // Load sensors once before the loop
            IEnumerable<Element> sensors = await LoadSensorsAsync(dbContext);

            // The persisted pause, again here: the loop must never start a replay that the page says is paused,
            // whatever order startup ran in.
            ApplyFetchPaused(await AppSettings.GetBoolAsync(AppSettings.ReportFetchPaused, false));
            bool pauseAnnounced = false;


            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Paused from /alarm-management: read nothing. Latched alarms keep reminding - they are real,
                    // and the page says detection stopped, not that the alarms went away. Said ONCE per pause: this
                    // is the loop that flooded the console in the first place.
                    if (FetchPaused)
                    {
                        if (!pauseAnnounced)
                        {
                            Console.WriteLine("[StartContinuousFetch] Collecte suspendue : aucun rapport n'est lu jusqu'à la reprise.");
                            pauseAnnounced = true;
                        }

                        await SendPeriodicAlarmReminders(scopeFactory);

                        try { await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken); }
                        catch (TaskCanceledException) { break; }
                        continue;
                    }

                    if (pauseAnnounced)
                    {
                        Console.WriteLine($"[StartContinuousFetch] Collecte reprise à partir de {_latestTimestamp}.");
                        pauseAnnounced = false;
                    }

                    // Read at the start of the cycle: a pause cancels exactly this token, and a resume that
                    // replaces it must not wake a stream that has already been told to stop.
                    var stopToken = FetchStopToken;

                    hasNewAlarms = false;

                    // Refresh sensors periodically instead of every cycle
                    if (cycleCount % SENSOR_REFRESH_CYCLES == 0 && cycleCount > 0)
                    {
                        sensors = await LoadSensorsAsync(dbContext);
                    }
                    cycleCount++;

                    if (sensors == null)
                    {
                        Console.WriteLine("[StartContinuousFetch] No sensors loaded, skipping cycle.");
                        await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                        continue;
                    }
                    // Callback to process each batch as it arrives during streaming
                    async Task ProcessBatch(List<Report> batch)
                    {
                        var pointsList = new List<PointData>();

                        // Query tag mappings ONCE per batch, not per report. The accepted set is the
                        // code tag itself PLUS every user alias, so a threshold attribute is found
                        // whether it is named "seuil_alarme" or whatever the user mapped to it.
                        HashSet<string> thresholdNames;
                        using (var batchScope = scopeFactory.CreateScope())
                        {
                            var batchDb = batchScope.ServiceProvider.GetRequiredService<AliantWebContext>();
                            thresholdNames = await TagMappingResolver.AcceptedNamesAsync(batchDb, "seuil_alarme");
                        }

                        foreach (var report in batch)
                        {
                            var result = await ConvertReportToPointData(report, sensors, scopeFactory, httpClient, thresholdNames);
                            pointsList.AddRange(result.Points);
                            hasNewAlarms |= result.HasNewAlarms;
                        }

                        // Write this batch to InfluxDB immediately
                        if (pointsList.Any())
                        {
                            await InfluxService.WriteData(pointsList, BucketName);
                        }

                        onNewReports?.Invoke(batch);

                        if (OnReportsReceived != null)
                        {
                            var tasks = OnReportsReceived
                                .GetInvocationList()
                                .Cast<Func<List<Report>, Task>>()
                                .Select(handler => handler(batch));
                            await Task.WhenAll(tasks);
                        }
                    }

                    // Check and consume a one-time transient timestamp override
                    lock (_timestampOverrideLock)
                    {
                        if (_timestampOverride.HasValue)
                        {
                            Console.WriteLine($"[ReportsService] Applying transient override: {_timestampOverride.Value} (was {_latestTimestamp})");
                            _latestTimestamp = _timestampOverride.Value;
                            _timestampOverride = null;
                        }
                    }

                    // Stream reports and process in batches
                    var (reportCount, maxReportTime) = await GetReportsAsync(_latestTimestamp, httpClient, ProcessBatch, BATCH_SIZE, stopToken);

                    if (reportCount > 0)
                    {
                        if (hasNewAlarms && !EmailMuted)
                            await SendConsolidatedAlarmEmail(scopeFactory, isReminder: false);

                        if (maxReportTime > _latestTimestamp)
                            _latestTimestamp = maxReportTime;

                        // One place drives the siren (honours the mute switch and only calls the
                        // controller on a real change), so resets and mutes act immediately too.
                        await DriveSirenAsync();

                        if (hasNewAlarms)
                            await PersistAsync();   // survive a container restart
                    }

                    await SendPeriodicAlarmReminders(scopeFactory);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[StartContinuousFetch] Error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        // Report names the collecting server sends that match no sensor in this database - each one said ONCE.
        // Said on every reading, it was one line per sensor per report: about 1 500 a second while months of
        // reports were replayed, every one of them pushed through the console to Docker and into Visual Studio,
        // which slowed the application until its circuits dropped. The first occurrence already names the
        // sensor that is missing; the rest only repeated it.
        //
        // Keyed the way the lookup below compares a report name to a sensor - trimmed, case ignored - so a name
        // that is one sensor to the lookup is one line here. A few hundred distinct names at most: the set is the
        // server's vocabulary, not its traffic.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _unknownReportNames =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True the first time a report name that matches no sensor is seen, false every time after, until the
        /// application restarts. Thread-safe: the collection loop and anything else may ask at once.
        /// </summary>
        public static bool IsFirstUnknownReportName(string? name) =>
            _unknownReportNames.TryAdd(name?.Trim() ?? string.Empty, 0);

        static private async Task<(List<PointData> Points, bool HasNewAlarms)> ConvertReportToPointData(
    Report report,
    IEnumerable<Element> sensors,
    IServiceScopeFactory scopeFactory,
    HttpClient httpClient,
    HashSet<string> thresholdNames)
{
    List<PointData> points = new List<PointData>();
    bool hasNewAlarms = false;

    // Read once per report rather than per sample: AppSettings serves this from its cache, but the
    // levels must not change halfway through a report either.
    var alarmLevels = await AlarmLevels.LoadAsync();

    using (var scope = scopeFactory.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AliantWebContext>();
        var reportsService = scope.ServiceProvider.GetRequiredService<ReportsService>();

        foreach (var rd in report.report_data)
        {
            if (string.IsNullOrEmpty(rd.error))
            {
                var sensor = sensors.FirstOrDefault(e =>
                    string.Equals(e.Name?.Trim(), rd.name?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (sensor != null)
                {
                    var p = PointData.Measurement("SHM")
                        .Tag("sensor", sensor.ElementId.ToString())
                        .Timestamp(DateTimeOffset.FromUnixTimeSeconds(report.report_datetime), WritePrecision.Us);

                    Dictionary<string, double> thresholdValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    if (sensor.ElementType?.AttributeTypeElementTypes.Count > 0)
                    {
                        foreach (var at in sensor.ElementType.AttributeTypeElementTypes)
                        {
                            bool isAlarmThreshold = TagMappingResolver.Matches(thresholdNames, at.AttributeType.Name);

                            if (isAlarmThreshold)
                            {
                                var alarmValue = sensor.AttributeElements
                                    .Where(x => x.AttributeTypeId == at.AttributeTypeId)
                                    .Select(x => x.Value)
                                    .FirstOrDefault();

                                if (!string.IsNullOrEmpty(alarmValue))
                                {
                                    string[]? realValues = alarmValue.Split(';');
                                    foreach (var v in realValues)
                                    {
                                        string[]? realValue = v.Split(':');
                                        if (realValue.Length == 2 && double.TryParse(realValue[1], out double thresholdValue))
                                        {
                                            thresholdValues.Add(realValue[0], thresholdValue);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    foreach (var v in rd.values)
                    {
                        double? numericValue = null;

                        if (v.Value.ValueKind == JsonValueKind.Number)
                        {
                            numericValue = v.Value.GetDouble();
                        }
                        else if (v.Value.ValueKind == JsonValueKind.String)
                        {
                            if (double.TryParse(v.Value.GetString(), out double parsed))
                                numericValue = parsed;
                        }

                        if (!numericValue.HasValue) continue;

                        var ChannelDataTypeID = await reportsService.GetOrCreateChannelDataTypeIdAsync(v.Key, sensor.ElementType, dbContext);

                        if (ChannelDataTypeID != Guid.Empty)
                        {
                            // InfluxDB keeps the RAW sample — it is the canonical measurement and the
                            // calibration is applied on read.
                            p = p.Field(ChannelDataTypeID.ToString(), numericValue.Value);

                            // ...but the threshold is compared against the REPORTED value, i.e. the
                            // same y = C1·x + C0 the charts, gauges and SVG show. Comparing the raw
                            // sample here would mean a threshold typed in calibrated units never
                            // fires (or fires constantly) once a sensor is calibrated.
                            var (c1, c0) = await SensorCalibration.GetCoefficientsAsync(sensor.ElementId, ChannelDataTypeID);
                            double reportedValue = c1 * numericValue.Value + c0;

                            // Three levels, each a percentage of this sensor's own threshold. Level 0
                            // means the value is below level 1, i.e. no alarm at all.
                            int level = thresholdValues.ContainsKey(v.Key)
                                ? alarmLevels.LevelFor(reportedValue, thresholdValues[v.Key])
                                : 0;

                            if (level > 0)
                            {
                                string alarmKey = $"{sensor.Name}_{v.Key}";
                                AlarmDetail triggered = null;
                                AlarmDetail escalated = null;
                                int previousLevel = 0;

                                lock (_alarmLock)
                                {
                                    if (!_activeAlarms.ContainsKey(alarmKey))
                                    {
                                        _activeAlarms[alarmKey] = new AlarmDetail
                                        {
                                            SensorName = sensor.Name,
                                            ChannelName = v.Key,
                                            StructureName = sensor.Structure?.Name,
                                            CurrentValue = reportedValue,
                                            ThresholdValue = thresholdValues[v.Key],
                                            TriggeredTime = DateTime.Now,
                                            Level = level
                                        };
                                        AppData.IsAlarmActive = true;
                                        hasNewAlarms = true;
                                        triggered = _activeAlarms[alarmKey];
                                    }
                                    else
                                    {
                                        var existing = _activeAlarms[alarmKey];
                                        existing.CurrentValue = reportedValue;

                                        // Climbing to a higher level is a new event: it can start the
                                        // siren and warrants a notification. Falling back does not
                                        // lower the level — the alarm is latched until reset.
                                        if (level > existing.Level)
                                        {
                                            previousLevel = existing.Level;
                                            existing.Level = level;
                                            hasNewAlarms = true;
                                            escalated = existing;
                                        }
                                    }
                                }

                                // Journaled outside the lock (no await inside a lock), and only on a
                                // transition — an alarm sitting at the same level must not write a row
                                // on every polling cycle.
                                if (triggered != null)
                                {
                                    await JournalWriter.WriteSystemAsync(JournalCategory.Alarme, "alarm.triggered",
                                        target: $"{triggered.SensorName} / {triggered.ChannelName}",
                                        details: $"{AlarmLevels.Name(level)} — valeur {triggered.CurrentValue:F2} "
                                               + $"= {triggered.RatioPercent:F1} % du seuil {triggered.ThresholdValue:F2} "
                                               + $"(déclenchement à {alarmLevels.PercentFor(level):F0} %)",
                                        severity: AlarmLevels.SeverityFor(level));
                                }
                                else if (escalated != null)
                                {
                                    await JournalWriter.WriteSystemAsync(JournalCategory.Alarme, "alarm.escalated",
                                        target: $"{escalated.SensorName} / {escalated.ChannelName}",
                                        details: $"{AlarmLevels.Name(previousLevel)} → {AlarmLevels.Name(level)} — "
                                               + $"valeur {escalated.CurrentValue:F2} = {escalated.RatioPercent:F1} % du seuil",
                                        severity: AlarmLevels.SeverityFor(level));
                                }
                            }
                        }
                    }

                    if (p.HasFields())
                        points.Add(p);
                }
                else
                {
                    // Once per name, not once per reading: see IsFirstUnknownReportName. The readings are still
                    // ignored every time, exactly as before - only the line is no longer repeated.
                    if (IsFirstUnknownReportName(rd.name))
                        Console.WriteLine($"[ConvertReportToPointData] No sensor found for report name: '{rd.name}' "
                                          + "(its readings are ignored; not repeated for this name until the application restarts)");
                }
            }
        }
    }

    return (points, hasNewAlarms);
}

        static private async Task<IEnumerable<Element>> LoadSensorsAsync(AliantWebContext dbContext)
        {
            try
            {
                var category = await dbContext.Categories
                    .Include(c => c.ElementTypes)
                        .ThenInclude(et => et.AttributeTypeElementTypes)
                            .ThenInclude(atte => atte.AttributeType)
                                .ThenInclude(at => at.AttributeElements)
                    .Include(c => c.ElementTypes)
                        .ThenInclude(et => et.ChannelDataTypeElementTypes)
                            .ThenInclude(cdte => cdte.ChannelDataType)
                    .Include(c => c.ElementTypes)
                        .ThenInclude(et => et.Elements)
                            .ThenInclude(e => e.Structure)   // alarm e-mails name the structure
                    .Where(c => c.Name == "sensing device")
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                var sensors = category?.ElementTypes.SelectMany(et => et.Elements).ToList() 
                              ?? new List<Element>();
                Console.WriteLine($"[LoadSensorsAsync] Loaded {sensors.Count()} sensors.");
                return sensors;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LoadSensorsAsync] Failed to load sensors: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sends periodic reminder emails for all active alarms (consolidated in one email)
        /// </summary>
        static private async Task SendPeriodicAlarmReminders(IServiceScopeFactory scopeFactory)
        {
            DateTime now = DateTime.Now;
            bool shouldSendReminder = false;

            // Configurable in AppSetting; falls back to the compiled-in interval.
            int _reminderIntervalMinutes =
                await AppSettings.GetIntAsync(AppSettings.EmailIntervalMin, _emailResendIntervalMinutes);

            lock (_alarmLock)
            {
                if (_activeAlarms.Any())
                {
                    var timeSinceLastEmail = now - _lastEmailSentTime;
                    if (timeSinceLastEmail.TotalMinutes >= _reminderIntervalMinutes)
                    {
                        shouldSendReminder = true;
                    }
                }
            }

            if (shouldSendReminder && !EmailMuted)
            {
                await SendConsolidatedAlarmEmail(scopeFactory, isReminder: true);
            }
        }

        // ── Alarm state persistence ───────────────────────────────────────────────────────────
        // Active alarms and the mute switches live in a small table so a container restart cannot
        // silently drop a live alarm (the state used to be in-process only). The table is created
        // idempotently at startup by Program.EnsureDatabaseSchema.

        /// <summary>Called once at startup: reloads persisted alarms and re-asserts the siren.</summary>
        public static async Task InitializeAsync(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AliantWebContext>();

                var rows = await db.Database
                    .SqlQueryRaw<AlarmStateRow>(
                        "SELECT AlarmKey, SensorName, ChannelName, StructureName, CurrentValue, ThresholdValue, TriggeredTime, Level FROM AlarmState")
                    .ToListAsync();

                // Mute switches live in AppSetting, not here — AlarmState holds alarms only.
                bool siren = await AppSettings.GetBoolAsync(AppSettings.SirenMuted, false);
                bool email = await AppSettings.GetBoolAsync(AppSettings.EmailMuted, false);

                bool fetchPaused = await AppSettings.GetBoolAsync(AppSettings.ReportFetchPaused, false);
                ApplyFetchPaused(fetchPaused);
                if (fetchPaused)
                    Console.WriteLine("[Monitoring] La collecte des rapports reste SUSPENDUE depuis le dernier arrêt : "
                                      + "aucune nouvelle alarme ne sera détectée tant qu'elle n'est pas reprise.");

                lock (_alarmLock)
                {
                    _sirenMuted = siren;
                    _emailMuted = email;

                    _activeAlarms.Clear();
                    foreach (var r in rows)
                    {
                        if (r.AlarmKey.StartsWith("__MUTE_", StringComparison.Ordinal)) continue;   // legacy rows

                        _activeAlarms[r.AlarmKey] = new AlarmDetail
                        {
                            SensorName = r.SensorName,
                            ChannelName = r.ChannelName,
                            StructureName = r.StructureName,
                            CurrentValue = r.CurrentValue,
                            ThresholdValue = r.ThresholdValue,
                            TriggeredTime = r.TriggeredTime,
                            Level = r.Level < 1 ? 3 : r.Level    // rows written before levels existed
                        };
                    }
                    AppData.IsAlarmActive = _activeAlarms.Any();
                }

                Console.WriteLine($"[Alarms] Restored {_activeAlarms.Count} active alarm(s) "
                                + $"(siren muted: {_sirenMuted}, e-mail muted: {_emailMuted}).");

                // Re-assert the physical siren — the controller loses its state on restart too.
                await DriveSirenAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Alarms] Could not restore persisted state: {ex.Message}");
            }
        }

        private class AlarmStateRow
        {
            public string AlarmKey { get; set; }
            public string SensorName { get; set; }
            public string ChannelName { get; set; }
            public string StructureName { get; set; }
            public double CurrentValue { get; set; }
            public double ThresholdValue { get; set; }
            public DateTime TriggeredTime { get; set; }
            public int Level { get; set; }
        }

        /// <summary>Writes the current alarms + mute flags back to the store.</summary>
        private static async Task PersistAsync()
        {
            if (_scopeFactory == null) return;

            List<AlarmDetail> snapshot;
            List<string> keys;
            lock (_alarmLock)
            {
                keys = _activeAlarms.Keys.ToList();
                snapshot = _activeAlarms.Values.ToList();
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AliantWebContext>();

                await db.Database.ExecuteSqlRawAsync("DELETE FROM AlarmState");

                for (int i = 0; i < keys.Count; i++)
                {
                    var d = snapshot[i];
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT INTO AlarmState (AlarmKey, SensorName, ChannelName, StructureName, CurrentValue, ThresholdValue, TriggeredTime, Level) " +
                        "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})",
                        keys[i], d.SensorName ?? "", d.ChannelName ?? "", d.StructureName ?? "",
                        d.CurrentValue, d.ThresholdValue, d.TriggeredTime, d.Level);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Alarms] Could not persist state: {ex.Message}");
            }
        }

        /// <summary>
        /// Pushes the siren to the state it should be in (danger when an alarm is active and the
        /// siren is not muted, otherwise normal). Only calls the controller when that changes, so
        /// it is safe to call from the fetch loop, a reset or a mute toggle.
        /// </summary>
        public static async Task DriveSirenAsync()
        {
            // Awaited before taking the lock — the settings read must not happen inside it.
            var levels = await AlarmLevels.LoadAsync();

            bool shouldSound;
            lock (_alarmLock)
            {
                // Only alarms that reached the configured level sound the siren. A level-1 alarm is
                // still latched, listed and journaled — it is simply silent.
                shouldSound = _activeAlarms.Values.Any(a => a.Level >= levels.SirenLevel) && !_sirenMuted;
                if (shouldSound == _lastSentSirenState) return;
                _lastSentSirenState = shouldSound;
            }

            await SendAlarmStateToApi(shouldSound ? "danger" : "normal", _alarmHttpClient);
        }

        /// <summary>Highest level among the active alarms, 0 when there are none.</summary>
        public static int HighestActiveLevel()
        {
            lock (_alarmLock)
            {
                return _activeAlarms.Count == 0 ? 0 : _activeAlarms.Values.Max(a => a.Level);
            }
        }

        /// <summary>Mute or unmute the physical siren. Detection and e-mail are unaffected.</summary>
        public static async Task SetSirenMutedAsync(bool muted)
        {
            lock (_alarmLock) { _sirenMuted = muted; }
            Console.WriteLine($"[Alarms] Siren {(muted ? "MUTED" : "UNMUTED")}.");
            await AppSettings.SetAsync(AppSettings.SirenMuted, muted, "Physical siren muted by an administrator");
            await DriveSirenAsync();     // silences immediately, or re-asserts on unmute
        }

        /// <summary>Stop or resume alarm e-mails. Detection and the siren are unaffected.</summary>
        public static async Task SetEmailMutedAsync(bool muted)
        {
            lock (_alarmLock) { _emailMuted = muted; }
            Console.WriteLine($"[Alarms] Alarm e-mail {(muted ? "STOPPED" : "RESUMED")}.");
            await AppSettings.SetAsync(AppSettings.EmailMuted, muted, "Alarm e-mails stopped by an administrator");
        }

        /// <summary>
        /// Pause or resume the collection of the sensors' reports. Pausing stops DETECTION, not just an output:
        /// see the note on <see cref="FetchPaused"/>. It takes effect at the next report read, including in the
        /// middle of a long replay, and survives a restart.
        /// </summary>
        public static async Task SetFetchPausedAsync(bool paused)
        {
            ApplyFetchPaused(paused);
            Console.WriteLine($"[Monitoring] Collecte des rapports {(paused ? "SUSPENDUE" : "REPRISE")}.");
            await AppSettings.SetAsync(AppSettings.ReportFetchPaused, paused,
                "Collection of sensor reports paused from the alarm page - no new alarm can be detected");
        }

        /// <summary>
        /// Resets a specific alarm or all alarms, then immediately clears the physical siren if
        /// nothing is left active — rather than waiting for the next polling cycle.
        /// </summary>
        public static async Task ResetAlarmAsync(string sensorName = null, string channelName = null)
        {
            ResetAlarm(sensorName, channelName);
            await DriveSirenAsync();
            await PersistAsync();
        }

        /// <summary>
        /// Resets a specific alarm or all alarms (state only). Prefer ResetAlarmAsync, which also
        /// clears the siren and persists the change.
        /// </summary>
        public static void ResetAlarm(string sensorName = null, string channelName = null)
        {
            lock (_alarmLock)
            {
                if (string.IsNullOrEmpty(sensorName))
                {
                    _activeAlarms.Clear();
                    AppData.IsAlarmActive = false;
                    Console.WriteLine("All alarms have been reset.");
                }
                else
                {
                    string alarmKey = $"{sensorName}_{channelName}";
                    if (_activeAlarms.Remove(alarmKey))
                    {
                        Console.WriteLine($"Alarm reset for {alarmKey}");
                    }

                    AppData.IsAlarmActive = _activeAlarms.Any();
                }
            }

        }

        /// <summary>
        /// Gets list of all active alarms
        /// </summary>
        public static List<string> GetActiveAlarms()
        {
            lock (_alarmLock)
            {
                return _activeAlarms.Keys.ToList();
            }
        }

        /// <summary>
        /// Gets dictionary of all active alarm details
        /// </summary>
        public static Dictionary<string, AlarmDetail> GetAlarmDetails()
        {
            lock (_alarmLock)
            {
                return new Dictionary<string, AlarmDetail>(_activeAlarms);
            }
        }

        /// <summary>
        /// Sends one consolidated email containing all active alarms
        /// </summary>
        static private async Task SendConsolidatedAlarmEmail(IServiceScopeFactory scopeFactory, bool isReminder = false)
        {
            try
            {
                var levels = await AlarmLevels.LoadAsync();

                List<AlarmDetail> alarmSnapshot;

                lock (_alarmLock)
                {
                    // Only alarms that reached the configured e-mail level are reported. With the
                    // default (level 1) that is every alarm; raise it to stop early-warning levels
                    // generating mail.
                    alarmSnapshot = _activeAlarms.Values.Where(a => a.Level >= levels.EmailLevel).ToList();
                    if (alarmSnapshot.Count == 0)
                        return;

                    _lastEmailSentTime = DateTime.Now;
                }

                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AliantWebContext>();
                var emailSender = scope.ServiceProvider.GetRequiredService<EmailSender>();

                var alarmRecipients = await dbContext.AspNetUsers
                    .Include(u => u.Roles)
                    .Where(u => u.Roles.Any(r => r.Name == "AlarmRecipient"))
                    .ToListAsync();

                if (alarmRecipients.Any())
                {
                    int topLevel = alarmSnapshot.Max(a => a.Level);

                    // Structures concerned, so the subject says WHERE without opening the mail.
                    var structures = alarmSnapshot
                        .Select(a => string.IsNullOrWhiteSpace(a.StructureName) ? "?" : a.StructureName)
                        .Distinct().OrderBy(s => s).ToList();
                    string structureLabel = structures.Count == 1 ? structures[0] : string.Join(", ", structures);

                    string subject = isReminder
                        ? $"⚠️ REMINDER [{structureLabel}]: {alarmSnapshot.Count} Active Alarm(s), max {AlarmLevels.Name(topLevel)} - Action Required"
                        : $"⚠️ Alarm Alert [{structureLabel}] - {alarmSnapshot.Count} alarm(s), max {AlarmLevels.Name(topLevel)}";
                    
                    var alarmsHtml = new StringBuilder();
                    int alarmNumber = 1;
                    
                    // Grouped by structure, then most severe first inside each group.
                    foreach (var alarm in alarmSnapshot
                                 .OrderBy(a => a.StructureName)
                                 .ThenByDescending(a => a.Level)
                                 .ThenBy(a => a.SensorName).ThenBy(a => a.ChannelName))
                    {
                        alarmsHtml.AppendLine($@"
                            <div style='margin-bottom: 20px; padding: 15px; background-color: #fff3cd; border-left: 4px solid #ff6b6b; border-radius: 4px;'>
                                <h4 style='margin-top: 0; color: #d32f2f;'>Alarm #{alarmNumber} — {AlarmLevels.Name(alarm.Level)}</h4>
                                <ul style='list-style: none; padding-left: 0;'>
                                    <li><strong>Structure:</strong> {(string.IsNullOrWhiteSpace(alarm.StructureName) ? "(inconnue)" : alarm.StructureName)}</li>
                                    <li><strong>Sensor:</strong> {alarm.SensorName}</li>
                                    <li><strong>Channel:</strong> {alarm.ChannelName}</li>
                                    <li><strong>Level:</strong> {alarm.Level} / 3 ({alarm.RatioPercent:F1} % of the threshold)</li>
                                    <li><strong>Current Value:</strong> {alarm.CurrentValue:F2}</li>
                                    <li><strong>Threshold Value:</strong> {alarm.ThresholdValue:F2}</li>
                                    <li><strong>{(alarm.CurrentValue >= alarm.ThresholdValue ? "Exceeded By" : "Below Threshold By")}:</strong> {Math.Abs(alarm.CurrentValue - alarm.ThresholdValue):F2}</li>
                                    <li><strong>Triggered:</strong> {alarm.TriggeredTime:yyyy-MM-dd HH:mm:ss}</li>
                                </ul>
                            </div>
                        ");
                        alarmNumber++;
                    }

                    string message;
                    if (isReminder)
                    {
                        message = $@"
                            <html>
                            <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                                <div style='max-width: 800px; margin: 0 auto; padding: 20px;'>
                                    <h2 style='color: orange; border-bottom: 3px solid orange; padding-bottom: 10px;'>
                                        ⚠️ Alarm Reminder - {alarmSnapshot.Count} Active Alarm(s)
                                    </h2>
                                    <p style='font-size: 16px;'>
                                        This is a reminder that <strong>{alarmSnapshot.Count} alarm(s)</strong> are still active on the monitoring system.
                                    </p>
                                    
                                    <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                        <strong>Structure(s):</strong> {structureLabel}
                                        <br/>
                                        <strong>Summary:</strong> {alarmSnapshot.Count} active alarm(s) requiring attention
                                        <br/>
                                        <strong>Reminder Time:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                                    </div>

                                    <h3 style='color: #d32f2f; margin-top: 30px;'>Active Alarms:</h3>
                                    {alarmsHtml}

                                    <div style='background-color: #ffebee; padding: 20px; border-radius: 5px; margin-top: 30px;'>
                                        <p style='color: red; font-weight: bold; margin: 0;'>
                                            ⚠️ Action Required: These alarms have not been reset. Please investigate and take appropriate action.
                                        </p>
                                    </div>

                                    <p style='margin-top: 30px; color: #666; font-size: 14px;'>
                                        <em>You will continue to receive reminder emails every {_emailResendIntervalMinutes} minutes until all alarms are manually reset.</em>
                                    </p>
                                </div>
                            </body>
                            </html>
                        ";
                    }
                    else
                    {
                        message = $@"
                            <html>
                            <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                                <div style='max-width: 800px; margin: 0 auto; padding: 20px;'>
                                    <h2 style='color: #d32f2f; border-bottom: 3px solid #d32f2f; padding-bottom: 10px;'>
                                        ⚠️ Alarm Alert - {alarmSnapshot.Count} Threshold(s) Exceeded
                                    </h2>
                                    <p style='font-size: 16px;'>
                                        <strong>{alarmSnapshot.Count} new alarm(s)</strong> have been triggered on the monitoring system.
                                    </p>

                                    <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                        <strong>Structure(s):</strong> {structureLabel}
                                        <br/>
                                        <strong>Total Active Alarms:</strong> {alarmSnapshot.Count}
                                        <br/>
                                        <strong>Alert Time:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                                    </div>

                                    <h3 style='color: #d32f2f; margin-top: 30px;'>Alarm Details:</h3>
                                    {alarmsHtml}

                                    <div style='background-color: #ffebee; padding: 20px; border-radius: 5px; margin-top: 30px;'>
                                        <p style='color: red; font-weight: bold; margin: 0;'>
                                            ⚠️ Action Required: Please investigate these alarms immediately.
                                        </p>
                                    </div>

                                    <p style='margin-top: 30px; color: #666; font-size: 14px;'>
                                        <em>You will receive reminder emails every {_emailResendIntervalMinutes} minutes until all alarms are manually reset.</em>
                                    </p>
                                </div>
                            </body>
                            </html>
                        ";
                    }

                    // Sent one at a time and each failure captured, so a single bad address or a
                    // rejected key cannot abort delivery to everybody else — which is what
                    // Task.WhenAll did: the first exception cancelled the whole notification.
                    var addresses = alarmRecipients.Where(u => !string.IsNullOrEmpty(u.Email))
                                                   .Select(u => u.Email).ToList();
                    var sent = new List<string>();
                    var failures = new List<string>();

                    foreach (var address in addresses)
                    {
                        try
                        {
                            await emailSender.SendEmailAsync(address, subject, message);
                            sent.Add(address);
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"{address} : {ex.Message}");
                        }
                    }

                    string emailType = isReminder ? "Reminder" : "Initial";

                    if (failures.Count == 0)
                    {
                        Console.WriteLine($"{emailType} consolidated alarm email sent to {sent.Count} recipients with {alarmSnapshot.Count} alarm(s)");
                        AppData.ClearEmailFailure();

                        await JournalWriter.WriteSystemAsync(JournalCategory.Alarme,
                            isReminder ? "alarm.email.reminder" : "alarm.email.sent",
                            target: $"{sent.Count} destinataire(s)",
                            details: $"{alarmSnapshot.Count} alarme(s) active(s) sur {structureLabel} — "
                                   + string.Join(", ", sent),
                            severity: 1);
                    }
                    else
                    {
                        // An undelivered alarm notification is itself an alarm condition: the
                        // operators would otherwise believe they had been told.
                        Console.WriteLine($"{emailType} alarm email FAILED for {failures.Count}/{addresses.Count} recipient(s): "
                                        + string.Join(" | ", failures));

                        AppData.ReportEmailFailure(
                            $"{failures.Count}/{addresses.Count} e-mail(s) d'alarme non délivré(s) — {failures[0]}");

                        await JournalWriter.WriteSystemAsync(JournalCategory.Alarme, "alarm.email.failed",
                            target: $"{failures.Count}/{addresses.Count} destinataire(s) en échec",
                            details: $"{alarmSnapshot.Count} alarme(s) sur {structureLabel}\n"
                                   + string.Join("\n", failures)
                                   + (sent.Count > 0 ? $"\nDélivré à : {string.Join(", ", sent)}" : ""),
                            severity: 2);
                    }
                }
                else
                {
                    Console.WriteLine("No alarm recipients found with AlarmRecipient role.");

                    // Nobody is in the AlarmRecipient role: the alarm fired and no one was told.
                    await JournalWriter.WriteSystemAsync(JournalCategory.Alarme, "alarm.email.norecipient",
                        details: $"{alarmSnapshot.Count} alarme(s) active(s) — aucun utilisateur dans le rôle AlarmRecipient",
                        severity: 2);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending consolidated alarm email: {ex.Message}");

                // Everything that got this far failed before a single message was attempted
                // (recipient lookup, sender resolution). Still surfaced — silence here is what let
                // a broken key go unnoticed.
                AppData.ReportEmailFailure($"Envoi des e-mails d'alarme impossible — {ex.Message}");

                await JournalWriter.WriteSystemAsync(JournalCategory.Alarme, "alarm.email.failed",
                    details: ex.Message, severity: 2);

                // Don't throw - email failures must not stop the monitoring process
            }
        }

        /// <summary>
        /// Gets or creates a ChannelDataType and ChannelDataTypeElementType if they don't exist
        /// </summary>
        /// <param name="channelName">The name of the channel data type</param>
        /// <param name="elementType">The element type to associate with</param>
        /// <param name="dbContext">Database context</param>
        /// <returns>The ChannelDataTypeId, or Guid.Empty if not found/created</returns>
        private async Task<Guid> GetOrCreateChannelDataTypeIdAsync(string channelName, ElementType elementType, AliantWebContext dbContext)
        {
            if (!_autoCreateChannelDataTypes)
            {
                // If auto-creation is disabled, just look up existing
                return elementType.ChannelDataTypeElementTypes
                    .Where(x => x.ChannelDataType.Name == channelName)
                    .Select(x => x.ChannelDataTypeId)
                    .FirstOrDefault();
            }

            // Check if ChannelDataTypeElementType already exists
            var existingChannelDataTypeElementType = elementType.ChannelDataTypeElementTypes
                .FirstOrDefault(x => string.Equals(x.ChannelDataType.Name, channelName, StringComparison.OrdinalIgnoreCase));

            if (existingChannelDataTypeElementType != null)
            {
                return existingChannelDataTypeElementType.ChannelDataTypeId;
            }

            // Look for existing ChannelDataType by name
            var existingChannelDataType = await dbContext.ChannelDataTypes
                .FirstOrDefaultAsync(c => c.Name == channelName);

            Guid channelDataTypeId;

            if (existingChannelDataType == null)
            {
                // Create new ChannelDataType
                var newChannelDataType = new ChannelDataType
                {
                    ChannelDataTypeId = Guid.NewGuid(),
                    Name = channelName,
                    Description = $"Auto-generated channel data type for {channelName}",
                    Units = null,
                    Format = null,
                    FileAddr = null,
                    Icon = null
                };

                dbContext.ChannelDataTypes.Add(newChannelDataType);
                await dbContext.SaveChangesAsync();

                channelDataTypeId = newChannelDataType.ChannelDataTypeId;
                Console.WriteLine($"Created new ChannelDataType: {channelName} with ID {channelDataTypeId}");
            }
            else
            {
                channelDataTypeId = existingChannelDataType.ChannelDataTypeId;
            }

            // Check if ChannelDataTypeElementType exists for this combination
            var existingMapping = await dbContext.ChannelDataTypeElementTypes
                .FirstOrDefaultAsync(x => x.ChannelDataTypeId == channelDataTypeId && x.ElementTypeId == elementType.ElementTypeId);

            if (existingMapping == null)
            {
                // Create new ChannelDataTypeElementType
                var newChannelDataTypeElementType = new ChannelDataTypeElementType
                {
                    ChannelDataTypeElementTypeId = Guid.NewGuid(),
                    ChannelDataTypeId = channelDataTypeId,
                    ElementTypeId = elementType.ElementTypeId
                };

                dbContext.ChannelDataTypeElementTypes.Add(newChannelDataTypeElementType);
                await dbContext.SaveChangesAsync();

                Console.WriteLine($"Created new ChannelDataTypeElementType mapping for {channelName} and ElementType {elementType.Name}");
            }

            return channelDataTypeId;
        }
    }
}
