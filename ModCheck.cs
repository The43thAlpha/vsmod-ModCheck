using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Server;

[assembly: ModInfo("ModCheck", 
    Side = "Universal",
    Description = "Ensures that clients only use mods approved by a server, including client-only mods.",
    Version = "0.1.0",
    Authors = new[] { "goxmeor", "Novocain", "Yorokobii" }
    )]

namespace ModCheck
{
    internal class ModCheck : ModSystem
    {
        public override double ExecuteOrder() => double.NegativeInfinity;

        internal INetworkChannel? channel = null;
        internal IClientNetworkChannel? CChannel { get => channel as IClientNetworkChannel; }
        internal IServerNetworkChannel? SChannel { get => channel as IServerNetworkChannel; }

        internal string? lastPlayer;

        internal AllowList allowList = new AllowList();
        internal ModCheckServerConfig config = new ModCheckServerConfig();

        internal Dictionary<string, DateTime> nonReportingTimeByUID = new Dictionary<string, DateTime>();
        internal Dictionary<string, List<ModCheckReport>> recentUnrecognizedReportsByUID = new Dictionary<string, List<ModCheckReport>>();
        internal double tmpLongestGraceRequired = 0;

        public override void StartPre(ICoreAPI api)
        {
            channel = api.Network.RegisterChannel("modcheck").RegisterMessageType(typeof(ModCheckPacket));

            switch (api.Side)
            {
                case EnumAppSide.Server:
                    StartPreServer(api as ICoreServerAPI);
                    break;
                case EnumAppSide.Client:
                    StartPreClient(api as ICoreClientAPI);
                    break;
                case EnumAppSide.Universal:
                    break;
                default:
                    break;
            }
        }
        
        const string kickTooLong = @"ModCheck: Kicking {0} ({1}) for taking too long to report mods. To change timeout, change 'clientReportGraceSeconds' in modcheck/server.json";

        const string receivedPacket = @"ModCheck: Received a packet from {0} ({1}) after {2} ms";
        const string noTime = @"ModCheck: Internal Error. Packet received from {0} ({1}), but no time was recorded.";

        const string modProblems = @"ModCheck: Problems were found with your mods:";
        const string kickUnrecognized = @"ModCheck: Kicked {0} ({1} for the following unrecognized mod(s):";
        const string toAdd = @"To add all of the above mod fingerprints to the ModCheck allow list, trusting that {0}'s versions are untampered with, type:";
        const string modcheckApproveName = @"/modcheckapprove {0}";
        const string modcheckApproveUid = @"/modcheckapproveuid {0}";
        const string modcheckApproveLast = @"/modcheckapprovelast";
        const string orString = @"or";
        const string kickNoMods = @"Your reports sent was empty, no bypass here!";

