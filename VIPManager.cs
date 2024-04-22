using System;
using System.Collections.Generic;
using System.IO;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.Net;

namespace Oxide.Plugins
{
    [Info("ToroVIPManager", "Yac Vaguer", "1.0.1")]
    [Description("Manage VIP status with expiration")]
    class ToroVIPManager : RustPlugin
    {
        private Dictionary<ulong, VIPEntry> activeVIPs = new Dictionary<ulong, VIPEntry>();
        private string dataFile = Path.Combine(Interface.Oxide.DataDirectory, "VIPManager/active-vip");
        private ConfigData configData;
        private VIPEntry vipEntry;

        [PluginReference]
        Plugin SteamProfile;

        [ConsoleCommand("vip.add")]
        void GrantUserVIPStatus(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) {
                Puts("You don't have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length != 2) {
                Puts("Usage: vip.add <STEAMID> <REWARD_POINTS>");
                return;
            }

            string steamID = arg.Args[0];
            string serverReward = arg.Args[1];
            ulong userID;

            if (!ulong.TryParse(steamID, out userID)) {
                Puts("Invalid STEAMID");
                return;
            }

            Dictionary<string, object> profile = GetSteamProfile(userID);

            Puts("------------------------------");
            Puts(" ");
            Puts($"Setting VIP Status for {profile["displayName"]}");
            AddOrUpdateVIPToDB(userID);
            AddUserToVipGroup(userID);
            AddPerks(userID);
            AddServerRewards(userID, serverReward);

            Puts($"Added/extended VIP for {profile["displayName"]}");
            Puts("");

            activeVIPs[userID] = vipEntry;
            SaveData();

            Puts("------------------------------");

            SendDiscordMessage($"User {profile["displayName"]} activated as VIP for 30 days");
        }

        private Dictionary<string, object> GetSteamProfile(ulong userID)
        {
            Puts("Loading Steam Profile.... ");
            Dictionary<string, object> profile = SteamProfile.Call("GetProfileById", userID.ToString()) as Dictionary<string, object>;

            if (profile == null) {
                Puts("Profile is null .... ");
                profile = new Dictionary<string, object>
                {
                    { "displayName", "unknown" },
                    { "steamID", userID.ToString() },
                    { "lastSeen",  DateTime.Now }
                };

            }

            return profile;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            //If player is VIP, we want to check if is expiring soon and send a message
            if (activeVIPs.TryGetValue(player.userID, out VIPEntry vipEntry)) {
                CheckIfVIPAboutToExpire(player, vipEntry);
                return;
            }
            CommunicateAboutTheVIP(player);
        }

        private void CommunicateAboutTheVIP(BasePlayer player)
        {
            Puts("Player is not VIP");
            timer.Once(10f, () => {
                SendReply(player, $"<size=14>Hola <color=#eeecad>@{player.displayName}</color>, si quieres ser VIP contactate con Llanero en Discord tenemos planes desde $1000 Argentinos o 2USD</size>");
            });
        }

        private void CheckIfVIPAboutToExpire(BasePlayer player, VIPEntry vipEntry)
        {
            Puts($"Player is VIP with Status {vipEntry.Status}");
            if (vipEntry.Status == "disabled") {
                CommunicateAboutTheVIP(player);
                return;
            }

            if (vipEntry.Expiration < DateTime.Now.AddDays(5)) {
                
                int daysLeft = (vipEntry.Expiration.Date - DateTime.Now.Date).Days;
                Puts("Player VIP expire in " + daysLeft);
                string message = daysLeft == 0 ? "Hoy" : "en " + daysLeft + " dias";
                timer.Once(10f, () => {
                    SendReply(player, $"<size=14>Hi <color=#eeecad>{player.displayName}</color> your VIP expire {message}. If you want to renew it hit me on discord</size>");
                });
                    
            }
        }

        void OnServerInitialized()
        {
            LoadData();
            LoadConfigVariables();
            CheckExpiredVIPs();
            ReportCurrentActiveVIP();
        }

        private void CheckExpiredVIPs()
        {
            Puts("------------------------------");
            Puts("");
            Puts("Checking Expired VIPs");
            List<ulong> expiredVIPs = new List<ulong>();

            foreach (KeyValuePair<ulong, VIPEntry> kvp in activeVIPs) {
                ulong userID = kvp.Key;
                VIPEntry vipEntry = kvp.Value;

                if (vipEntry != null && vipEntry.Expiration < DateTime.Now && vipEntry.Status == "enabled") {
                    expiredVIPs.Add(userID);
                }
            }

            foreach (ulong userID in expiredVIPs) {
                ExpireVIP(userID);
            }

            Puts("Check Finished");
            Puts("------------------------------");
            Puts("");
        }

