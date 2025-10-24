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
         private static readonly string token = "nj1b923x3pWuVrV7WT_DV0mBe3N8fGfZzj9zhDLV0Uy4CfsR06D-e4DB08zKNxwwoiKWYeBj6yY9WWvrLZdVCA==";

       // private static readonly string orgId = "***REMOVED***";

        //  private static readonly string token = "***REMOVED***";

        //   private static readonly string orgId = "***REMOVED***";

        //   private static readonly string url = "http://localhost:8086";

        private static readonly string url = "http://influxdb2:8086"; // Use the service name defined in docker-compose.yaml
    


        private static readonly string bucket = "AliantWebT";
        private static readonly string orgName = "Aliant";

        public InfluxService()
        {
        }

        /// <summary>
        /// Counts the number of records in the bucket.
        /// </summary>
        /// <param name="sensorsIds">Optional list of sensor IDs to filter by sensor</param>
        /// <param name="channelsIds">Optional list of channel IDs to filter by channel</param>
        /// <param name="startDate">Optional start date</param>
        /// <param name="endDate">Optional end date</param>
        /// <returns>Number of records as an integer</returns>
        public static async Task<int> CountRecords(List<string> sensorsIds = null, List<string> channelsIds = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            int recordCount = 0;

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

            // Build the Flux query
            string flux = $"from(bucket:\"{bucket}\") " +
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

        static public async Task<List<object[]>> GetData(List<string> sensorsIds, List<string> channelsIds, DateTime startDate, DateTime endDate)
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

            var flux = $"from(bucket:\"{bucket}\") |> range(start: {startDateStr}, stop: {endDateStr}) |> filter(fn: (r) => ({sensorFilterClause}) and ({filterClause})) |> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")";

            //var flux = $"from(bucket:\"{bucket}\") |> range(start: {startDateStr}, stop: {endDateStr}) |> filter(fn: (r) => r.sensor == \"{sensorId}\" and ({filterClause})) |> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")";
            var queryApi = client.GetQueryApi();
            //
            // QueryData
            //
            await queryApi.QueryAsync(flux, record =>
            {
                var array = new object[channelsIds.Count() + 2];
                //
                // The callback to consume a FluxRecord.
                //
                array[0] = record.GetTime().Value.ToDateTimeUtc();//-new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

                array[1] = record.GetValueByKey("sensor");


                int index = 2;
                foreach (string key in channelsIds)
                {

                    object value = record.GetValueByKey(key);
                    if (value !=null)
                    {
                        array[index] = (double)value;
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
                        array[index] = (double)value;
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

        static public async Task<long> QueryLatestTimestampFromInfluxAsync()
        {
            using var client = new InfluxDBClient(url, token);
            // The organization name or ID in which your bucket resides
            // var orgName = "Aliant";
            // The bucket in which you're storing SHM data
            //var bucketName = "my-bucket";

            // 1) If you don’t know your org ID, you can look it up once:
            var orgs = await client.GetOrganizationsApi().FindOrganizationsAsync();
            var org = orgs?.FirstOrDefault(o => o.Name == orgName);
            if (org == null)
            {
                Console.WriteLine($"Organization {orgName} not found");
                return 0;
            }

            // 2) Flux query: get all data in measurement=SHM, then find the max _time
            //    This example sorts descending by _time and takes 1 record. Alternatively, you could:
            //    |> aggregateWindow(every:inf, fn:max, createEmpty:false)
            //    or |> max(column: "_time")
            //    but sorting and limiting is often straightforward:
            var flux = $@"
    from(bucket: ""{bucket}"")
        |> range(start: 0) 
        |> filter(fn: (r) => r._measurement == ""SHM"")
        |> keep(columns: [""_time""])
        |> sort(columns: [""_time""], desc: true)
        |> limit(n:1)
    ";

            var queryApi = client.GetQueryApi();

            try
            {
                var tables = await queryApi.QueryAsync(flux, org.Id);
                // If there’s at least one row
                if (tables.Count > 0 && tables[0].Records.Count > 0)
                {
                    // The first (and only) record returned is the max-time record
                    var record = tables[0].Records[0];
                    var time = record.GetTime(); // This is a DateTime? in UTC
                    if (time.HasValue)
                    {
                        // Convert to Unix time in seconds if that’s how you track your “report_datetime”
                        return time.Value.ToUnixTimeSeconds();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying Influx: {ex.Message}");
            }

            // No data or error => treat it as "start from 0" so we fetch everything
            return 0;
        }


        static public async Task WriteData(List<PointData> points)
        {
            /*                var point = PointData.Measurement("SHM")
                    .Tag("sensor", "5cdd75fd-fb15-ee11-8da6-5820b17cefc4")
                    .Field("c08e6d09-fc15-ee11-8da6-5820b17cefc4", 21.1)
                    .Field("e8c8f514-fc15-ee11-8da6-5820b17cefc4", 35.9)
                    .Field("b1df4339-fc15-ee11-8da6-5820b17cefc4", 0)
                    .Timestamp(DateTimeOffset.FromUnixTimeSeconds(1741024000), WritePrecision.S);*/


            using var client = new InfluxDBClient(url, token);
            string orgId = string.Empty;
            var organizations = await client.GetOrganizationsApi().FindOrganizationsAsync();

            if (organizations != null && organizations.Count > 0)
            {
                var org = organizations.FirstOrDefault(o => o.Name == orgName); // Find by org name
                orgId = org?.Id; // Get the orgID
            }


            using (var writeApi = client.GetWriteApi())
            {

                writeApi.WritePoints(points, bucket, orgId);

      
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

    }
}
