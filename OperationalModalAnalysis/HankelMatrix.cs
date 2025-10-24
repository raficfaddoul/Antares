using MathNet.Numerics.LinearAlgebra;

namespace OperationalModalAnalysis;

public static class HankelMatrix
{
    public async static Task<IEnumerable<Matrix<double>>> Calcul(IEnumerable<Matrix<double>> datas, int nPseudo, int nbBlocks, int nt)
    {
        Matrix<double> Hp = Matrix<double>.Build.Dense(nPseudo * nbBlocks, nt);
        Matrix<double> Hf = Matrix<double>.Build.Dense(nPseudo * nbBlocks, nt);
        await Task.Run(() =>
        {
            List<Matrix<double>> datasList = datas.ToList();
            if (nbBlocks < 2) {
                throw new ArgumentException("nbBlocks must be 2 or more");
            }
            if ( nPseudo + nt > datasList[0].ColumnCount)
            {
                throw new ArgumentException("nPseudo + nt must be less than or equal to the number of columns in datas");
            }

            try
            {

                for (int i = 0; i < nbBlocks; i++)
                {
                    for (int j = 0; j < nPseudo; j++)
                    {
                        for (int k = 0; k < nt; k++)
                        {
                            Hp[i * nPseudo + j, k] = datasList[j][0, k + i];        // 0 need to be modified later to use the full RD matrix (take only the first row for now)
                            Hf[i * nPseudo + j, k] = datasList[j][0, k + i + 1];    // Same here
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                int k = 1;
            }
        });

        return [Hp, Hf];
    }
}