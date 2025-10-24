using MathNet.Numerics.LinearAlgebra;

namespace OperationalModalAnalysis;

public class RandomDecrement
{
    private Zones Zones { get; set; }

    // Cannot use await in constructor, need to initialize fields differently
    public RandomDecrement(
        double[] datas,
        double threshold,
        Func<double, double, double, bool> condition,
        int saveTime,
        bool overlapping,
        int shifting = 0)
    {
        // Initialize immediately with synchronous creation
        var task = Zones.CreateAsync(datas, threshold, condition, saveTime, overlapping, shifting);
        task.Wait(); // Not ideal but necessary in constructor
        Zones = task.Result;
    }

    // Alternative async factory method (preferred approach)
    public static async Task<RandomDecrement> CreateAsync(
        double[] datas,
        double threshold,
        Func<double, double, double, bool> condition,
        int saveTime,
        bool overlapping,
        int shifting = 0)
    {
        var instance = new RandomDecrement();
        instance.Zones = await Zones.CreateAsync(datas, threshold, condition, saveTime, overlapping, shifting);
        return instance;
    }

    // Needed for factory method
    private RandomDecrement() { }

    public IEnumerable<int> GetIndices()
    {
        return Zones.GetInterrestingPoint();
    }

    public async Task SetIndices(IEnumerable<int> indices)
    {
        await Zones.SetInterrestingPoint(indices);
    }

    public Matrix<double> GetMatrix()
    {
        return Zones.GetInterrestingDatas() ?? throw new InvalidOperationException("The matrix is not yet calculated");
    }

    public async Task<Matrix<double>> GetSameDecrement(IEnumerable<double> datas)
    {
        return await Zones.GetAtSameIndexOn(datas);
    }

    public static async Task<Matrix<double>> GetMatrix(double[] datas, double threshold, Func<double, double, double, bool> condition, int saveTime, bool overlapping, int shifting = 0)
    {
        Zones zones = await Zones.CreateAsync(datas, threshold, condition, saveTime, overlapping, shifting);
        return zones.GetInterrestingDatas() ?? throw new InvalidOperationException("The matrix is not yet calculated");
    }
}
