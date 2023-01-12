﻿using McProtoNet.Core;
using McProtoNet.Core.Packets;
using McProtoNet.Core.Protocol;
using McProtoNet.Protocol754;
using McProtoNet.Protocol754.Packets.Client;
using McProtoNet.Protocol754.Packets.Server;
using McProtoNet.Utils;
using System.Net.Sockets;

namespace ClientExample
{
    internal class Program
    {
        static void Main(string[] args)
        {
            MinecraftClient minecraftClient = new MinecraftClient("TestBot");
            minecraftClient.PacketReceived += (packet) =>
            {
                if (packet is ServerKeepAlivePacket keepAlivePacket)
                {
                    minecraftClient.SendPacket(new ClientKeepAlivePacket(keepAlivePacket.PingID));
                }
            };
            minecraftClient.Start("192.168.0.2", 25565);
            Console.ReadLine();
        }
    }
    public class MinecraftClient
    {
        private string _nick;

        public MinecraftClient(string nick)
        {
            _nick = nick;
        }

        public event Action<MinecraftPacket> PacketReceived;
        private PacketCategory currentCategory;
        private CancellationTokenSource CTS = new();
        private static readonly IPacketFactory p754 = new PacketFactory754();

        private IPacketReaderWriter packetReaderWriter = null;
        public void SendPacket(MinecraftPacket minecraftPacket)
        {
            packetReaderWriter.SendPacket(minecraftPacket);
        }

        public void Start(string host, ushort port)
        {
            OnStart(host, port);
        }
        private async void OnStart(string host, ushort port)
        {
            TcpClient tcpClient = new TcpClient();

            tcpClient.Connect(host, port);
            IMinecraftProtocol mc_proto = new MinecraftProtocol(tcpClient, true);
            CTS.Token.Register(mc_proto.Dispose);

            try
            {
                await HandShake(mc_proto);
                await Login(mc_proto);
                Game(mc_proto);
            }
            catch (Exception e)
            {
                Console.WriteLine("error: " + e);
            }



        }


        private async Task HandShake(IMinecraftProtocol mc_proto)
        {
            currentCategory = PacketCategory.HandShake;
            using (IPacketProvider handshakePackets = p754.CreateProvider(currentCategory, PacketSide.Client))
            using (IPacketReaderWriter packetReaderWriter = new PacketReaderWriter(mc_proto, handshakePackets, false))
            {
                await packetReaderWriter.SendPacketAsync(
                    new HandShakePacket(HandShakeIntent.LOGIN, 754, "", 25565), CTS.Token);

                Console.WriteLine("SendHand");
            }
        }
        private async Task Login(IMinecraftProtocol mc_proto)
        {
            currentCategory = PacketCategory.Login;
            using (IPacketProvider loginPackets = p754.CreateProvider(currentCategory, PacketSide.Client))
            using (IPacketReaderWriter packetReaderWriter = new PacketReaderWriter(mc_proto, loginPackets, false))
            {
                await packetReaderWriter.SendPacketAsync(new LoginStartPacket("Nick"), CTS.Token);
                bool loginSucc = false;
                while (!loginSucc)
                {
                    MinecraftPacket packet = await packetReaderWriter.ReadNextPacketAsync(CTS.Token);
                    loginSucc = await HandleLogin(packetReaderWriter, packet);
                }
            }
        }
        private async void Game(IMinecraftProtocol mc_proto)
        {
            currentCategory = PacketCategory.Game;
            using (IPacketProvider gamePackets = p754.CreateProvider(currentCategory, PacketSide.Client))
            using (IPacketReaderWriter packetReaderWriter = new PacketReaderWriter(mc_proto, gamePackets, false))
            {
                this.packetReaderWriter = packetReaderWriter;
                while (true)
                {
                    MinecraftPacket packet = await packetReaderWriter.ReadNextPacketAsync(CTS.Token);
                    HandleGame(packetReaderWriter, packet);
                }
            }
        }

        private void HandleGame(IPacketReaderWriter sender, MinecraftPacket packet)
        {
            PacketReceived?.Invoke(packet);
        }
        private async Task<bool> HandleLogin(IPacketReaderWriter sender, MinecraftPacket packet)
        {
            if (packet is EncryptionRequestPacket encryption)
            {
                var RSAService = CryptoHandler.DecodeRSAPublicKey(encryption.PublicKey);
                var privateKey = CryptoHandler.GenerateAESPrivateKey();
                var key_enc = RSAService.Encrypt(privateKey, false);
                var token_enc = RSAService.Encrypt(encryption.VerifyToken, false);
                var response = new EncryptionResponsePacket(key_enc, token_enc);
                await sender.SendPacketAsync(response, CTS.Token);
                sender.SwitchEncryption(privateKey);
                Console.WriteLine("Encrypt");
            }
            else if (packet is LoginSuccessPacket)
            {
                Console.WriteLine("LoginSucc");
                return true;
            }
            else if (packet is LoginSetCompressionPacket compression)
            {
                Console.WriteLine("Compr");
                sender.SwitchCompression(compression.Threshold);
            }
            else if (packet is LoginDisconnectPacket disconnect)
            {
                throw new LoginRejectedException(disconnect.Message);
            }
            return false;
        }
    }
}