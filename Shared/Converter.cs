using AliantWebS.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace AliantWebS.Data
{
    public static class Converter
    {

        //static public string? ConvertXmlToDisplayFormat(string? xml)
        //{
        //    if (xml == null) return null;
        //    var tuples = new List<string>();
        //    // Parse XML and extract tuples. 
        //    // This assumes your XML structure. Adjust as necessary.
        //    var doc = new XmlDocument();
        //    doc.LoadXml(xml);
        //    foreach (XmlNode node in doc.SelectNodes("//tuple"))
        //    {
        //        var x = node.Attributes["x"].Value;
        //        var y = node.Attributes["y"].Value;
        //        tuples.Add($"({x},{y})");
        //    }
        //    return string.Join(", ", tuples);
        //}

        //static public string? ConvertDisplayFormatToXml(string? displayFormat)
        //{
        //    // Use regular expression to parse the tuples
        //    if (displayFormat == null) return null;
        //    var regex = new Regex(@"\(([^,]+),([^)]+)\)");
        //    var matches = regex.Matches(displayFormat);

        //    var doc = new XmlDocument();
        //    var root = doc.CreateElement("root");  // Create root element

        //    foreach (Match match in matches)
        //    {
        //        if (match.Groups.Count == 3) // Ensure both numbers are captured
        //        {
        //            var tupleElement = doc.CreateElement("tuple");
        //            tupleElement.SetAttribute("x", match.Groups[1].Value);
        //            tupleElement.SetAttribute("y", match.Groups[2].Value);
        //            root.AppendChild(tupleElement);
        //        }
        //    }

        //    doc.AppendChild(root);
        //    return doc.OuterXml;
        //}

        public static Dictionary<double, double> ConvertFromXml(string xmlString)
        {
            if (xmlString == null) return null;
            var root = XElement.Parse(xmlString);
            var dictionary = new Dictionary<double, double>();

            foreach (var pairElement in root.Elements("Pair"))
            {
                var key = double.Parse(pairElement.Element("Key").Value, CultureInfo.InvariantCulture);
                var value = double.Parse(pairElement.Element("Value").Value, CultureInfo.InvariantCulture);
                dictionary[key] = value;
            }

            return dictionary;
        }

        public static string? ConvertToXml(string oldxml, Dictionary<double, double> dictionary)
        {
            if (dictionary == null) return oldxml;
            var root = new XElement("Dictionary");

            foreach (var pair in dictionary)
            {
                var pairElement = new XElement("Pair",
                    new XElement("Key", pair.Key),
                    new XElement("Value", pair.Value)
                );
                root.Add(pairElement);
            }

            return root.ToString();
        }

        static public  string? DisplayDictionary(Dictionary<double, double> dictionary)
        {
            if (dictionary == null) return null;
            StringBuilder output = new StringBuilder();

            foreach (var pair in dictionary)
            {
                output.AppendFormat("({0},{1}), ", pair.Key, pair.Value);
            }

            if (output.Length > 0)
                output.Remove(output.Length - 2, 2);  // Remove the trailing comma and space

            return output.ToString();
        }

        static public Dictionary<double, double> ParseToDictionary(string input)
        {
            Dictionary<double, double> result = new Dictionary<double, double>();

            // Remove any spaces and split the string into pairs
            var pairs = input.Replace(" ", "").Split("),(");

            foreach (var pair in pairs)
            {
                // Remove any parentheses
                var cleanedPair = pair.Trim('(', ')');

                // Split the string into key and value
                var keyValue = cleanedPair.Split(',');

                // Parse the key and value, and add them to the dictionary
                if (keyValue.Length == 2
                    && double.TryParse(keyValue[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double key)
                    && double.TryParse(keyValue[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                {
                    result[key] = value; // Using indexer to add or update the value for the key
                }
                else
                {
                    return null;
                }
            }

            return result;
        }

        public static double? ParseDoubleAttributes(Element el, string attribute)
        {
            double? y;
            if (Double.TryParse(el.AttributeElements.FirstOrDefault(n => n.AttributeType.Name.Trim() == attribute.Trim())?.Value, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                y = result;
            }
            else
            {
                y = null;
            }
            return y;
        }


    }
}
