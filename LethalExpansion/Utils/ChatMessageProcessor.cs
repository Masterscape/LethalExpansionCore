﻿using LethalExpansion.Patches;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using static LethalExpansion.Utils.NetworkPacketManager;

namespace LethalExpansion.Utils
{
    internal class ChatMessageProcessor
    {
        public static bool ProcessMessage(string message)
        {
            if (!Regex.IsMatch(message, @"^\[sync\].*\[sync\]$"))
            {
                return false;
            }

            try
            {
                string content = Regex.Match(message, @"^\[sync\](.*)\[sync\]$").Groups[1].Value;

                string[] parts = content.Split('|');
                if (parts.Length != 3)
                {
                    return true;
                }

                PacketType type = (PacketType)int.Parse(parts[0]);
                string[] mid = parts[1].Split('>');
                ulong sender = ulong.Parse(mid[0]);
                long destination = long.Parse(mid[1]);
                string[] last = parts[2].Split('=');
                string header = last[0];
                string packet = last[1];

                if (destination != -1 && (ulong) destination != RoundManager.Instance.NetworkManager.LocalClientId)
                {
                    return true;
                }

                if (sender != 0)
                {
                    NetworkPacketManager.Instance.CancelTimeout((long)sender);
                }

                LethalExpansion.Log.LogInfo(message);
                switch (type)
                {
                    case PacketType.Request:
                        ProcessRequest(sender, header, packet);
                        break;
                    case PacketType.Data:
                        ProcessData(sender, header, packet);
                        break;
                    case PacketType.Other:
                        LethalExpansion.Log.LogInfo("Unsupported type.");
                        break;
                    default:
                        LethalExpansion.Log.LogInfo("Unrecognized type.");
                        break;
                }

                return true;
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex);
                return false;
            }
        }

        private static void ProcessClientInfoRequest(ulong sender, string header, string packet)
        {
            if (LethalExpansion.ishost || sender != 0)
            {
                return;
            }

            string configPacket = $"{LethalExpansion.ModVersion}-";
            foreach (var bundle in AssetBundlesManager.Instance.assetBundles)
            {
                configPacket += $"{bundle.Key}v{bundle.Value.Item2.GetVersion()}&";
            }
            configPacket = configPacket.Remove(configPacket.Length - 1);

            NetworkPacketManager.Instance.SendPacket(PacketType.Data, "clientinfo", configPacket, 0);
        }

        private static void ProcessHostConfigRequest(ulong sender, string header, string packet)
        {
            if (!LethalExpansion.ishost || sender == 0)
            {
                return;
            }

            NetworkPacketManager.Instance.SendPacket(NetworkPacketManager.PacketType.Request, "clientinfo", string.Empty, (long)sender);
        }

        private static void ProcessHostWeathersRequest(ulong sender, string header, string packet)
        {
            if (!LethalExpansion.ishost || sender == 0 || !LethalExpansion.weathersReadyToShare)
            {
                return;
            }

            string weathers = string.Empty;
            foreach (var weather in StartOfRound_Patch.currentWeathers)
            {
                weathers += weather + "&";
            }
            weathers = weathers.Remove(weathers.Length - 1);

            NetworkPacketManager.Instance.SendPacket(PacketType.Data, "hostweathers", weathers, (long)sender, false);
        }

        private static void ProcessRequest(ulong sender, string header, string packet)
        {
            try
            {
                switch (header)
                {
                    case "clientinfo": //client receive info request from host
                        ProcessClientInfoRequest(sender, header, packet);
                        break;
                    case "hostconfig": //host receive config request from client
                        ProcessHostConfigRequest(sender, header, packet);
                        break;
                    case "hostweathers": //host receive weather request from client
                        ProcessHostWeathersRequest(sender, header, packet);
                        break;
                    default:
                        LethalExpansion.Log.LogInfo("Unrecognized command.");
                        break;
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex);
            }
        }

