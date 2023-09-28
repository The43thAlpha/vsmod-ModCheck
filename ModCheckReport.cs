using ProtoBuf;
using System;
using Vintagestory.API.Common;

namespace ModCheck
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ModCheckReport
    {
        public string Id;
        public string Name;
        public string Version;
        public string FileName;
        public int SourceType;
        public string Fingerprint;

        private const string unformattedString = @"[ModCheckReport] - Type: {0} - Name: {1} - ID: {2} - Version: {3} - FileName: {4}, SHA256Hash: {5}";
        public static ModCheckReport Create(Mod mod)
        {
            return new ModCheckReport()
            {
                Id = mod.Info.ModID,
                Name = mod.Info.Name,
                Version = mod.Info.Version,
                FileName = mod.FileName,
                SourceType = (int)mod.SourceType,
                Fingerprint = ExtraMath.Sha256HashMod(mod)
            };
        }

        public string GetString()
        {
            return string.Format(unformattedString, Enum.GetName(typeof(EnumModSourceType), SourceType), Name, Id, Version, FileName, Fingerprint);
        }
    }
}
