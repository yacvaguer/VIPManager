using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections.Generic;
using Oxide.Core.Plugins;
using UnityEngine;
using System.IO;

namespace Oxide.Plugins
{
    [Info("SteamProfile", "Yac Vaguer", "0.1.0")]
    [Description("Save info about players connecting to the server")]
    public class SteamProfile : RustPlugin
    {
        private List<PlayerSteamProfile> _profiles;
        private PluginConfig config;
        protected override void SaveConfig() => Config.WriteObject(config);

        private const string _filename = "SteamProfiles";

        #region
        [HookMethod("GetProfileById")]
        public Dictionary<string, object> GetProfileById(string id)
        {
            PlayerSteamProfile profile = _profiles.Find(p => p.steamID == id);
            if (profile == null) {
                throw new Exception($"Profile with id {id} not found");
            }

            return new Dictionary<string, object>
            {
                { "displayName", profile.displayName },
                { "steamID", profile.steamID },
                { "steamNames", profile.steamNames },
                { "lastSeen", profile.lastSeen }
            };
        } 
        #endregion

        #region Hook events
        private void OnServerInitialized()
        {
            log = new Log(config, this);
            _profiles = Interface.Oxide.DataFileSystem.ReadObject<List<PlayerSteamProfile>>(_filename) ?? new List<PlayerSteamProfile>();
            log.Debug($"Loaded {_profiles.Count} profiles");
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            log.Debug($"Player connected {player.displayName}");
            PlayerConnecting(player);
        }

        protected override void LoadDefaultConfig()
        {
            config = PluginConfig.DefaultConfig();
            SaveConfig();
            Puts("Creation of the configuration file completed");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<PluginConfig>();
        }
        #endregion

        private void PlayerConnecting(BasePlayer player)
        {
            if (player == null) {
                log.Debug("Player is null");
                return;
            }

            if (PlayerExists(player)) {
                SaveExistingPlayer(player);
            } else {
                AddNewPlayer(player);
            }

            Save();

        }

        private bool PlayerExists(BasePlayer player) => _profiles.Find(p => p.steamID == player.UserIDString) != null;

        private void SaveExistingPlayer(BasePlayer player)
        {
            PlayerSteamProfile existingProfile = _profiles.Find(p => p.steamID == player.UserIDString);

            if (!existingProfile.steamNames.Contains(player.displayName)) {
                existingProfile.steamNames.Add(player.displayName);
                existingProfile.displayName = player.displayName;
            }
            existingProfile.lastSeen = DateTime.Now;

            log.Debug($"Player {player.displayName} already exists");
        }

        private void AddNewPlayer(BasePlayer player)
        {
            PlayerSteamProfile newProfile = new PlayerSteamProfile {
                displayName = player.displayName,
                steamID = player.UserIDString,
                lastSeen = DateTime.Now
            };
            newProfile.steamNames.Add(player.displayName);

            _profiles.Add(newProfile);
            log.Debug($"Player {player.displayName} added");
        }
        public void Save()
        {
            Interface.Oxide.DataFileSystem.WriteObject("SteamProfiles", _profiles);
            log.Debug($"Saved {_profiles.Count} profiles");
        }

        public List<PlayerSteamProfile> Load()
        {
            _profiles = Interface.Oxide.DataFileSystem.ReadObject<List<PlayerSteamProfile>>("SteamProfiles");
            log.Debug($"Loaded {_profiles.Count} profiles");
            return _profiles;
        }

        #region Configuration
        private class PluginConfig
        {
            [JsonProperty("Developer mode (verbose debug information")] public bool debug { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig() {
                    debug = true
                };
            }
        }

        #endregion

        #region Logging 
        private Log log;

        private class Log
        {
            private PluginConfig pluginConfig;
            private string logFilePath;
            private string logDirectory;


            const string ERROR_LEVEL = "ERROR";
            const string WARNING_LEVEL = "WARNING";
            const string INFO_LEVEL = "INFO";
            const string DEBUG_LEVEL = "DEBUG";

            private SteamProfile plugin;

            public Log(PluginConfig config, SteamProfile plugin)
            {
                pluginConfig = config;

                this.plugin = plugin;

                logDirectory = $"{Interface.Oxide.LogDirectory}/SteamProfile";

                // Check if the log directory exists, if not create it
                if (!Directory.Exists(logDirectory)) {
                    Directory.CreateDirectory(logDirectory);
                }

                /**
                 * Ideally every day we generate a new file so you can rotate or directly remove olders
                 */
                logFilePath = $"{logDirectory}/Log_{DateTime.Now:yyyy-MM-dd}.txt";

            }

            public void Info(string message)
            {
                LogIntoFile(message, INFO_LEVEL);
            }

            public void Debug(string message)
            {
                LogIntoFile(message, DEBUG_LEVEL);
            }

            public void Error(string message)
            {
                LogIntoFile(message, ERROR_LEVEL);
            }

            public void Warning(string message)
            {
                LogIntoFile(message, WARNING_LEVEL);
            }

            private void LogIntoFile(string message, string type)
            {
                if (pluginConfig.debug == false && (type == INFO_LEVEL || type == DEBUG_LEVEL)) {
                    return;
                }

                var logMessage = $"{DateTime.Now:HH:mm:ss} [SteamProfile] [{type}] {message}";

                try {
                    File.AppendAllText(logFilePath, logMessage + Environment.NewLine);

                    if ((type == ERROR_LEVEL || type == WARNING_LEVEL) || pluginConfig.debug == true) {
                        plugin.Puts($"[{type}] {logMessage}");
                    }
                } catch (Exception ex) {
                    plugin.Puts("[ERROR] We couldn't save the logs into file, so we will show it in the console");
                    plugin.Puts($"[ERROR] {ex.Message}");
                    plugin.Puts(logMessage);
                }
            }
        }
        #endregion

        #region PlayerSteamProfile class
        public class PlayerSteamProfile
        {
            public string? displayName { get; set; }
            public string? steamID { get; set; }
            public List<string> steamNames { get; set; } = new List<string>();
            public DateTime lastSeen { get; set; }
        }
        #endregion
    }
}
