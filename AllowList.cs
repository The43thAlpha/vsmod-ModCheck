using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ModCheck
{
    internal class AllowList
    {
        internal Dictionary<string, List<ModCheckReport>> allowedReportsById = new Dictionary<string, List<ModCheckReport>>();

        internal void AddReport(ModCheckReport report)
        {
            if (!allowedReportsById.ContainsKey(report.Id))
            {
                allowedReportsById.Add(report.Id, new List<ModCheckReport>());
            }
            allowedReportsById[report.Id].Add(report);
        }

        internal EnumProblemFlags GetReportProblem(ModCheckReport report)
        {
            EnumProblemFlags flags = EnumProblemFlags.All;

            if (allowedReportsById.TryGetValue(report.Id, out var modcheckReports))
            {
                foreach (var allowed in modcheckReports)
                {
                    flags = EnumProblemFlags.All;
                    flags &= ~EnumProblemFlags.UnrecognizedModId;
                    flags &= ~EnumProblemFlags.EmptyReports;

                    if (allowed.Fingerprint == report.Fingerprint)
                    {
                        flags &= ~EnumProblemFlags.UnrecognizedFingerprint;
                    }

                    if (allowed.Version == report.Version)
                    {
                        flags &= ~EnumProblemFlags.UnrecognizedVersion;
                    }

                    if (allowed.SourceType == report.SourceType)
                    {
                        flags &= ~EnumProblemFlags.UnrecognizedSourceType;
                    }

                    if (flags == 0) break;
                }
            }

            return flags;
        }

        internal readonly string[] modProblems = new string[]
        {
            @"| Unrecognized Mod ID |",
            @"| Unrecognized Version |",
            @"| Unrecognized Type |", 
            @"| Unrecognized Fingerprint |",
            @"| Empty Mod Reports |"
        };

        internal bool HasErrors(ModCheckReport report, out string errors)
        {
            errors = "";
            EnumProblemFlags flags = GetReportProblem(report);
            
            if (flags != EnumProblemFlags.None)
            {
                errors = GetFriendlyError(report, flags);
                return true;
            }

            return false;
        }

        internal string GetFriendlyError(ModCheckReport report, EnumProblemFlags flags)
        {
            StringBuilder builder = new StringBuilder();
            if (flags != EnumProblemFlags.None)
            {
                builder.AppendLine(string.Format("{0}:", report.Name));

                builder.Append('|');
                if ((((int)flags >> 0) & 1) > 0)
                {
                    builder.Append(Lang.Get(modProblems[0]));
                }

                if ((((int)flags >> 1) & 1) > 0)
                {
                    builder.Append(Lang.Get(modProblems[1]));
                }

                if ((((int)flags >> 2) & 1) > 0)
                {
                    builder.Append(Lang.Get(modProblems[2]));
                }

                if ((((int)flags >> 3) & 1) > 0)
                {
                    builder.Append(Lang.Get(modProblems[3]));
                }

                if ((((int)flags >> 4) & 1) > 0)
                {
                    builder.Append(Lang.Get(modProblems[4]));
                }
                builder.Append('|');
            }
            else
            {
                builder.Append("No Errors.");
            }
            return builder.ToString();
        }

        internal IEnumerable<string> GetAllowedVersionsForMod(string modId)
        {
            if (allowedReportsById.TryGetValue(modId, out var modcheckReports))
            {
                return modcheckReports.Select((rp) => rp.Version).Distinct();
            }
            return Enumerable.Empty<string>();
        }

        internal IEnumerable<int> GetAllowedSourceTypesForMod(string modId)
        {
            if (allowedReportsById.TryGetValue(modId, out var modcheckReports))
            {
                return modcheckReports.Select((rp) => rp.SourceType).Distinct();
            }
            return Enumerable.Empty<int>();
        }
    }
}
