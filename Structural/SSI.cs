using System.Reflection.Emit;
using System;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra.Factorization;
using System.Numerics;
using MathNetVectorDouble = MathNet.Numerics.LinearAlgebra.Vector<double>;
using MathNetVectorComplex = MathNet.Numerics.LinearAlgebra.Vector<System.Numerics.Complex>;
using MathNet.Numerics.Statistics;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using MathNet.Numerics;
using MathNet.Numerics.LinearRegression;
using Accord.Math;
using System.Text.Json;

namespace SSI
{
    class SSI
    {



        /// Assuming you have a 2D array called "sensorData" with rows representing sensors and columns representing time steps.
        ///
        public SSI(double[,] sensorData, double samplingFrequency = 1.0, int blockSize = 60, int? modelOrderMax = null, double threshold = 0.1)
        {


            // 2. Data preprocessing
            // Perform data preprocessing to remove trends, periodic components, or outliers.
            //sensorData = PreprocessData(sensorData);

            //sensorData = Detrend(sensorData);

            // 3. Construct covariance matrices



            //   int blockSize = 3; // Choose an appropriate block size
            // double threshold = 0.1; // Set a threshold value for the gap between singular values

            int sensorCount = sensorData.GetLength(0);

         

            Matrix<double>[] covarianceMatrices = null;
            covarianceMatrices = ConstructCovarianceMatrices2(sensorData, 2 * blockSize + 1);


            // Save matrices to disk
            //string filePath = "matrices.bin";
            //SaveMatricesToDisk(covarianceMatrices, filePath);

            // Load matrices from disk
            // covarianceMatrices = LoadMatricesFromDisk(filePath);
            // covarianceMatrices = covarianceMatrices.Take(1500).ToArray();


            // Assemble the Toeplitz matrix
            //  Tb = np.vstack([np.hstack([R_is[_o,:,:] for _o in range(br + _l, _l, -1)]) for _l in range(br)])

            Matrix<double> ToeplitzMatrix = DenseMatrix.Create(blockSize * sensorCount, blockSize * sensorCount, 0.0);

            for (int l = 0; l < blockSize; l++)
            {
                int colOffset = 0;
                for (int o = blockSize + l; o > l; o--)
                {
                    int rowOffset = l * sensorCount;
                    //int colOffset = o * sensorCount;
                    ToeplitzMatrix.SetSubMatrix(rowOffset, colOffset, covarianceMatrices[o]);
                    colOffset += sensorCount;
                }
            }



            // 5. Subspace decomposition using Singular Value Decomposition (SVD)
            Svd<double> svd = ToeplitzMatrix.Svd();


            //// Extract the matrices U, Σ, and V*
            //Matrix<double> U = svd.U;
            //Matrix<double> Sigma = svd.W;
            //Matrix<double> Vt = svd.VT;

            //MathNetVectorDouble singularValues = Sigma.Diagonal();


            // 6. Model order estimation
            // int modelOrder = EstimateModelOrder(singularValues, threshold);
            //  modelOrder = 40;
            // 7. System parameter identification
            // Extract system parameters (natural frequencies, damping ratios, mode shapes) from the deterministic subspace.
            // Implement a method to extract the parameters based on the selected model order.

            if (modelOrderMax == null)
                modelOrderMax = blockSize * sensorCount;

            for (int modelOrder = (int)modelOrderMax; modelOrder < modelOrderMax + 1; modelOrder++)
            {

                // Obtain the deterministic subspace
                Matrix<double> U1 = svd.U.SubMatrix(0, blockSize * sensorCount, 0, modelOrder);
                Matrix<double> Sigma1 = svd.W.SubMatrix(0, modelOrder, 0, modelOrder).PointwiseSqrt();
                Matrix<double> V1 = svd.VT.SubMatrix(0, modelOrder, 0, blockSize * sensorCount);


                Matrix<double> O = U1 * Sigma1;

                Matrix<double> O1 = O.SubMatrix(0, O.RowCount - sensorCount, 0, O.ColumnCount);
                Matrix<double> O2 = O.SubMatrix(sensorCount, O.RowCount - sensorCount, 0, O.ColumnCount);

                Matrix<double> A = O1.PseudoInverse() * O2;


                // experiment/////////////////////////////////////////////////////////////////////////////////////////
                Matrix<double>[] covarianceMatrices2 = ConstructCovarianceMatrices(sensorData, 1, 2 * blockSize + 1);
                int modelOrder2 = 40;
                Matrix<double> hankelMatrix = ConstructBlockHankelMatrix(covarianceMatrices2, blockSize + 1, blockSize - 1);
                Svd<double> svd2 = hankelMatrix.Svd();
                Matrix<double> deterministicSubspaceO = svd2.U.SubMatrix(0, modelOrder2, 0, modelOrder2);
                Matrix<double> deterministicSigmaO = svd2.W.SubMatrix(0, modelOrder2, 0, modelOrder2);
                Matrix<double> O22 = deterministicSubspaceO * deterministicSigmaO.PointwiseSqrt();

                Matrix<double> A2 = O22.SubMatrix(sensorCount, modelOrder2 - sensorCount, 0, modelOrder2).PseudoInverse() * O.SubMatrix(0, modelOrder2 - sensorCount, 0, modelOrder2);

                // Calculate the system matrix A
                //   int numberOfRows = deterministicSubspace.RowCount - sensorCount;
                // Matrix<double> A = deterministicSubspace.SubMatrix(0, numberOfRows, 0, modelOrder) * deterministicSubspace.SubMatrix(sensorCount, numberOfRows, 0, modelOrder).PseudoInverse();
                //Matrix<double> A = deterministicSubspace.SubMatrix(sensorCount, modelOrder, 0, modelOrder) * deterministicSubspace.SubMatrix(0, modelOrder, 0, modelOrder).PseudoInverse();

                Evd<double> evd2 = A2.Evd();

                MathNetVectorDouble nf1 = MathNetVectorDouble.Build.Dense(modelOrder2);

                for (int i = 0; i < modelOrder2; i++)
                {
                    //MathNetVectorComplex eigenvector = eigenvectors.Column(i);

                    // Calculate natural frequency (in Hz)
                    double omega = Math.Sqrt(evd2.EigenValues[i].Magnitude); // square root of eigenvalue magnitude
                    nf1[i] = (omega / (2 * Math.PI * samplingFrequency));
                }
                ////////////////////////////////////////////////////////////////////////////////////////////////////

                //The output matrix [C] i

                Matrix<double> C = O.SubMatrix(0, sensorCount, 0, O.ColumnCount);


                // Perform eigenvalue decomposition of the system matrix A
                Evd<double> evd = A.Evd();


                // Extract eigenvalues and eigenvectors
                var eigenvalues = evd.EigenValues;
                Matrix<double> eigenvectors = evd.EigenVectors;

                // Calculate natural frequencies, damping ratios, and mode shapes
                MathNetVectorDouble naturalFrequencies = MathNetVectorDouble.Build.Dense(modelOrder);
                MathNetVectorDouble nf = MathNetVectorDouble.Build.Dense(modelOrder);

                MathNetVectorDouble dampingRatios = MathNetVectorDouble.Build.Dense(modelOrder);
                Matrix<double> modeShapes = C * eigenvectors;// Matrix<double>.Build.Dense(sensorCount, modelOrder);

                for (int i = 0; i < modelOrder; i++)
                {

                    double a = Math.Abs(Math.Atan2(eigenvalues[i].Imaginary, eigenvalues[i].Real));
                    nf[i] = (a / (2 * Math.PI * samplingFrequency));

                    var p = Complex.Log(eigenvalues[i]) * samplingFrequency;
                    naturalFrequencies[i] = Math.Abs(p.Magnitude / (2 * Math.PI));


                    // Calculate damping ratio
                    dampingRatios[i] = -eigenvalues[i].Real / eigenvalues[i].Magnitude;

                    //// Calculate mode shape
                    //modeShapes.SetColumn(i, eigenvector.Real());
                }

                var v = naturalFrequencies.ToArray();

                Array.Sort(v);
            }


            // 8. Model validation and refinement
            // Validate the identified model against measured data or independent datasets.
            // If necessary, refine the model to improve its accuracy.

        }

