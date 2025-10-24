using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Numerics;
using Accord.Math;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using System.Text.RegularExpressions;
using AliantWebS.Models;
using AliantWebS.Data;
using Accord.Math.Distances;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Helpers
{
    public static class RandomExtensions
    {
        public static double NextGaussian(this Random random, double mean = 0, double stdDev = 1)
        {
            // Box-Muller transform to generate Gaussian random numbers
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }
    }

    public static class Helpers
    {
        public static double?[,] ConvertToDoubleArray(object[,] inputArray)
        {
            int rows = inputArray.GetLength(0); // Get the number of rows in the array
            int columns = inputArray.GetLength(1); // Get the number of columns in the array
            double?[,] outputArray = new double?[rows, columns]; // Create a new double[,] array with the same dimensions

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < columns; j++)
                {
                    // Try to convert each element to double and store it in the output array
                    // If the conversion fails, it will catch the exception and set the value to 0.0
                    try
                    {
                        outputArray[i, j] = Convert.ToDouble(inputArray[i, j]);
                    }
                    catch (Exception)
                    {
                        outputArray[i, j] = null;
                    }
                }
            }

            return outputArray;
        }


        static public async Task WriteArrayToCsvAsync(double[,] array, string filePath)
        {
            int rows = array.GetLength(0);
            int cols = array.GetLength(1);

            using (StreamWriter file = new StreamWriter(filePath))
            {
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        await file.WriteAsync(array[i, j].ToString());
                        if (j < cols - 1)
                            await file.WriteAsync(",");
                    }
                    await file.WriteLineAsync();
                }
            }
        }

        public static double[,] ConvertToDoubleMatrix(double?[,] nullableMatrix, double defaultValue = 0.0)
        {
            int numRows = nullableMatrix.GetLength(0);
            int numCols = nullableMatrix.GetLength(1);
            double[,] matrix = new double[numRows, numCols];

            for (int i = 0; i < numRows; i++)
            {
                for (int j = 0; j < numCols; j++)
                {
                    matrix[i, j] = nullableMatrix[i, j] ?? defaultValue;
                }
            }

            return matrix;
        }
        public static void InterpolateColumn(double?[,] matrix, int columnIndex)
        {
       

            int numRows = matrix.GetLength(0);

            for (int i = 0; i < numRows; i++)
            {
                if (matrix[i, columnIndex] == null)
                {
                    int startRow = i - 1;
                    int endRow = i + 1;

                    // Find the previous known value
                    while (startRow >= 0 && (matrix[startRow, columnIndex] == null))
                        startRow--;

                    // Find the next known value
                    while (endRow < numRows && (matrix[endRow, columnIndex] == null))
                        endRow++;

                    // If surrounding values are found, interpolate
                    if (startRow >= 0 && endRow < numRows)
                    {
                        double? slope = (matrix[endRow, columnIndex] - matrix[startRow, columnIndex]) / (endRow - startRow);
                        for (int j = startRow + 1; j < endRow; j++)
                        {
                            matrix[j, columnIndex] = matrix[startRow, columnIndex] + slope * (j - startRow);
                        }
                    }
                    else if (startRow >= 0)  // Only the ending values are null
                    {
                        for (int j = i; j < numRows; j++)
                        {
                            matrix[j, columnIndex] = matrix[startRow, columnIndex];
                        }
                        break;  // Exit the loop since we've handled all nulls to the end of the column
                    }
                    else if (endRow < numRows)  // Only the starting values are null
                    {
                        for (int j = 0; j < i; j++)
                        {
                            matrix[j, columnIndex] = matrix[endRow, columnIndex];
                        }
                    }
                }
            }
        }

        public static double?[,] TrimEdgeNullRows(double?[,] matrix)
        {
            int numRows = matrix.GetLength(0);
            int numCols = matrix.GetLength(1);

            // Determine the first non-null row
            int firstNonNullRow = 0;
            while (firstNonNullRow < numRows && ContainsNullInRow(matrix, firstNonNullRow))
                firstNonNullRow++;

            // Determine the last non-null row
            int lastNonNullRow = numRows - 1;
            while (lastNonNullRow >= 0 && ContainsNullInRow(matrix, lastNonNullRow))
                lastNonNullRow--;

            // If all rows are null rows, return an empty matrix
            if (firstNonNullRow > lastNonNullRow)
                return new double?[0, numCols];

            int newRowCount = lastNonNullRow - firstNonNullRow + 1;
            double?[,] newMatrix = new double?[newRowCount, numCols];

            for (int i = 0; i < newRowCount; i++)
            {
                for (int j = 0; j < numCols; j++)
                {
                    newMatrix[i, j] = matrix[firstNonNullRow + i, j];
                }
            }

            return newMatrix;
        }

        private static bool ContainsNullInRow(double?[,] matrix, int rowIndex)
        {
            int numCols = matrix.GetLength(1);
            for (int j = 0; j < numCols; j++)
            {
                if (matrix[rowIndex, j] == null)
                    return true;
            }
            return false;
        }

        static public bool IsCurrentPage(NavigationManager navigationManager, params string[] route)
        {
            // Get the current URI and base URI
            var currentUri = navigationManager.ToBaseRelativePath(navigationManager.Uri);


            foreach (var r in route)
            {

                if (currentUri.Contains(r, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static string GetSource(byte[] imageBytes)
        {
            if (imageBytes == null) return string.Empty;
            string imagesrc = Convert.ToBase64String(imageBytes);
            string imageDataURL = string.Format("data:image/jpeg;base64,{0}", imagesrc);
            return imageDataURL;
        }

        public static object[,] ConvertTo2DArray(List<object[]> list)
        {
            int rows = list.Count;
            int cols = list[0].Length;

            object[,] result = new object[rows, cols];

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    result[i, j] = list[i][j];
                }
            }

            return result;
        }

        public static Dictionary<string, object> ProcessLine(string line)
        {
            //string pattern = @"(\w+):\s*(\w+),?";
            string pattern = @"([^:]+):\s*([^,]+),?";
            var matches = Regex.Matches(line, pattern);

            Dictionary<string, object> dict = new Dictionary<string, object>();

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string key = match.Groups[1].Value.Trim();
                string value = match.Groups[2].Value.Trim();
                bool booleanValue;
                int intValue;

                if (int.TryParse(value, out intValue))
                {
                    dict.Add(key, intValue);
                }
                else if (bool.TryParse(value, out booleanValue))
                {
                    dict.Add(key, booleanValue);
                }
                else
                {
                    dict.Add(key, value);
                }
            }

            return dict;
        }

       


        public async static Task<(ElementType? elementType, string? typeName)> CreateElementType(EntitiesService entitiesService,
            Category category,
            Dictionary<string,object>dict,
            Dictionary<string, object> elementsTypesWanted,
            List<string> elementsTypesExcluded,
            IJSRuntime JsRuntime
            )
        {

            string typeName = string.Empty;
            if (dict.Keys.Contains("type"))
            {
                typeName = dict["type"].ToString()?.Trim();
                dict.Remove("type");
            }

            if (typeName == string.Empty) return (null,null);

            if (elementsTypesExcluded.Contains(typeName)) return (null,null);

            ElementType elementType = null;

            if (!elementsTypesWanted.Keys.Contains(typeName))
            {
                elementType = await entitiesService.GetFilteredFirstEntityAsync<ElementType>(a => a.Name == typeName);

                if (elementType == null)
                {
                    var isConfirmed = true; // await JsRuntime.InvokeAsync<bool>("confirm", System.Threading.CancellationToken.None, $"The Element type {typeName} does not exist.\nDo you want to create one automatically?");

                    if (isConfirmed)
                    {

                        ElementType et = new ElementType();
                        et.Name = typeName;
                        if (category != null)
                        {
                            et.CategoryId = category.CategoryId;
                        }
                        elementType = await entitiesService.InsertEntityAsync(et);
                        elementsTypesWanted.Add(elementType.Name, elementType);




                    }
                    else
                    {
                        elementsTypesExcluded.Add(typeName);
                        return (null, null);
                    }

                }
                else
                {
                    elementsTypesWanted.Add(elementType.Name, elementType);
                }

            }

            return (elementType: elementType, typeName: typeName);
        }

        public static double[][] ConvertToJagged(double[,] multiDimensionalArray)
        {
            int rows = multiDimensionalArray.GetLength(0);
            int cols = multiDimensionalArray.GetLength(1);
            double[][] jaggedArray = new double[rows][];
            for (int i = 0; i < rows; i++)
            {
                jaggedArray[i] = new double[cols];
                for (int j = 0; j < cols; j++)
                {
                    jaggedArray[i][j] = multiDimensionalArray[i, j];
                }
            }
            return jaggedArray;
        }



        static public async Task<List<Dictionary<string, string>>> GetDataFromCSV2(System.IO.Stream stream)
        {
            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();

            using (StreamReader reader = new StreamReader(stream))
            {
                // Read the entire stream into a string
                string content = await reader.ReadToEndAsync();

                // Split the string by lines
                string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                if (lines.Length > 0)
                {
                    // First line is the header
                    string[] headers = lines[0].Split(',');

                    // Iterate over the remaining lines
                    for (int j = 1; j < lines.Length; j++)
                    {
                        string[] values = lines[j].Split(',');
                        if (values.Length < headers.Length)
                        {
                            continue;
                        }

                        Dictionary<string, string> row = new Dictionary<string, string>();
                        for (int i = 0; i < headers.Length; i++)
                        {
                            if (i < values.Length)  // This check prevents out of range if some rows have fewer columns than headers.
                            {
                                row[headers[i]] = values[i];
                            }
                        }

                        rows.Add(row);
                    }
                }
            }

            return rows;
        }

        static public async Task<List<Dictionary<string, string>>> GetDataFromCSV(System.IO.Stream stream)
        {
            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();

            using (StreamReader reader = new StreamReader(stream))
            {

                // Read the header line
                string headerLine = await reader.ReadLineAsync();
                string[] headers = headerLine.Split(',');

                // Read the rest of the lines
                while (!reader.EndOfStream)
                {
                    string line = await reader.ReadLineAsync();
                    string[] values = line.Split(',');

                    Dictionary<string, string> row = new Dictionary<string, string>();
                    for (int i = 0; i < headers.Length; i++)
                    {
                        row[headers[i]] = values[i];
                    }

                    rows.Add(row);

                }
            }

            return rows;
        }

        static public Dictionary<int, Dictionary<long, List<double>>> GetDataFromJson(string json)
        {
            Dictionary<int, Dictionary<long, List<double>>> data = new Dictionary<int, Dictionary<long, List<double>>>();

            JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);

            foreach (var item in root.EnumerateArray())
            {
                int sensor_address = item.GetProperty("hardware").GetProperty("address").GetInt32();
                JsonElement samples = item.GetProperty("samples");
                JsonElement values = samples.GetProperty("values");

                DateTime dt = item.GetProperty("software").GetProperty("datetime_utc").GetDateTime();

                foreach (var value in values.EnumerateArray())
                {
                    int timestamp = value[0].GetInt32();

                    TimeSpan additionalTime = TimeSpan.FromTicks(timestamp * 10); // There are 10 ticks in a microsecond

                    List<double> rowData = new List<double>();

                    for (int i = 1; i < 4; i++)
                    {
                        rowData.Add(value[i].GetDouble());
                    }

                    if (!data.ContainsKey(sensor_address))
                    {
                        data[sensor_address] = new Dictionary<long, List<double>>();
                    }

                    DateTime newDateTime = dt.Add(additionalTime);
                    DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    long microsecondsSinceEpoch = (newDateTime - unixEpoch).Ticks /10;

                    data[sensor_address][microsecondsSinceEpoch] = rowData;//newDateTime.Ticks*10
                }

            }


            return data;

        }



        static public void GetChildren(Element element, List<Guid?> listIDs, List<Element> childrens = null)
        {
            listIDs.Add(element.ElementId);
            if (childrens != null) childrens.Add(element);
            foreach (Element e in element.InverseParent)
            {
                GetChildren(e, listIDs, childrens);
            }

        }

        static public List<Guid?> GetTheChildren(IEnumerable<object> parents, List<Element> childrens = null)
        {
            List<Guid?> listIDs = new List<Guid?>();


            foreach (Element v in parents)
            {

                GetChildren(v, listIDs, childrens);

            }

            listIDs = listIDs.Distinct().ToList();
            return listIDs;
        }
        public static IEnumerable<T> ListFromString<T>(string s)
        {
            var ss = s.Split(',');


            return ss.Select(x => Convert.ChangeType(x, typeof(T))).Cast<T>();

        }

        public static (string,int?) SplitStringInStringAndNum(string s)
        {
            string stringPart;
            int? intPart;

            string[] numbers = Regex.Split(s, @"\D+");
            stringPart = (s.Substring(0, s.Length - numbers[numbers.Length - 1].Length));
            if (Int32.TryParse(numbers[numbers.Length - 1], out int numValue))
            {
                intPart = numValue;
            }
            else
            {
                intPart = null;
            }

            return(stringPart, intPart);    
        }

        public static (string, int) GetlastName(List<string> inputs)
        {

            List<string> stringList = new List<string>();
            List<int?> intList = new List<int?>();
            foreach (var input in inputs)
            {
                var v = SplitStringInStringAndNum(input);
                stringList.Add(v.Item1);
                intList.Add(v.Item2);                       
            }

            int? maximum = intList.Max();
            int indexOfMaximum = intList.IndexOf(maximum);
            
            string stringOfMaximum = stringList[indexOfMaximum];

            return (stringOfMaximum, (int)maximum);
        }

        static public (Tensor, Tensor) CSVToArray(string filePath, char SplitChar, bool SkipHeader)
        {

            string[] lines = System.IO.File.ReadAllLines(filePath);


            var valuesTemp = lines[0].Split(SplitChar);

            double[,] a = new double[0, valuesTemp.Length];


            int start = 0;


            if (SkipHeader)
            {
                start = 1;
            }

            for (int i = start; i < lines.Length; i++)
            {

                var values = lines[i].Split(SplitChar);



                a = a.InsertRow(values.Select(x => Convert.ToDouble(x)).ToArray());

            }

            var item1 = a.GetColumn(0);
            //  var indexes = Enumerable.Range(1, a.Columns() - 1).ToArray();
            var indexes = Enumerable.Range(1, 2).ToArray();
            var item2 = a.GetColumns(indexes);
            //   var item2 = a.GetColumn(1);


            return (torch.tensor(item2, ScalarType.Float32), torch.tensor(item1, ScalarType.Float32));
        }


        public static IEnumerable Cartesian(this IEnumerable<IEnumerable> items)
        {
            var slots = items
               // initialize enumerators
               .Select(x => x.GetEnumerator())
               // get only those that could start in case there is an empty collection
               .Where(x => x.MoveNext())
               .ToArray();

            while (true)
            {
                // yield current values
                yield return slots.Select(x => x.Current);

                // increase enumerators
                foreach (var slot in slots)
                {
                    // reset the slot if it couldn't move next
                    if (!slot.MoveNext())
                    {
                        // stop when the last enumerator resets
                        if (slot == slots.Last()) { yield break; }
                        slot.Reset();
                        slot.MoveNext();
                        // move to the next enumerator if this reseted
                        continue;
                    }
                    // we could increase the current enumerator without reset so stop here
                    break;
                }
            }
        }
    }
}
