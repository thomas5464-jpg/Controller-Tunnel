using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security.Cryptography;

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
    static extern int XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: client <server-ip> <port> [psk]");
            return;
        }

        var serverIp = args[0];
        var port = int.Parse(args[1]);
        string? psk = args.Length >= 3 ? args[2] : null;
        byte[]? key = null;
        if (!string.IsNullOrEmpty(psk))
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            key = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(psk));
            Console.WriteLine("Using PSK-derived AES-GCM key.");
        }

        var endpoint = new IPEndPoint(IPAddress.Parse(serverIp), port);

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

        Console.WriteLine("Client started. Polling XInput and sending to {0}:{1}", serverIp, port);

        while (true)
        {
            XINPUT_STATE state;
            int res = XInputGetState(0, out state);

            if (res == 0) // ERROR_SUCCESS
            {
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

                var plaintext = ms.ToArray();

                if (key != null)
                {
                    // flags(1) + nonce(12) + ciphertext + tag(16)
                    byte flags = 1;
                    byte[] nonce = new byte[12];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);

                    byte[] ciphertext = new byte[plaintext.Length];
                    byte[] tag = new byte[16];
                    using var aes = new System.Security.Cryptography.AesGcm(key);
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
            }

            Thread.Sleep(8); // ~125Hz
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