        static Matrix<double>[] ConstructCovarianceMatrices(double[,] sensorData, int lagStep, int numTimeSteps)
        {
            int numSensors = sensorData.GetLength(0);


            // Convert sensorData to a MathNet Matrix
            Matrix<double> dataMatrix = DenseMatrix.OfArray(sensorData);

            // Calculate the covariance matrices for different time steps
            int maxLag = (numTimeSteps - 1) / lagStep;
            Matrix<double>[] covarianceMatrices = new Matrix<double>[maxLag + 1];

            // Create ParallelOptions instance and set the maximum degree of parallelism
            ParallelOptions parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 100 // Set the maximum number of parallel tasks to 4, for example
            };

            int start = 0;
            int end = maxLag + 1;
            int step = lagStep; // Change this value to adjust the step.

            var partitioner = Partitioner.Create(start, end, step);

            // Use Parallel.For with the specified options
            Parallel.ForEach(partitioner, parallelOptions, range =>
            {
                for (int lag = range.Item1; lag < range.Item2; lag += 1)
                {
                    Matrix<double> sum = DenseMatrix.Create(numSensors, numSensors, 0.0);

                    for (int t = 0; t < numTimeSteps - lag; t++)
                    {
                        MathNetVectorDouble vec1 = dataMatrix.Column(t);
                        MathNetVectorDouble vec2 = dataMatrix.Column(t + lag);

                        // Calculate the outer product and accumulate the sum
                        sum += vec1.OuterProduct(vec2);
                    }

                    // Calculate the covariance matrix for the current lag by dividing the sum by the number of time steps
                    lock (covarianceMatrices)
                    {
                        covarianceMatrices[lag] = sum / (numTimeSteps - lag);
                    }
                }
            });


