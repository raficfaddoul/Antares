using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>
    /// Resolves a hardcoded system tag (CodeTag) to every name that should be accepted for it.
    ///
    /// A name matches when it is EITHER the code tag itself OR any user alias mapped to it in
    /// TagMappingTable. Accepting the code tag matters because a fresh database has no mappings at
    /// all — without it, an attribute genuinely named "seuil_alarme" would go unrecognised until
    /// somebody added a redundant mapping row.
    ///
    /// Comparison is case-insensitive and ignores surrounding whitespace, since these names are
    /// typed by hand in the settings UI.
    /// </summary>
    public static class TagMappingResolver
    {
        public static HashSet<string> AcceptedNames(IEnumerable<TagMappingTable> mappings, string codeTag)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { codeTag };

            foreach (var m in mappings ?? Enumerable.Empty<TagMappingTable>())
            {
                if (m?.UserTag == null || m.CodeTag == null) continue;
                if (!string.Equals(m.CodeTag.Trim(), codeTag, StringComparison.OrdinalIgnoreCase)) continue;

                var alias = m.UserTag.Trim();
                if (alias.Length > 0) names.Add(alias);
            }

            return names;
        }

        public static async Task<HashSet<string>> AcceptedNamesAsync(AliantWebContext db, string codeTag)
        {
            var rows = await db.Set<TagMappingTable>()
                .Where(t => t.CodeTag == codeTag)
                .AsNoTracking()
                .ToListAsync();

            return AcceptedNames(rows, codeTag);
        }

        /// <summary>True when <paramref name="name"/> is the code tag or one of its aliases.</summary>
        public static bool Matches(HashSet<string> acceptedNames, string name)
            => name != null && acceptedNames != null && acceptedNames.Contains(name.Trim());
    }
}
