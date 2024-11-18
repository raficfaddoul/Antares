using System.Collections.Generic;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

//namespace AliantWebS.Pages.AIPages
//{
//    public class FCNN : Module
//    {

//        const int numberOfFulltConnectedLayers = 3;

//        bool bn;
//        double dropout;

//        ModuleList mxps = nn.ModuleList();

//        ModuleList modules = nn.ModuleList();
//        ModuleList bns = nn.ModuleList();


  
//        int[,] fcParams = new int[numberOfFulltConnectedLayers, 2];
//        int[] bnPaams = new int[numberOfFulltConnectedLayers];



//        public FCNN(string name, double dropout, bool bn, int outputSize) : base(name)
//        {

//            this.bn = bn;
//            this.dropout = dropout;


//            fcParams[0, 0] = 2; fcParams[0, 1] = 18;
//            fcParams[1, 0] = 18; fcParams[1, 1] = 9;
//            fcParams[2, 0] = 9; fcParams[2, 1] = outputSize;





//            for (int i = 0; i < numberOfFulltConnectedLayers; i++)
//            {
//                modules.append(Linear(inputSize: fcParams[i, 0], outputSize: fcParams[i, 1]));
//                bns.append(BatchNorm1d(fcParams[i, 1]));
//            }

//            RegisterComponents();

//        }

//        public override Tensor forward(Tensor r)
//        {

//            for (int i = 0; i < numberOfFulltConnectedLayers - 1; i++)
//            {
//                r = modules[i].forward(r);
//                if (bn) r = bns[i].forward(r);
//                r = nn.functional.dropout(r, dropout, true); //r = Dropout(dropout).forward(r);
//                r = nn.functional.relu(r); //ReLU().forward(r);
//            }

//            r = modules[numberOfFulltConnectedLayers - 1].forward(r);

//            return r;

//        }




//    }
//}
