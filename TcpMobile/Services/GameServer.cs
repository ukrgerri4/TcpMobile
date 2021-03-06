﻿using Core.Models;
using Infrastracture.Interfaces;
using Infrastracture.Interfaces.GameMunchkin;
using Infrastracture.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using TcpMobile.Game.Models;
using TcpMobile.Tcp.Enums;
using TcpMobile.Tcp.Models;
using Xamarin.Forms;

namespace TcpMobile.Services
{
    public class GameServer : IGameServer
    {
        private IGameLogger _gameLogger => DependencyService.Get<IGameLogger>();
        private ILanServer _lanServer => DependencyService.Get<ILanServer>();
        private IDeviceInfoService _deviceInfoService => DependencyService.Get<IDeviceInfoService>();

        private IDisposable _hostBroadcaster;
        private IDisposable _connectedPlayersBroadcaster;

        private Subject<Unit> _updatePlayersSubject = new Subject<Unit>();
        private Subject<Unit> _destroy = new Subject<Unit>();

        private double HOST_BROADCAST_PERIOD_MS = 1000;
        private double CONNECTED_PLAYERS_BROADCAST_PERIOD_MS = 1000;

        public MunchkinHost Host { get; set; }
        public ConcurrentDictionary<string, PlayerInfo> ConnectedPlayers { get; set; }

        public GameServer()
        {
            Host = new MunchkinHost
            {
                Id = _deviceInfoService.DeviceId,
                Name = "Game_1"
            };

            ConnectedPlayers = new ConcurrentDictionary<string, PlayerInfo>();
        }

        public Result Start()
        {
            try
            {
                _lanServer.StartTcpServer();
                _lanServer.StartUdpServer();

                StartBroadcastHostData();
                StartBroadcastConnectedPlayersData();
                StartListeningNewPlayersConnections();
                StartListeningPlayersDisconnections();

                return Result.Ok();
            }
            catch (Exception e)
            {
                var errorMessage = $"GameServer: start game server error: {e.Message}";
                _gameLogger.Error(errorMessage);
                return Result.Fail(errorMessage);
            }
        }

        public Result Stop()
        {
            try
            {
                StopConnectedPlayersBroadcast();

                _lanServer.StopUdpServer();
                _lanServer.StopTcpServer();

                _destroy.OnNext(Unit.Default);

                Host.Fullness = 0;
                ConnectedPlayers.Clear();

                return Result.Ok();
            }
            catch (Exception e)
            {
                var errorMessage = $"GameServer: stop game server error: {e.Message}";
                _gameLogger.Error(errorMessage);
                return Result.Fail(errorMessage);
            }
        }

        public Result StopBroadcast()
        {
            try
            {
                _hostBroadcaster?.Dispose();
                _lanServer.StopUdpServer();
                return Result.Ok();
            }
            catch(Exception e)
            {
                return Result.Fail($"GameServer: stop broadcasting error: {e.Message}");
            }
        }

        public void StopConnectedPlayersBroadcast()
        {
            _connectedPlayersBroadcaster?.Dispose();
        }

        private void StartBroadcastHostData()
        {
            _hostBroadcaster = Observable.Interval(TimeSpan.FromMilliseconds(HOST_BROADCAST_PERIOD_MS))
                .TakeUntil(_destroy)
                .Finally(() => _gameLogger.Debug("GameServer: host data broadcast stoped."))
                .Select(data =>
                {
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        memoryStream.Write(BitConverter.GetBytes((ushort)0), 0, 2);
                        memoryStream.WriteByte((byte)MunchkinMessageType.HostFound);

                        var byteId = Encoding.UTF8.GetBytes(Host.Id ?? string.Empty);
                        memoryStream.WriteByte((byte)byteId.Length);
                        memoryStream.Write(byteId, 0, byteId.Length);

                        var byteName = Encoding.UTF8.GetBytes(Host.Name ?? string.Empty);
                        memoryStream.WriteByte((byte)byteName.Length);
                        memoryStream.Write(byteName, 0, byteName.Length);

                        memoryStream.WriteByte(Host.Capacity);
                        memoryStream.WriteByte(Host.Fullness);

                        memoryStream.Seek(0, SeekOrigin.Begin);
                        memoryStream.Write(BitConverter.GetBytes((ushort)memoryStream.Length), 0, 2);
                        memoryStream.Seek(0, SeekOrigin.End);

                        return memoryStream.ToArray();
                    }
                })
                .Do(_ => _gameLogger.Debug("GameServer: sart send host data"))
                .Subscribe(
                    message =>
                    {
                        _lanServer.BroadcastMessage(message);
                    },
                    error =>
                    {
                        _gameLogger.Error($"GameServer: error during broadcast host: {error.Message}");
                    }
                );
        }