        public void StartPreServer(ICoreServerAPI? api)
        {
            config.setApi(api);
            config.Load();
            config.Save();

            foreach (var serverMod in api!.ModLoader.Mods)
            {
                allowList.AddReport(ModCheckReport.Create(serverMod));
            }

            foreach (var allowed in config.AllowedClientMods)
            {
                allowList.AddReport(allowed);
            }

            api!.Event.PlayerNowPlaying += (IServerPlayer player) => 
            {
                nonReportingTimeByUID.Add(player.PlayerUID, DateTime.Now);
                recentUnrecognizedReportsByUID.Remove(player.PlayerUID);
                
                api.World.RegisterCallback((float deltaTime) => {
                    if (nonReportingTimeByUID.ContainsKey(player.PlayerUID))
                    {
                        nonReportingTimeByUID.Remove(player.PlayerUID);
                        api.Logger.Event(string.Format(kickTooLong, player.PlayerName, player.PlayerUID));

                        DisconnectPlayerWithFriendlyMessage(player, "ModCheck: Timed out waiting for your client's report. Please try again?");
                    }
                }, 1000 * config.ClientReportGraceSeconds);
            };

            api.Event.PlayerLeave += (IServerPlayer player) => {
                nonReportingTimeByUID.Remove(player.PlayerUID);
            };

            SChannel!.SetMessageHandler((IServerPlayer byPlayer, ModCheckPacket packet) =>
            {
                if (packet.Reports.Count == 0)
                {
                    DisconnectPlayerWithFriendlyMessage(byPlayer, kickNoMods);
                    return;
                }

                if (nonReportingTimeByUID.TryGetValue(byPlayer.PlayerUID, out var startTime))
                {
                    double totalMs = (DateTime.Now - startTime).TotalMilliseconds;
                    api.Logger.Event(string.Format(receivedPacket, byPlayer.PlayerName, byPlayer.PlayerUID, totalMs));
                    tmpLongestGraceRequired = Math.Max(tmpLongestGraceRequired, totalMs);
                }
                else
                {
                    api.Logger.Error(string.Format(noTime, byPlayer.PlayerName, byPlayer.PlayerUID));
                    return;
                }

                nonReportingTimeByUID.Remove(byPlayer.PlayerUID);

                var unrecognizedReports = new List<ModCheckReport>();
                var modIssuesForClient = new List<string>();

                foreach (var report in packet.Reports)
                {
                    if (allowList.HasErrors(report, out string errors))
                    {
                        unrecognizedReports.Add(report);
                        modIssuesForClient.Add(errors);
                    }
                }

                if (unrecognizedReports.Count > 0)
                {
                    recentUnrecognizedReportsByUID.Add(byPlayer.PlayerUID, unrecognizedReports);
                    string playerName = byPlayer.PlayerName;
                    string playerUID = byPlayer.PlayerUID;
                    
                    StringBuilder disconnectMsg = new StringBuilder(Lang.Get(modProblems));

                    disconnectMsg.AppendLine();

                    foreach (string issue in modIssuesForClient)
                    {
                        disconnectMsg.AppendLine(issue);
                        disconnectMsg.AppendLine();
                    }

                    disconnectMsg.AppendLine(config.ExtraDisconnectMessage);
                    if (config.HelpLink.Length > 0)
                    {
                        disconnectMsg.Append(string.Format("Contact Server At: {0}", config.HelpLink));
                    }

                    DisconnectPlayerWithFriendlyMessage(byPlayer, disconnectMsg.ToString());
                    lastPlayer = byPlayer.PlayerUID;
                    api.Logger.Event(Lang.Get(kickUnrecognized, playerName, playerUID));

                    foreach (var modReport in unrecognizedReports)
                    {
                        api.Logger.Event(modReport.GetString());
                    }
                    api.Logger.Event(string.Format(toAdd, byPlayer.PlayerName));
                    api.Logger.Event(modcheckApproveUid, byPlayer.PlayerUID);
                    api.Logger.Event(orString);
                    api.Logger.Event(modcheckApproveName, byPlayer.PlayerName);
                    api.Logger.Event(orString);
                    api.Logger.Event(modcheckApproveLast);
                }

                api.ChatCommands.GetOrCreate("modcheckapprove")
                    .RequiresPrivilege(Privilege.root)
                    .WithDescription("Approves all mod fingerprints a player was recently kicked for.")
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("player")
                    )
                    .HandleWith((TextCommandCallingArgs args) => {
                        return approveAllByName(api, (string)args.Parsers[0].GetValue());
                    });

                api.ChatCommands.GetOrCreate("modcheckapproveuid")
                    .RequiresPrivilege(Privilege.root)
                    .WithDescription("Approves all mod fingerprints a player was recently kicked for.")
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("player")
                    )
                    .HandleWith((TextCommandCallingArgs args) => {
                        return approveAllByUid(api, (string)args.Parsers[0].GetValue());
                    });

                api.ChatCommands.GetOrCreate("modcheckapprovelast")
                    .RequiresPrivilege(Privilege.root)
                    .WithDescription("Approves all mod fingerprints of the last kicked player.")
                    .HandleWith(_ => {
                        return approveAllByUid(api, lastPlayer);
                    });

                api.ChatCommands.GetOrCreate("modchecklongestgrace")
                    .RequiresPrivilege(Privilege.chat)
                    .WithDescription("Shows longest grace time required for a player to join.")
                    .RequiresPlayer()
                    .HandleWith(_ => {
                        return TextCommandResult.Success(string.Format("Longest Grace Required is {0} ms.", tmpLongestGraceRequired));
                    });
            });
        }

        private TextCommandResult approveAllByUid(ICoreServerAPI? api, string? playerUid) {
            if (recentUnrecognizedReportsByUID.TryGetValue(playerUid!, out var reportList))
            {
                foreach (var report in reportList)
                {
                    config.AllowedClientMods = config.AllowedClientMods.AddToArray(report);
                    allowList.AddReport(report);
                }
                return TextCommandResult.Success("Ok, added mods to list.");
            }
            else
            {
                return TextCommandResult.Error(string.Format("Unrecognized player UID '{0}'.", playerUid));
            }
        }

        private TextCommandResult approveAllByName(ICoreServerAPI? api, string playerName) {
            var data = api!.PlayerData.GetPlayerDataByLastKnownName(playerName);

            if (data == null) {
                return TextCommandResult.Error(string.Format("Could not find player with name {0}", playerName));
            }
            else {
                return approveAllByUid(api, data!.PlayerUID);
            }
        }

        private void DisconnectPlayerWithFriendlyMessage(IServerPlayer player, string message)
        {
            ServerMain server = player.GetField<ServerMain>("server");
            ConnectedClient client = player.GetField<ConnectedClient>("client");
            server.DisconnectPlayer(client, message, message);
        }

        public void StartPreClient(ICoreClientAPI? api)
        {
            ModCheckPacket packet = new ModCheckPacket();
            foreach (var mod in api!.ModLoader.Mods)
            {
                ModCheckReport report = ModCheckReport.Create(mod);
                packet.AddReport(report);
            }

            api.Event.IsPlayerReady += (ref EnumHandling handling) =>
            {
                CChannel!.SendPacket(packet);
                return true;
            };
        }
    }
}
