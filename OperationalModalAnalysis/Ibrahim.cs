using Accord.Math;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;

namespace OperationalModalAnalysis;

public class Ibrahim
{
    // TODO: Implement Ibrahim Time Domain
    private Matrix<double> CaptorsDatas { get; set; } // [ (ax) ([ (cpt1) d1, d2, ...], [ (cpt2) d1, d2, ...], ...),  ]
    private double Dt { get; set; }
    private int DirectiveCaptor { get; set; }
    private int SampleToSave { get; set; }
    private RandomDecrement RandomDecrement { get; set; }
    private IEnumerable<Matrix<double>> HpHf { get; set; } = [];
    private Vector<System.Numerics.Complex>? Fp { get; set; }
    private Matrix<System.Numerics.Complex>? Vp { get; set; }
    private Vector<double>? Frequency { get; set; }
    private Vector<double>? Damping { get; set; }
    private Matrix<double>? Modes { get; set; }
    private int HankelBlock { get; set; }
    private int Nt { get; set; }
    private int NbSensors { get; set; }

    public Ibrahim(double[,] datas, double dt, int directiveCaptor, int sampleToSave, Func<double, double, double, bool> rdCondition, int nt, int hankelBlock, bool overlapping)
    {
        SampleToSave = sampleToSave;
        DirectiveCaptor = directiveCaptor;
        CaptorsDatas = Matrix<double>.Build.DenseOfArray(datas);

        Dt = dt;
        HankelBlock = hankelBlock;
        Nt = nt;
        NbSensors = CaptorsDatas.RowCount;

        double[] v = [.. CaptorsDatas.Row(directiveCaptor)];

        RandomDecrement = new([.. CaptorsDatas.Row(directiveCaptor)], 0.1, rdCondition, SampleToSave, overlapping);
    }

    public IEnumerable<int> GetIndices()
    {
        return RandomDecrement.GetIndices();
    }

    public async Task SetIndices(IEnumerable<int> indices)
    {
        await RandomDecrement.SetIndices(indices);
    }

    protected async Task ITD()
    {
        await Task.Run(async () =>
        {
            List<Matrix<double>> matrices = [RandomDecrement.GetMatrix()];
            for (int cpt = 0; cpt < NbSensors; cpt++)
            {
                if (cpt == DirectiveCaptor)
                {
                    continue;
                }
                matrices.Add(await RandomDecrement.GetSameDecrement(CaptorsDatas.Row(cpt)));
            }

            HpHf = await HankelMatrix.Calcul(matrices, CaptorsDatas.RowCount, HankelBlock, Nt); // Error here :sight:

            Matrix<double> ap1 = HpHf.Last() * HpHf.First().PseudoInverse();

            Evd<double> evd = ap1.Evd();
            Fp = evd.EigenValues;
            Vp = evd.EigenVectors.ToComplex();

            Frequency = Vector<double>.Build.Dense(Fp.Count);
            Damping = Vector<double>.Build.Dense(Fp.Count);

            for (int i = 0; i < Fp.Count; i++)
            {
                double ln_gamma = Math.Log(Math.Sqrt(Math.Pow(Fp[i].Real, 2) + Math.Pow(Fp[i].Imaginary, 2))) / Dt;
                double arg_gamma = Math.Atan2(Fp[i].Imaginary, Fp[i].Real) / Dt;
                double omega = Math.Sqrt(Math.Pow(ln_gamma, 2) + Math.Pow(arg_gamma, 2));

                Frequency[i] = omega / (2 * Math.PI);
                Damping[i] = -ln_gamma / omega;
            }

            DeleteBadModes();

            SortModes();

            ModesNormalisation();

            if (Modes == null)
            {
                throw new Exception("Error in the Ibrahim Time Domain calcul (Modes null)");
            }

            for (int i = 0; i < Modes.ColumnCount; i++) // modes normalisation on the maximum value (point of the mode <= 1)
            {
                double xmax = Modes.Column(i).AbsoluteMaximum();
                Modes.SetColumn(i, Modes.Column(i) / xmax);
            }

            // Add zeros to the modes at the beginning and the end
            Vector<double> zeroesVector = Vector<double>.Build.Dense(Modes.RowCount, 0);
            Modes = Modes.InsertColumn(0, zeroesVector);
            Modes = Modes.InsertColumn(Modes.ColumnCount, zeroesVector);
        });
    }

