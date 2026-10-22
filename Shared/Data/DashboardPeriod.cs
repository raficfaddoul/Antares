using System;
using System.Collections.Generic;
using System.Linq;

namespace AliantWebS.Data
{
    /// <summary>
    /// The dashboard's one period (the 2026-10-22 redesign): the span the gauges, the plan colours and both curves all
    /// cover. Five choices, one month by default. An unknown code throws: there is no "since the beginning" to fall
    /// back on.
    /// </summary>
    public sealed class DashboardPeriod
    {
        public static readonly DashboardPeriod OneMonth = new("1m", "1 mois", 1);
        public static readonly DashboardPeriod ThreeMonths = new("3m", "3 mois", 3);
        public static readonly DashboardPeriod SixMonths = new("6m", "6 mois", 6);
        public static readonly DashboardPeriod OneYear = new("1y", "1 an", 12);
        public static readonly DashboardPeriod FiveYears = new("5y", "5 ans", 60);

        /// <summary>The choices, in the order the dashboard offers them.</summary>
        public static IReadOnlyList<DashboardPeriod> All { get; } = new[] { OneMonth, ThreeMonths, SixMonths, OneYear, FiveYears };

        public static DashboardPeriod Default => OneMonth;

        private DashboardPeriod(string code, string label, int months)
        {
            Code = code;
            Label = label;
            Months = months;
        }

        /// <summary>What is stored (AppData) and what the dropdown's value is.</summary>
        public string Code { get; }

        /// <summary>What the dropdown and the messages show, in French.</summary>
        public string Label { get; }

        public int Months { get; }

        /// <summary>The period of a stored code; any other code throws, naming it.</summary>
        public static DashboardPeriod Of(string? code) =>
            All.FirstOrDefault(p => p.Code == code)
            ?? throw new ArgumentException(
                $"Unknown dashboard period code {(code is null ? "null" : $"\"{code}\"")}; expected one of {string.Join(", ", All.Select(p => p.Code))}.",
                nameof(code));

        /// <summary>[now - the period's months, now]. A month back from the 31st lands on the shorter month's last day.</summary>
        public (DateTime Start, DateTime End) Span(DateTime nowUtc)
        {
            if (nowUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"A dashboard span is counted from a UTC now, not a {nowUtc.Kind} one.", nameof(nowUtc));
            return (nowUtc.AddMonths(-Months), nowUtc);
        }
    }
}
