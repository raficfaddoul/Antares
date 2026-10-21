using RTools_NTS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using InfluxDB.Client;
using Azure;
using InfluxDB.Client.Core;
using InfluxDB.Client.Api.Domain;
using System.Numerics;
using Accord.Math;
using System.Globalization;
using Microsoft.JSInterop;
using System.Security.Cryptography;
using InfluxDB.Client.Writes;

namespace AliantWebS.Data
{


    public class InfluxService
    {
        // The token is configuration, not code: INFLUXDB_TOKEN, or INFLUXDB_TOKEN_FILE naming a file that holds it
        // (docker-compose.yml mounts the InfluxDB admin-token secret there; Portainer passes INFLUXDB_TOKEN). The
        // literal that used to be here is in the git history: rotate it.
        private static readonly Lazy<string> Token = new(ReadToken);
        private static string token => Token.Value;

        /// <summary>False when neither INFLUXDB_TOKEN nor INFLUXDB_TOKEN_FILE gives a token (Startup warns).</summary>
        public static bool IsConfigured => Token.Value.Length > 0;

        private static string ReadToken()
        {
            var value = Environment.GetEnvironmentVariable("INFLUXDB_TOKEN");
            if (string.IsNullOrWhiteSpace(value))
            {
                var file = Environment.GetEnvironmentVariable("INFLUXDB_TOKEN_FILE");
                if (!string.IsNullOrWhiteSpace(file) && System.IO.File.Exists(file)) value = System.IO.File.ReadAllText(file);
            }
            return (value ?? "").Trim();
        }

        // The compose service name by default; INFLUXDB_URL for anything else (e.g. http://localhost:8086 outside Docker).
        private static readonly string url =
            Environment.GetEnvironmentVariable("INFLUXDB_URL") is { Length: > 0 } configuredUrl ? configuredUrl : "http://influxdb2:8086";



        private static readonly string bucket = "AliantWebT";
        private static readonly string orgName = "Aliant";

        public InfluxService()
        {
        }

