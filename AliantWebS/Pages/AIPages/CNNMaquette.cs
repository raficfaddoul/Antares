using System.Collections.Generic;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace AliantWebS.Pages.AIPages
{
    public class CNNMaquette : nn.Module<Tensor, Tensor>
    {

        const int numberOfConvolutionalLayers = 6;
        const int numberOfFulltConnectedLayers = 3;

        bool bn;
        double dropout;

        ModuleList<MaxPool2d> mxps = new ModuleList<MaxPool2d>();

        //ModuleList<Conv2d> Conv2dmodules = new ModuleList<Conv2d>();
         nn.Module<Tensor, Tensor> c2d1 = Conv2d(2, 32, 3, 1, 1);

         nn.Module<Tensor, Tensor> c2d2 = Conv2d(32, 32, 3, 1, 1);
         nn.Module<Tensor, Tensor> c2d3 = Conv2d(32, 64, 3, 1, 1);
         nn.Module<Tensor, Tensor> c2d4 = Conv2d(64, 64, 3, 1, 1);
         nn.Module<Tensor, Tensor> c2d5 = Conv2d(64, 64, 3, 1, 1);
         nn.Module<Tensor, Tensor> c2d6 = Conv2d(64, 64, 3, 1, 1);

         nn.Module<Tensor, Tensor> bn2d1 = BatchNorm2d(32);
         nn.Module<Tensor, Tensor> bn2d2 = BatchNorm2d(32);
         nn.Module<Tensor, Tensor> bn2d3 = BatchNorm2d(64);
         nn.Module<Tensor, Tensor> bn2d4 = BatchNorm2d(64);
         nn.Module<Tensor, Tensor> bn2d5 = BatchNorm2d(64);
         nn.Module<Tensor, Tensor> bn2d6 = BatchNorm2d(64);





        //ModuleList<Linear> LineaRmodules = new ModuleList<Linear>();
         nn.Module<Tensor, Tensor> lm1 = Linear(7936, 1024);
         nn.Module<Tensor, Tensor> lm2 = Linear(1024, 60);
         nn.Module<Tensor, Tensor> lm3 = Linear(60, 1);

         nn.Module<Tensor, Tensor> bn1d1 = BatchNorm1d(1024);
         nn.Module<Tensor, Tensor> bn1d2 = BatchNorm1d(60);

        Sequential seq; 


        //ModuleList<BatchNorm2d> bns2d = new ModuleList<BatchNorm2d>();

        //ModuleList<BatchNorm1d> bns1d = new ModuleList<BatchNorm1d>();

        int[,] convParams = new int[numberOfConvolutionalLayers, 5];
        int[,] maxPoolParams = new int[numberOfConvolutionalLayers, 2];
        int[,] fcParams = new int[numberOfFulltConnectedLayers, 2];
        //   int[] bnPaams = new int[numberOfConvolutionalLayers + numberOfFulltConnectedLayers];



        public CNNMaquette(string name, double dropout, bool bn, int outputSize) : base(name)
        {

            this.bn = bn;
            this.dropout = dropout;

            convParams[0, 0] = 2; convParams[0, 1] = 32; convParams[0, 2] = 3; convParams[0, 3] = 1; convParams[0, 4] = 1;
            convParams[1, 0] = 32; convParams[1, 1] = 32; convParams[1, 2] = 3; convParams[1, 3] = 1; convParams[1, 4] = 1;
            convParams[2, 0] = 32; convParams[2, 1] = 64; convParams[2, 2] = 3; convParams[2, 3] = 1; convParams[2, 4] = 1;
            convParams[3, 0] = 64; convParams[3, 1] = 64; convParams[3, 2] = 3; convParams[3, 3] = 1; convParams[3, 4] = 1;
            convParams[4, 0] = 64; convParams[4, 1] = 64; convParams[4, 2] = 3; convParams[4, 3] = 1; convParams[4, 4] = 1;
            convParams[5, 0] = 64; convParams[5, 1] = 64; convParams[5, 2] = 3; convParams[5, 3] = 1; convParams[5, 4] = 1;

            maxPoolParams[0, 0] = 0; maxPoolParams[0, 1] = 0;
            maxPoolParams[1, 0] = 1; maxPoolParams[1, 1] = 2;
            maxPoolParams[2, 0] = 0; maxPoolParams[2, 1] = 0;
            maxPoolParams[3, 0] = 1; maxPoolParams[3, 1] = 2;
            maxPoolParams[4, 0] = 1; maxPoolParams[4, 1] = 2;
            maxPoolParams[5, 0] = 2; maxPoolParams[5, 1] = 2;

            fcParams[0, 0] = 7936; fcParams[0, 1] = 1024;
            fcParams[1, 0] = 1024; fcParams[1, 1] = 60;
            fcParams[2, 0] = 60; fcParams[2, 1] = outputSize;

            seq = Sequential(("c2d1", c2d1), ("bn2d1", bn2d1), ("c2d2", c2d2), ("bn2d2", bn2d2), ("c2d3", c2d3), ("bn2d3", bn2d3),
            ("c2d4", c2d4), ("bn2d4", bn2d4), ("c2d5", c2d5), ("bn2d5", bn2d5), ("c2d6", c2d6), ("bn2d6", bn2d6),
            ("lm1", lm1), ("bn1d1", bn1d1), ("lm2", lm2), ("bn1d2", bn1d2), ("lm3", lm3));

            //for (int i = 0; i < numberOfConvolutionalLayers; i++)
            //{
            //    Conv2dmodules.append(Conv2d(inputChannel: convParams[i, 0], outputChannel: convParams[i, 1],
            //        kernelSize: convParams[i, 2], stride: convParams[i, 3], padding: convParams[i, 4]));
            //    bns2d.append(BatchNorm2d(convParams[i, 1]));
            //}

            string s = c2d1.GetName();

            for (int i = 0; i < numberOfConvolutionalLayers; i++)
            {
                mxps.Add(MaxPool2d((maxPoolParams[i, 0], maxPoolParams[i, 1])));
            }

            //for (int i = 0; i < numberOfFulltConnectedLayers; i++)
            //{
            //    LineaRmodules.append(Linear(inputSize: fcParams[i, 0], outputSize: fcParams[i, 1]));
            //    bns1d.append(BatchNorm1d(fcParams[i, 1]));
            //}

            RegisterComponents();

        }

        public override Tensor forward(Tensor r)
        {


            r = seq[0].forward(r);
            if (bn) r = seq[1].forward(r);
            r = nn.functional.dropout(r, dropout, true);// Dropout(dropout).forward(r);
            r = nn.functional.relu(r);// ReLU().forward(r);
            if (maxPoolParams[0, 0] != 0)
            {
                r = mxps[0].forward(r);
            }

            r = seq[2].forward(r);
            if (bn) r = seq[3].forward(r);
            r = nn.functional.dropout(r, dropout, true);// Dropout(dropout).forward(r);
            r = nn.functional.relu(r);// ReLU().forward(r);
            if (maxPoolParams[1, 0] != 0)
            {
                r = mxps[1].forward(r);
            }

            r = seq[4].forward(r);
            if (bn) r = seq[5].forward(r);
            r = nn.functional.dropout(r, dropout, true);// Dropout(dropout).forward(r);
            r = nn.functional.relu(r);// ReLU().forward(r);
            if (maxPoolParams[2, 0] != 0)
            {
                r = mxps[2].forward(r);
            }

            r = seq[6].forward(r);
            if (bn) r = seq[7].forward(r);
            r = nn.functional.dropout(r, dropout, true);// Dropout(dropout).forward(r);
            r = nn.functional.relu(r);// ReLU().forward(r);
            if (maxPoolParams[3, 0] != 0)
            {
                r = mxps[3].forward(r);
            }

            r = seq[8].forward(r);
            if (bn) r = seq[9].forward(r);
            r = nn.functional.dropout(r, dropout, true);// Dropout(dropout).forward(r);
            r = nn.functional.relu(r);// ReLU().forward(r);
            if (maxPoolParams[4, 0] != 0)
            {
                r = mxps[4].forward(r);
            }

            r = seq[10].forward(r);
            if (bn) r = seq[11].forward(r);
            r = nn.functional.dropout(r, dropout, true);// Dropout(dropout).forward(r);
            r = nn.functional.relu(r);// ReLU().forward(r);
            if (maxPoolParams[5, 0] != 0)
            {
                r = mxps[5].forward(r);
            }


            r = Flatten(startDim: 1).forward(r);


            r = seq[12].forward(r);
            if (bn) r = seq[13].forward(r);
            r = nn.functional.dropout(r, dropout, true); //r = Dropout(dropout).forward(r);
            r = nn.functional.relu(r); //ReLU().forward(r);

            r = seq[14].forward(r);
            if (bn) r = seq[15].forward(r);
            r = nn.functional.dropout(r, dropout, true); //r = Dropout(dropout).forward(r);
            r = nn.functional.relu(r); //ReLU().forward(r);




            r = seq[16].forward(r);

            return r;

        }




    }
}





