using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HDF5CSharp;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using System.Linq;
using Accord.Math;
using static Community.CsharpSqlite.Sqlite3;
using System.Text;
using DiffSharp.Util;
using Radzen.Blazor.Rendering;
using AliantWebS.Data;

namespace AliantWebS.Pages.AIPages
{


    public class PredictW
    {
        static public Structural.Structure structuralStructure;
        static public Structural.SimulationParameters simPa;

     



        static public List<float> Predictt(string h5File, string modelFile, int[] sensedNodes, int elementIndex)
        {

            //  Tensor inputt = AI.Predict.GetInputTensor(@"C:\Users\rafic\Desktop\data\23000-5s-1000hzIntacte.H5", new long[] { 3, 6, 10, 12 }).Item1;


            Tensor inputt = AI.Predict.GetInputTensor(h5File, sensedNodes, (new int[] { elementIndex })).Item1;


            string checkpointfilename = modelFile;

            if (!System.IO.File.Exists(checkpointfilename))
            {
                return new List<float>() { 0 };
            }
            else
            {
                AppData.network = new CNNMaquette("rew", 0, true, 1);



                AppData.network = ((CNNMaquette)(AppData.network)).load(checkpointfilename);


                ((CNNMaquette)AppData.network).eval();
                var result = ((CNNMaquette)AppData.network).forward(inputt);

                var yy = result.data<float>().ToArray().ToList();


                return yy;
            }



        }
        public delegate Task TestEventHandler(object sender, params string[] messages);
        public static event TestEventHandler EpochEnding;


