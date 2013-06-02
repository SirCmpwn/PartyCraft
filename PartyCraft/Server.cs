﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Craft.Net.Data;
using Craft.Net.Data.Generation;
using Craft.Net.Server;
using System.Net;
using Craft.Net.Server.Events;
using Craft.Net;
using PartyCraft.API;

namespace PartyCraft
{
    public class Server : IServer
    {
        public MinecraftServer MinecraftServer { get; set; }
        public ISettingsProvider SettingsProvider { get; set; }
        public event EventHandler<ChatMessageEventArgs> ChatMessage;
        public event EventHandler<TabCompleteEventArgs> TabComplete;
        public SpamController spamController = new SpamController();

        public Server(ISettingsProvider settingsProvider)
        {
            SettingsProvider = settingsProvider;
            var port = SettingsProvider.Get<int>("server.port");
            MinecraftServer = new MinecraftServer(new IPEndPoint(IPAddress.Any, port));
            MinecraftServer.Settings.MotD = SettingsProvider.Get<string>("server.motd");
            MinecraftServer.Settings.OnlineMode = SettingsProvider.Get<bool>("server.onlinemode");
            MinecraftServer.ChatMessage += MinecraftServerOnChatMessage;
            MinecraftServer.PlayerLoggedIn += MinecraftServerOnPlayerLoggedIn;
            MinecraftServer.PlayerLoggedOut += MinecraftServerOnPlayerLoggedOut;
            MinecraftServer.TabComplete += MinecraftServer_TabComplete;
        }

        public void Start()
        {
            MinecraftServer.AddLevel(new Level(Level.GetGenerator(SettingsProvider.Get<string>("level.type")), 
                SettingsProvider.Get<string>("level.name")));
            MinecraftServer.DefaultLevel.GameMode = SettingsProvider.Get<GameMode>("level.gamemode");
            MinecraftServer.Start();
        }

        public void Stop()
        {
            MinecraftServer.Stop();
        }

        public List<string> GetUserGroups(string user)
        {
            var groups = new List<string>(new[] { "server.default" });
            if (SettingsProvider.ContainsKey(user + ".groups"))
                groups.AddRange(SettingsProvider.Get<List<string>>(user + ".groups"));
            return groups;
        }

        public void SetUserGroups(string user, List<string> groups)
        {
            if (groups.Contains("server.default"))
                groups.Remove("server.default");
            SettingsProvider.Set(user + ".groups", groups);
        }

        /// <summary>
        /// Sends a chat message to all members of a specified permission group.
        /// </summary>
        public void SendChatToGroup(string group, string text)
        {
            foreach (var client in MinecraftServer.Clients)
            {
                if (GetUserGroups(client.Username).Contains(group))
                    client.SendChat(text);
            }
        }

        #region Event Handlers

        private void MinecraftServerOnChatMessage(object sender, ChatMessageEventArgs chatMessageEventArgs)
        {
            var internalArgs = new ChatMessageEventArgs(chatMessageEventArgs.Origin, chatMessageEventArgs.RawMessage);
            if (ChatMessage != null)
            {
                ChatMessage(this, internalArgs);
                if (internalArgs.Handled)
                {
                    chatMessageEventArgs.Handled = true;
                    return;
                }
            }
            chatMessageEventArgs.Handled = true;
            if (chatMessageEventArgs.RawMessage.StartsWith("/"))
            {
                if (chatMessageEventArgs.RawMessage.StartsWith("//"))
                {
                    MinecraftServer.SendChat(string.Format(SettingsProvider.Get<string>("chat.format"),
                    chatMessageEventArgs.Origin.Username, chatMessageEventArgs.RawMessage.Substring(1)));
                }
                else
                    Command.ExecuteCommand(this, chatMessageEventArgs.Origin, chatMessageEventArgs.RawMessage);
            }
            else
            {
                if (!spamController.CheckForSpam(chatMessageEventArgs.Origin.Username, chatMessageEventArgs.RawMessage))
                {
                    MinecraftServer.SendChat(string.Format(SettingsProvider.Get<string>("chat.format"),
                        chatMessageEventArgs.Origin.Username, chatMessageEventArgs.RawMessage));
                }
                else
                {
                    chatMessageEventArgs.Origin.SendChat(ChatColors.Yellow + "Please stop spamming!");
                }
            }
        }

        private void MinecraftServerOnPlayerLoggedIn(object sender, PlayerLogInEventArgs playerLogInEventArgs)
        {
            playerLogInEventArgs.Handled = true;
            playerLogInEventArgs.Client.Tags = new Dictionary<string, object>();
            playerLogInEventArgs.Client.Tags.Add("PartyCraft.UserGroups", GetUserGroups(playerLogInEventArgs.Username));
            MinecraftServer.SendChat(string.Format(SettingsProvider.Get<string>("chat.join"), playerLogInEventArgs.Username));
            SpamController.Init(playerLogInEventArgs.Username);
        }

        private void MinecraftServerOnPlayerLoggedOut(object sender, PlayerLogInEventArgs playerLogInEventArgs)
        {
            playerLogInEventArgs.Handled = true;
            MinecraftServer.SendChat(string.Format(SettingsProvider.Get<string>("chat.leave"), playerLogInEventArgs.Username));
            SpamController.Remove(playerLogInEventArgs.Username);
        }

        void MinecraftServer_TabComplete(object sender, TabCompleteEventArgs e)
        {
            if (TabComplete != null)
            {
                var eventArgs = new TabCompleteEventArgs(e.Text, e.Client);
                TabComplete(this, eventArgs);
                if (eventArgs.Handled)
                    return;
            }
            // Handle it ourselves
            string[] matches = new string[0];
            if (e.Text.StartsWith("/"))
            {
                // Command
                if (e.Text.Contains(' '))
                {
                    // Command parameter
                    var name = e.Text.Substring(1, e.Text.IndexOf(' ') - 1);
                    var text = e.Text.Substring(e.Text.IndexOf(' ') + 1);
                    var command = Command.GetCommand(name);
                    if (command.TabComplete(this, text, out matches))
                        e.Text = matches.First();
                    else
                        TabCompleteUsername(e.Text, out matches);
                }
                else
                {
                    var commands = new List<string>();
                    foreach (var command in Command.Commands)
                    {
                        commands.Add("/" + command.DefaultCommand);
                        commands.AddRange(command.Aliases.Select(s => "/" + s));
                    }
                    matches = commands.Where(c => c.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length == 1)
                        e.Text = "/" + matches.First();
                }
            }
            else
                TabCompleteUsername(e.Text, out matches);
            if (matches.Length == 1)
            {
                e.Handled = true;
                e.Text = matches[0];
            }
            else
                e.Client.SendChat(string.Join(", ", matches));
        }

        public bool TabCompleteUsername(string text, out string[] matches)
        {
            if (text.Contains(' '))
                text = text.Substring(text.LastIndexOf(' ') + 1);
            matches = MinecraftServer.Clients.Where(c => c.IsLoggedIn
                && c.Username.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Username).Select(c => c.Username).ToArray();
            if (matches.Length == 1)
                return true;
            return false;
        }

        #endregion
    }
}
