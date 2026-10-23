using System;
using System.Collections.Generic;
using System.Linq;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>
    /// What the dashboard's two graphs (AlarmRatioCurves, WeeklyMinMaxCurves) need from a sensor type: the name shown
    /// for it, its channels, and the rule its series names must keep to be drawn. Each caller words its own refusals.
    /// </summary>
    public static class SensorTypeCurves
    {
        /// <summary>
        /// A type's name wherever the dashboard shows it (curves, notes, log lines): its id when the name is blank, as a
        /// sensor without a name is shown by its id, so it is visible, never a blank legend.
        /// </summary>
        public static string TypeName(ElementType type) =>
            string.IsNullOrWhiteSpace(type.Name) ? type.ElementTypeId.ToString() : type.Name;

        /// <summary>
        /// The type's channels, distinct by id. A type given without its channels (its links loaded, not the channels
        /// they point to) is refused with an ArgumentException that ends with <paramref name="consequence"/>.
        /// </summary>
        public static List<ChannelDataType> ChannelsOf(ElementType type, string consequence)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type.ChannelDataTypeElementTypes.Any(link => link.ChannelDataType == null))
                throw new ArgumentException($"The type {type.ElementTypeId} was given without its channels: {consequence}.", nameof(type));

            return type.ChannelDataTypeElementTypes
                .Select(link => link.ChannelDataType)
                .DistinctBy(channel => channel.ChannelDataTypeId)
                .ToList();
        }

        /// <summary>
        /// Refuses series the graph cannot draw, with InvalidOperationException: IndicesInTimeComponent.DisplayData joins
        /// the series names with commas and cannot tell two series of one name apart. The messages are the caller's:
        /// <paramref name="withComma"/> gets the first item whose name holds a comma, <paramref name="twice"/> a name
        /// two items share.
        /// </summary>
        public static void RefuseUndrawableNames<T>(IEnumerable<T> items, Func<T, string> name,
            Func<T, string> withComma, Func<string, string> twice) where T : class
        {
            var list = items.ToList();
            if (list.FirstOrDefault(item => name(item).Contains(',')) is { } comma)
                throw new InvalidOperationException(withComma(comma));

            if (list.GroupBy(name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } shared)
                throw new InvalidOperationException(twice(shared.Key));
        }
    }
}
