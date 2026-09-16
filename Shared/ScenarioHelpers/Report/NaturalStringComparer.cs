// =============================================================
// NaturalStringComparer.cs — Orders names the way a person numbers them: T1, T2 ... T10, T11.
//
// Load names are numbered by hand ("T12", "LX2.3", "OCCJ_1.1"). Sorted as plain text they come out
// T1, T10, T11, T12, T2, which is not how anyone reads a load plan.
// =============================================================

using System;
using System.Collections.Generic;

namespace AliantWebS.Reporting;

/// <summary>
/// Compares strings with each run of digits taken as a number, so "T2" sorts before "T10" and "LX1.2"
/// before "LX1.10". Letters compare without regard to case. Ties that remain - "T01" against "T1", or
/// "t1" against "T1" - fall back to ordinal order, so the result never depends on the input order.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                // Compare the two numbers without parsing them, so their length is unbounded: skip
                // leading zeros, then the longer run is the larger number, then digit by digit.
                int si = i, sj = j;
                while (si < x.Length && x[si] == '0') si++;
                while (sj < y.Length && y[sj] == '0') sj++;

                int ei = si, ej = sj;
                while (ei < x.Length && char.IsAsciiDigit(x[ei])) ei++;
                while (ej < y.Length && char.IsAsciiDigit(y[ej])) ej++;

                int lengthX = ei - si, lengthY = ej - sj;
                if (lengthX != lengthY) return lengthX.CompareTo(lengthY);

                for (int k = 0; k < lengthX; k++)
                {
                    int digit = x[si + k].CompareTo(y[sj + k]);
                    if (digit != 0) return digit;
                }

                // Leading zeros are consumed with the run, so "T01" and "T1" line up here.
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                continue;
            }

            int letter = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (letter != 0) return letter;
            i++;
            j++;
        }

        if (i < x.Length) return 1;     // y ran out first: it is a prefix of x
        if (j < y.Length) return -1;
        return string.CompareOrdinal(x, y);
    }
}