//    public class CNNMaquette : nn.Module<Tensor,Tensor>
//    {

//        const int numberOfConvolutionalLayers = 6;
//        const int numberOfFulltConnectedLayers = 3;

//        bool bn;
//        double dropout;

//        ModuleList<MaxPool2d> mxps = new ModuleList<MaxPool2d>();

//        ModuleList<Conv2d> Conv2dmodules = new ModuleList<Conv2d>();
//        ModuleList<Linear> LineaRmodules = new ModuleList<Linear>();

//        ModuleList<BatchNorm2d> bns2d = new ModuleList<BatchNorm2d>();

//        ModuleList<BatchNorm1d> bns1d = new ModuleList<BatchNorm1d>();

//        int[,] convParams = new int[numberOfConvolutionalLayers, 5];
//        int[,] maxPoolParams = new int[numberOfConvolutionalLayers, 2];
//        int[,] fcParams = new int[numberOfFulltConnectedLayers, 2];
//     //   int[] bnPaams = new int[numberOfConvolutionalLayers + numberOfFulltConnectedLayers];



//        public CNNMaquette(string name, double dropout, bool bn, int outputSize) : base(name)
//        {

//            this.bn = bn;
//            this.dropout = dropout;

//            convParams[0, 0] = 2; convParams[0, 1] = 32; convParams[0, 2] = 3; convParams[0, 3] = 1; convParams[0, 4] = 1;
//            convParams[1, 0] = 32; convParams[1, 1] = 32; convParams[1, 2] = 3; convParams[1, 3] = 1; convParams[1, 4] = 1;
//            convParams[2, 0] = 32; convParams[2, 1] = 64; convParams[2, 2] = 3; convParams[2, 3] = 1; convParams[2, 4] = 1;
//            convParams[3, 0] = 64; convParams[3, 1] = 64; convParams[3, 2] = 3; convParams[3, 3] = 1; convParams[3, 4] = 1;
//            convParams[4, 0] = 64; convParams[4, 1] = 64; convParams[4, 2] = 3; convParams[4, 3] = 1; convParams[4, 4] = 1;
//            convParams[5, 0] = 64; convParams[5, 1] = 64; convParams[5, 2] = 3; convParams[5, 3] = 1; convParams[5, 4] = 1;

