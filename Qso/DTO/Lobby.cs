﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Qso.DTO
{
    public class Lobby
    {
        [JsonProperty( "chatRoomId" )]
        public string ChatRoomID { get; internal set; }
        [JsonProperty( "chatRoomKey" )]
        public string ChatRoomKey { get; internal set; }
        [JsonProperty( "localMember" )]
        public LobbyParticipant LocalMember { get; internal set; }
        [JsonProperty( "members" )]
        public LobbyParticipant[] Members { get; internal set; }
        [JsonProperty( "partyId" )]
        public string PartyID { get; internal set; }
        [JsonProperty( "partyType" )]
        public string PartyType { get; internal set; }
        [JsonProperty( "gameConfig" )]
        public LobbyGameConfig GameConfig { get; internal set; }

        public LobbyInvitation[] GetInvitations()
        {
            return QsoApi.GetDTO<LobbyInvitation[]>( "/lol-lobby/v2/lobby/invitations", HttpMethod.Get );
        }

        public LobbyInvitation[] Invite( long id )
        {
            dynamic obj = new JObject();
            obj.toSummonerId = id;
            dynamic json = new JArray( obj );
            return QsoApi.GetDTO<LobbyInvitation[]>( "/lol-lobby/v2/lobby/invitations", HttpMethod.Post, json.ToString() );
        }

        public void StartChampSelect()
        {
            QsoApi.Call( "/lol-lobby/v1/lobby/custom/start-champ-select", HttpMethod.Post );
        }

        public void StopChampSelect()
        {
            QsoApi.Call( "/lol-lobby/v1/lobby/custom/cancel-champ-select", HttpMethod.Post );
        }

        public void Kick( Summoner s )
        {
            QsoApi.Call( "/lol-lobby/v2/lobby/members/{0}/kick", HttpMethod.Post, null, s.ID );
        }

        public void Promote( Summoner s )
        {
            QsoApi.Call( "/lol-lobby/v2/lobby/members/{0}/promote", HttpMethod.Post, null, s.ID );
        }

        public void Kick( int id )
        {
            QsoApi.Call( "/lol-lobby/v2/lobby/members/{0}/kick", HttpMethod.Post, null, id );
        }

        public void Promote( int id )
        {
            QsoApi.Call( "/lol-lobby/v2/lobby/members/{0}/promote", HttpMethod.Post, null, id );
        }

        public void RemoveBot( ChampionID champ, TeamID team )
        {
            QsoApi.Call( "/lol-lobby/v1/lobby/custom/bots/{0}", HttpMethod.Delete, null, $"bot_{Enum.GetName( typeof( ChampionID ), champ )}_{team}" );
        }

        public void AddBot( ChampionID champion, TeamID team, string difficulty )
        {
            dynamic json = new JObject();
            json.championId = champion;
            json.teamId = Convert.ToString( (int)team ); // bullshit. the API expects a string, but can't use ToString cause enums are stupid, so I'm forced to cast it to an int to make it a string.
            json.botDifficulty = difficulty;
            QsoApi.Call( "/lol-lobby/v1/lobby/custom/bots", HttpMethod.Post, json.ToString() );
        }

        /// <summary>
        /// This will reset the position you do not specify. Specify both at once to set both.
        /// </summary>
        public void SetPositionPreferences( string primary = null, string secondary = null )
        {
            dynamic json = new JObject();
            if ( primary != null )
                json.firstPreference = primary;
            if ( secondary != null )
                json.secondPreference = secondary;
            QsoApi.Call( "/lol-lobby/v2/lobby/members/localMember/position-preferences", HttpMethod.Put, json.ToString() );
        }
    }
}