        /// <summary>
        /// Counts the number of records in the bucket.
        /// </summary>
        /// <param name="sensorsIds">Optional list of sensor ID to filter by sensor</param>
        /// <param name="channelsIds">Optional list of channel IDs to filter by channel</param>
        /// <param name="startDate">Optional start date</param>
        /// <param name="endDate">Optional end date</param>
        /// <param name="bucketName">Optional bucket name (uses default if not specified)</param>
        /// <returns>Number of records as an integer</returns>
        public static async Task<int> CountRecords(
            List<string> sensorsIds = null, 
            List<string> channelsIds = null, 
            DateTime? startDate = null, 
            DateTime? endDate = null,
            string bucketName = null)
        {
            int recordCount = 0;
            
            // Use provided bucket or default
            var targetBucket = bucketName ?? bucket;

            using var client = new InfluxDBClient(url, token);
            string orgId = string.Empty;

            // Get the orgID dynamically
            var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();
            if (organizations != null && organizations.Count > 0)
            {
                var org = organizations.FirstOrDefault(o => o.Name == "Aliant"); // Find by org name
                orgId = org?.Id; // Get the orgID
            }

            // Format the date range (optional)
            string startDateStr = startDate.HasValue ? startDate.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : "0";
            string endDateStr = endDate.HasValue ? endDate.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : "now()";

            // Create the filter for channels and sensors (optional)
            string filterClause = (channelsIds != null && channelsIds.Count > 0)
                ? string.Join(" or ", channelsIds.Select(key => $"r._field == \"{key}\""))
                : string.Empty;

            string sensorFilterClause = (sensorsIds != null && sensorsIds.Count > 0)
                ? string.Join(" or ", sensorsIds.Select(id => $"r.sensor == \"{id}\""))
                : string.Empty;

            // Combine the filter clause
            string combinedFilter = string.Empty;
            if (!string.IsNullOrEmpty(sensorFilterClause) && !string.IsNullOrEmpty(filterClause))
            {
                combinedFilter = $"({sensorFilterClause}) and ({filterClause})";
            }
            else if (!string.IsNullOrEmpty(sensorFilterClause))
            {
                combinedFilter = sensorFilterClause;
            }
            else if (!string.IsNullOrEmpty(filterClause))
            {
                combinedFilter = filterClause;
            }

            // Build the Flux query - use targetBucket instead of bucket
            string flux = $"from(bucket:\"{targetBucket}\") " +
                          $"|> range(start: {startDateStr}, stop: {endDateStr}) ";

            if (!string.IsNullOrEmpty(combinedFilter))
            {
                flux += $"|> filter(fn: (r) => {combinedFilter}) ";
            }

            flux += "|> count()";

            Console.WriteLine($"Generated Flux query: {flux}");

            // Execute the query
            try
            {
                var queryApi = client.GetQueryApi();
                var tables = await queryApi.QueryAsync(flux, orgId);

                if (tables != null && tables.Count > 0)
                {
                    // Sum the count of records from the query response
                    recordCount = tables.Sum(table => table.Records.Sum(record => Convert.ToInt32(record.GetValue())));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred while counting records: {ex.Message}");
            }

            return recordCount;
        }

        //static public async Task<List<object[]>> GetData(List<string> sensorsIds, List<string> channelsIds, DateTime startDate, DateTime endDate, string? BucketName = null)
        //{
        //    // if BucketName is provided, use it; otherwise, use the default bucket
        //    var bucket = BucketName ?? InfluxService.bucket;

        //    List<object[]> ll = new List<object[]>();
        //    //object[,] ll = new object[0, channelsIds.Count];

        //    //List<Array> l = new List<Array>();

        //    using var client = new InfluxDBClient(url, token);

        //    string orgId = string.Empty;
        //    var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

        //    if (organizations != null && organizations.Count > 0)
        //    {
        //        var org = organizations.FirstOrDefault(o => o.Name == "Aliant"); // Find by org name
        //        orgId = org?.Id; // Get the orgID
        //    }

        //    string filterClause = string.Join(" or ", channelsIds.Select(key => $"r._field == \"{key}\""));
        //    string sensorFilterClause = string.Join(" or ", sensorsIds.Select(id => $"r.sensor == \"{id}\""));


        //    // var flux = $"from(bucket:\"{bucket}\") |> range(start: 0) |> filter(fn: (r) => r.sensor == \"{sensorId}\" and ({filterClause})) |> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")";

        //    string startDateStr = startDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        //    string endDateStr = endDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        //    var flux = $"from(bucket:\"{bucket}\") |> range(start: {startDateStr}, stop: {endDateStr}) |> filter(fn: (r) => ({sensorFilterClause}) and ({filterClause})) |> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")";

        //    //var flux = $"from(bucket:\"{bucket}\") |> range(start: {startDateStr}, stop: {endDateStr}) |> filter(fn: (r) => r.sensor == \"{sensorId}\" and ({filterClause})) |> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")";
        //    var queryApi = client.GetQueryApi();
        //    //
        //    // QueryData
        //    //
        //    await queryApi.QueryAsync(flux, record =>
        //    {
        //        var array = new object[channelsIds.Count() + 2];
        //        //
        //        // The callback to consume a FluxRecord.
        //        //
        //        array[0] = record.GetTime().Value.ToDateTimeUtc();//-new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

        //        array[1] = record.GetValueByKey("sensor");


        //        int index = 2;
        //        foreach (string key in channelsIds)
        //        {

        //            object value = record.GetValueByKey(key);
        //            if (value !=null)
        //            {
        //                array[index] = (double)value;
        //            }
                                       
        //            index++;
        //        }
        //        ll.Add(array);
        //        //Console.WriteLine($"{record.GetTime()}: {record.GetValueByKey("HorizontalAcceleration1")}");

        //    }, exception =>
        //    {
        //        //
        //        // The callback to consume any error notification.
        //        //
        //        Console.WriteLine($"Error occurred: {exception.Message}");
        //    }, () =>
        //    {
        //        //
        //        // The callback to consume a notification about successfully end of stream.
        //        //
        //        //Console.WriteLine("Query completed");

        //    }, orgId);

        //    return ll;

        //}


        static public async Task<List<object[]>> GetDataByTag(List<string> sensorsIds, List<string> channelsIds, DateTime startDate, DateTime endDate)
        {
            List<object[]> ll = new List<object[]>();
            //object[,] ll = new object[0, channelsIds.Count];

            //List<Array> l = new List<Array>();

            using var client = new InfluxDBClient(url, token);
            string orgId = string.Empty;
            var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

            if (organizations != null && organizations.Count > 0)
            {
                var org = organizations.FirstOrDefault(o => o.Name == "Aliant"); // Find by org name
                orgId = org?.Id; // Get the orgID
            }

            string filterClause = string.Join(" or ", channelsIds.Select(key => $"r._field == \"{key}\""));
            string sensorFilterClause = string.Join(" or ", sensorsIds.Select(id => $"r.sensor == \"{id}\""));


            // var flux = $"from(bucket:\"{bucket}\") |> range(start: 0) |> filter(fn: (r) => r.sensor == \"{sensorId}\" and ({filterClause})) |> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")";

            string startDateStr = startDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            string endDateStr = endDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

            var flux = $"from(bucket:\"{bucket}\") |> range(start: {startDateStr}, stop: {endDateStr}) |> filter(fn: (r) => ({sensorFilterClause}) and ({filterClause})) |> pivot(rowKey: [\"_time\"], columnKey: [\"sensor\"], valueColumn: \"_value\")";

            //var flux = $"from(bucket:\"{bucket}\") |> range(start: {startDateStr}, stop: {endDateStr}) |> filter(fn: (r) => r.sensor == \"{sensorId}\" and ({filterClause})) |> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")";
            var queryApi = client.GetQueryApi();
            //
            // QueryData
            //
            await queryApi.QueryAsync(flux, record =>
            {
                var array = new object[sensorsIds.Count() + 2];
                //
                // The callback to consume a FluxRecord.
                //
                array[0] = record.GetTime().Value.ToDateTimeUtc();//-new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

                array[1] = record.GetValueByKey("_field");


                int index = 2;
                foreach (string key in sensorsIds)
                {

                    object value = record.GetValueByKey(key);
                    if (value != null)
                    {
                        array[index] = Convert.ToDouble(value, CultureInfo.InvariantCulture);

                    }

                    index++;
                }
                ll.Add(array);
                //Console.WriteLine($"{record.GetTime()}: {record.GetValueByKey("HorizontalAcceleration1")}");

            }, exception =>
            {
                //
                // The callback to consume any error notification.
                //
                Console.WriteLine($"Error occurred: {exception.Message}");
            }, () =>
            {
                //
                // The callback to consume a notification about successfully end of stream.
                //
                //Console.WriteLine("Query completed");

            }, orgId);

            return ll;

        }

        static public async Task WriteData(string[] records)
        {
            using var client = new InfluxDBClient(url, token);
            string orgId = string.Empty;
            var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

            if (organizations != null && organizations.Count > 0)
            {
                var org = organizations.FirstOrDefault(o => o.Name == "Aliant"); // Find by org name
                orgId = org?.Id; // Get the orgID
            }

            //string[] records = new string[]
            //{
            //    "SHM,sensor=5cdd75fd-fb15-ee11-8da6-5820b17cefc4 c08e6d09-fc15-ee11-8da6-5820b17cefc4=21.1,e8c8f514-fc15-ee11-8da6-5820b17cefc4=35.9,b1df4339-fc15-ee11-8da6-5820b17cefc4=0 1741024000",
            //    "SHM,sensor=5cdd75fd-fb15-ee11-8da6-5820b17cefc4 c08e6d09-fc15-ee11-8da6-5820b17cefc4=21.0,e8c8f514-fc15-ee11-8da6-5820b17cefc4=35.9,b1df4339-fc15-ee11-8da6-5820b17cefc4=0 1641025000",
            //    "SHM,sensor=5cdd75fd-fb15-ee11-8da6-5820b17cefc4 c08e6d09-fc15-ee11-8da6-5820b17cefc4=21.4,e8c8f514-fc15-ee11-8da6-5820b17cefc4=35.9,b1df4339-fc15-ee11-8da6-5820b17cefc4=0 1641026600",
            //    "SHM,sensor=5cdd75fd-fb15-ee11-8da6-5820b17cefc4 c08e6d09-fc15-ee11-8da6-5820b17cefc4=23.0,e8c8f514-fc15-ee11-8da6-5820b17cefc4=36.2,b1df4339-fc15-ee11-8da6-5820b17cefc4=0 1641027600",
            //    "SHM,sensor=5cdd75fd-fb15-ee11-8da6-5820b17cefc4 c08e6d09-fc15-ee11-8da6-5820b17cefc4=21.8,e8c8f514-fc15-ee11-8da6-5820b17cefc4=36.0,b1df4339-fc15-ee11-8da6-5820b17cefc4=0 1641031200",
            //    "SHM,sensor=5cdd75fd-fb15-ee11-8da6-5820b17cefc4 c08e6d09-fc15-ee11-8da6-5820b17cefc4=22.7,e8c8f514-fc15-ee11-8da6-5820b17cefc4=36.1,b1df4339-fc15-ee11-8da6-5820b17cefc4=0 1641032200",
            //    "SHM,sensor=5cdd75fd-fb15-ee11-8da6-5820b17cefc4 c08e6d09-fc15-ee11-8da6-5820b17cefc4=22.2,e8c8f514-fc15-ee11-8da6-5820b17cefc4=36.0,b1df4339-fc15-ee11-8da6-5820b17cefc4=0 1641034800",
            //    "SHM,sensor=5cdd75fd-fb15-ee11-8da6-5820b17cefc4 c08e6d09-fc15-ee11-8da6-5820b17cefc4=22.4,e8c8f514-fc15-ee11-8da6-5820b17cefc4=36.0,b1df4339-fc15-ee11-8da6-5820b17cefc4=0 1641035800",
            //    "SHM,sensor=5cdd75fd-fb15-ee11-8da6-5820b17cefc4 c08e6d09-fc15-ee11-8da6-5820b17cefc4=22.2,e8c8f514-fc15-ee11-8da6-5820b17cefc4=35.9,b1df4339-fc15-ee11-8da6-5820b17cefc4=0 1641038400"

            //};

            using (var writeApi = client.GetWriteApi())
            {

                writeApi.WriteRecords(records, WritePrecision.Us, bucket, orgId);

            }
        }

        static public async Task<long> QueryLatestTimestampFromInfluxAsync(string bucketName)
        {
            using var client = new InfluxDBClient(url, token);

            var orgs = await client.GetOrganizationsApi().FindOrganizationsAsync();
            var org = orgs?.FirstOrDefault(o => o.Name == orgName);
            if (org == null)
            {
                Console.WriteLine($"Organization {orgName} not found");
                return 0;
            }

            // Use last() instead of max(column:"_time") to avoid InfluxDB nil pointer panic
            var flux = $@"
from(bucket: ""{bucketName}"")
    |> range(start: -100y)
    |> filter(fn: (r) => r._measurement == ""SHM"")
    |> group()
    |> last()
    |> keep(columns: [""_time""])
";

            var queryApi = client.GetQueryApi();

            try
            {
                var tables = await queryApi.QueryAsync(flux, org.Id);
                if (tables.Count > 0 && tables[0].Records.Count > 0)
                {
                    var record = tables[0].Records[0];
                    var time = record.GetTime();
                    if (time.HasValue)
                    {
                        long timestamp = time.Value.ToUnixTimeSeconds();
                        Console.WriteLine($"✓ Latest timestamp in '{bucketName}': {timestamp} ({time.Value.ToDateTimeUtc():yyyy-MM-dd HH:mm:ss} UTC)");
                        return timestamp;
                    }
                }

                Console.WriteLine($"⚠ No data found in bucket '{bucketName}' - starting from beginning");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error querying latest timestamp: {ex.Message}");
                return 0;
            }
        }


        /// <summary>
/// Ensures that a bucket exists in InfluxDB. Creates it if it doesn't exist.
/// </summary>
/// <param name="client">The InfluxDB client instance</param>
/// <param name="bucketName">The name of the bucket to check/create</param>
/// <param name="orgId">The organization ID</param>
/// <param name="retentionPeriodInSeconds">Optional retention period in seconds (default: 0 for infinite)</param>
/// <returns>True if bucket exists or was created successfully, false otherwise</returns>
private static async Task<bool> EnsureBucketExistsAsync(InfluxDBClient client, string bucketName, string orgId, int retentionPeriodInSeconds = 0)
{
    try
    {
        var bucketsApi = client.GetBucketsApi();
        
        // Check if bucket already exists using FindBucketsAsync with orgID parameter
        var existingBuckets = await bucketsApi.FindBucketsAsync(orgID: orgId);
        var bucketExists = existingBuckets?.Any(b => b.Name == bucketName) ?? false;
        
        if (!bucketExists)
        {
            Console.WriteLine($"Bucket '{bucketName}' does not exist. Creating...");
            
            Bucket bucket;
            
            // For infinite retention (0 or negative), don't set retention rules
            if (retentionPeriodInSeconds <= 0)
            {
                // Create bucket with no retention (infinite retention)
                bucket = await bucketsApi.CreateBucketAsync(bucketName, null, orgId);
            }
            else
            {
                // Create the bucket with retention rules
                var retention = new BucketRetentionRules
                {
                    EverySeconds = retentionPeriodInSeconds,
                    Type = BucketRetentionRules.TypeEnum.Expire
                };
                
                bucket = await bucketsApi.CreateBucketAsync(bucketName, retention, orgId);
            }
            
            if (bucket != null)
            {
                Console.WriteLine($"Bucket '{bucketName}' created successfully with {(retentionPeriodInSeconds <= 0 ? "infinite" : $"{retentionPeriodInSeconds} seconds")} retention.");
                return true;
            }
            else
            {
                Console.WriteLine($"Failed to create bucket '{bucketName}'.");
                return false;
            }
        }
        else
        {
            Console.WriteLine($"Bucket '{bucketName}' already exists.");
            return true;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error ensuring bucket exists: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        return false;
    }
}

        static public async Task WriteData(List<PointData> points, string bucketName = null)
        {
            /*                var point = PointData.Measurement("SHM")
                    .Tag("sensor", "5cdd75fd-fb15-ee11-8da6-5820b17cefc4")
                    .Field("c08e6d09-fc15-ee11-8da6-5820b17cefc4", 21.1)
                    .Field("e8c8f514-fc15-ee11-8da6-5820b17cefc4", 35.9)
                    .Field("b1df4339-fc15-ee11-8da6-5820b17cefc4", 0)
                    .Timestamp(DateTimeOffset.FromUnixTimeSeconds(1741024000), WritePrecision.S);*/


            // Use default bucket if none specified
            string targetBucket = bucketName ?? bucket;

            using var client = new InfluxDBClient(url, token);
            string orgId = string.Empty;
            var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

            if (organizations != null && organizations.Count > 0)
            {
                var org = organizations.FirstOrDefault(o => o.Name == orgName); // Find by org name
                orgId = org?.Id; // Get the orgID
            }

            if (string.IsNullOrEmpty(orgId))
            {
                Console.WriteLine($"Organization '{orgName}' not found.");
                return;
            }

            // Ensure bucket exists before writing
            bool bucketReady = await EnsureBucketExistsAsync(client, targetBucket, orgId);
            
            if (!bucketReady)
            {
                Console.WriteLine($"Cannot write data. Bucket '{targetBucket}' is not available.");
                return;
            }

            using (var writeApi = client.GetWriteApi())
            {

                writeApi.WritePoints(points, targetBucket, orgId);

      
            }
        }

        static public async Task<bool> DeleteData(DateTime startDate, DateTime endDate, List<string> sensorsIds)
        {
            using var client = new InfluxDBClient(url, token);
            string orgId = string.Empty;
            var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

            if (organizations != null && organizations.Count > 0)
            {
                var org = organizations.FirstOrDefault(o => o.Name == "Aliant"); // Find by org name
                orgId = org?.Id; // Get the orgID
            }

            try
            {
                string predicate = "";
                if (sensorsIds != null)
                {
                    List<string> filters = new List<string>();
                    filters.Add("_measurement=\"SHM\"");

                    //     filters.Add(string.Join(" or ", channelsIds.Select(key => $"_field = \"{key}\""))); delete by field not supported in InfluxDB yet!!!!
                    filters.Add(string.Join(" or ", sensorsIds.Select(id => $"sensor = \"{id}\"")));

                    predicate = string.Join(" and ", filters);
                }


                await client.GetDeleteApi().Delete(startDate, endDate, predicate, bucket, orgId);
                //await client.GetDeleteApi().Delete(DateTime.UtcNow.AddMinutes(-1), DateTime.Now, "", "bucket", orgId);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
        }

        /// <summary>
/// Gets all buckets for the organization
/// </summary>
/// <returns>List of buckets</returns>
public static async Task<List<Bucket>> GetAllBucketsAsync()
{
    try
    {
        using var client = new InfluxDBClient(url, token);
        string orgId = string.Empty;
        var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

        if (organizations != null && organizations.Count > 0)
        {
            var org = organizations.FirstOrDefault(o => o.Name == orgName);
            orgId = org?.Id;
        }

        if (string.IsNullOrEmpty(orgId))
        {
            Console.WriteLine($"Organization '{orgName}' not found.");
            return new List<Bucket>();
        }

        var bucketsApi = client.GetBucketsApi();
        var buckets = await bucketsApi.FindBucketsAsync(orgID: orgId);
        return buckets ?? new List<Bucket>();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error getting buckets: {ex.Message}");
        return new List<Bucket>();
    }
}

/// <summary>
/// Creates a new bucket
/// </summary>
/// <param name="bucketName">Name of the bucket to create</param>
/// <param name="retentionPeriodInSeconds">Retention period in seconds (0 for infinite)</param>
/// <returns>True if created successfully</returns>
public static async Task<bool> CreateBucketAsync(string bucketName, int retentionPeriodInSeconds = 0)
{
    try
    {
        using var client = new InfluxDBClient(url, token);
        string orgId = string.Empty;
        var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

        if (organizations != null && organizations.Count > 0)
        {
            var org = organizations.FirstOrDefault(o => o.Name == orgName);
            orgId = org?.Id;
        }

        if (string.IsNullOrEmpty(orgId))
        {
            Console.WriteLine($"Organization '{orgName}' not found.");
            return false;
        }

        return await EnsureBucketExistsAsync(client, bucketName, orgId, retentionPeriodInSeconds);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error creating bucket: {ex.Message}");
        return false;
    }
}

/// <summary>
/// Deletes a bucket by name
/// </summary>
/// <param name="bucketName">Name of the bucket to delete</param>
/// <returns>True if deleted successfully</returns>
public static async Task<bool> DeleteBucketAsync(string bucketName)
{
    try
    {
        using var client = new InfluxDBClient(url, token);
        string orgId = string.Empty;
        var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

        if (organizations != null && organizations.Count > 0)
        {
            var org = organizations.FirstOrDefault(o => o.Name == orgName);
            orgId = org?.Id;
        }

        if (string.IsNullOrEmpty(orgId))
        {
            Console.WriteLine($"Organization '{orgName}' not found.");
            return false;
        }

        var bucketsApi = client.GetBucketsApi();
        var existingBuckets = await bucketsApi.FindBucketsAsync(orgID: orgId);
        var bucketToDelete = existingBuckets?.FirstOrDefault(b => b.Name == bucketName);

        if (bucketToDelete != null)
        {
            await bucketsApi.DeleteBucketAsync(bucketToDelete.Id);
            Console.WriteLine($"Bucket '{bucketName}' deleted successfully.");
            return true;
        }
        else
        {
            Console.WriteLine($"Bucket '{bucketName}' not found.");
            return false;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error deleting bucket: {ex.Message}");
        return false;
    }
}

/// <summary>
/// Gets the record count for a specific bucket
/// </summary>
/// <param name="bucketName">Name of the bucket</param>
/// <returns>Number of records</returns>
public static async Task<int> GetBucketRecordCountAsync(string bucketName)
{
    try
    {
        using var client = new InfluxDBClient(url, token);
        string orgId = string.Empty;
        var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

        if (organizations != null && organizations.Count > 0)
        {
            var org = organizations.FirstOrDefault(o => o.Name == orgName);
            orgId = org?.Id;
        }

        if (string.IsNullOrEmpty(orgId))
        {
            return 0;
        }

        // Count all data points across all series
        var flux = $@"
from(bucket: ""{bucketName}"")
    |> range(start: -100y)
    |> filter(fn: (r) => r._measurement == ""SHM"")
    |> group()
    |> count()
    |> group()
    |> sum()";

        var queryApi = client.GetQueryApi();
        var tables = await queryApi.QueryAsync(flux, orgId);

        if (tables != null && tables.Count > 0)
        {
            var firstRecord = tables.FirstOrDefault()?.Records?.FirstOrDefault();
            if (firstRecord != null && firstRecord.GetValue() != null)
            {
                int count = Convert.ToInt32(firstRecord.GetValue());
                Console.WriteLine($"✓ Bucket '{bucketName}' contains {count:N0} records");
                return count;
            }
        }

        Console.WriteLine($"⚠ No data found in bucket '{bucketName}'");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error counting records in bucket '{bucketName}': {ex.Message}");
        return 0;
    }
}

/// <summary>A weekly window: a calendar week, Monday 00:00 to Sunday 23:59 UTC, dated by its Monday.</summary>
public const string WeeklyWindow = "1w";

/// <summary>
/// The Monday 00:00 of the calendar week holding the instant: the week a <see cref="WeeklyWindow"/> row belongs
/// to. Rows are filed with it because a first week cut by the query range is dated by the range start.
/// </summary>
public static DateTime WeekStart(DateTime at)
{
    int daysSinceMonday = (7 + (at.DayOfWeek - DayOfWeek.Monday)) % 7;
    return at.Date.AddDays(-daysSinceMonday);
}

/// <summary>
/// The Flux aggregateWindow step. Flux counts windows from the Unix epoch, a Thursday, and dates a row by its
/// window's end, so a weekly window gets offset -3d (Monday) and timeSrc _start (dated by that Monday).
/// A missing window or function throws: it would otherwise reach InfluxDB as an unreadable query.
/// </summary>
public static string AggregateWindowClause(string? every, string? fn)
{
    ArgumentException.ThrowIfNullOrEmpty(every);
    ArgumentException.ThrowIfNullOrEmpty(fn);

    string calendarWeek = every == WeeklyWindow ? @", offset: -3d, timeSrc: ""_start""" : "";
    return $"|> aggregateWindow(every: {every}{calendarWeek}, fn: {fn}, createEmpty: false)";
}

static public async Task<List<object[]>> GetData(
    List<string> sensorsIds,
    List<string> channelsIds,
    DateTime startDate,
    DateTime endDate,
    string? BucketName = null,
    int? maxDataPoints = null,
    string? aggregateWindowDuration = null,
    string? aggregateFn = null,
    System.Threading.CancellationToken cancellationToken = default)
{
    // Abort before doing any work if the caller (e.g. a page the user navigated away from) is gone.
    cancellationToken.ThrowIfCancellationRequested();

    var bucket = BucketName ?? InfluxService.bucket;
    List<object[]> ll = new List<object[]>();

    var options = new InfluxDBClientOptions.Builder()
        .Url(url)
        .AuthenticateToken(token)
        .TimeOut(TimeSpan.FromSeconds(60))
        .Build();

    using var client = new InfluxDBClient(options);

    string orgId = string.Empty;
    var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

    if (organizations != null && organizations.Count > 0)
    {
        var org = organizations.FirstOrDefault(o => o.Name == "Aliant");
        orgId = org?.Id; // Get the orgID
    }

    string filterClause = string.Join(" or ", channelsIds.Select(key => $"r._field == \"{key}\""));
    string sensorFilterClause = string.Join(" or ", sensorsIds.Select(id => $"r.sensor == \"{id}\""));

    string startDateStr = startDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    string endDateStr = endDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    // Calculate appropriate aggregation window if maxDataPoints is specified. The calibration planner
    // needs aggFn, and may re-emit the aggregation with a flipped fn.
    string aggEvery = null;
    string aggFn = null;
    if (!string.IsNullOrEmpty(aggregateWindowDuration) && !string.IsNullOrEmpty(aggregateFn))
    {
        // Explicit aggregation window and function take priority
        aggEvery = aggregateWindowDuration;
        aggFn = aggregateFn;
        Console.WriteLine($"Applying server-side aggregation: every {aggregateWindowDuration}, fn: {aggregateFn}");
    }
    else if (maxDataPoints.HasValue && maxDataPoints.Value > 0)
    {
        // Calculate time span and determine window size
        TimeSpan timeRange = endDate - startDate;
        double totalSeconds = timeRange.TotalSeconds;
        
        // Calculate window size to achieve target data points
        double windowSeconds = totalSeconds / maxDataPoints.Value;
        
        // Convert to appropriate time unit
        string windowDuration;
        if (windowSeconds < 1)
        {
            windowDuration = $"{(int)(windowSeconds * 1000)}ms";
        }
        else if (windowSeconds < 60)
        {
            windowDuration = $"{(int)windowSeconds}s";
        }
        else if (windowSeconds < 3600)
        {
            windowDuration = $"{(int)(windowSeconds / 60)}m";
        }
        else if (windowSeconds < 86400)
        {
            windowDuration = $"{(int)(windowSeconds / 3600)}h";
        }
        else
        {
            windowDuration = $"{(int)(windowSeconds / 86400)}d";
        }

        // Use aggregateWindow to downsample at the query level. fn: max (not mean) so brief
        // load spikes survive downsampling and remain visible on the plotted curves.
        aggEvery = windowDuration;
        aggFn = "max";

        Console.WriteLine($"Applying downsampling: {windowDuration} window for ~{maxDataPoints} points");
    }
    string aggregationClause = aggEvery == null ? "" : AggregateWindowClause(aggEvery, aggFn);

    // Reported-value calibration y = C1·x + C0. The planner decides WHERE it can run: after the
    // aggregation whenever that is mathematically equivalent (cheap — a few hundred rows), and only
    // before it when it cannot be moved. See SensorCalibration.BuildPlanAsync.
    var plan = await SensorCalibration.BuildPlanAsync(sensorsIds, channelsIds, aggFn);

    const string pivotClause = @"|> pivot(rowKey: [""_time""], columnKey: [""_field""], valueColumn: ""_value"")";
    string flux;

    if (plan.Mode == SensorCalibration.CalibrationMode.Split)
    {
        // Order-based aggregation (max/min) with at least one negative slope: a decreasing transform
        // reverses the ordering, so those series are aggregated with the OPPOSITE function and
        // calibrated afterwards — which yields exactly the calibrated max/min.
        //
        // The plan splits the data into DISJOINT (channel, sign) branches, each read by its own
        // from(). Every predicate is a plain OR-of-equality that Influx pushes down into the storage
        // read, so each series is read once and aggregated once — the same total work as a single
        // pass — and the calibration map still runs only on the aggregated rows.
        string BranchQuery(SensorCalibration.SplitBranch b) => $@"from(bucket:""{bucket}"")
        |> range(start: {startDateStr}, stop: {endDateStr})
        |> filter(fn: (r) => {b.Filter})
        {AggregateWindowClause(aggEvery, b.Fn)}";

        if (plan.Branches.Count == 1)
        {
            flux = $@"{BranchQuery(plan.Branches[0])}
        {plan.MapClause}
        {pivotClause}";
        }
        else
        {
            var defs = new System.Text.StringBuilder();
            var names = new List<string>();
            for (int b = 0; b < plan.Branches.Count; b++)
            {
                string name = $"b{b}";
                names.Add(name);
                defs.AppendLine($"{name} = {BranchQuery(plan.Branches[b])}");
                defs.AppendLine();
            }

            flux = $@"{defs}union(tables: [{string.Join(", ", names)}])
        {plan.MapClause}
        {pivotClause}";
        }

        Console.WriteLine($"[SensorCalibration] split aggregation into {plan.Branches.Count} branch(es): "
            + string.Join(", ", plan.Branches.Select(b => b.Fn)));
    }
    else
    {
        string preMap = plan.Mode == SensorCalibration.CalibrationMode.PreMap ? plan.MapClause : "";
        string postMap = plan.Mode == SensorCalibration.CalibrationMode.PostMap ? plan.MapClause : "";

        flux = $@"from(bucket:""{bucket}"")
        |> range(start: {startDateStr}, stop: {endDateStr})
        |> filter(fn: (r) => ({sensorFilterClause}) and ({filterClause}))
        {preMap}
        {aggregationClause}
        {postMap}
        {pivotClause}";
    }

    Console.WriteLine($"Flux query: {flux}");

    var queryApi = client.GetQueryApi();

    await queryApi.QueryAsync(flux, record =>
    {
        var array = new object[channelsIds.Count() + 2];
        array[0] = record.GetTime().Value.ToDateTimeUtc();
        array[1] = record.GetValueByKey("sensor");

        int index = 2;
        foreach (string key in channelsIds)
        {
            object value = record.GetValueByKey(key);
            if (value != null)
            {
                array[index] = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            index++;
        }
        ll.Add(array);

    }, exception =>
    {
        Console.WriteLine($"Error occurred: {exception.Message}");
    }, () =>
    {
        // Query completed
    }, orgId, cancellationToken);

    Console.WriteLine($"Influx returned {ll.Count} row(s) for {sensorsIds.Count} sensor(s) / {channelsIds.Count} channel(s).");

    return ll;
}

        /// <summary>
/// Optimized batch data retrieval for CSV export.
/// Returns data as Dictionary[timestamp][sensorId|channelId] = value
/// </summary>
public static async Task<Dictionary<DateTime, Dictionary<string, double>>> GetDataForExport(
    List<(string sensorId, string channelId)> sensorChannelPairs,
    DateTime startDate,
    DateTime endDate,
    string? bucketName = null)
{
    var result = new Dictionary<DateTime, Dictionary<string, double>>();
    var targetBucket = bucketName ?? bucket;

    if (sensorChannelPairs == null || sensorChannelPairs.Count == 0)
        return result;

    var options = new InfluxDBClientOptions.Builder()
        .Url(url)
        .AuthenticateToken(token)
        .TimeOut(TimeSpan.FromMinutes(10))
        .Build();

    using var client = new InfluxDBClient(options);

    string orgId = string.Empty;
    var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();
    if (organizations != null && organizations.Count > 0)
    {
        var org = organizations.FirstOrDefault(o => o.Name == orgName);
        orgId = org?.Id;
    }

    if (string.IsNullOrEmpty(orgId))
    {
        Console.WriteLine($"Organization '{orgName}' not found.");
        return result;
    }

    var validPairs = new HashSet<string>(
        sensorChannelPairs.Select(p => $"{p.sensorId.ToLowerInvariant()}|{p.channelId.ToLowerInvariant()}")
    );

    var distinctSensors = sensorChannelPairs.Select(p => p.sensorId).Distinct().ToList();
    var distinctChannels = sensorChannelPairs.Select(p => p.channelId).Distinct().ToList();

    string startDateStr = startDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    string endDateStr = endDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    // MATCH EXACTLY the working GetData query structure - NO _measurement filter
    string sensorFilter = string.Join(" or ", distinctSensors.Select(id => $"r.sensor == \"{id}\""));
    string channelFilter = string.Join(" or ", distinctChannels.Select(id => $"r._field == \"{id}\""));

    var flux = $@"from(bucket:""{targetBucket}"") 
        |> range(start: {startDateStr}, stop: {endDateStr}) 
        |> filter(fn: (r) => ({sensorFilter}) and ({channelFilter}))";

    Console.WriteLine($"Export Flux query (first 500 chars): {flux.Substring(0, Math.Min(500, flux.Length))}...");

    var queryApi = client.GetQueryApi();
    int recordCount = 0;

    try
    {
        var tables = await queryApi.QueryAsync(flux, orgId);
        
        Console.WriteLine($"Query returned {tables.Count} tables");
        
        foreach (var table in tables)
        {
            foreach (var record in table.Records)
            {
                try
                {
                    var time = record.GetTime();
                    if (!time.HasValue) continue;

                    var timestamp = time.Value.ToDateTimeUtc();
                    var sensorId = record.GetValueByKey("sensor")?.ToString();
                    var channelId = record.GetValueByKey("_field")?.ToString();
                    var value = record.GetValue();

                    if (string.IsNullOrEmpty(sensorId) || string.IsNullOrEmpty(channelId) || value == null)
                        continue;

                    var pairKey = $"{sensorId.ToLowerInvariant()}|{channelId.ToLowerInvariant()}";

                    if (!validPairs.Contains(pairKey))
                        continue;

                    double numericValue;
                    try
                    {
                        numericValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!result.ContainsKey(timestamp))
                        result[timestamp] = new Dictionary<string, double>();

                    result[timestamp][pairKey] = numericValue;
                    recordCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing record: {ex.Message}");
                }
            }
        }
        
        Console.WriteLine($"✓ Export completed. Tables: {tables.Count}, Records: {recordCount}, Timestamps: {result.Count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Export query failed: {ex.Message}");
        throw;
    }

    // Export the reported (calibrated) values: y = C1·x + C0 per (sensor, channel).
    await SensorCalibration.ApplyToExportAsync(result, distinctSensors, distinctChannels);

    return result;
}



    }
}