            return covarianceMatrices;
        }


        static public Matrix<double>[] ConstructCovarianceMatricesRafic(double[,] sensorData, int maxLag)
        {
            int numSensors = sensorData.GetLength(1);
            int numTimeSteps = sensorData.GetLength(0);

            // Convert sensorData to a MathNet Matrix
            Matrix<double> dataMatrix = DenseMatrix.OfArray(sensorData);

            // Calculate the covariance matrices for different time steps

            Matrix<double>[] covarianceMatrices = new Matrix<double>[maxLag];


            //ParallelOptions parallelOptions = new ParallelOptions
            //{
            //    MaxDegreeOfParallelism = 100 // Set the maximum number of parallel tasks to 4, for example
            //};

            //int start = 0;
            //int end = maxLag;
            //int step = maxLag / 4; // Change this value to adjust the step.

          //  var partitioner = Partitioner.Create(start, end, step);

            //// Use Parallel.For with the specified options
            //Parallel.ForEach(partitioner, parallelOptions, range =>
            //{
            //    for (int lag = range.Item1; lag < range.Item2; lag += 1)
            for (int lag = 0; lag < maxLag; lag++)
            {

                //  for (int lag = 0; lag <= maxLag; lag++)

                Matrix<double> sum = DenseMatrix.Create(numSensors, numSensors, 0.0);

                for (int t = 0; t < numTimeSteps - lag; t++)
                {
                    MathNetVectorDouble vec1 = dataMatrix.Row(t);
                    MathNetVectorDouble vec2 = dataMatrix.Row(t + lag);

                    // Calculate the outer product and accumulate the sum
                    sum += vec1.OuterProduct(vec2);
                }


                // Calculate the covariance matrix for the current lag by dividing the sum by the number of time steps
                covarianceMatrices[lag] = sum / (numTimeSteps - lag);
            }

            //       });

            return covarianceMatrices;
        }

        // R_is = np.array([1 / (ndat - _s) * (Yy[:, : ndat - _s]@Yy[:, _s:].T) for _s in range(br * 2 + 1)])

        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sensorData">the rows correspond to the sensors and the columns to time steps</param>
        /// <param name="maxLag"></param>
        /// <returns></returns>
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

