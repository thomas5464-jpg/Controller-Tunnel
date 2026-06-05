using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace ClientUI
{
    public class MainForm : Form
    {
        TextBox txtServer;
        TextBox txtPort;
        TextBox txtPsk;
        Button btnStart;
        Button btnStop;
        Label lblStatus;

        CancellationTokenSource? cts;

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

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern int XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        public MainForm()
        {
            Text = "Controller Tunnel - Client";
            Width = 420; Height = 220;

            var lbl1 = new Label() { Left = 10, Top = 10, Text = "Server IP:" };
            txtServer = new TextBox() { Left = 100, Top = 8, Width = 200, Text = "127.0.0.1" };

            var lbl2 = new Label() { Left = 10, Top = 40, Text = "Port:" };
            txtPort = new TextBox() { Left = 100, Top = 38, Width = 80, Text = "5555" };

            var lbl3 = new Label() { Left = 10, Top = 70, Text = "PSK (optional):" };
            txtPsk = new TextBox() { Left = 100, Top = 68, Width = 250 };

            btnStart = new Button() { Left = 100, Top = 100, Text = "Start", Width = 80 };
            btnStop = new Button() { Left = 190, Top = 100, Text = "Stop", Width = 80, Enabled = false };

            lblStatus = new Label() { Left = 10, Top = 140, Width = 380, Text = "Idle" };

            btnStart.Click += BtnStart_Click;
            btnStop.Click += BtnStop_Click;

            Controls.AddRange(new Control[] { lbl1, txtServer, lbl2, txtPort, lbl3, txtPsk, btnStart, btnStop, lblStatus });
        }

        private void BtnStart_Click(object? sender, EventArgs e)
        {
            if (cts != null) return;
            if (!IPAddress.TryParse(txtServer.Text, out _))
            {
                MessageBox.Show("Invalid server IP");
                return;
            }

            if (!int.TryParse(txtPort.Text, out int port))
            {
                MessageBox.Show("Invalid port");
                return;
            }

            cts = new CancellationTokenSource();
            btnStart.Enabled = false; btnStop.Enabled = true;
            lblStatus.Text = "Starting...";

            var server = txtServer.Text;
            var psk = string.IsNullOrWhiteSpace(txtPsk.Text) ? null : txtPsk.Text;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    RunClientLoop(server, port, psk, cts.Token);
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() => lblStatus.Text = "Error: " + ex.Message));
                }
            });
        }

        private void BtnStop_Click(object? sender, EventArgs e)
        {
            cts?.Cancel();
            cts = null;
            btnStart.Enabled = true; btnStop.Enabled = false;
            lblStatus.Text = "Stopped";
        }

        private void RunClientLoop(string serverIp, int port, string? psk, CancellationToken token)
        {
            BeginInvoke(new Action(() => lblStatus.Text = "Running"));

            var endpoint = new IPEndPoint(IPAddress.Parse(serverIp), port);
            using var udp = new UdpClient();

            byte[]? key = null;
            if (!string.IsNullOrEmpty(psk))
            {
                using var sha = SHA256.Create();
                key = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(psk));
            }

            // STUN discovery (best-effort)
            try
            {
                var pub = DiscoverPublicEndpoint();
                if (pub != null) BeginInvoke(new Action(() => lblStatus.Text = "Public: " + pub.ToString()));
            }
            catch { }

            uint seq = 0;

            while (!token.IsCancellationRequested)
            {
                XINPUT_STATE state;
                int res = XInputGetState(0, out state);

                if (res == 0)
                {
                    using var ms = new System.IO.MemoryStream();
                    using var bw = new System.IO.BinaryWriter(ms);

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
                        byte flags = 1;
                        byte[] nonce = new byte[12];
                        RandomNumberGenerator.Fill(nonce);

                        byte[] ciphertext = new byte[plaintext.Length];
                        byte[] tag = new byte[16];
                        using var aes = new AesGcm(key, 16);
                        aes.Encrypt(nonce, plaintext, ciphertext, tag);

                        using var outMs = new System.IO.MemoryStream();
                        using var outBw = new System.IO.BinaryWriter(outMs);
                        outBw.Write(flags);
                        outBw.Write(nonce);
                        outBw.Write(ciphertext);
                        outBw.Write(tag);

                        var data = outMs.ToArray();
                        udp.Send(data, data.Length, endpoint);
                    }
                    else
                    {
                        using var outMs = new System.IO.MemoryStream();
                        using var outBw = new System.IO.BinaryWriter(outMs);
                        outBw.Write((byte)0);
                        outBw.Write(plaintext);
                        var data = outMs.ToArray();
                        udp.Send(data, data.Length, endpoint);
                    }

                    BeginInvoke(new Action(() => lblStatus.Text = $"Sent seq {seq - 1} at {DateTime.Now:T}"));
                }

                Thread.Sleep(8);
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
            req[0] = 0x00; req[1] = 0x01;
            req[4] = 0x21; req[5] = 0x12; req[6] = 0xA4; req[7] = 0x42;
            Array.Copy(tid, 0, req, 8, 12);

            udp.Send(req, req.Length);

            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                var resp = udp.Receive(ref remote);
                if (resp.Length < 20) return null;

                for (int i = 0; i < 12; ++i)
                    if (resp[8 + i] != tid[i]) return null;

                int offset = 20;
                while (offset + 4 <= resp.Length)
                {
                    ushort attrType = (ushort)((resp[offset] << 8) | resp[offset + 1]);
                    ushort attrLen = (ushort)((resp[offset + 2] << 8) | resp[offset + 3]);
                    offset += 4;

                    if (offset + attrLen > resp.Length) break;

                    if (attrType == 0x0020)
                    {
                        int idx = offset;
                        byte family = resp[idx + 1];
                        ushort xport = (ushort)((resp[idx + 2] << 8) | (resp[idx + 3]));
                        uint port = (uint)(xport ^ (MagicCookie >> 16));

                        if (family == 0x01)
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
}