//            maxPoolParams[0, 0] = 0; maxPoolParams[0, 1] = 0;
//            maxPoolParams[1, 0] = 1; maxPoolParams[1, 1] = 2;
//            maxPoolParams[2, 0] = 0; maxPoolParams[2, 1] = 0;
//            maxPoolParams[3, 0] = 1; maxPoolParams[3, 1] = 2;
//            maxPoolParams[4, 0] = 1; maxPoolParams[4, 1] = 2;
//            maxPoolParams[5, 0] = 2; maxPoolParams[5, 1] = 2;

//            fcParams[0, 0] = 7936; fcParams[0, 1] = 1024;
//            fcParams[1, 0] = 1024; fcParams[1, 1] = 60;
//            fcParams[2, 0] = 60;   fcParams[2, 1] = outputSize;

//            for (int i = 0; i < numberOfConvolutionalLayers; i++)
//            {
//                Conv2dmodules.append(Conv2d(inputChannel: convParams[i, 0], outputChannel: convParams[i, 1],
//                    kernelSize: convParams[i, 2], stride: convParams[i, 3], padding: convParams[i, 4]));
//                bns2d.append(BatchNorm2d(convParams[i, 1]));
//            }

//            for (int i = 0; i < numberOfConvolutionalLayers; i++)
//            {
//                mxps.Add(MaxPool2d((maxPoolParams[i,0], maxPoolParams[i,1])));
//            }

//            for (int i = 0; i < numberOfFulltConnectedLayers; i++)
//            {
//                LineaRmodules.append(Linear(inputSize: fcParams[i, 0], outputSize: fcParams[i, 1]));
//                bns1d.append(BatchNorm1d(fcParams[i, 1]));
//            }

//            RegisterComponents();

//        }

//        public override Tensor forward(Tensor r)
//        {
//            ;
//            for (int i = 0; i < numberOfConvolutionalLayers; i++)
//            {
//                r = Conv2dmodules[i].forward(r);
//                if (bn) r = bns2d[i].forward(r);
//                r = nn.functional.dropout(r, dropout, true);// Dropout(dropout).forward(r);
//                r = nn.functional.relu(r);// ReLU().forward(r);


//                if (maxPoolParams[i,0] != 0)
//                {
//                    r = mxps[i].forward(r);
//                }
//            }

//            r =  Flatten(startDim: 1).forward(r);


