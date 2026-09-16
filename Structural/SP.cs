using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using Accord.Math.Transforms;
using Accord.Math;
using Accord.Statistics.Models;

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra.Factorization;
using MathNetVectorDouble = MathNet.Numerics.LinearAlgebra.Vector<double>;
using MathNetVectorComplex = MathNet.Numerics.LinearAlgebra.Vector<System.Numerics.Complex>;
using MathNet.Numerics.Statistics;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace Structural
{
    public class SP
    {
        public static List<Complex[,]> SmoothAndAverageFFT(int nt, int duree, double[,] qTime, int divider, int multiplicity)
        {

            //   dt.PlotTimeResponse(qt);

            double[,] subqTime;

            List<Complex[,]> averageCms = new List<Complex[,]>();


            for (int k = 0; k < multiplicity; k++)
            {
                Complex[,] averageCm = new Complex[qTime.Columns(), qTime.Rows() / (2 * divider * multiplicity)];


                for (int j = 0; j < divider; j++)
                {

                    subqTime = qTime.Get(k * j * qTime.Rows() / (divider * multiplicity), (((k * j) + 1) * qTime.Rows() / (divider * multiplicity)), 0, (qTime.Columns()));

                    Complex[,] cm = SP.FFT(subqTime);

                    averageCm = averageCm.Apply((d, q, r) => d + cm[q, r]);

                }

                averageCm.Apply(d => d / divider);
                averageCms.Add(averageCm);

            }
            /*
            double[,] fmagnitudeFFT = new double[0, 0];
                double[,] fphaseFT = new double[0, 0];

            
                int rowsNum = averagemagnitudeFFT.Rows();

                NWaves.Filters.SavitzkyGolayFilter sg = new NWaves.Filters.SavitzkyGolayFilter(21);

                for (int i = 0; i < rowsNum; i++)
                {
                    NWaves.Signals.DiscreteSignal ds = new NWaves.Signals.DiscreteSignal(nt, averagemagnitudeFFT.GetRow(i).Apply(d => (float)d));
                    NWaves.Signals.DiscreteSignal ds1 = sg.ApplyTo(ds);
                    fmagnitudeFFT = fmagnitudeFFT.InsertRow(ds1.Samples.Apply(d => (double)d));
                }
               */

            return averageCms;

          
        }

        public static Complex[,] FFT(double[,] data)
        {
            Complex[,] FFT = new Complex[0, 0];

            Complex[,] complexData = data.Apply(d => new Complex(d, 0));

            for (int i = 0; i < data.Columns(); i++)
            {
                Complex[] d = complexData.GetColumn(i);
                FourierTransform2.FFT(d, FourierTransform.Direction.Forward);
                d = d.Take(d.Length / 2).ToArray();

                FFT = FFT.InsertRow(d);


            }

            /*

            var firstPart = complexData.Take(data.Count / 2).ToArray();
            var freqThreshold = firstPart.Max(f => Complex.Abs(f)) * 0.1;

            for (int i = 0; i < data.Count; i++)
            {
                var synthesizedOutput = Synthesize(firstPart, i * stepSize, data.Count, freqThreshold);
                result.Add(synthesizedOutput);
            }
            */

            return FFT;
        }

        public static Complex[] FFT2(double[] data)
        {
   
            Complex[] complexData = data.Apply(d => new Complex(d, 0));
            FourierTransform2.FFT(complexData, FourierTransform.Direction.Forward);
            complexData = complexData.Take(complexData.Length / 2).ToArray();

            return complexData;
        }


        static public Matrix<double>[] ConstructCovarianceMatrices2(double[,] sensorData, int maxLag)
        {
            int numSensors = sensorData.GetLength(0);


            // Convert sensorData to a MathNet Matrix
            Matrix<double> dataMatrix = DenseMatrix.OfArray(sensorData);

            Matrix<double>[] covarianceMatrices = new Matrix<double>[maxLag];

            for (int s = 0; s < maxLag; s++)
            {

                covarianceMatrices[s] = (dataMatrix.SubMatrix(0, dataMatrix.RowCount, 0, dataMatrix.ColumnCount - s) * dataMatrix.SubMatrix(0, dataMatrix.RowCount, s, dataMatrix.ColumnCount - s).Transpose()) / ((double)(dataMatrix.ColumnCount - s));
            }


            return covarianceMatrices;
        }

        public static (List<double[,]> Subsequences, double[,] AverageSubsequence, List<int> TriggerPoints) GetSubsequences(double[,] accelerationData, int sequenceLength, double thresholdPercentage, int refSensor)
        {
            double[] datap = accelerationData.GetColumn(refSensor);
            double mean = Statistics.Mean(datap);
            var col = datap.Select(x => x - mean).ToArray();

            var v = FindMaxAcceleration(col);

            double maxAcceleration = v.Item1;
            int index = v.Item2;

            double triggerThreshold = maxAcceleration * thresholdPercentage;

            List<int> triggerPoints = FindTriggerPoints(col, triggerThreshold);

            List<double[,]> subsequences = ExtractNonOverlappingSubsequences(accelerationData, triggerPoints, sequenceLength);

            double[,] averageSubsequence = CalculateAverageSubsequence(subsequences);

            return (Subsequences: subsequences, AverageSubsequence: averageSubsequence, TriggerPoints: triggerPoints);


        }

        static (double, int) FindMaxAcceleration(double[] data)
        {
            double max = data[0];
            int inde = 0;

            for (int i = 1; i < data.Length; i++)
            {
                if (data[i] > max)
                {
                    max = data[i];
                    inde = i;
                }
            }
            return (max, inde);
        }

        static List<int> FindTriggerPoints(double[] data, double threshold)
        {

            List<int> triggerPoints = new List<int>();

            for (int i = 1; i < data.Length - 1; i++)
            {
                if ((data[i] >= threshold) && (data[i - 1] < threshold))
                {
                    triggerPoints.Add(i);
                }
            }

            return triggerPoints;
        }

        static List<double[,]> ExtractNonOverlappingSubsequences(double[,] data, List<int> triggerPoints, int sequenceLength)
        {
            List<double[,]> subsequences = new List<double[,]>();
            int lastIndex = -sequenceLength;

            foreach (int startPoint in triggerPoints)
            {
                if (startPoint + sequenceLength <= data.GetLength(0) && startPoint >= lastIndex + sequenceLength)
                {
                    double[,] subsequence = data.Get(startPoint, startPoint + sequenceLength, 0, data.GetLength(1));
                    subsequences.Add(subsequence);
                    lastIndex = startPoint;
                }
            }

            return subsequences;
        }

        static double[,] CalculateAverageSubsequence(List<double[,]> subsequences)
        {
            int sequenceLength = subsequences[0].GetLength(1);
            double[,] averageSubsequence = new double[subsequences[0].GetLength(0), subsequences[0].GetLength(1)];


            foreach (var subsequence in subsequences)
            {
                // for (int i = 0; i < subsequence.GetLength(1); i++)
                //  {
                averageSubsequence = averageSubsequence.Add(subsequence);
                //}
            }

            //   for (int i = 0; i < averageSubsequence.Count; i++)
            //  {
            averageSubsequence = averageSubsequence.Divide(subsequences.Count);
            //   }

            return averageSubsequence;
        }

        public static Complex[,] OuterProduct(Complex[] a, Complex[] b)
        {

            Complex[,] result = new Complex[a.Length, b.Length];

            for (int i = 0; i < a.Length; i++)
            {
                for (int j = 0; j < b.Length; j++)
                {
                    result[i, j] = a[i] * b[j];
                }

            }

            return result;

        }

        public static List<Complex[,]> PSD(Complex[,] FFT)
        {
            List<Complex[,]> psds = new List<Complex[,]>();

            Complex[,] FFTConjugateTranspose = FFT.Transpose();

            for (int i = 0; i < FFTConjugateTranspose.Rows(); i++)
            {
                for (int j = 0; j < FFTConjugateTranspose.Columns(); j++)
                {
                    FFTConjugateTranspose[i, j] = Complex.Conjugate(FFTConjugateTranspose[i, j]);
                }
            }

            for (int w = 0; w < FFT.Columns(); w++)
            {

                psds.Add(OuterProduct(FFT.GetColumn(w), FFTConjugateTranspose.GetRow(w)));
            }

            var v = psds[0].Re().IsSymmetric();

            return psds;
        }

        public static List<List<Complex[,]>> SmoothAndAveragePSD(int nt, int duree, double[,] qTime, int divider, int multiplicity)
        {

            //   dt.PlotTimeResponse(qt);

            double[,] subqTime;

            List<List<Complex[,]>> averageCms = new List<List<Complex[,]>>();


            for (int k = 0; k < multiplicity; k++)
            {
                List<Complex[,]> averageCm = null;
                //new Complex[qTime.Columns(), qTime.Rows() / (2 * divider * multiplicity)];


                for (int j = 0; j < divider; j++)
                {

                    subqTime = qTime.Get(k * j * qTime.Rows() / (divider * multiplicity), (((k * j) + 1) * qTime.Rows() / (divider * multiplicity)), 0, (qTime.Columns()));

                    Complex[,] cm = SP.FFT(subqTime);
                    var v = PSD(cm);

                    if (averageCm == null) averageCm = v;

                    for (int i = 0; i < v.Count; i++)
                    {
                        averageCm[i] = averageCm[i].Apply((d, q, r) => d + v[i][q, r]);
                    }


                }

                averageCm.Apply(d => d.Apply(t => t / divider));
                averageCms.Add(averageCm);

            }
            /*
            double[,] fmagnitudeFFT = new double[0, 0];
                double[,] fphaseFT = new double[0, 0];

            
                int rowsNum = averagemagnitudeFFT.Rows();

                NWaves.Filters.SavitzkyGolayFilter sg = new NWaves.Filters.SavitzkyGolayFilter(21);

                for (int i = 0; i < rowsNum; i++)
                {
                    NWaves.Signals.DiscreteSignal ds = new NWaves.Signals.DiscreteSignal(nt, averagemagnitudeFFT.GetRow(i).Apply(d => (float)d));
                    NWaves.Signals.DiscreteSignal ds1 = sg.ApplyTo(ds);
                    fmagnitudeFFT = fmagnitudeFFT.InsertRow(ds1.Samples.Apply(d => (double)d));
                }
               */

            return averageCms;
        }



    }
}
