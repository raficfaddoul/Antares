
using MathNet.Numerics.LinearAlgebra;
using AliantWebS.Data;  

namespace OperationalModalAnalysis;

public class LocalisationDetectionMethod
{
    public static IEnumerable<FileResults> MatchModes(FileResults undammaged, FileResults dammaged)
    {
        bool uLonguest = undammaged.Frequencies.Count > dammaged.Frequencies.Count;

        var longuestList = uLonguest ? undammaged : dammaged;
        var shortestList = uLonguest ? dammaged : undammaged;
        int nm = shortestList.Frequencies.Count;

        List<FileResults> matchModes;

        if (uLonguest)
        {
            matchModes =
            [
                new FileResults(Vector<double>.Build.Dense(nm), Vector<double>.Build.Dense(nm), Matrix<double>.Build.Dense(shortestList.Modes.RowCount, nm)),
                new FileResults(shortestList.Frequencies, shortestList.Damping, Matrix<double>.Build.Dense(shortestList.Modes.RowCount, nm))
            ];
        }
        else
        {
           matchModes =
           [
               new FileResults(shortestList.Frequencies, shortestList.Damping, Matrix<double>.Build.Dense(shortestList.Modes.RowCount, nm)),
               new FileResults(Vector<double>.Build.Dense(nm), Vector<double>.Build.Dense(nm), Matrix<double>.Build.Dense(shortestList.Modes.RowCount, nm))
           ];
        }


        for (int i = 0; i < shortestList.Frequencies.Count; ++i)
        {
            int index = -1;
            for (int j = 0; j < longuestList.Frequencies.Count; ++j)
            {
                if (index == -1 || (Math.Abs(shortestList.Frequencies[i] - longuestList.Frequencies[j]) <= Math.Abs(shortestList.Frequencies[i] - longuestList.Frequencies[index])))
                {
                    index = j;
                }
            }

            if (index != -1)
            {
                if (uLonguest)
                {
                    matchModes[0].Frequencies[i] = undammaged.Frequencies[index];
                    matchModes[0].Damping[i] = undammaged.Damping[index];
                    matchModes[0].Modes.SetColumn(i, undammaged.Modes.Column(index + 1));
                    matchModes[1].Modes.SetColumn(i, dammaged.Modes.Column(i + 1));
                }
                else
                {
                    matchModes[1].Frequencies[i] = dammaged.Frequencies[index];
                    matchModes[1].Damping[i] = dammaged.Damping[index];
                    matchModes[1].Modes.SetColumn(i, dammaged.Modes.Column(index + 1));
                    matchModes[0].Modes.SetColumn(i, undammaged.Modes.Column(i + 1));
                }
            }
        }

        return matchModes;
    }

    public static bool IsSameFrequences(double frequence_undamaged, double second_frequence, double threshold)
    {
        return Math.Abs(frequence_undamaged - second_frequence) < threshold;
    }

    public static double Mac(Matrix<double> mode_undamaged, Matrix<double> second_mode)
    {
        return Matrix<double>.op_DotDivide(Matrix<double>.op_DotHat(mode_undamaged.Transpose() * second_mode, 2),
                Matrix<double>.op_DotMultiply(mode_undamaged.Transpose() * mode_undamaged, second_mode.Transpose() * second_mode)).At(0, 0);
    }

    public static double Comac(Matrix<double> modesUndamaged, Matrix<double> secondModes, int index)
    {
        return Math.Pow(modesUndamaged.Column(index).PointwiseMultiply(secondModes.Column(index)).Sum(), 2) /
                (modesUndamaged.Column(index).Select(x => Math.Pow(x, 2)).Sum() * secondModes.Column(index).Select(x => Math.Pow(x, 2)).Sum());
    }

    public static double Ecomac(Matrix<double> modesUndamaged, Matrix<double> secondModes, int nm, int index)
    {
        return Enumerable.Range(0, nm).Sum(i => Math.Abs(modesUndamaged[i, index] - secondModes[i, index])) / (2 * nm);
    }

    public static double ModeCurvature(Vector<double> mode, double distance, int index)
    {
        if (index < 1)
        {
            throw new ArgumentException("index must be 1 or more");
        }
        else if (index+1 > mode.Count)
        {
            throw new ArgumentException("index must be less than the length of the mode");
        }
        return (mode[index+1] - (2 * mode[index]) + mode[index - 1])/Math.Pow(distance, 2);
    }

    public static Matrix<double> FlexibilityMatrix(Matrix<double> modes, Vector<double> xi)
    {
        var result = Matrix<double>.Build.Dense(modes.ColumnCount, modes.ColumnCount, 0.0);

        for (int i = 0; i < modes.RowCount; i++)
        {
            var mode = modes.Row(i).ToColumnMatrix();
            double scalar = 1.0 / (xi[i] * xi[i]);

            result += scalar * mode * mode.Transpose();
        }

        return result;
    }

    public static Matrix<double> DeltaFlexibilityMatrix(Matrix<double> modesU, Vector<double> xiU, Matrix<double> modesD, Vector<double> xiD) 
    {
        Matrix<double> flex_u = FlexibilityMatrix(modesU, xiU);
        Matrix<double> flex_d = FlexibilityMatrix(modesD, xiD);

        return flex_u - flex_d;
    }

    public static double GammaFlexibilityMatrix(Matrix<double> modesU, Vector<double> xiU, Matrix<double> modesD, Vector<double> xiD)
    {
        Matrix<double> deltaMatrix = DeltaFlexibilityMatrix(modesU, xiU, modesD, xiD);

        return deltaMatrix.Enumerate().Max();
    }
}