//            for (int i = 0; i < numberOfFulltConnectedLayers - 1; i++)
//            {
//                r = LineaRmodules[i].forward(r);
//                if (bn) r = bns1d[i].forward(r);
//                r = nn.functional.dropout(r, dropout, true); //r = Dropout(dropout).forward(r);
//                r = nn.functional.relu(r); //ReLU().forward(r);
//            }

//            r = LineaRmodules[numberOfFulltConnectedLayers - 1].forward(r);

//            return r;

//        }




//    }
//}


//class CNNmaquette(nn.Module):
//    def __init__(self, dropout, bn):
//        super().__init__()


//        self.bn = bn
//        self.conv1 = nn.Conv2d(in_channels = 2, out_channels = 32, kernel_size = 3, padding = (1, 1))
//        if (bn): self.conv1_bn = nn.BatchNorm2d(32)
//        self.drop1 = nn.Dropout(dropout)
//        self.conv2 = nn.Conv2d(in_channels = 32, out_channels = 32, kernel_size = 3, padding = (1, 1))
//        if (bn): self.conv2_bn = nn.BatchNorm2d(32)
//        self.drop2 = nn.Dropout(dropout)


//        self.conv3 = nn.Conv2d(in_channels = 32, out_channels = 64, kernel_size = 3, padding = (1, 1))
//        if (bn): self.conv3_bn = nn.BatchNorm2d(64)
//        self.drop3 = nn.Dropout(dropout)
//        self.conv4 = nn.Conv2d(in_channels = 64, out_channels = 64, kernel_size = 3, padding = (1, 1))
//        if (bn): self.conv4_bn = nn.BatchNorm2d(64)
//        self.drop4 = nn.Dropout(dropout)


//        self.conv5 = nn.Conv2d(in_channels = 64, out_channels = 64, kernel_size = 3, padding = (1, 1))
//        if (bn): self.conv5_bn = nn.BatchNorm2d(64)
//        self.drop5 = nn.Dropout(dropout)
//        self.conv6 = nn.Conv2d(in_channels = 64, out_channels = 64, kernel_size = 3, padding = (1, 1))
//        if (bn): self.conv6_bn = nn.BatchNorm2d(64)
//        self.drop6 = nn.Dropout(dropout)




//      # self.fc1 = nn.Linear(in_features=12288, out_features=1228)
//        self.fc1 = nn.Linear(in_features = 7936, out_features = 1024)
//        if (bn): self.fc1_bn = nn.BatchNorm1d(1024)
//        self.drop7 = nn.Dropout(dropout)
//        self.fc2 = nn.Linear(in_features = 1024, out_features = 60)
//        if (bn):self.fc2_bn = nn.BatchNorm1d(60)
//        self.drop8 = nn.Dropout(dropout)
//        self.out = nn.Linear(in_features = 60, out_features = 1)


//    def forward(self, t):

//        t = self.conv1(t)
//        if (self.bn): t = self.conv1_bn(t)
//        t = self.drop1(t)
//        t = F.relu(t)

//        t = self.conv2(t)
//        if (self.bn): t = self.conv2_bn(t)
//        t = self.drop2(t)
//        t = F.relu(t)
//        t = F.max_pool2d(t, kernel_size = (1, 2))



//        t = self.conv3(t)
//        if (self.bn): t = self.conv3_bn(t)
//        t = self.drop3(t)
//        t = F.relu(t)
//        t = self.conv4(t)
//        if (self.bn): t = self.conv4_bn(t)
//        t = self.drop4(t)
//        t = F.relu(t)
//        t = F.max_pool2d(t, kernel_size = (1, 2))

//        # sys.exit(t.shape)  

//        t = self.conv5(t)
//        if (self.bn): t = self.conv5_bn(t)
//        t = self.drop5(t)
//        t = F.relu(t)
//        t = F.max_pool2d(t, kernel_size = (1, 2))
//        t = self.conv6(t)
//        if (self.bn): t = self.conv6_bn(t)
//        t = self.drop6(t)
//        t = F.relu(t)
//        t = F.max_pool2d(t, kernel_size = (2, 2))


//        t = t.flatten(start_dim = 1)

//        # sys.exit(t.shape) 
//        t = self.fc1(t)
//        if (self.bn): t = self.fc1_bn(t)
//        t = F.relu(t)
//        t = self.fc2(t)
//        if (self.bn): t = self.fc2_bn(t)
//        t = F.relu(t)


//        t = self.out(t)


//        return t
//                //features = Sequential(
//                //   ("c1", Conv2d(3, 64, kernelSize: 3, stride: 2, padding: 1)),
//                //    ("c1", BatchNorm2d(32))
//                //   );