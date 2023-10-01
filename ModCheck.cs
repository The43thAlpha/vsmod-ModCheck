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
    Version = "0.3.1",
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
        internal List<string> blacklist = new List<string>();
        internal List<string> whitelist = new List<string>();
        internal ModCheckServerConfig config = new ModCheckServerConfig();

        internal Dictionary<string, DateTime> nonReportingTimeByUID = new Dictionary<string, DateTime>();
        internal Dictionary<string, List<ModCheckReport>> recentUnrecognizedReportsByUID = new Dictionary<string, List<ModCheckReport>>();
        internal List<string> playersToKick = new List<string>();
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
        
        class Logs {
            // Server side console logs
            public static string receivedPacket = @"ModCheck: Received a packet from {0} ({1}) after {2} ms";
            public static string noTime = @"ModCheck: Internal Error. Packet received from {0} ({1}), but no time was recorded.";

            // Client side logs
            public static string modProblems = @"ModCheck: Problems were found with your mods:";
            public static string kickNoMods = @"Your reports sent was empty, no bypass here!";
            public static string reportTimeout = @"ModCheck: Timed out waiting for your client's report. Please try again?";

            // Server side chat logs
            public static string kickTooLong = @"ModCheck: Kicking {0} ({1}) for taking too long to report mods. To change timeout, change 'clientReportGraceSeconds' in modcheck/server.json";
            public static string kickNotApproved = @"ModCheck: Kicking {0} ({1}) because no moderator approved their mod list. To change timeout, change 'clientApproveGraceSeconds' in modcheck/server.json";

            public static string modsUnrecognized = @"ModCheck: {0} ({1}) connected with the following unrecognized mod(s):";
            public static string toAdd = @"To add all of the above mod fingerprints to the ModCheck allow list, trusting that {0}'s versions are untampered with, type on of the following commands:";
            public static string modcheckApproveName = @"/modcheckapprove {0}";
            public static string modcheckApproveUid = @"/modcheckapproveuid {0}";
            public static string modcheckApproveLast = @"/modcheckapprovelast";
            public static string kickTimeout = @"The player {0} will be kicked in {1} seconds otherwise.";

            public static string blacklistKick = @"Player {0} kicked for using the following blacklisted mods:";
        }

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
                        api.Logger.Event(Logs.kickTooLong, player.PlayerName, player.PlayerUID);

                        DisconnectPlayerWithFriendlyMessage(player, Logs.reportTimeout);
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
                    DisconnectPlayerWithFriendlyMessage(byPlayer, Logs.kickNoMods);
                    return;
                }

                if (nonReportingTimeByUID.TryGetValue(byPlayer.PlayerUID, out var startTime))
                {
                    double totalMs = (DateTime.Now - startTime).TotalMilliseconds;
                    api.Logger.Event(string.Format(Logs.receivedPacket, byPlayer.PlayerName, byPlayer.PlayerUID, totalMs));
                    tmpLongestGraceRequired = Math.Max(tmpLongestGraceRequired, totalMs);
                }
                else
                {
                    api.Logger.Error(string.Format(Logs.noTime, byPlayer.PlayerName, byPlayer.PlayerUID));
                    return;
                }

                nonReportingTimeByUID.Remove(byPlayer.PlayerUID);

                var unrecognizedReports = new List<ModCheckReport>();
                var modIssuesForClient = new List<string>();
                var blacklistedMods = new List<string>();

                foreach (var report in packet.Reports)
                {
                    if (blacklist.Contains(report.Id)) {
                        blacklistedMods.Add(report.Id);
                    }

                    if (!whitelist.Contains(report.Id) && allowList.HasErrors(report, out string errors))
                    {
                        unrecognizedReports.Add(report);
                        modIssuesForClient.Add(errors);
                    }
                }

                if (blacklistedMods.Count() != 0) {
                    StringBuilder log = new StringBuilder(Logs.blacklistKick);
                    foreach (string mod in blacklistedMods) {
                        log.Append(string.Format(" {0},", mod));
                    }

                    api.Logger.Event(string.Format(log.ToString(), byPlayer.PlayerName));
                    api.BroadcastMessageToAllGroups(string.Format(log.ToString(), byPlayer.PlayerName), EnumChatType.AllGroups);
                    DisconnectPlayerWithFriendlyMessage(byPlayer, string.Format(log.ToString(), byPlayer.PlayerName));
                    return;
                }

                if (unrecognizedReports.Count() > 0)
                {
                    recentUnrecognizedReportsByUID.Add(byPlayer.PlayerUID, unrecognizedReports);
                    
                    StringBuilder disconnectMsg = new StringBuilder(Lang.Get(Logs.modProblems));

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
                    // TODO: Simplifie disconnect msg for client and chat

                    api.World.RegisterCallback(_ => {
                        if (playersToKick.Contains(byPlayer.PlayerUID))
                        {
                            playersToKick.Remove(byPlayer.PlayerUID);
                            api.Logger.Event(Logs.kickNotApproved, byPlayer.PlayerName, byPlayer.PlayerUID);
                            api.BroadcastMessageToAllGroups(string.Format(Logs.kickNotApproved, byPlayer.PlayerName, byPlayer.PlayerUID), EnumChatType.AllGroups);

                            DisconnectPlayerWithFriendlyMessage(byPlayer, disconnectMsg.ToString());
                        }
                    }, 1000 * config.ClientApproveGraceSeconds);
                    playersToKick.Add(byPlayer.PlayerUID);
                    lastPlayer = byPlayer.PlayerUID;

                    StringBuilder toApproveMessage = new StringBuilder(string.Format(Logs.modsUnrecognized, byPlayer.PlayerName, byPlayer.PlayerUID));

                    foreach (var modReport in unrecognizedReports)
                    {
                        toApproveMessage.AppendLine(modReport.GetString());
                    }
                    toApproveMessage.AppendLine(string.Format(Logs.toAdd, byPlayer.PlayerName));
                    toApproveMessage.AppendLine(string.Format(Logs.modcheckApproveUid, byPlayer.PlayerUID));
                    toApproveMessage.AppendLine(string.Format(Logs.modcheckApproveName, byPlayer.PlayerName));
                    toApproveMessage.AppendLine(string.Format(Logs.modcheckApproveLast));
                    toApproveMessage.AppendLine(string.Format(Logs.kickTimeout, byPlayer.PlayerName, config.ClientApproveGraceSeconds));

                    api.Logger.Event(toApproveMessage.ToString());
                    api.BroadcastMessageToAllGroups(toApproveMessage.ToString(), EnumChatType.AllGroups);
                }

                api.ChatCommands.GetOrCreate("modcheckapprove")
                    .RequiresPrivilege(Privilege.root)
                    .WithDescription("Approves all mod fingerprints of the player given as a parameter.")
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("player")
                    )
                    .HandleWith((TextCommandCallingArgs args) => {
                        return approveAllByName(api, (string)args.Parsers[0].GetValue());
                    });

                api.ChatCommands.GetOrCreate("modcheckapproveuid")
                    .RequiresPrivilege(Privilege.root)
                    .WithDescription("Approves all mod fingerprints of the player given as a parameter.")
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("player")
                    )
                    .HandleWith((TextCommandCallingArgs args) => {
                        return approveAllByUid(api, (string)args.Parsers[0].GetValue());
                    });

                api.ChatCommands.GetOrCreate("modcheckapprovelast")
                    .RequiresPrivilege(Privilege.root)
                    .WithDescription("Approves all mod fingerprints of the last player that joined.")
                    .HandleWith(_ => {
                        return approveAllByUid(api, lastPlayer);
                    });

                api.ChatCommands.GetOrCreate("modcheckblacklistmod")
                    .RequiresPrivilege(Privilege.root)
                    .WithDescription("Blacklist the mod with the id given as parameter")
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("id")
                    )
                    .HandleWith((TextCommandCallingArgs args) => {
                        string id = (string)args.Parsers[0].GetValue();

                        blacklist.Add(id);

                        string log = "Blacklisted mod with id {0}.";
                        api.Logger.Event(log, id);
                        return TextCommandResult.Success(string.Format(log, id));
                    });

                api.ChatCommands.GetOrCreate("modcheckwhitelistmod")
                    .RequiresPrivilege(Privilege.root)
                    .WithDescription("Whitelist the mod with the id given as parameter")
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("id")
                    )
                    .HandleWith((TextCommandCallingArgs args) => {
                        string id = (string)args.Parsers[0].GetValue();

                        whitelist.Add(id);

                        string log = "Whitelisted mod with id {0}.";
                        api.Logger.Event(log, id);
                        return TextCommandResult.Success(string.Format(log, id));
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
                    playersToKick.Remove(playerUid!);
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
