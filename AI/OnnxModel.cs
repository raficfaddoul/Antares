using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace AI
{
    /// <summary>
    /// https://netron.app/
    /// </summary>
    public class ModelInput
    {
        [VectorType(1*2*4*996)]
        [ColumnName("actual_input_1")]
        public float[] Features { get; set; }
    }

    public class ModelOutput
    {
        [VectorType(1)]
        [ColumnName("output1")]
        public float[] output { get; set; }
        //[VectorType(1)]
        //[ColumnName("34")]
        //public float[] state { get; set; }
    }
    class OnnxModel
    {
        private readonly string modelPath;
        private readonly MLContext mlContext;
        Microsoft.ML.Transforms.Onnx.OnnxScoringEstimator? pipeline;


        public OnnxModel(string modelPath)
        {
            this.modelPath = modelPath;
            mlContext = new MLContext();

            var inputColumnNames = new[] { "actual_input_1" };
            var outputColumnNames = new[] { "output1" };
            pipeline = mlContext.Transforms.ApplyOnnxModel(modelFile: modelPath, outputColumnNames: outputColumnNames, inputColumnNames: inputColumnNames);
        }

        public IEnumerable<float> Score(float[] vvv)
        {
            List<ModelInput> input = new List<ModelInput>();
            input.Add(new ModelInput()
            {
                Features = vvv

            });

            var dataView = mlContext.Data.LoadFromEnumerable(input);
            var transformedValues = pipeline.Fit(dataView).Transform(dataView);
            var output = mlContext.Data.CreateEnumerable<ModelOutput>(transformedValues, reuseRowObject: false);

            return output.Single().output;

 
        }
    }
}