using Accord.Math;
using MathNet.Numerics.LinearAlgebra;

namespace OperationalModalAnalysis;

public class Zones
{
    private Vector<double> Datas { get; set; }
    private IList<int> InterrestingPoint { get; set; } = new List<int>();
    private int SaveTime { get; set; }
    private Matrix<double>? InterrestingDatas { get; set; } = null;
    private int Shifting { get; set; }

    private Zones(Vector<double> datas, int saveTime, int shifting)
    {
        Datas = datas;
        SaveTime = saveTime;
        Shifting = shifting;
    }

    public static async Task<Zones> CreateAsync(IEnumerable<double> datas, double threshold,
    Func<double, double, double, bool> condition, int saveTime, bool overlapping, int shifting)
    {
        Vector<double> dataVector = Vector<double>.Build.DenseOfArray(datas.ToArray());
        var zones = new Zones(dataVector, saveTime, shifting);

        zones.CalculInterrestingPoints(threshold, condition, overlapping);
        zones.CalculInterrestingDatas();

        return zones;
    }


    protected void CalculInterrestingPoints(double threshold, Func<double, double, double, bool> condition, bool overlapping)
    {
        InterrestingPoint = new List<int>();
        double previous = Datas.First();
        int i = 1;

            while (i < Datas.Count)
            {
                if (condition(previous, Datas[i], threshold))
                {
                    InterrestingPoint.Add(i);
                }
                previous = Datas[i];

                if (overlapping)
                {
                    if (Shifting <= 0)
                    {
                        ++i;
                    }
                    else
                    {
                        i += Shifting;
                    }
                }
                else
                {
                    i += SaveTime;
                }
            }
       
    }

    public IEnumerable<int> GetInterrestingPoint()
    {
        return InterrestingPoint;
    }

    public async Task SetInterrestingPoint(IEnumerable<int> interrestingPoint)
    {
        InterrestingPoint = new List<int>(interrestingPoint);
        CalculInterrestingDatas();
    }

    protected void CalculInterrestingDatas()
    {
        InterrestingDatas = Matrix<double>.Build.Dense(0, SaveTime);
     
            for (int i = 0; i < InterrestingPoint.Count; i++)
            {
                if (InterrestingPoint[i] + SaveTime >= Datas.Count)
                {
                    break;
                }
                InterrestingDatas = InterrestingDatas.Stack(Matrix<double>.Build.DenseOfRowVectors(Datas.SubVector(InterrestingPoint[i], SaveTime)));
            }
       
    }

    public Matrix<double>? GetInterrestingDatas()
    {
        return InterrestingDatas;
    }

    public async Task<Matrix<double>> GetAtSameIndexOn(IEnumerable<double> datas)
    {
        return await Task.Run(() =>
        {
            Matrix<double> result = Matrix<double>.Build.Dense(0, SaveTime);
            List<double> datasList = datas.ToList();
            for (int i = 0; i < InterrestingPoint.Count; i++)
            {
                if (InterrestingPoint[i] + SaveTime >= datasList.Count)
                {
                    break;
                }
                result = result.Stack(Matrix<double>.Build.DenseOfRowVectors(Vector<double>.Build.DenseOfArray([.. datasList.GetRange(InterrestingPoint[i], SaveTime)])));
            }
            return result;
        });
    }
}