        static public double[][,] ConstructCovarianceMatrices3(double[,] sensorData, int maxLag)
        {
            int numSensors = sensorData.GetLength(0);





            double[][,] covarianceMatrices = new double[maxLag + 1][,];

            for (int s = 0; s < maxLag; s++)
            {

                covarianceMatrices[s] =Accord.Math.Matrix.DotWithTransposed(sensorData.Get(0, sensorData.Rows() -1, 0, sensorData.Columns() - s), sensorData.Get(0, sensorData.Rows() -1, s, sensorData.Columns())).Divide((double)(sensorData.Columns() - s));
            }


            return covarianceMatrices;
        }

        static public double[][,] ConstructCovarianceMatrices4(double[,] sensorData, int maxLag)
        {
            int chunkSize = 100; // Adjust this value based on available memory and performance
            int numChunks = (sensorData.Columns() + chunkSize - 1) / chunkSize;

            double[][,] covarianceMatrices = new double[maxLag + 1][,];

            // Initialize covarianceMatrices with zero matrices
            for (int s = 0; s <= maxLag; s++)
            {
                covarianceMatrices[s] = new double[sensorData.Columns(), sensorData.Columns()];
            }

            for (int chunk = 0; chunk < numChunks; chunk++)
            {
                int startColumn = chunk * chunkSize;
                int endColumn = Math.Min(startColumn + chunkSize, sensorData.Columns());

                for (int s = 0; s <= maxLag; s++)
                {
                    int maxColumn = sensorData.Columns() - s;
                    if (startColumn < maxColumn)
                    {
                        int chunkEndColumn = Math.Min(endColumn, maxColumn);
                        var chunk1 = sensorData.Get(0, sensorData.Rows() - 1, startColumn, chunkEndColumn);
                        var chunk2 = sensorData.Get(0, sensorData.Rows(), s + startColumn, s + chunkEndColumn);

                        int normalizationFactor = chunkEndColumn - startColumn;
                        var chunkCovarianceMatrix = Accord.Math.Matrix.DotWithTransposed(chunk1, chunk2)
                            .Divide((double)normalizationFactor);

                        // Aggregate the results (sum) to obtain the final covariance matrices
                        for (int i = 0; i < covarianceMatrices[s].GetLength(0); i++)
                        {
                            for (int j = 0; j < covarianceMatrices[s].GetLength(1); j++)
                            {
                                covarianceMatrices[s][i, j] += chunkCovarianceMatrix[i, j];
                            }
                        }
                    }
                }
            }

            return covarianceMatrices;
        }

        static Matrix<double> ConstructBlockHankelMatrix(Matrix<double>[] covarianceMatrices, int blockSize, int numCols)
        {
            int numCovarianceMatrices = covarianceMatrices.Length;

            int numRows = blockSize;
            //  int numCols = blockSize;// numCovarianceMatrices - blockSize + 1;

            if (blockSize < 1 || blockSize >= numCovarianceMatrices)
            {
                throw new ArgumentException("Invalid block size. Block size must be between 1 and the number of covariance matrices - 1.");
            }

            int sensorCount = covarianceMatrices[0].RowCount;

            Matrix<double> blockHankelMatrix = DenseMatrix.Create(numRows * sensorCount, numCols * sensorCount, 0.0);

            for (int i = 0; i < blockSize; i++)
            {
                for (int j = 0; j < numCols; j++)
                {
                    int rowIndex = i * sensorCount;
                    int colIndex = j * sensorCount;
                    blockHankelMatrix.SetSubMatrix(rowIndex, colIndex, covarianceMatrices[i + j]);
                }
            }

            return blockHankelMatrix;
        }

        static int EstimateModelOrder(MathNetVectorDouble singularValues, double threshold)
        {
            // Implement a method to estimate the model order based on singular values in S.
            // This can be done by finding a clear "gap" between the singular values or using a model order selection criterion.
            // Return the estimated model order.
            int modelOrder = 0;

            for (int i = 0; i < singularValues.Count - 1; i++)
            {
                double gap = singularValues[i] / singularValues[i + 1];

                if (gap > threshold)
                {
                    modelOrder = i + 1;
                    break;
                }
            }

            return modelOrder;
        }


