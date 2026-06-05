using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security.Cryptography;
using ControllerTunnel;

[StructLayout(LayoutKind.Sequential)]
struct XINPUT_GAMEPAD
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
struct XINPUT_STATE
{
    public uint dwPacketNumber;
    public XINPUT_GAMEPAD Gamepad;
}

class Program
{
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern int XInputGetState_1_4(uint dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    static extern int XInputGetState_1_3(uint dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    static extern int XInputGetState_9_1_0(uint dwUserIndex, out XINPUT_STATE pState);

    static int XInputGetState(uint dwUserIndex, out XINPUT_STATE pState)
    {
        pState = default;

        try
        {
            return XInputGetState_1_4(dwUserIndex, out pState);
        }
        catch (DllNotFoundException)
        {
        }

        try
        {
            return XInputGetState_1_3(dwUserIndex, out pState);
        }
        catch (DllNotFoundException)
        {
        }

        try
        {
            return XInputGetState_9_1_0(dwUserIndex, out pState);
        }
        catch (DllNotFoundException)
        {
        }

        return -1;
    }

    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: client <server-host-or-ip> <port> [psk]");
            return;
        }

        var serverHost = args[0];
        var port = int.Parse(args[1]);
        string? psk = args.Length >= 3 ? args[2] : null;
        byte[]? key = null;
        if (!string.IsNullOrEmpty(psk))
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            key = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(psk));
            Console.WriteLine("Using PSK-derived AES-GCM key.");
        }

        var endpoint = ResolveEndpoint(serverHost, port);
        Console.WriteLine("Resolved server {0}:{1} to {2}", serverHost, port, endpoint);

        // Discover public mapping via STUN (best-effort)
        try
        {
            var publicEp = DiscoverPublicEndpoint();
            if (publicEp != null)
                Console.WriteLine($"Local public mapping: {publicEp}");
            else
                Console.WriteLine("STUN discovery failed or timed out.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("STUN discovery error: {0}", ex.Message);
        }
        using var udp = new UdpClient();

        uint seq = 0;
        DateTime nextStatusLog = DateTime.UtcNow;
        int lastControllerResult = int.MinValue;

        Console.WriteLine("Client started. Polling XInput and sending to {0}", endpoint);
        Console.WriteLine("Keep the controller still for 2 seconds while gyro calibration runs.");
        ControllerTransport.ResetGyroCalibration();

        while (true)
        {
            XINPUT_STATE state;
            int res = XInputGetState(0, out state);

            if (res == 0) // ERROR_SUCCESS
            {
                lastControllerResult = 0;

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);

                bw.Write(seq++);
                bw.Write(DateTime.UtcNow.Ticks);
                bw.Write(state.Gamepad.wButtons);
                bw.Write(state.Gamepad.bLeftTrigger);
                bw.Write(state.Gamepad.bRightTrigger);
                bw.Write(state.Gamepad.sThumbLX);
                bw.Write(state.Gamepad.sThumbLY);
                bw.Write(state.Gamepad.sThumbRX);
                bw.Write(state.Gamepad.sThumbRY);

                var gyro = ControllerTransport.ReadGyroSample();
                if (gyro.HasGyro)
                {
                    bw.Write(gyro.X);
                    bw.Write(gyro.Y);
                    bw.Write(gyro.Z);
                }

                var plaintext = ms.ToArray();

                if (key != null)
                {
                    // flags(1) + nonce(12) + ciphertext + tag(16)
                    byte flags = 1;
                    byte[] nonce = new byte[12];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);

                    byte[] ciphertext = new byte[plaintext.Length];
                    byte[] tag = new byte[16];
                    using var aes = new System.Security.Cryptography.AesGcm(key, 16);
                    aes.Encrypt(nonce, plaintext, ciphertext, tag);

                    using var outMs = new MemoryStream();
                    using var outBw = new BinaryWriter(outMs);
                    outBw.Write(flags);
                    outBw.Write(nonce);
                    outBw.Write(ciphertext);
                    outBw.Write(tag);

                    var data = outMs.ToArray();
                    udp.Send(data, data.Length, endpoint);
                }
                else
                {
                    // unencrypted: flags(0) + plaintext
                    using var outMs = new MemoryStream();
                    using var outBw = new BinaryWriter(outMs);
                    outBw.Write((byte)0);
                    outBw.Write(plaintext);
                    var data = outMs.ToArray();
                    udp.Send(data, data.Length, endpoint);
                }

                if (DateTime.UtcNow >= nextStatusLog)
                {
                    Console.WriteLine(
                        "Sent seq {0} to {1}. Buttons=0x{2:X4} LT={3} RT={4} LX={5} LY={6} RX={7} RY={8}{9}",
                        seq - 1,
                        endpoint,
                        state.Gamepad.wButtons,
                        state.Gamepad.bLeftTrigger,
                        state.Gamepad.bRightTrigger,
                        state.Gamepad.sThumbLX,
                        state.Gamepad.sThumbLY,
                        state.Gamepad.sThumbRX,
                        state.Gamepad.sThumbRY,
                        gyro.HasGyro ? $" Gyro={gyro.X:F2},{gyro.Y:F2},{gyro.Z:F2}" : string.Empty);
                    nextStatusLog = DateTime.UtcNow.AddSeconds(1);
                }
            }
            else if (res != lastControllerResult || DateTime.UtcNow >= nextStatusLog)
            {
                Console.WriteLine("Controller 0 is not available through XInput. XInputGetState returned {0}.", res);
                Console.WriteLine("For DualShock/DualSense, enable an XInput wrapper such as DS4Windows or choose the controller in the GUI client.");
                lastControllerResult = res;
                nextStatusLog = DateTime.UtcNow.AddSeconds(2);
            }

            Thread.Sleep(8); // ~125Hz
        }
    }

    static IPEndPoint ResolveEndpoint(string host, int port)
    {
        if (IPAddress.TryParse(host, out IPAddress? parsed))
            return new IPEndPoint(parsed, port);

        IPAddress[] addresses = Dns.GetHostAddresses(host);
        foreach (var address in addresses)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return new IPEndPoint(address, port);
        }

        foreach (var address in addresses)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return new IPEndPoint(address, port);
        }

        throw new InvalidOperationException($"Could not resolve server host '{host}'.");
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
                    ushort xport = (ushort)((resp[idx + 2] << 8) | resp[idx + 3]);
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
                // padding to 4
                if (attrLen % 4 != 0) offset += 4 - (attrLen % 4);
            }
        }
        catch { }

        return null;
    }
}
