using ProtoBuf;
using System.Collections.Generic;

namespace ModCheck
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    internal class ModCheckPacket
    {
        public List<ModCheckReport> Reports = new List<ModCheckReport>();

        internal void AddReport(ModCheckReport report)
        {
            Reports.Add(report);
        }
    }
}
