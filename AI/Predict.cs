using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using HDF5CSharp;
using Structural;



namespace AI
{
    public class Predict
    {
        public Predict()
        {

        }

        public static void DoPrediction()
        {


            Tensor inputt = GetInputTensor(@"C:\Users\rafic\Desktop\data\23000-5s-1000hzIntacte.H5", new int[] { 3, 6, 10, 12 }, (new int[] { 0 })).Item1;
                        
            var vv = inputt.flatten().data<double>().ToArray();

            var vvv = vv.Select(x => (float)x).ToArray();  

            // Load trained model

            var modelScorer = new OnnxModel(@"C:\Users\rafic\Desktop\data\CNNMaquette.onnx");

            var pred = modelScorer.Score(vvv);
   
        }

        public static (Tensor, Tensor) GetInputTensor(string fileName, int[] sensedNodes, int[] elementsOfIntrest)
        {
            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException(fileName);
            }

            long fileId = Hdf5.OpenFile(fileName,true);
            var FFTMagnitudeH = Hdf5.ReadDataset<float>(fileId, "/FFTMagnitudeTrain");
            var phaseH = Hdf5.ReadDataset<float>(fileId, "/FFTPhaseTrain");
            var deltaIH = Hdf5.ReadDataset<float>(fileId, "/ETrain");



            Structural.SimulationParameters simParameters = new Structural.SimulationParameters();


            simParameters = Hdf5.ReadObject(fileId, simParameters, "simulationParameters");





            float[,] FFTMagnitude = (float[,])FFTMagnitudeH.result;
            float[,] FFTPhase = (float[,])phaseH.result;
            float[,] deltaI = null;
            if (deltaIH.success)
            {
                deltaI = (float[,])deltaIH.result;
            }

            int maximumFrequency = simParameters.SamplingFrequency * simParameters.Duree / 2;

            long longLength = FFTMagnitude.GetLongLength(1);

            long YNum = longLength/ maximumFrequency; 

            //long[] dd = (new long[] { 3, 6, 10, 12 });
            var nt = torch.tensor(sensedNodes);

            var ch1 = torch.tensor(FFTMagnitude, ScalarType.Float32).reshape(-1, 1, YNum, maximumFrequency)[TensorIndex.Colon, TensorIndex.Colon, TensorIndex.Tensor(nt), TensorIndex.Slice(0, 996)];
            var ch2 = torch.tensor(FFTPhase, ScalarType.Float32).reshape(-1, 1, YNum, maximumFrequency)[TensorIndex.Colon, TensorIndex.Colon, TensorIndex.Tensor(nt), TensorIndex.Slice(0, 996)];

            List<Tensor> listOfChannels = new List<Tensor>() { ch1, ch2 };


       //     long[] elementsOfIntrest = (new long[] { 0 });// element 0

            Tensor output = null;
            if (deltaIH.success)
            {
                output = torch.tensor(deltaI, ScalarType.Float32)[TensorIndex.Colon, TensorIndex.Tensor(elementsOfIntrest)].reshape(-1, 1);

              //  output = torch.tensor(deltaI, ScalarType.Float32);

                output = output / 3.2e+10;
            }


            return (torch.cat(listOfChannels, 1), output);
        }




    }
}