        static public async Task Train(int epochNum, int batchSize, double learningRate, double dropoutRate,
            bool batchNormalization, double testSetPercentage, bool shuffle, string TrainingSetPath, string NNModelfilePath,
            int[] sensedNodes, int elementIndex, bool onlineSimulatiom)
        {
            int localEpochNum;

            var generator = torch.random.manual_seed(0);

            AppData.network = new CNNMaquette("rew", dropoutRate, batchNormalization, 1);

            if (File.Exists(NNModelfilePath))
            {
                //using (FileStream fs = File.Create(NNModelfilePath))
                //{ 
                //    BinaryWriter writer = new BinaryWriter(fs);
                //writer.Write(network.state_dict().Count);
                //foreach (var kvp in network.state_dict())
                //{
                //    writer.Write(kvp.Key);

                //        Tensor ts;
                //        ts.to

                //    writer.Write((kvp.Value);
                //}
                //writer.Flush();
            //}
                //using (var stream = File.Open(NNModelfilePath, FileMode.Open))
                //{
                //    using (var reader = new BinaryReader(stream, Encoding.UTF8, false))

                //    {
                //        Dictionary<string,Tensor> d = reader.Read()

                //        TorchSharp.torch.nn.Module.save_state_dict(writer, network.state_dict());

                //        TorchSharp.torch.nn.Module.lo
                //                    }
                //}


                ((CNNMaquette)AppData.network).load(NNModelfilePath);
            }

            var optimizer = torch.optim.Adam(((CNNMaquette)AppData.network).parameters(), lr: learningRate);
            var loss = nn.MSELoss();

            double lossMin = 1000;

            int megaEpochNum;

            //@"C:\Users\rafic\Desktop\AliantWebS\AliantWebS\AliantWebS\TrainingSets\3f91a7fc-c408-4afb-bdeb-fdd3472c8f79\myultimatetest.H5"
            (Tensor, Tensor) data;
            if (onlineSimulatiom)
            {
                megaEpochNum = epochNum;
                localEpochNum = 1;
            }
            else
            {
                megaEpochNum = 1;
                localEpochNum = epochNum;

            }


            for (int m = 0; m < megaEpochNum; m++)
            {

                if (onlineSimulatiom)
                {
                    data = await Structural.Simulation.OnlineSimulate(structuralStructure, simulationParameters: simPa, sensedNodes: sensedNodes, (new long[] { elementIndex }));

                }
                else
                {
                    data = AI.Predict.GetInputTensor(TrainingSetPath, sensedNodes, (new int[] { elementIndex }));
                }




                long datasetLength = data.Item2.size(0);

                Tensor permutation = torch.randperm(datasetLength, generator: generator);

                Tensor inputTrain = data.Item1[permutation.slice(0, 0, (long)(datasetLength * (1 - testSetPercentage)), 1)];
                Tensor inputTest = data.Item1[permutation.slice(0, (long)(datasetLength * (1 - testSetPercentage)), datasetLength, 1)];

                Tensor outputTrain = data.Item2[permutation.slice(0, 0, (long)(datasetLength * (1 - testSetPercentage)), 1)];
                Tensor outputTest = data.Item2[permutation.slice(0, (long)(datasetLength * (1 - testSetPercentage)), datasetLength, 1)];




                long batchNumberTrain = outputTrain.size(0) / batchSize;
                long batchNumberTest = outputTest.size(0) / batchSize;


                // LoadModelAndOptimizerCheckpoint(checkpointfilename, network, optimizer)




                ((CNNMaquette)AppData.network).train();
                for (int i = 0; i < localEpochNum; i++)
                {
                    using (var d = torch.NewDisposeScope())
                    {

                        //training
                        permutation = torch.randperm(outputTrain.size(0), generator: generator);


                        Tensor AverageTrainLoss = torch.tensor(0);
                        for (int j = 0; j < batchNumberTrain; j++)
                        {
                            Tensor inputt = inputTrain[permutation.slice(0, j, j + batchSize, 1)];
                            Tensor outputt = outputTrain[permutation.slice(0, j, j + batchSize, 1)];

                            var preds = ((CNNMaquette)AppData.network).forward(inputt);
                            var los = loss.forward(preds, outputt);
                            AverageTrainLoss += los;
                            optimizer.zero_grad();
                            los.backward();
                            optimizer.step();

                            //   EpochEnding?.Invoke(null, (j).ToString(), los.item<float>().ToString(),"0");



                        }
                        AverageTrainLoss = AverageTrainLoss / batchNumberTrain;


                        //testing
                        permutation = torch.randperm(outputTest.size(0), generator: generator);
                        ((CNNMaquette)AppData.network).eval();

                        Tensor AverageTestLoss = torch.tensor(0);
                        using (torch.no_grad())
                        {
                            for (int j = 0; j < batchNumberTest; j++)
                            {
                                Tensor inputt = inputTest[permutation.slice(0, j, j + batchSize, 1)];
                                Tensor outputt = outputTest[permutation.slice(0, j, j + batchSize, 1)];

                                var preds = ((CNNMaquette)AppData.network).forward(inputt);
                                var los = loss.forward(preds, outputt);
                                AverageTestLoss += los;
                            }
                        }
                        AverageTestLoss = AverageTestLoss / batchNumberTest;

                        EpochEnding?.Invoke(null, (m + i).ToString(), AverageTrainLoss.item<float>().ToString(), AverageTestLoss.item<float>().ToString());

                        if (Math.Abs(testSetPercentage) <  0.001)
                        {
                            if (AverageTrainLoss.item<float>() < lossMin)
                            {
                                //using (var stream = File.Open(NNModelfilePath, FileMode.OpenOrCreate))
                                //{
                                //    using (var writer = new BinaryWriter(stream, Encoding.UTF8, false))
                                //    {
                                //        TorchSharp.torch.nn.Module.save_state_dict(writer, network.state_dict());


                                //    }
                                //}

                                ((CNNMaquette)AppData.network).save(NNModelfilePath);

                                lossMin = AverageTrainLoss.item<float>();
                            }
                        }
                        else
                        {
                            if (AverageTestLoss.item<float>() < lossMin)
                            {
                                ((CNNMaquette)AppData.network).save(NNModelfilePath);
                                lossMin = AverageTestLoss.item<float>();
                            }
                        }



                        d.DisposeEverything();
                    }
                }

            }




        }



    //    static public async Task Train2(int epochNum, int batchSize, double learningRate, double dropoutRate,
    //bool batchNormalization, double testSetPercentage, bool shuffle, string TrainingSetPath, string TestSetPath, string NNModelfilePath,
    //long[] sensedNodes, int elementIndex, bool onlineSimulatiom)
    //    {
    //        int localEpochNum;

    //        var generator = torch.random.manual_seed(0);

    //        var network = new FCNN("rew", dropoutRate, batchNormalization, 1);

    //        //if (File.Exists(NNModelfilePath))
    //        //{
    //        //    network.load(NNModelfilePath);
    //        //}

    //        var optimizer = torch.optim.Adam(network.parameters(), lr: learningRate);
    //        var loss = nn.MSELoss();

    //        double lossMin = 1000;

    //        int megaEpochNum;

    //        //@"C:\Users\rafic\Desktop\AliantWebS\AliantWebS\AliantWebS\TrainingSets\3f91a7fc-c408-4afb-bdeb-fdd3472c8f79\myultimatetest.H5"
    //        (Tensor, Tensor) dataTraining;
    //        (Tensor, Tensor) dataTest;
    //        if (onlineSimulatiom)
    //        {
    //            megaEpochNum = epochNum;
    //            localEpochNum = 1;
    //        }
    //        else
    //        {
    //            megaEpochNum = 1;
    //            localEpochNum = epochNum;

    //        }


    //        for (int m = 0; m < megaEpochNum; m++)
    //        {

