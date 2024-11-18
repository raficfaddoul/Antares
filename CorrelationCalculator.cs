using MathNet.Numerics.Statistics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AI
{
    public class CorrelationCalculator
    {
        public double[,] CalculateCorrelationMatrix(List<(string, Dictionary<DateTime, double>)> indices)
        {
            // Get all unique timestamps
            var allTimestamps = indices.SelectMany(index => index.Item2.Keys).Distinct().OrderBy(x => x).ToList();

            // Interpolate each time series to the common timestamps
            var interpolatedSeries = indices.Select(index => InterpolateSeries(index.Item2, allTimestamps)).ToList();

            int numSeries = interpolatedSeries.Count;
            int seriesLength = allTimestamps.Count;

            // Convert to 2D array for MathNet
            var data = new double[numSeries, seriesLength];
            for (int i = 0; i < numSeries; i++)
            {
                for (int j = 0; j < seriesLength; j++)
                {
                    data[i, j] = interpolatedSeries[i][j];
                }
            }

            // Calculate correlation matrix
            var correlationMatrix = Correlation.PearsonMatrix(data);
            return correlationMatrix;
        }

        private List<double> InterpolateSeries(Dictionary<DateTime, double> series, List<DateTime> allTimestamps)
        {
            var interpolated = new List<double>();
            var sortedSeries = series.OrderBy(x => x.Key).ToList();

            for (int i = 0; i < allTimestamps.Count; i++)
            {
                DateTime t = allTimestamps[i];
                if (series.ContainsKey(t))
                {
                    interpolated.Add(series[t]);
                }
                else
                {
                    // Find surrounding points for interpolation
                    var (left, right) = FindSurroundingPoints(sortedSeries, t);
                    if (left.HasValue && right.HasValue)
                    {
                        double interpolatedValue = Interpolate(t, left.Value, right.Value);
                        interpolated.Add(interpolatedValue);
                    }
                    else if (left.HasValue)
                    {
                        interpolated.Add(left.Value.Value);
                    }
                    else if (right.HasValue)
                    {
                        interpolated.Add(right.Value.Value);
                    }
                    else
                    {
                        interpolated.Add(0); // Default value if no surrounding points
                    }
                }
            }

            return interpolated;
        }

        private (KeyValuePair<DateTime, double>?, KeyValuePair<DateTime, double>?) FindSurroundingPoints(List<KeyValuePair<DateTime, double>> sortedSeries, DateTime t)
        {
            KeyValuePair<DateTime, double>? left = null;
            KeyValuePair<DateTime, double>? right = null;

            for (int i = 0; i < sortedSeries.Count; i++)
            {
                if (sortedSeries[i].Key <= t)
                {
                    left = sortedSeries[i];
                }
                if (sortedSeries[i].Key >= t && right == null)
                {
                    right = sortedSeries[i];
                    break;
                }
            }

            return (left, right);
        }

        private double Interpolate(DateTime t, KeyValuePair<DateTime, double> left, KeyValuePair<DateTime, double> right)
        {
            double t1 = left.Key.Ticks;
            double t2 = right.Key.Ticks;
            double v1 = left.Value;
            double v2 = right.Value;
            double tTicks = t.Ticks;

            return v1 + (v2 - v1) * ((tTicks - t1) / (t2 - t1));
        }
    }
}
