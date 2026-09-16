using System;
using System.Threading.Tasks;

namespace AliantWebS.Data
{
    /// <summary>
    /// The three alarm levels, expressed as a percentage of each sensor's own "Seuil Alarme"
    /// threshold. A sensor at 100 % is at its threshold, so the levels are relative and the same
    /// three percentages apply to every sensor and every channel.
    ///
    /// Two levels decide what actually happens: the siren only sounds from SirenLevel upwards, and
    /// e-mails are only sent from EmailLevel upwards. Detection itself always starts at level 1 —
    /// a level-1 alarm is listed and journaled even when it is silent, which is the point of having
    /// an early level.
    ///
    /// Stored in AppSetting, so changing them needs no rebuild. The defaults reproduce the previous
    /// behaviour for the siren: it sounds at 100 %, not before.
    /// </summary>
    public class AlarmLevels
    {
        public double Level1Percent { get; set; } = 60;
        public double Level2Percent { get; set; } = 80;
        public double Level3Percent { get; set; } = 100;

        /// <summary>Lowest level that drives the physical siren (1, 2 or 3).</summary>
        public int SirenLevel { get; set; } = 3;

        /// <summary>
        /// Lowest level that triggers an alarm e-mail (1, 2 or 3). Defaults to 3 so that deploying
        /// the levels does not, on its own, start mailing recipients at 60 % — the early levels are
        /// silent until an administrator asks for them.
        /// </summary>
        public int EmailLevel { get; set; } = 3;

        public static async Task<AlarmLevels> LoadAsync()
        {
            var lv = new AlarmLevels
            {
                Level1Percent = await AppSettings.GetDoubleAsync(AppSettings.AlarmLevel1Pct, 60),
                Level2Percent = await AppSettings.GetDoubleAsync(AppSettings.AlarmLevel2Pct, 80),
                Level3Percent = await AppSettings.GetDoubleAsync(AppSettings.AlarmLevel3Pct, 100),
                SirenLevel    = await AppSettings.GetIntAsync(AppSettings.AlarmSirenLevel, 3),
                EmailLevel    = await AppSettings.GetIntAsync(AppSettings.AlarmEmailLevel, 3)
            };

            lv.Normalise();
            return lv;
        }

        /// <summary>
        /// Keeps the percentages ordered and the level selectors in range. A stored 80/60/100 would
        /// otherwise make level 2 unreachable, which reads as "the alarm level is stuck".
        /// </summary>
        public void Normalise()
        {
            if (Level1Percent <= 0) Level1Percent = 60;
            if (Level2Percent < Level1Percent) Level2Percent = Level1Percent;
            if (Level3Percent < Level2Percent) Level3Percent = Level2Percent;

            SirenLevel = Math.Clamp(SirenLevel, 1, 3);
            EmailLevel = Math.Clamp(EmailLevel, 1, 3);
        }

        /// <summary>Percentage of the threshold that level 1/2/3 corresponds to.</summary>
        public double PercentFor(int level) => level switch
        {
            1 => Level1Percent,
            2 => Level2Percent,
            3 => Level3Percent,
            _ => 0
        };

        /// <summary>
        /// Level reached by <paramref name="value"/> against <paramref name="threshold"/>:
        /// 0 = below level 1, i.e. no alarm at all.
        /// </summary>
        public int LevelFor(double value, double threshold)
        {
            if (threshold == 0) return 0;

            double ratio = value / threshold * 100.0;

            if (ratio >= Level3Percent) return 3;
            if (ratio >= Level2Percent) return 2;
            if (ratio >= Level1Percent) return 1;
            return 0;
        }

        public static string Name(int level) => level switch
        {
            1 => "Niveau 1 — vigilance",
            2 => "Niveau 2 — alerte",
            3 => "Niveau 3 — critique",
            _ => "—"
        };

        /// <summary>Journal/UI severity: level 3 is an alarm, level 2 a warning, level 1 info.</summary>
        public static byte SeverityFor(int level) => level >= 3 ? (byte)2 : level == 2 ? (byte)1 : (byte)0;
    }
}
