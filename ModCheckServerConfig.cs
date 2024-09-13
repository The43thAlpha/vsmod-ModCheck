using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Server;

namespace ModCheck
{
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    internal class ModCheckServerConfig
    {
        //Only forward versions if new default value needs pushed to all configs
        private static readonly Dictionary<string, string> Versions = new Dictionary<string, string>()
        {
            { @"configVersionByField",      @"1.0.0"},
            { @"clientReportGraceSeconds",  @"1.0.0"},
            { @"clientApproveGraceSeconds",  @"1.0.0"},
            { @"extraDisconnectMessage",    @"1.0.0"},
            { @"allowedClientMods",         @"1.0.0"},
        };

        private ICoreServerAPI? sapi = null!;

        [JsonProperty]
        private int? clientReportGraceSeconds = 15;

        [JsonProperty]
        private int? clientApproveGraceSeconds = 30;

        [JsonProperty]
        private string extraDisconnectMessage = @"Please contact the server owner with any problems or to request new mods be added to the whitelist.";

        [JsonProperty]
        private ModCheckReport[] allowedClientMods = new ModCheckReport[0];

        [JsonProperty]
        private Dictionary<string, string> configVersionByField = Versions;

        [JsonProperty]
        private string helpLink = "";

        public ModCheckServerConfig() { }

        public void setApi(ICoreServerAPI? sapi)
        {
            if(sapi != null) {
                this.sapi = sapi;
            }
        }

        public int ClientApproveGraceSeconds
        {
            get { Load(); return clientApproveGraceSeconds!.Value; }
            set { clientApproveGraceSeconds = value; Save(); }
        }

        public int ClientReportGraceSeconds
        {
            get { Load(); return clientReportGraceSeconds!.Value; }
            set { clientReportGraceSeconds = value; Save(); }
        }

        public Dictionary<string, string> ConfigVersionByField
        { 
            get { Load(); return configVersionByField; } 
            set { configVersionByField = value; Save(); } 
        }

        public string HelpLink { 
            get { Load(); return helpLink; } 
            set { helpLink = value; Save(); } 
        }

        public string ExtraDisconnectMessage
        {
            get { Load(); return extraDisconnectMessage; }
            set { extraDisconnectMessage = value; Save();}
        }

        public ModCheckReport[] AllowedClientMods
        {
            get { Load(); return allowedClientMods; }   
            set { allowedClientMods = value; Save(); }
        }

        public void Save()
        {
            if(sapi != null) {
                sapi.StoreModConfig(this, "modcheck/server.json");
            }
        }

        public void Load()
        {
            try
            {
                var newConfig = new ModCheckServerConfig();
                newConfig.setApi(sapi);

                var conf = sapi?.LoadModConfig<ModCheckServerConfig>("modcheck/server.json") ?? newConfig;

                clientReportGraceSeconds = conf?.clientReportGraceSeconds ?? newConfig.ClientReportGraceSeconds;
                clientApproveGraceSeconds = conf?.clientApproveGraceSeconds ?? newConfig.ClientApproveGraceSeconds;
                extraDisconnectMessage = conf?.extraDisconnectMessage ?? newConfig.extraDisconnectMessage;
                allowedClientMods = conf?.allowedClientMods ?? newConfig.allowedClientMods;
                configVersionByField = conf?.configVersionByField ?? newConfig.configVersionByField;
                helpLink = conf?.helpLink ?? newConfig.helpLink; 
                var fieldNames = AccessTools.GetFieldNames(this);
                fieldNames.Remove("sapi");
                fieldNames.Remove("Versions");
                
                foreach (string field in fieldNames)
                {
                    if (Versions.TryGetValue(field, out string? version0) && configVersionByField.TryGetValue(field, out string? version1))
                    {
                        var v0 = Version.Parse(version0);
                        var v1 = Version.Parse(version1);
                        if (v0 > v1)
                        {
                            this.SetField(field, newConfig.GetField<object>(field));
                            conf!.configVersionByField[field] = version0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sapi!.Logger.Error("Malformed ModConfig file modcheck/server.json, Exception: \n {0}", ex.StackTrace);
            }
        }
    }
}