        private static void ProcessClientInfo(ulong sender, string header, string packet)
        {
            if (!LethalExpansion.ishost || sender == 0)
            {
                return;
            }

            string[] values;
            if (packet.Contains('-'))
            {
                values = packet.Split('-');
            }
            else
            {
                values = new string[1] { packet };
            }

            string bundles = string.Empty;
            foreach (var bundle in AssetBundlesManager.Instance.assetBundles)
            {
                bundles += $"{bundle.Key}v{bundle.Value.Item2.GetVersion()}&";
            }

            if (bundles.Length > 0)
            {
                bundles = bundles.Remove(bundles.Length - 1);
            }

            if (values[0] != LethalExpansion.ModVersion.ToString())
            {
                if (StartOfRound.Instance.ClientPlayerList.ContainsKey(sender))
                {
                    LethalExpansion.Log.LogError($"Kicking {sender} for wrong version.");
                    NetworkPacketManager.Instance.SendPacket(PacketType.Data, "kickreason", "Wrong version.", (long)sender);
                    StartOfRound.Instance.KickPlayer(StartOfRound.Instance.ClientPlayerList[sender]);
                }

                return;
            }
            else if (values.Length > 1 && values[1] != bundles)
            {
                if (StartOfRound.Instance.ClientPlayerList.ContainsKey(sender))
                {
                    LethalExpansion.Log.LogError($"Kicking {sender} for wrong bundles.");
                    NetworkPacketManager.Instance.SendPacket(PacketType.Data, "kickreason", "Wrong bundles.", (long)sender);
                    StartOfRound.Instance.KickPlayer(StartOfRound.Instance.ClientPlayerList[sender]);
                }

                return;
            }

            string config = string.Empty;
            foreach (var item in ConfigManager.Instance.GetAll())
            {
                switch (item.type.Name)
                {
                    case "Int32":
                        config += "i" + ((int)item.Value).ToString(CultureInfo.InvariantCulture);
                        break;
                    case "Single":
                        config += "f" + ((float)item.Value).ToString(CultureInfo.InvariantCulture);
                        break;
                    case "Boolean":
                        config += "b" + ((bool)item.Value);
                        break;
                    case "String":
                        config += "s" + item;
                        break;
                    default:
                        break;
                }
                config += "&";
            }
            config = config.Remove(config.Length - 1);
            NetworkPacketManager.Instance.SendPacket(PacketType.Data, "hostconfig", config, (long)sender);
        }

        private static void ProcessHostConfig(ulong sender, string header, string packet)
        {
            if (LethalExpansion.ishost || sender != 0)
            {
                return;
            }

            string[] values = packet.Split('&');

            LethalExpansion.Log.LogInfo("Received host config: " + packet);

            for (int i = 0; i < values.Length; i++)
            {
                if (i < ConfigManager.Instance.GetCount())
                {
                    if (ConfigManager.Instance.MustBeSync(i))
                    {
                        ConfigManager.Instance.SetItemValue(i, values[i].Substring(1), values[i][0]);
                    }
                }
            }

            LethalExpansion.hostDataWaiting = false;
            LethalExpansion.Log.LogInfo("Updated config");
        }

        private static void ProcessHostWeathers(ulong sender, string header, string packet)
        {
            if (LethalExpansion.ishost || sender != 0)
            {
                return;
            }

            string[] values = packet.Split('&');

            LethalExpansion.Log.LogInfo("Received host weathers: " + packet);

            StartOfRound_Patch.currentWeathers = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int tmp = 0;
                if (int.TryParse(values[i], out tmp))
                {
                    StartOfRound_Patch.currentWeathers[i] = tmp;
                    StartOfRound.Instance.levels[i].currentWeather = (LevelWeatherType)tmp;
                }
            }
        }

        private static void ProcessKickReason(ulong sender, string header, string packet)
        {
            if (LethalExpansion.ishost || sender != 0)
            {
                return;
            }

            LethalExpansion.lastKickReason = packet;
        }

        private static void ProcessData(ulong sender, string header, string packet)
        {
            try
            {
                switch (header)
                {
                    case "clientinfo": //host receive info from client
                        ProcessClientInfo(sender, header, packet);
                        break;
                    case "hostconfig": //client receive config from host
                        ProcessHostConfig(sender, header, packet);
                        break;
                    case "hostweathers": //client receive weathers from host
                        ProcessHostWeathers(sender, header, packet);
                        break;
                    case "kickreason":
                        ProcessKickReason(sender, header, packet);
                        break;
                    default:
                        LethalExpansion.Log.LogInfo("Unrecognized property.");
                        break;
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex);
            }
        }
    }
}