        private void LoadData()
        {
            activeVIPs = Interface.GetMod().DataFileSystem.ReadObject<Dictionary<ulong, VIPEntry>>(dataFile);
        }

        private void SaveData()
        {
            Interface.GetMod().DataFileSystem.WriteObject(dataFile, activeVIPs);
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();
        }

        private void SaveConfig()
        {
            Config.WriteObject(configData, true);
        }

        VIPEntry GetVipEntry(ulong userID)
        {
            if (!activeVIPs.TryGetValue(userID, out VIPEntry vipEntry)) {
                vipEntry = new VIPEntry();
            }

            return vipEntry;
        }

        void AddOrUpdateVIPToDB(ulong userID)
        {
            vipEntry = GetVipEntry(userID);

            if (vipEntry.Expiration > DateTime.Now) {
                vipEntry.Expiration = vipEntry.Expiration.AddDays(30);
            } else {
                vipEntry.Expiration = DateTime.Now.AddDays(30);
            }

            vipEntry.Status = "enabled";
            Puts($"User {userID} added to the Database as VIP");
            Puts("");
        }

        void AddUserToVipGroup(ulong userID)
        {
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "oxide.usergroup add " + userID.ToString() + " vip");
            Puts($"User {userID} Granted VIP features");
            Puts("");
        }

        void ExpireVIP(ulong userID)
        {
            if (activeVIPs.TryGetValue(userID, out VIPEntry vipEntry)) {

                ConsoleSystem.Run(ConsoleSystem.Option.Server, "oxide.usergroup remove " + userID.ToString() + " vip");
                vipEntry.Status = "disabled";

                activeVIPs[userID] = vipEntry;
                SaveData();

                Dictionary<string, object> profile = GetSteamProfile(userID);
                Puts($"{profile["displayName"]} VIP expired and it was removed from our VIPs");
                SendDiscordMessage($"{profile["displayName"]} VIP expired and it was removed from our VIPs");
            }
        }

        void AddServerRewards(ulong userID, string serverRewardPoints)
        {
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "sr add " + userID.ToString() + " " + serverRewardPoints);
            Puts("We added " + serverRewardPoints + " to " + userID);
            Puts("");
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "sr check " + userID.ToString());
        }

        void AddPerks(ulong userID)
        {

            if (!vipEntry.Bonus) {
                vipEntry.Bonus = true;
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zl.lvl " + userID.ToString() + " * +5");
                Puts($"We added Perks to {userID}");
                return;
            }

            Puts("Perks were given before");

        }

        private void ReportCurrentActiveVIP()
        {
            string message = "------------------------------------------------\n" +
                "\nCurrent Active VIPs: \n";
            foreach (KeyValuePair<ulong, VIPEntry> vip in activeVIPs) {

                if (vip.Value.Status == "enabled") {
                    Dictionary<string, object> profile = GetSteamProfile(vip.Key);
                    message += $"{vip.Value.Expiration.ToString("d-M-yyyy")} - {profile["displayName"]} \n";
                }
            }
            message += "------------------------------------------------\n";
            SendDiscordMessage(message);
         }

        private void SendDiscordMessage(string message)
        {
            if (configData == null) {
                Puts("Config data is not loaded.");
                return;
            }

            if (string.IsNullOrEmpty(configData.DiscordWebhookUrl)) {
                Puts("Discord webhook URL is not configured.");
                return;
            }

            var payload = JsonConvert.SerializeObject(new { content = message });

            using (var client = new WebClient()) {
                client.Headers[HttpRequestHeader.ContentType] = "application/json";

                try {
                    client.UploadString(configData.DiscordWebhookUrl, "POST", payload);
                } catch (Exception ex) {
                    Puts($"Error sending Discord message: {ex.Message}");
                }
            }
        }

        public class VIPEntry
        {
            public DateTime Expiration { get; set; }
            public bool Bonus { get; set; }
            public string Status { get; set; }
        }

        class ConfigData
        {
            public string DiscordWebhookUrl { get; set; }
        }

        protected override void LoadDefaultConfig()
        {
            configData = new ConfigData {
                DiscordWebhookUrl = "WEB HOOK URL"
            };

            SaveConfig();
        }

    }
}
