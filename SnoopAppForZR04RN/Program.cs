﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;
using System.IO;

namespace SnoopAppForZR04RN
{
    static class Program
    {
        static TcpListener listener5000;
        static TcpListener listener80;
        static string target = "192.168.226.180";
        static bool running = true;

        public static T[] SubArray<T>(this T[] data, int index, int length)
        {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        public static string NullTerminate(this string data)
        {
            int i = data.IndexOf('\0');
            if (i >= 0)
                return data.Substring(0, i);
            return data;
        }

        public static string Passwordize(this string data)
        {
            return new string('*', data.Length);
        }

        static async Task Main(string[] args)
        {
            listener5000 = new TcpListener(IPAddress.Any, 5000);
            listener5000.Start();
            listener80 = new TcpListener(IPAddress.Any, 80);
            listener80.Start();
            Task a = listen5000();
            Task b = listen80();
            Console.ReadKey();
            running = false;
            listener5000.Stop();
            listener80.Stop();
            await a;
            await b;
        }
        static async Task listen5000()
        {
            while (running)
            {
                Console.WriteLine("Wait for 5000");
                Console.WriteLine();
                try
                {
                    TcpClient client = await listener5000.AcceptTcpClientAsync();
                    TcpClient nvr = new TcpClient(target, 5000);
                    Console.WriteLine("Have 5000");
                    Console.WriteLine();
                    Task a = forwardZR04RN(client, nvr, "Client");
                    Task b = forwardZR04RN(nvr, client, "NVR");
                    await a;
                    await b;
                    try { client.Close(); } catch { }
                    try { nvr.Close(); } catch { }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        static async Task listen80()
        {
            while (running)
            {
                Console.WriteLine("Wait for 80");
                Console.WriteLine();
                try
                {
                    TcpClient client = await listener80.AcceptTcpClientAsync();
                    TcpClient nvr = new TcpClient(target, 80);
                    Console.WriteLine("Have 80");
                    Console.WriteLine();
                    Task a = forwardRaw(client, nvr);
                    Task b = forwardRaw(nvr, client);
                    await a;
                    await b;
                    try { client.Close(); } catch { }
                    try { nvr.Close(); } catch { }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        static async Task forwardRaw(TcpClient from, TcpClient to)
        {
            NetworkStream fromStream = from.GetStream();
            NetworkStream toStream = to.GetStream();
            try
            {
                byte[] buffer = new byte[64 * 1024];
                for (; ; )
                {
                    int len = await fromStream.ReadAsync(buffer, 0, buffer.Length);
                    if (len <= 0)
                        break;
                    await toStream.WriteAsync(buffer, 0, len);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        const int MagicMarkerAAAA = 0x41414141;
        const int MagicMarkerHEAD = 0x64616568;

        static async Task forwardZR04RN(TcpClient from, TcpClient to, string fromName)
        {
            NetworkStream fromStream = from.GetStream();
            NetworkStream toStream = to.GetStream();
            try
            {
                byte[] buffer = new byte[64 * 1024];
                int i = 0;
                bool mustRead = true;
                for (; ; )
                {
                    int len;
                    if (mustRead)
                    {
                        len = await fromStream.ReadAsync(buffer, i, buffer.Length - i);
                        if (len <= 0)
                            break;
                    }
                    else
                    {
                        len = 0;
                    }
                    i += len;
                    if (i < 8)
                        continue;

                    // We now have at least 8 bytes of a packet
                    int magicMarker = buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24;
                    int packetLen = buffer[4] | buffer[5] << 8 | buffer[6] << 16 | buffer[7] << 24;

                    if (packetLen > buffer.Length + 8)
                    {
                        Console.WriteLine("Oversize packet {0} from {1}", packetLen, fromName);
                        Console.WriteLine();
                    }

                    switch (magicMarker)
                    {
                        case MagicMarkerAAAA:
                            if (i < (packetLen + 8))
                            {
                                mustRead = true;
                                continue;
                            }
                            Console.WriteLine("Received packet with length {0} from {1}", packetLen, fromName);

                            if (packetLen >= (8 + (4 * 4)))
                            {
                                // cmdtype, cmdid, cmdver, datalen
                                // Decode command
                                int vi = 8;
                                int cmdType = buffer[vi] | buffer[vi + 1] << 8 | buffer[vi + 2] << 16 | buffer[vi + 3] << 24;
                                vi += 4; // cmdType
                                int cmdId = buffer[vi] | buffer[vi + 1] << 8 | buffer[vi + 2] << 16 | buffer[vi + 3] << 24;
                                vi += 4; // cmdId
                                int cmdVer = buffer[vi] | buffer[vi + 1] << 8 | buffer[vi + 2] << 16 | buffer[vi + 3] << 24;
                                vi += 4; // cmdVer
                                int dataLen = buffer[vi] | buffer[vi + 1] << 8 | buffer[vi + 2] << 16 | buffer[vi + 3] << 24;
                                vi += 4; // dataLen
                                Console.WriteLine("Command 0x{0}, id {1}, version 0x{2}, with length {3}",
                                    Convert.ToString(cmdType, 16),
                                    cmdId, Convert.ToString(cmdVer, 16), dataLen);
                                if ((packetLen + 8) >= (vi + dataLen))
                                {
                                    byte[] data = buffer.SubArray(vi, dataLen);
                                    parseCommand(cmdType, data);
                                }
                                else
                                {
                                    Console.WriteLine("Command length exceeds packet by {0} bytes", (vi + dataLen) - (packetLen + 8));
                                }
                                if ((packetLen + 8) > (vi + dataLen))
                                {
                                    Console.WriteLine("Packet length exceeds command by {0} bytes", (packetLen + 8) - (vi + dataLen));
                                }
                            }
                            else if (packetLen == 16)
                            {
                                int vi = 8;
                                int cmdType = buffer[vi] | buffer[vi + 1] << 8 | buffer[vi + 2] << 16 | buffer[vi + 3] << 24;
                                vi += 4; // cmdType
                                int command = buffer[vi] | buffer[vi + 1] << 8 | buffer[vi + 2] << 16 | buffer[vi + 3] << 24;
                                vi += 4; // command?
                                Console.WriteLine("Short command 0x{0}: {1} ({2})", Convert.ToString(cmdType, 16), BitConverter.ToString(buffer, vi - 4, 4), command);
                                parseCommand(cmdType, null);
                            }

                            Console.WriteLine();
                            await toStream.WriteAsync(buffer, 0, packetLen + 8);
                            for (int j = (packetLen + 8); j < i; ++j)
                                buffer[j - (packetLen + 8)] = buffer[j];
                            i -= (packetLen + 8);
                            mustRead = (i == 0);
                            break;
                        case MagicMarkerHEAD:
                            if (i < 64)
                            {
                                mustRead = true;
                                continue;
                            }
                            Console.WriteLine("Received head marker from {0}", fromName);
                            Console.WriteLine();
                            await toStream.WriteAsync(buffer, 0, 64);
                            for (int j = 64; j < i; ++j)
                                buffer[j - 64] = buffer[j];
                            i -= 64;
                            mustRead = (i == 0);
                            break;
                        default:
                            Console.WriteLine("Unknown marker {0}", magicMarker);
                            Console.WriteLine();
                            return;
                    }

                    // await toStream.WriteAsync(buffer, 0, i);
                    // i = 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        static void parseCommand(int cmdType, byte[] data)
        {
            int vi;
            switch (cmdType)
            {
                case 0x1101:
                    Console.WriteLine("DVRV3_LOGIN");
                    if (data.Length != 120)
                        goto UnknownCommandLength;
                    vi = 0;
                    Console.WriteLine("ConnectType: {0}", data[vi] | data[vi + 1] << 8 | data[vi + 2] << 16 | data[vi + 3] << 24);
                    Console.WriteLine("IP: {0}.{0}.{0}.{0}", data[4], data[5], data[6], data[7]);
                    Console.WriteLine("Username: {0}", Encoding.ASCII.GetString(data, 8, 36).NullTerminate()); // 8-44, 32 with enforced 00 on last 4 bytes
                    Console.WriteLine("Password: {0}", Encoding.ASCII.GetString(data, 44, 36).NullTerminate().Passwordize()); // 44-80, 32 with enforced 00 on last 4 bytes
                    Console.WriteLine("ComputerName: {0}", Encoding.ASCII.GetString(data, 80, 28).NullTerminate());
                    Console.WriteLine("Mac: {0}", BitConverter.ToString(data, 108, 6));
                    Console.WriteLine("Reserved (NULL): {0}", BitConverter.ToString(data, 114, 2));
                    vi = 116;
                    Console.WriteLine("NetProtocolVer: {0}", Convert.ToString(data[vi] | data[vi + 1] << 8 | data[vi + 2] << 16 | data[vi + 3] << 24, 16));
                    break;
                case 0x10001:
                    Console.WriteLine("DVRV3_LOGIN_SUCCESS");
                    if (data.Length != 352)
                        goto UnknownCommandLength;
                    vi = 0;
                    Console.WriteLine("Unknown: {0}", data[vi] | data[vi + 1] << 8 | data[vi + 2] << 16 | data[vi + 3] << 24);
                    Console.WriteLine("Authority: {0}", BitConverter.ToString(data, 4, 4));
                    Console.WriteLine("AuthLiveCH: {0}", BitConverter.ToString(data, 8, 8));
                    Console.WriteLine("AuthRecordCH: {0}", BitConverter.ToString(data, 16, 8));
                    Console.WriteLine("AuthPlaybackCH: {0}", BitConverter.ToString(data, 24, 8));
                    Console.WriteLine("AuthBackupCH: {0}", BitConverter.ToString(data, 32, 8));
                    Console.WriteLine("AuthPTZCtrlCH: {0}", BitConverter.ToString(data, 40, 8));
                    Console.WriteLine("AuthRemoteViewCH: {0}", BitConverter.ToString(data, 48, 8));
                    Console.WriteLine("Unknown: {0}", BitConverter.ToString(data, 56, 28));
                    Console.WriteLine("VideoInputNum: {0}", data[84] | data[85] << 8);
                    Console.WriteLine("DeviceID: {0}", data[86] | data[87] << 8);
                    Console.WriteLine("VideoFormat: {0}", BitConverter.ToString(data, 88, 4));
                    for (int i = 0; i < 8; ++i)
                        Console.WriteLine("Function[{0}]: {1}", i, BitConverter.ToString(data, 92 + (4*i), 4));
                    Console.WriteLine("IP: {0}.{0}.{0}.{0}", data[124], data[125], data[126], data[127]);
                    Console.WriteLine("Mac: {0}", BitConverter.ToString(data, 128, 6));
                    Console.WriteLine("Reserved (NULL): {0}", BitConverter.ToString(data, 134, 2));
                    // Console.WriteLine("BuildDate: {0}", BitConverter.ToString(command, 136, 4));
                    vi = 136;
                    int buildDate = data[vi] | data[vi + 1] << 8 | data[vi + 2] << 16 | data[vi + 3] << 24;
                    Console.WriteLine("BuildDate: {0}-{1}-{2}", 
                        (buildDate >> 16).ToString("0000"), ((buildDate >> 8) & 0xFF).ToString("00"), (buildDate & 0xFF).ToString("00"));
                    // Console.WriteLine("BuildTime: {0}", BitConverter.ToString(command, 140, 4));
                    vi = 140;
                    int buildTime = data[vi] | data[vi + 1] << 8 | data[vi + 2] << 16 | data[vi + 3] << 24;
                    Console.WriteLine("BuildTime: {0}:{1}:{2}", 
                        ((buildTime >> 16) & 0xFF).ToString("00"), ((buildTime >> 8) & 0xFF).ToString("00"), (buildTime & 0xFF).ToString("00"));
                    Console.WriteLine("DeviceName: {0}", Encoding.ASCII.GetString(data, 144, 36).NullTerminate());
                    Console.WriteLine("FirmwareVersion: {0}", Encoding.ASCII.GetString(data, 180, 36).NullTerminate());
                    Console.WriteLine("KernelVersion: {0}", Encoding.ASCII.GetString(data, 216, 64).NullTerminate());
                    Console.WriteLine("HardwareVersion: {0}", Encoding.ASCII.GetString(data, 280, 36).NullTerminate());
                    Console.WriteLine("McuVersion: {0}", Encoding.ASCII.GetString(data, 316, 36).NullTerminate());
                    break;
                case 0x1402:
                    if (data != null)
                        goto UnknownCommandData;
                    Console.WriteLine("DVRV3_CONFIG_EXIT");
                    break;
                case 0x1401:
                    if (data != null)
                        goto UnknownCommandData;
                    Console.WriteLine("DVRV3_CONFIG_ENTER");
                    break;
                // 0x1403
                // 0x1405
                case 0x40001:
                    if (data != null)
                        goto UnknownCommandData;
                    Console.WriteLine("DVRV3_CONFIG_ENTER_SUCCESS");
                    break;
                default:
                    Console.WriteLine("Unknown command type");
                    break;
            }
            return;
        UnknownCommandLength:
            Console.WriteLine("Unknown command length");
            return;
        UnknownCommandData:
            Console.WriteLine("Unknown command data");
            return;
        }

        /*
        static void accept5000(Task<TcpClient> client)
        {
            listener5000.AcceptTcpClientAsync().ContinueWith(accept5000);
            TcpClient real = new TcpClient(target, 5000);
        }

        static void accept80(Task<TcpClient> client)
        {
            listener80.AcceptTcpClientAsync().ContinueWith(accept80);
            TcpClient real = new TcpClient(target, 80);

        }
        */
    }
}
