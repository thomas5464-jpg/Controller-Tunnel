using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System.Security.Cryptography;

class Program
{
    static void Main(string[] args)
    {
        int port = 5555;
        string? psk = null;
        if (args.Length >= 1) port = int.Parse(args[0]);
        if (args.Length >= 2) psk = args[1];

        byte[]? key = null;
        if (!string.IsNullOrEmpty(psk))
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            key = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(psk));
            Console.WriteLine("Using PSK-derived AES-GCM key.");
        }

        Console.WriteLine("Starting server on UDP port {0}", port);

        // Discover public mapping via STUN (best-effort)
        try
        {
            var publicEp = DiscoverPublicEndpoint();
            if (publicEp != null)
                Console.WriteLine($"Server public mapping: {publicEp}");
            else
                Console.WriteLine("STUN discovery failed or timed out.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("STUN discovery error: {0}", ex.Message);
        }

        using var udp = new UdpClient(port);
        using var client = new ViGEmClient();

        var controller = client.CreateXbox360Controller();
        controller.Connect();

        Console.WriteLine("ViGEm virtual Xbox 360 controller connected.");

        IPEndPoint? remote = null;
        int packetCount = 0;
        DateTime nextStatusLog = DateTime.UtcNow;

        while (true)
        {
            var res = udp.Receive(ref remote!);
            try
            {
                if (res.Length < 1) continue;
                byte flags = res[0];
                byte[] payload;

                if ((flags & 1) != 0)
                {
                    if (key == null)
                    {
                        Console.WriteLine("Received encrypted packet but no PSK configured.");
                        continue;
                    }

                    // encrypted: [flags(1)] [nonce(12)] [ciphertext] [tag(16)]
                    if (res.Length < 1 + 12 + 16)
                    {
                        Console.WriteLine("Encrypted packet too short.");
                        continue;
                    }

                    byte[] nonce = new byte[12];
                    Array.Copy(res, 1, nonce, 0, 12);
                    int cipherLen = res.Length - 1 - 12 - 16;
                    if (cipherLen < 0) continue;
                    byte[] ciphertext = new byte[cipherLen];
                    Array.Copy(res, 1 + 12, ciphertext, 0, cipherLen);
                    byte[] tag = new byte[16];
                    Array.Copy(res, 1 + 12 + cipherLen, tag, 0, 16);

                    byte[] plaintext = new byte[cipherLen];
                    try
                    {
                        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
                        aes.Decrypt(nonce, ciphertext, tag, plaintext);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to decrypt packet: {0}", ex.Message);
                        continue;
                    }

                    payload = plaintext;
                }
                else
                {
                    // unencrypted: flags(0) + plaintext
                    payload = new byte[res.Length - 1];
                    Array.Copy(res, 1, payload, 0, payload.Length);
                }

                using var ms = new MemoryStream(payload);
                using var br = new BinaryReader(ms);

                uint seq = br.ReadUInt32();
                long ticks = br.ReadInt64();
                ushort wButtons = br.ReadUInt16();
                byte leftTrigger = br.ReadByte();
                byte rightTrigger = br.ReadByte();
                short thumbLX = br.ReadInt16();
                short thumbLY = br.ReadInt16();
                short thumbRX = br.ReadInt16();
                short thumbRY = br.ReadInt16();

                float gyroX = 0f;
                float gyroY = 0f;
                float gyroZ = 0f;
                bool hasGyro = ms.Length - ms.Position >= 12;
                if (hasGyro)
                {
                    gyroX = br.ReadSingle();
                    gyroY = br.ReadSingle();
                    gyroZ = br.ReadSingle();
                }

                // Map buttons
                controller.SetButtonState(Xbox360Button.A, (wButtons & 0x1000) != 0);
                controller.SetButtonState(Xbox360Button.B, (wButtons & 0x2000) != 0);
                controller.SetButtonState(Xbox360Button.X, (wButtons & 0x4000) != 0);
                controller.SetButtonState(Xbox360Button.Y, (wButtons & 0x8000) != 0);
                controller.SetButtonState(Xbox360Button.Up, (wButtons & 0x0001) != 0);
                controller.SetButtonState(Xbox360Button.Down, (wButtons & 0x0002) != 0);
                controller.SetButtonState(Xbox360Button.Left, (wButtons & 0x0004) != 0);
                controller.SetButtonState(Xbox360Button.Right, (wButtons & 0x0008) != 0);
                controller.SetButtonState(Xbox360Button.Start, (wButtons & 0x0010) != 0);
                controller.SetButtonState(Xbox360Button.Back, (wButtons & 0x0020) != 0);
                controller.SetButtonState(Xbox360Button.LeftThumb, (wButtons & 0x0040) != 0);
                controller.SetButtonState(Xbox360Button.RightThumb, (wButtons & 0x0080) != 0);
                controller.SetButtonState(Xbox360Button.LeftShoulder, (wButtons & 0x0100) != 0);
                controller.SetButtonState(Xbox360Button.RightShoulder, (wButtons & 0x0200) != 0);

                // Triggers (0-255) map to slider 0-255
                controller.SetSliderValue(Xbox360Slider.LeftTrigger, leftTrigger);
                controller.SetSliderValue(Xbox360Slider.RightTrigger, rightTrigger);

                // Thumbsticks: assume same range (-32768 .. 32767)
                controller.SetAxisValue(Xbox360Axis.LeftThumbX, thumbLX);
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, thumbLY);
                controller.SetAxisValue(Xbox360Axis.RightThumbX, thumbRX);
                controller.SetAxisValue(Xbox360Axis.RightThumbY, thumbRY);

                if (hasGyro)
                {
                    Console.WriteLine($"Gyro data received: X={gyroX:F3}, Y={gyroY:F3}, Z={gyroZ:F3}");
                }

                packetCount++;
                if (DateTime.UtcNow >= nextStatusLog)
                {
                    Console.WriteLine(
                        "Received {0} packets. Last seq={1} from {2}. Buttons=0x{3:X4} LT={4} RT={5} LX={6} LY={7} RX={8} RY={9}{10}",
                        packetCount,
                        seq,
                        remote,
                        wButtons,
                        leftTrigger,
                        rightTrigger,
                        thumbLX,
                        thumbLY,
                        thumbRX,
                        thumbRY,
                        hasGyro ? $" Gyro={gyroX:F2},{gyroY:F2},{gyroZ:F2}" : string.Empty);
                    nextStatusLog = DateTime.UtcNow.AddSeconds(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to parse packet: {0}", ex.Message);
            }
        }
    }

    static IPEndPoint? DiscoverPublicEndpoint(string stunHost = "stun.l.google.com", int stunPort = 19302, int timeoutMs = 2000)
    {
        const uint MagicCookie = 0x2112A442;

        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = timeoutMs;
        udp.Connect(stunHost, stunPort);

        byte[] tid = new byte[12];
        RandomNumberGenerator.Fill(tid);

        byte[] req = new byte[20];
        // Binding request
        req[0] = 0x00; req[1] = 0x01;
        // length 0
        req[4] = 0x21; req[5] = 0x12; req[6] = 0xA4; req[7] = 0x42;
        Array.Copy(tid, 0, req, 8, 12);

        udp.Send(req, req.Length);

        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            var resp = udp.Receive(ref remote);
            if (resp.Length < 20) return null;

            // check transaction id
            for (int i = 0; i < 12; ++i)
                if (resp[8 + i] != tid[i]) return null;

            int offset = 20;
            while (offset + 4 <= resp.Length)
            {
                ushort attrType = (ushort)((resp[offset] << 8) | resp[offset + 1]);
                ushort attrLen = (ushort)((resp[offset + 2] << 8) | resp[offset + 3]);
                offset += 4;

                if (offset + attrLen > resp.Length) break;

                if (attrType == 0x0020) // XOR-MAPPED-ADDRESS
                {
                    int idx = offset;
                    byte family = resp[idx + 1];
                    ushort xport = (ushort)((resp[idx + 2] << 8) | (resp[idx + 3]));
                    uint port = (uint)(xport ^ (MagicCookie >> 16));

                    if (family == 0x01) // IPv4
                    {
                        uint xaddr = (uint)((resp[idx + 4] << 24) | (resp[idx + 5] << 16) | (resp[idx + 6] << 8) | resp[idx + 7]);
                        uint addr = xaddr ^ MagicCookie;
                        var ip = new IPAddress(new byte[] { (byte)(addr >> 24), (byte)(addr >> 16), (byte)(addr >> 8), (byte)addr });
                        return new IPEndPoint(ip, (int)port);
                    }
                }

                offset += attrLen;
                if (attrLen % 4 != 0) offset += 4 - (attrLen % 4);
            }
        }
        catch { }

        return null;
    }
}