    private void DeleteBadModes()
    {
        if (Fp != null && Vp != null && Frequency != null && Damping != null)
        {
            int i = 0;
            int nbModes = Fp.Count;
            List<int> indexToDelete = [];

            while (i <= nbModes - 2)
            {
                if (System.Numerics.Complex.Abs(Fp[i]) != System.Numerics.Complex.Abs(Fp[i + 1]))
                {
                    if (i + 1 >= nbModes-2)
                    {
                        indexToDelete.Add(i + 1);
                    }
                    indexToDelete.Add(i);
                    ++i;
                }
                else
                {
                    i += 2;
                }
            }

            for (i = 0; i < nbModes; ++i)
            {
                if (Frequency[i] < 0 || Damping[i] < 0)
                {
                    indexToDelete.Add(i);
                }
                else
                {
                    for (int j = i + 1; j < nbModes; ++j)
                    {
                        if (Frequency[i] == Frequency[j])
                        {
                            indexToDelete.Add(j);
                        }
                    }
                }
            }

            if (indexToDelete.Count > 0)
            {
                Fp = Vector<System.Numerics.Complex>.Build.DenseOfEnumerable(Fp.ToArray().Where((x, index) => !indexToDelete.Contains(index)));
                Vp = Matrix<System.Numerics.Complex>.Build.DenseOfColumns(Vp.EnumerateColumns().Where((x, index) => !indexToDelete.Contains(index)));
                Frequency = Vector<double>.Build.DenseOfEnumerable(Frequency.ToArray().Where((x, index) => !indexToDelete.Contains(index)));
                Damping = Vector<double>.Build.DenseOfEnumerable(Damping.ToArray().Where((x, index) => !indexToDelete.Contains(index)));
            }
        }
    }

    private void SortModes()
    {
        if (Fp != null && Vp != null && Frequency != null && Damping != null)
        {
            List<int> index = Frequency.Select((x, i) => new { Value = x, Index = i }).OrderBy(x => x.Value).Select(x => x.Index).ToList();
            Fp = Vector<System.Numerics.Complex>.Build.DenseOfEnumerable(Fp.ToArray().Select((x, i) => Fp[index[i]]));
            Vp = Matrix<System.Numerics.Complex>.Build.DenseOfColumns(Vp.EnumerateColumns().Select((x, i) => Vp.Column(index[i])));
            Frequency = Vector<double>.Build.DenseOfEnumerable(Frequency.ToArray().Select((x, i) => Frequency[index[i]]));
            Damping = Vector<double>.Build.DenseOfEnumerable(Damping.ToArray().Select((x, i) => Damping[index[i]]));
        }
    }

    private void ModesNormalisation()
    {
        if (Vp != null)
        {
            int nr = Vp.ColumnCount;
            Modes = Matrix<double>.Build.Dense(NbSensors, nr);

            for (int i = 0; i < nr; i++)
            {
                Matrix<double> vr = Matrix<double>.Build.Dense(NbSensors, 1); // TODO: Check mode normalisation 
                Matrix<double> vi = Matrix<double>.Build.Dense(NbSensors, 1); // Use nbSensors of Vp instead of all data
                for (int j = 0; j < NbSensors; j++)
                {
                    vr[j, 0] = Vp[i, j].Real;
                    vi[j, 0] = Vp[i, j].Imaginary;
                }


                Matrix<double> vrT = vr.Transpose();
                Matrix<double> phi = vr + vi * (vrT * vr).Inverse() * vrT * vi;

                Modes.SetColumn(i, phi.Column(0));
            }
        }
    }

    public async Task<Vector<System.Numerics.Complex>> GetFp()
    {
        if (Fp == null)
        {
            await ITD();
        }
        return Fp ?? throw new Exception("Error in the Ibrahim Time Domain calcul");
    }

    public async Task<Matrix<System.Numerics.Complex>> GetVp()
    {
        if (Vp == null)
        {
            await ITD();
        }
        return Vp ?? throw new Exception("Error in the Ibrahim Time Domain calcul");
    }

    public async Task<Vector<double>> GetFrequency()
    {
        if (Frequency == null)
        {
            await ITD();
        }
        return Frequency ?? throw new Exception("Error in the Ibrahim Time Domain calcul");
    }

    public async Task<Vector<double>> GetDamping()
    {
        if (Damping == null)
        {
            await ITD();
        }
        return Damping ?? throw new Exception("Error in the Ibrahim Time Domain calcul");
    }

    public async Task<Matrix<double>> GetModes()
    {
        if (Modes == null)
        {
            await ITD();
        }
        return Modes ?? throw new Exception("Error in the Ibrahim Time Domain calcul");
    }
}