using MathNet.Numerics.LinearAlgebra;

namespace AliantWebS.Data;

public readonly struct FileResults
{
    public FileResults()
    {
        Frequencies = Vector<double>.Build.Dense(0);
        Damping = Vector<double>.Build.Dense(0);
        Modes = Matrix<double>.Build.Dense(0, 0);
    }

    public FileResults(Vector<double> freq, Vector<double> damping, Matrix<double> modes)
    {
        Frequencies = freq;
        Damping = damping;
        Modes = modes;
    }

    public Vector<double> Frequencies { get; init; }
    public Vector<double> Damping { get; init; }
    public Matrix<double> Modes { get; init; }
}