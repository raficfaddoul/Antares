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

namespace AliantWebS.Data
{


    public class InfluxService
    {
         private static readonly string token = "nj1b923x3pWuVrV7WT_DV0mBe3N8fGfZzj9zhDLV0Uy4CfsR06D-e4DB08zKNxwwoiKWYeBj6yY9WWvrLZdVCA==";

        private static readonly string orgId = "***REMOVED***";
        
        //  private static readonly string token = "***REMOVED***";

       //   private static readonly string orgId = "***REMOVED***";

        private static readonly string url = "http://localhost:8086";

        private static readonly string bucket = "AliantWebT";

        public InfluxService()
        {
        }

        static public async Task<List<object[]>> GetData(List<string> sensorsIds, List<string> channelsIds, DateTime startDate, DateTime endDate)
        {
            List<object[]> ll = new List<object[]>();
            //object[,] ll = new object[0, channelsIds.Count];

            //List<Array> l = new List<Array>();

            using var client = new InfluxDBClient(url, token);

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
                //
                //
                // Write by LineProtocol
                //
                
                writeApi.WriteRecords(records, WritePrecision.Us, bucket, orgId);

            }
        }

        static public async Task<bool> DeleteData(DateTime startDate, DateTime endDate, List<string> sensorsIds)
        {
            using var client = new InfluxDBClient(url, token);
                        
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
