namespace AliantWebS.Data
{
    /// <summary>
    /// Which Y axis each curve of a sensor plot goes on (Rafic, 2026-10-22): a temperature channel on the left axis,
    /// every other channel on the right axis, whatever the names and the order they come in. With no temperature
    /// plotted the other channels take the left axis, so no axis is drawn empty. The other channels share one axis
    /// whatever they measure, since nothing records their units (ChannelDataType.Units is empty): when several do,
    /// <see cref="Notice"/> says so, to be shown with the plot.
    /// </summary>
    public sealed class PlotAxes
    {
        /// <summary>The axes as dygraph names them (updateDygraphWithAxes in dygraphInterop.js).</summary>
        public const string Left = "y", Right = "y2";

        /// <summary>Each curve's header (its CSV column) and the axis it goes on, <see cref="Left"/> or <see cref="Right"/>.</summary>
        public IReadOnlyDictionary<string, string> AxisOfSeries { get; }

        /// <summary>The names of the channels on each axis, in alphabetical order; empty for an unused axis.</summary>
        public string LeftLabel { get; }
        public string RightLabel { get; }

        /// <summary>Set when several channels share one axis, so one scale: says which axis and which channels.</summary>
        public string? Notice { get; }

        PlotAxes(IReadOnlyDictionary<string, string> axisOfSeries, string leftLabel, string rightLabel, string? notice)
        {
            AxisOfSeries = axisOfSeries;
            LeftLabel = leftLabel;
            RightLabel = rightLabel;
            Notice = notice;
        }

        public static bool IsTemperature(string channel) =>
            channel.Contains("temperature", StringComparison.OrdinalIgnoreCase);

        /// <param name="curves">Each plotted curve: its header and the name of its channel.</param>
        public static PlotAxes For(IEnumerable<(string Series, string Channel)> curves)
        {
            ArgumentNullException.ThrowIfNull(curves);
            var list = curves.ToList();
            if (list.Any(c => c.Channel == null))
                throw new ArgumentException("A curve has no channel name, so no axis.", nameof(curves));

            var temperatures = NamesOf(list.Where(c => IsTemperature(c.Channel)));
            var others = NamesOf(list.Where(c => !IsTemperature(c.Channel)));
            var othersAxis = temperatures.Count > 0 ? Right : Left;

            var axisOfSeries = new Dictionary<string, string>();
            foreach (var (series, channel) in list)
            {
                var axis = IsTemperature(channel) ? Left : othersAxis;
                // dygraph gives each header one axis.
                if (axisOfSeries.TryGetValue(series, out var given) && given != axis)
                    throw new ArgumentException($"The curve '{series}' would need both axes: two of its channels go on different ones.", nameof(curves));
                axisOfSeries[series] = axis;
            }

            var othersLabel = string.Join(", ", others);
            var notice = others.Count > 1
                ? $"Axe de {(othersAxis == Right ? "droite" : "gauche")} partagé par {othersLabel} : une seule échelle pour ces canaux."
                : null;

            return temperatures.Count > 0
                ? new PlotAxes(axisOfSeries, string.Join(", ", temperatures), othersLabel, notice)
                : new PlotAxes(axisOfSeries, othersLabel, "", notice);
        }

        static List<string> NamesOf(IEnumerable<(string Series, string Channel)> curves) =>
            curves.Select(c => c.Channel).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
