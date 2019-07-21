﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.IO;
using ApiForZR04RN;

namespace CmdDemoForZR04RN
{
    class Program
    {
        static DeviceConnection connection;
        static uint streamId;
        static FileStream fs;

        static async Task Main(string[] args)
        {
            // Run this together with SnoopApp for diagnostics
            connection = new DeviceConnection();
            connection.UnknownCommandReceived += Connection_UnknownCommandReceived;
            await connection.Connect("127.0.0.1", 5000);
            Console.WriteLine("Connected");
            Console.Write("Password: ");
            string password = Console.ReadLine();
            LoginSuccess loginSuccess = await connection.Login("admin", password);
            Console.WriteLine("Logged in");
            Console.WriteLine("Device name: {0}", loginSuccess.ProductInfo.DeviceName);
            Console.WriteLine("Firmware version: {0}", loginSuccess.ProductInfo.FirmwareVersion);
            StreamFrame keyframe = await connection.SnapKeyframe(0);
            Console.WriteLine("Keyframe received");
            Console.WriteLine("Width: {0}", keyframe.Width);
            Console.WriteLine("Height: {0}", keyframe.Height);
            // File.WriteAllBytes("C:\\temp\\keyframe.h264", keyframe.Data);
            connection.StreamFrameReceived += Connection_StreamFrameReceived;
            fs = new FileStream("C:\\temp\\channel0.h264", FileMode.Create, FileAccess.Write, FileShare.Read);
            streamId = await connection.StreamStart(0);
            // Console.ReadKey();
            await (new TaskCompletionSource<bool>().Task);
        }

        private static void Connection_UnknownCommandReceived(CommandType cmdType, uint cmdId, uint cmdVer, byte[] data)
        {
            Console.WriteLine("Unknown command received");
        }

        private static void Connection_StreamFrameReceived(StreamFrame frame)
        {
            if (frame.StreamId == streamId && frame.FrameType == FrameType.Video)
            {
                // Console.WriteLine("Frame received");
                // Console.WriteLine("Attrib: 0x{0}", Convert.ToString((uint)frame.FrameAttrib, 16));
                fs.Write(frame.Data, 0, frame.Data.Length);
                connection.StreamChange(streamId);
            }
            else
            {
                Console.WriteLine("Non-frame received");
            }
        }
    }
}