        private void StartListeningNewPlayersConnections()
        {
            _lanServer.TcpServerEventSubject.AsObservable()
                .TakeUntil(_destroy)
                .Where(tcpEvent => tcpEvent.Type == TcpEventType.ReceiveData)
                .Where(tcpEvent => tcpEvent.Data != null)
                .Where(tcpEvent => ((Packet)tcpEvent.Data).MessageType == MunchkinMessageType.InitInfo ||
                    ((Packet)tcpEvent.Data).MessageType == MunchkinMessageType.UpdatePlayerState ||
                    ((Packet)tcpEvent.Data).MessageType == MunchkinMessageType.UpdatePlayerName)
                .Subscribe(
                    tcpEvent =>
                    {
                        var packet = (Packet)tcpEvent.Data;
                        var position = 3;

                        switch (packet.MessageType)
                        {
                            case MunchkinMessageType.InitInfo:
                                var playerInfo = new PlayerInfo();

                                playerInfo.Id = Encoding.UTF8.GetString(packet.Buffer, position + 1, packet.Buffer[position]);
                                position += packet.Buffer[position];
                                position++;

                                playerInfo.Name = Encoding.UTF8.GetString(packet.Buffer, position + 1, packet.Buffer[position]);
                                position += packet.Buffer[position];
                                position++;

                                playerInfo.Sex = packet.Buffer[position++];
                                playerInfo.Level = packet.Buffer[position++];
                                playerInfo.Modifiers = packet.Buffer[position++];
                                playerInfo.Dice = packet.Buffer[position++];

                                if (packet.SenderId != playerInfo.Id)
                                {
                                    // log some warning
                                }

                                var playerAdded = ConnectedPlayers.TryAdd(packet.SenderId, playerInfo);

                                if (!playerAdded)
                                {
                                    ConnectedPlayers[packet.SenderId].Name = playerInfo.Name;
                                    ConnectedPlayers[packet.SenderId].Level = playerInfo.Level;
                                    ConnectedPlayers[packet.SenderId].Modifiers = playerInfo.Modifiers;
                                    ConnectedPlayers[packet.SenderId].Dice = playerInfo.Dice;
                                }
                                else
                                {
                                    Host.Fullness++;
                                }

                                break;
                            case MunchkinMessageType.UpdatePlayerState:
                                var sex = packet.Buffer[position++];
                                var level = packet.Buffer[position++];
                                var modifiers = packet.Buffer[position++];
                                var dice = packet.Buffer[position++];

                                if (ConnectedPlayers.ContainsKey(packet.SenderId))
                                {
                                    ConnectedPlayers[packet.SenderId].Sex = sex;
                                    ConnectedPlayers[packet.SenderId].Level = level;
                                    ConnectedPlayers[packet.SenderId].Modifiers = modifiers;
                                    ConnectedPlayers[packet.SenderId].Dice = dice;
                                }

                                break;
                            case MunchkinMessageType.UpdatePlayerName:
                                var name = Encoding.UTF8.GetString(packet.Buffer, position + 1, packet.Buffer[position]);

                                if (ConnectedPlayers.ContainsKey(packet.SenderId))
                                {
                                    ConnectedPlayers[packet.SenderId].Name = name;
                                }
                                break;
                        }

                        _updatePlayersSubject.OnNext(Unit.Default);
                    },
                    error =>
                    {
                        _gameLogger.Error($"GameServer: error during listening for new players: {error.Message}");
                    }
                );
        }

        private void StartListeningPlayersDisconnections()
        {
            _lanServer.TcpServerEventSubject.AsObservable()
                .TakeUntil(_destroy)
                .Where(tcpEvent => tcpEvent.Type == TcpEventType.ClientDisconnect)
                .Where(tcpEvent => (string)tcpEvent.Data != null)
                .Subscribe(
                    tcpEvent =>
                    {
                        var removeResult = ConnectedPlayers.TryRemove((string)tcpEvent.Data, out PlayerInfo pi);
                        if (!removeResult)
                        {
                            _gameLogger.Error($"GameServer: player connection id:[{(string)tcpEvent.Data}] not found.");
                            return;
                        }

                        Host.Fullness--;
                        _updatePlayersSubject.OnNext(Unit.Default);
                    },
                    error =>
                    {
                        _gameLogger.Error($"GameServer: error during listening for players disconnections: {error.Message}");
                    }
                );
        }

        private void StartBroadcastConnectedPlayersData()
        {
            _connectedPlayersBroadcaster = _updatePlayersSubject.AsObservable()
                .TakeUntil(_destroy)
                .Finally(() => _gameLogger.Debug("GameServer: connected players data broadcast stoped."))
                .Sample(TimeSpan.FromMilliseconds(CONNECTED_PLAYERS_BROADCAST_PERIOD_MS))
                .Do(_ => _gameLogger.Debug("GameServer: start send players data."))
                .Subscribe(
                    _ =>
                    {
                        var players = ConnectedPlayers.ToArray();

                        using (MemoryStream memoryStream = new MemoryStream())
                        {
                            memoryStream.Write(BitConverter.GetBytes((ushort)0), 0, 2);
                            memoryStream.WriteByte((byte)MunchkinMessageType.UpdatePlayers);
                            memoryStream.WriteByte((byte)players.Length);

                            foreach (var player in players)
                            {
                                var byteId = Encoding.UTF8.GetBytes(player.Value.Id ?? string.Empty);
                                memoryStream.WriteByte((byte)byteId.Length);
                                memoryStream.Write(byteId, 0, byteId.Length);

                                var byteName = Encoding.UTF8.GetBytes(player.Value.Name ?? string.Empty);
                                memoryStream.WriteByte((byte)byteName.Length);
                                memoryStream.Write(byteName, 0, byteName.Length);

                                memoryStream.WriteByte(player.Value.Sex);
                                memoryStream.WriteByte(player.Value.Level);
                                memoryStream.WriteByte(player.Value.Modifiers);
                                memoryStream.WriteByte(player.Value.Dice);
                            }

                            memoryStream.Seek(0, SeekOrigin.Begin);
                            memoryStream.Write(BitConverter.GetBytes((ushort)memoryStream.Length), 0, 2);
                            memoryStream.Seek(0, SeekOrigin.End);

                            var message = memoryStream.ToArray();

                            foreach (var player in players)
                            {
                                var sendResult = _lanServer.BeginSendMessage(player.Key, message);
                                if (sendResult.IsFail)
                                {
                                    _gameLogger.Error(sendResult.Error);
                                }
                            }
                        }
                    },
                    error =>
                    {
                        _gameLogger.Error($"GameServer: error during broadcast connected players: {error.Message}");
                    }
                );
        }
    }
}
