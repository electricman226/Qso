﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Qso.DTO;
using WebSocketSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Qso
{
    public delegate void OnEndpointEvent( object src, EndpointEventArgs args );

    public class QsoApi
    {
        private static WebSocketManager WSManager { get; set; }
        private static HttpClient Client { get; set; }
        private static readonly string LEAGUE_CERT_THUMBPRINT = "8259aafd8f71a809d2b154dd1cdb492981e448bd";
        private static readonly Regex _cmdLineRegex = new Regex( "\"--([a-z-]+|-)=([^\"]+)\"" ); // without escapes: "--([a-z-]+|-)=([^"]+)"
        public static readonly RemoteCertificateValidationCallback RiotCertValidation = ( sender, cert, chain, sslPolErrors ) => { return sslPolErrors == SslPolicyErrors.None || cert.GetCertHashString().ToLower() == LEAGUE_CERT_THUMBPRINT; };
        public static event OnEndpointEvent EndpointEvent;
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public static bool VeryVerbose { get; set; }

        #region Qso Creation

        public static void Initialize( int port, string password )
        {
            Initialize( "127.0.0.1", port, password );
        }

        public static void Initialize( string ip, int port, string password )
        {
            ServicePointManager.ServerCertificateValidationCallback += RiotCertValidation;
            Client = new HttpClient();
            WSManager = new WebSocketManager( ip, port, password );
            WSManager.WS.OnMessage += WebSocketOnMessage;
            var build = GetBuild();
            logger.Info( "Qso initialized. (League Branch {0}, League Version {1})", build.Branch, build.Version );
        }

        private static void WebSocketOnMessage( object sender, MessageEventArgs e )
        {
            JArray json = null;
            try
            {
                json = JArray.Parse( e.Data );
            }
            catch ( JsonReaderException ex )
            {
                logger.Error( "Invalid JSON sent over WebSocket", ex );
                return;
            }
            var uri = json[2]["uri"];
            var eventType = json[2]["eventType"];
            var data = json[2]["data"];
            logger.Trace( "Event {0} ({1})", uri, eventType );
            if ( VeryVerbose )
                logger.Trace( data );
            EndpointEvent?.Invoke( null, new EndpointEventArgs( uri.Value<string>(), eventType.Value<string>(), data ) );
        }

        public static void Initialize()
        {
            Process[] leaguesRunning = Process.GetProcessesByName( "LeagueClientUx" );
            if ( leaguesRunning.Length != 1 )
                throw new QsoException( "Couldn't find the correct League Client. Is it not running, or are their multiple instances?" );

            var p = leaguesRunning[0];

            var mc = _cmdLineRegex.Matches( GetCommandLine( p ) );
            var uxArgs = mc.Cast<Match>().ToDictionary( m => m.Groups[1].Value, m => m.Groups[2].Value );
            string[] lockfileArgs;

            using ( var fs = new FileStream( $"{uxArgs["install-directory"]}/lockfile", FileMode.Open, FileAccess.Read, FileShare.ReadWrite ) )
            using ( var sr = new StreamReader( fs, Encoding.Default ) )
                lockfileArgs = sr.ReadToEnd().Split( ':' );

            Initialize( "127.0.0.1", int.Parse( uxArgs["app-port"] ), lockfileArgs[3] );
        }
        #endregion

        #region DTO Helpers
        // TODO: Support query parameters?
        public static string Call( string path, HttpMethod method, string body = null, params object[] parameters )
        {
            string finalPath = $"https://127.0.0.1:{WSManager.Port}{string.Format( path, parameters )}";
            logger.Debug( "Requesting URI {0} ({1})", finalPath, method.Method );

            using ( HttpRequestMessage req = new HttpRequestMessage( method, new Uri( finalPath ) ) )
            {
                req.Headers.Add( "Authorization", $"Basic { WSManager.AuthToken }" );
                req.Headers.Add( "Accept", "application/json" );
                if ( body != null )
                {
                    req.Content = new StringContent( body, Encoding.UTF8 );
                    req.Content.Headers.ContentType.MediaType = "application/json";
                }
                using ( HttpResponseMessage resp = Client.SendAsync( req ).Result )
                {
                    string res = resp.Content.ReadAsStringAsync().Result;
                    logger.Trace( "Response status: {0} ({1})", resp.StatusCode, resp.ReasonPhrase );
                    if ( VeryVerbose )
                        logger.Trace( res );

                    if ( !resp.IsSuccessStatusCode )
                        throw new QsoEndpointException( res, resp.StatusCode );
                    return res;
                }
            }
        }

        public static T GetDTO<T>( string path, HttpMethod method, string body = null, params object[] parameters )
        {
            return JsonConvert.DeserializeObject<T>( Call( path, method, body, parameters ) );
        }
        #endregion

        #region DTOs
        public static MySummoner GetMySummoner()
        {
            return GetDTO<MySummoner>( "/lol-summoner/v1/current-summoner", HttpMethod.Get );
        }

        public static MyChatUser GetMyChatUser()
        {
            return GetDTO<MyChatUser>( "/lol-chat/v1/me", HttpMethod.Get, null );
        }

        public static ChatUser[] GetMyFriends()
        {
            return GetDTO<ChatUser[]>( "/lol-chat/v1/friends", HttpMethod.Get );
        }

        public static Lobby GetMyLobby()
        {
            return GetDTO<Lobby>( "/lol-lobby/v2/lobby", HttpMethod.Get );
        }

        public static void LeaveMyLobby()
        {
            Call( "/lol-lobby/v2/lobby", HttpMethod.Delete );
        }

        public static Lobby CreateLobby( QueueType queueId )
        {
            dynamic json = new JObject();
            json.queueId = queueId;
            return GetDTO<Lobby>( "/lol-lobby/v2/lobby", HttpMethod.Post, json.ToString() );
        }

        public static LobbyBuilder BuildLobby( string lobbyName, string gamemode, GameType gametype, MapID mapId, int teamSize )
        {
            return new LobbyBuilder( lobbyName, gamemode, gametype, mapId, teamSize );
        }

        public static ServiceStatusTickerMessage[] GetTickerMessages()
        {
            return GetDTO<ServiceStatusTickerMessage[]>( "/lol-service-status/v1/ticker-messages", HttpMethod.Get );
        }

        public static void SendFriendRequest( int id )
        {
            dynamic json = new JObject();
            json.id = id;
            Call( "/lol-chat/v1/friend-requests", HttpMethod.Post, json.ToString() );
        }

        public static void SendFriendRequest( string name )
        {
            dynamic json = new JObject();
            json.name = name;
            Call( "/lol-chat/v1/friend-requests", HttpMethod.Post, json.ToString() );
        }

        public static void RevokeFriendRequest( int id )
        {
            Call( "/lol-chat/v1/friend-requests/{0}", HttpMethod.Delete, null, id );
        }

        public static void RemoveFriend( int id )
        {
            Call( "/lol-chat/v1/friends/{0}", HttpMethod.Delete, null, id );
        }

        public static Summoner GetSummonerByID( long id )
        {
            return GetDTO<Summoner>( "/lol-summoner/v1/summoners/{0}", HttpMethod.Get, null, id );
        }

        // TODO: Support query params (still!)
        public static Summoner[] GetSummonerByName( string name )
        {
            return GetDTO<Summoner[]>( $"/lol-summoner/v2/summoners?name={name}", HttpMethod.Get );
        }

        public static PlayerLoot[] GetMyPlayerLoot()
        {
            return GetDTO<PlayerLoot[]>( "/lol-loot/v1/player-loot", HttpMethod.Get );
        }

        public static PlayerLoot GetLootByID( string id )
        {
            return GetDTO<PlayerLoot>( "/lol-loot/v1/player-loot/{0}", HttpMethod.Get, null, id );
        }

        public static ChampSelectSession GetMyChampSelect()
        {
            return GetDTO<ChampSelectSession>( "/lol-champ-select/v1/session", HttpMethod.Get );
        }

        public static ReplayMetadata GetReplayMetadata( long gameId )
        {
            return GetDTO<ReplayMetadata>( "/lol-replays/v1/metadata/{0}", HttpMethod.Get, null, gameId );
        }

        public static Queue[] GetQueues()
        {
            return GetDTO<Queue[]>( "/lol-game-queues/v1/queues", HttpMethod.Get );
        }

        public static void DownloadReplay( long gameId )
        {
            Call( "/lol-replays/v1/rofls/{0}/download", HttpMethod.Post, "{}", gameId );
        }

        // TODO: Make this not require both the item and recipe.
        // TODO: Support query parameters. See Call method.
        public static PlayerLootUpdate CraftRecipe( PlayerLoot item, LootRecipe recipe, int repeat = 0 )
        {
            return CraftRecipe( item.Name, recipe.RecipeName, repeat );
        }

        public static PlayerLootUpdate CraftRecipe( string item, string recipe, int repeat = 0 )
        {
            dynamic json = new JArray( item );
            var uri = $"/lol-loot/v1/recipes/{{0}}/craft";
            if ( repeat > 0 )
                uri += $"?repeat={repeat}";
            return GetDTO<PlayerLootUpdate>( uri, HttpMethod.Post, json.ToString(), recipe );
        }

        public static string GetReplaysPath()
        {
            return GetDTO<string>( "/lol-replays/v1/rofls/path", HttpMethod.Get );
        }

        /// <summary>
        /// Gets Riot's content targeting filters they've made for you.
        /// </summary>
        /// <returns></returns>
        public static string[] GetContentFilters()
        {
            JObject json = JObject.Parse( Call( "/lol-content-targeting/v1/filters", HttpMethod.Get ) );
            return json["filters"].ToObject<string[]>();
        }

        public static void StartQueue()
        {
            Call( "/lol-lobby/v2/lobby/matchmaking/search", HttpMethod.Post );
        }

        public static void StopQueue()
        {
            Call( "/lol-lobby/v2/lobby/matchmaking/search", HttpMethod.Delete );
        }

        public static void AcceptQueue()
        {
            Call( "/lol-matchmaking/v1/ready-check/accept", HttpMethod.Post );
        }

        public static void DeclineQueue()
        {
            Call( "/lol-matchmaking/v1/ready-check/decline", HttpMethod.Post );
        }

        public static BuildInfo GetBuild()
        {
            return GetDTO<BuildInfo>( "/system/v1/builds", HttpMethod.Get );
        }

        public static ChatUserLoLBuilder BuildChatUser()
        {
            return new ChatUserLoLBuilder();
        }

        public static PerkPageResource[] GetMyRunePages()
        {
            return GetDTO<PerkPageResource[]>( "/lol-perks/v1/pages", HttpMethod.Get );
        }

        public static PerkPageResource GetRunePageByID( long id )
        {
            return GetDTO<PerkPageResource>( "/lol-perks/v1/pages/{0}", HttpMethod.Get, null, id );
        }

        /// <summary>
        /// Flash the window and process taskbar icon.
        /// </summary>
        public static void FlashUX()
        {
            Call( "/riotclient/ux-flash", HttpMethod.Post );
        }

        public static void MinimixeUX()
        {
            Call( "/riotclient/ux-minimize", HttpMethod.Post );
        }

        public static void ShowUX()
        {
            Call( "/riotclient/ux-show", HttpMethod.Post );
        }
        #endregion

        private static string GetCommandLine( Process process )
        {
            string cmdLine = null;
            using ( var searcher = new ManagementObjectSearcher(
              $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}" ) )
            {
                // By definition, the query returns at most 1 match, because the process 
                // is looked up by ID (which is unique by definition).
                using ( var matchEnum = searcher.Get().GetEnumerator() )
                {
                    if ( matchEnum.MoveNext() ) // Move to the 1st item.
                    {
                        cmdLine = matchEnum.Current["CommandLine"]?.ToString();
                    }
                }
            }
            if ( cmdLine == null )
            {
                // Not having found a command line implies 1 of 2 exceptions, which the
                // WMI query masked:
                // An "Access denied" exception due to lack of privileges.
                // A "Cannot process request because the process (<pid>) has exited."
                // exception due to the process having terminated.
                // We provoke the same exception again simply by accessing process.MainModule.
                var dummy = process.MainModule; // Provoke exception.
            }
            return cmdLine;
        }
    }
}