    //            //if (onlineSimulatiom)
    //            //{
    //            //    dataTraining = await Structural.Simulation.OnlineSimulate(structuralStructure, simulationParameters: simPa, sensedNodes: sensedNodes, (new long[] { elementIndex }));

    //            //}
    //            //else
    //            //{

    //            //var v = AI.Helpers.CSVToArray(TrainingSetPath,',',true);
    //            //var item1 = v.GetColumn(0);
    //            //var indexes = Enumerable.Range(1, v.Columns()-1).ToArray();
    //            //var item2 = v.GetColumns(indexes);
    //            dataTraining = AI.Helpers.CSVToArray(TrainingSetPath, ',', true); //(torch.tensor(item2, ScalarType.Float32), torch.tensor(item1, ScalarType.Float32));
    //            dataTest = AI.Helpers.CSVToArray(TestSetPath, ',', true);


    //            //     }




    //            long datasetLength = dataTraining.Item2.size(0);

    //            Tensor permutation = torch.randperm(datasetLength, generator: generator);

    //            Tensor inputTrain = dataTraining.Item1;// [permutation.slice(0, 0, (long)(datasetLength * (1 - testSetPercentage)), 1)];
    //            Tensor inputTest = dataTest.Item1;// [permutation.slice(0, (long)(datasetLength * (1 - testSetPercentage)), datasetLength, 1)];

    //            Tensor outputTrain = dataTraining.Item2;// [permutation.slice(0, 0, (long)(datasetLength * (1 - testSetPercentage)), 1)];
    //            Tensor outputTest = dataTest.Item2;// [permutation.slice(0, (long)(datasetLength * (1 - testSetPercentage)), datasetLength, 1)];




    //            long batchNumberTrain = outputTrain.size(0) / batchSize;
    //            long batchNumberTest = outputTest.size(0) / batchSize;



    //            network.train();
    //            for (int i = 0; i < localEpochNum; i++)
    //            {
    //                using (var d = torch.NewDisposeScope())
    //                {

    //                    //training
    //                    permutation = torch.randperm(outputTrain.size(0), generator: generator);


    //                    Tensor AverageTrainLoss = torch.tensor(0);
    //                    for (int j = 0; j < batchNumberTrain; j++)
    //                    {
    //                        Tensor inputt = inputTrain[permutation.slice(0, j, j + batchSize, 1)];
    //                        Tensor outputt = outputTrain[permutation.slice(0, j, j + batchSize, 1)];

    //                        var preds = network.forward(inputt);
    //                        var los = loss.forward(preds, outputt);
    //                        AverageTrainLoss += los;
    //                        optimizer.zero_grad();
    //                        los.backward();
    //                        optimizer.step();

    //                        //   EpochEnding?.Invoke(null, (j).ToString(), los.item<float>().ToString(),"0");



    //                    }
    //                    AverageTrainLoss = AverageTrainLoss / batchNumberTrain;


    //                    //testing
    //                    permutation = torch.randperm(outputTest.size(0), generator: generator);
    //                    network.eval();

    //                    Tensor AverageTestLoss = torch.tensor(0);
    //                    using (torch.no_grad())
    //                    {
    //                        for (int j = 0; j < batchNumberTest; j++)
    //                        {
    //                            Tensor inputt = inputTest[permutation.slice(0, j, j + batchSize, 1)];
    //                            Tensor outputt = outputTest[permutation.slice(0, j, j + batchSize, 1)];

    //                            var preds = network.forward(inputt);
    //                            var los = loss.forward(preds, outputt);
    //                            AverageTestLoss += los;
    //                        }
    //                    }
    //                    AverageTestLoss = AverageTestLoss / batchNumberTest;

    //                    EpochEnding?.Invoke(null, (m + i).ToString(), AverageTrainLoss.item<float>().ToString(), AverageTestLoss.item<float>().ToString());

          
    //                        if (AverageTestLoss.item<float>() < lossMin)
    //                        {
    //                      //     network.save(NNModelfilePath);

    //                            long TestSetSize = outputTest.size(0);
    //                            List<(float,float)> result = new List<(float,float)>();
    //                            for (int ii = 0; ii < TestSetSize; ii++)
    //                            {
    //                                var preds = network.forward(inputTest[ii]).item<float>();
    //                            result.Add((outputTest[ii].item<float>(), preds));
    //                            }

    //                           // string csv = String.Join(",", myList.Select(x => x.ToString()).ToArray());

    //                            await File.WriteAllLinesAsync(@"130preds.csv", result.Select(x => (x.Item1.ToString() +"," + x.Item2.ToString())));


    //                        }
                



    //                    d.DisposeEverything();
    //                }
    //            }

    //        }




    //    }






    }
}