        public static double[,] PreprocessData(double[,] sensorData, int medianFilterWindowSize = 0)
        {
            int numRows = sensorData.GetLength(0);
            int numCols = sensorData.GetLength(1);

            double[,] preprocessedData = new double[numRows, numCols];

            // 2.1 Remove mean (trend) from each column of the sensor data
            for (int col = 0; col < numCols; col++)
            {
                // Calculate the mean of the current column
                double[] columnData = new double[numRows];
                for (int row = 0; row < numRows; row++)
                {
                    columnData[row] = sensorData[row, col];
                }
                double mean = Statistics.Mean(columnData);

                // Remove the mean from each element in the column
                for (int row = 0; row < numRows; row++)
                {
                    preprocessedData[row, col] = sensorData[row, col] - mean;
                }
            }

            if (medianFilterWindowSize > 0)
            {

                // 2.2 Apply a median filter to each column of the sensor data to reduce the effect of outliers
                for (int col = 0; col < numCols; col++)
                {
                    double[] columnData = new double[numRows];
                    for (int row = 0; row < numRows; row++)
                    {
                        columnData[row] = preprocessedData[row, col];
                    }

                    double[] filteredColumnData = ApplyMedianFilter(columnData, medianFilterWindowSize);

                    for (int row = 0; row < numRows; row++)
                    {
                        preprocessedData[row, col] = filteredColumnData[row];
                    }
                }
            }

            return preprocessedData;
        }

        public static double[] ApplyMedianFilter(double[] data, int windowSize)
        {
            int n = data.Length;
            double[] filteredData = new double[n];
            int halfWindowSize = windowSize / 2;

            for (int i = 0; i < n; i++)
            {
                int start = Math.Max(0, i - halfWindowSize);
                int end = Math.Min(n - 1, i + halfWindowSize);
                int windowLength = end - start + 1;

                double[] windowData = new double[windowLength];
                Array.Copy(data, start, windowData, 0, windowLength);

                filteredData[i] = Statistics.Median(windowData);
            }

            return filteredData;
        }

        public static void SaveMatricesToDisk(Matrix<double>[] matrices, string filePath)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Create))
            using (StreamWriter writer = new StreamWriter(stream))
            {
                string json = JsonSerializer.Serialize(matrices);
                writer.Write(json);
            }
        }

        public static Matrix<double>[] LoadMatricesFromDisk(string filePath)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open))
            using (StreamReader reader = new StreamReader(stream))
            {
                string json = reader.ReadToEnd();
                return JsonSerializer.Deserialize<Matrix<double>[]>(json);
            }
        }



        public static double[,] Detrend(double[,] inputSignals)
        {
            int numSensors = inputSignals.GetLength(0);
            int numSamples = inputSignals.GetLength(1);

            // Generate an array of x values (0, 1, 2, ..., numSamples-1)
            double[] x = new double[numSamples];
            for (int i = 0; i < numSamples; i++)
            {
                x[i] = i;
            }

            // Create a new array to store the detrended signals
            double[,] detrendedSignals = new double[numSensors, numSamples];

            // Loop through each sensor and detrend its signal
            for (int sensorIndex = 0; sensorIndex < numSensors; sensorIndex++)
            {
                // Extract the signal of the current sensor
                double[] currentSignal = new double[numSamples];
                for (int sampleIndex = 0; sampleIndex < numSamples; sampleIndex++)
                {
                    currentSignal[sampleIndex] = inputSignals[sensorIndex, sampleIndex];
                }

                // Perform linear regression to find the coefficients (slope and intercept) of the linear trend
                (double, double) trendCoefficients = Fit.Line(x, currentSignal);
                double slope = trendCoefficients.Item2;
                double intercept = trendCoefficients.Item1;

                // Subtract the linear trend from the current sensor's signal
                for (int sampleIndex = 0; sampleIndex < numSamples; sampleIndex++)
                {
                    detrendedSignals[sensorIndex, sampleIndex] = currentSignal[sampleIndex] - (slope * x[sampleIndex] + intercept);
                }
            }

            return detrendedSignals;
        }


    }
}
