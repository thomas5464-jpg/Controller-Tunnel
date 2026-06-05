using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ClientUI
{
    public class MainForm : Form
    {
        TextBox txtServer;
        TextBox txtPort;
        TextBox txtPsk;
        ComboBox cmbControllerIndex;
        Button btnClientStart;
        Button btnClientStop;
        Label lblClientStatus;
        Label lblControllerStatus;
        List<int> availableControllers = new List<int>();

        TextBox txtListenPort;
        TextBox txtServerPsk;
        Button btnServerStart;
        Button btnServerStop;
        Label lblServerStatus;
        Label lblServerRemote;

        CancellationTokenSource? ctsClient;
        CancellationTokenSource? ctsServer;
        System.Windows.Forms.Timer controllerTimer;

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

        public MainForm()
        {
            Text = "Controller Tunnel";
            ClientSize = new Size(820, 370);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var tabs = new TabControl() { Left = 10, Top = 10, Width = 800, Height = 340, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            var clientTab = new TabPage("Client");
            var serverTab = new TabPage("Server");

            tabs.TabPages.AddRange(new TabPage[] { clientTab, serverTab });
            Controls.Add(tabs);

            clientTab.Padding = new Padding(10);
            serverTab.Padding = new Padding(10);

            var lblController = new Label() { Left = 10, Top = 10, Text = "Controller:" };
            cmbControllerIndex = new ComboBox() { Left = 120, Top = 8, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbControllerIndex.Items.Add("No controllers detected");
            cmbControllerIndex.SelectedIndex = 0;

            lblControllerStatus = new Label() { Left = 400, Top = 10, Width = 360, Text = "Detecting controllers..." };

            var lbl1 = new Label() { Left = 10, Top = 50, Text = "Server IP:" };
            txtServer = new TextBox() { Left = 120, Top = 48, Width = 530, Text = "127.0.0.1" };

            var lbl2 = new Label() { Left = 10, Top = 80, Text = "Port:" };
            txtPort = new TextBox() { Left = 120, Top = 78, Width = 120, Text = "5555" };

            var lbl3 = new Label() { Left = 10, Top = 110, Text = "PSK (optional):" };
            txtPsk = new TextBox() { Left = 120, Top = 108, Width = 620 };

            btnClientStart = new Button() { Left = 90, Top = 145, Text = "Start", Width = 80 };
            btnClientStop = new Button() { Left = 180, Top = 145, Text = "Stop", Width = 80, Enabled = false };
            lblClientStatus = new Label() { Left = 10, Top = 190, Width = 480, Text = "Idle" };

            btnClientStart.Click += BtnClientStart_Click;
            btnClientStop.Click += BtnClientStop_Click;

            clientTab.Controls.AddRange(new Control[] { lblController, cmbControllerIndex, lblControllerStatus, lbl1, txtServer, lbl2, txtPort, lbl3, txtPsk, btnClientStart, btnClientStop, lblClientStatus });

            var lblSrv1 = new Label() { Left = 10, Top = 10, Text = "Listen Port:" };
            txtListenPort = new TextBox() { Left = 110, Top = 8, Width = 100, Text = "5555" };

            var lblSrv2 = new Label() { Left = 10, Top = 40, Text = "PSK (optional):" };
            txtServerPsk = new TextBox() { Left = 110, Top = 38, Width = 620 };

            btnServerStart = new Button() { Left = 110, Top = 75, Text = "Start", Width = 80 };
            btnServerStop = new Button() { Left = 200, Top = 75, Text = "Stop", Width = 80, Enabled = false };
            lblServerStatus = new Label() { Left = 10, Top = 120, Width = 760, Text = "Idle" };
            lblServerRemote = new Label() { Left = 10, Top = 150, Width = 760, Text = "Remote: none" };

            btnServerStart.Click += BtnServerStart_Click;
            btnServerStop.Click += BtnServerStop_Click;

            serverTab.Controls.AddRange(new Control[] { lblSrv1, txtListenPort, lblSrv2, txtServerPsk, btnServerStart, btnServerStop, lblServerStatus, lblServerRemote });

            controllerTimer = new System.Windows.Forms.Timer() { Interval = 1000 };
            controllerTimer.Tick += ControllerTimer_Tick;
            controllerTimer.Start();
            UpdateControllerStatus();
        }

        private void ControllerTimer_Tick(object? sender, EventArgs e)
        {
            UpdateControllerStatus();
        }

        private void UpdateControllerStatus()
        {
            var detected = new List<int>();
            for (uint i = 0; i < 4; i++)
            {
                if (XInputGetState(i, out XINPUT_STATE state) == 0)
                    detected.Add((int)i);
            }

            cmbControllerIndex.Items.Clear();
            availableControllers.Clear();

            if (detected.Count == 0)
            {
                cmbControllerIndex.Items.Add("No controllers detected");
                cmbControllerIndex.SelectedIndex = 0;
                cmbControllerIndex.Enabled = false;
                btnClientStart.Enabled = false;
                lblControllerStatus.Text = "No XInput-compatible controllers detected. DualShock/DualSense require an XInput wrapper or adapter.";
            }
            else
            {
                foreach (var idx in detected)
                {
                    cmbControllerIndex.Items.Add($"Controller {idx}");
                    availableControllers.Add(idx);
                }

                if (cmbControllerIndex.Items.Count > 0)
                    cmbControllerIndex.SelectedIndex = 0;

                cmbControllerIndex.Enabled = true;
                btnClientStart.Enabled = true;
                lblControllerStatus.Text = "Detected: " + string.Join(", ", detected.ConvertAll(i => $"Controller {i}"));
            }
        }

        private void BtnClientStart_Click(object? sender, EventArgs e)
        {
            if (ctsClient != null) return;
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

            ctsClient = new CancellationTokenSource();
            btnClientStart.Enabled = false;
            btnClientStop.Enabled = true;
            lblClientStatus.Text = "Starting client...";

            var server = txtServer.Text;
            var psk = string.IsNullOrWhiteSpace(txtPsk.Text) ? null : txtPsk.Text;
            if (availableControllers.Count == 0)
            {
                BeginInvoke(new Action(() => lblClientStatus.Text = "No controller available."));
                return;
            }
            uint controllerIndex = (uint)availableControllers[cmbControllerIndex.SelectedIndex];

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    RunClientLoop(server, port, psk, controllerIndex, ctsClient.Token);
                    BeginInvoke(new Action(() => lblClientStatus.Text = "Stopped"));
                }
                catch (OperationCanceledException)
                {
                    BeginInvoke(new Action(() => lblClientStatus.Text = "Stopped"));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() => lblClientStatus.Text = "Error: " + ex.Message));
                }
            });
        }

        private void BtnClientStop_Click(object? sender, EventArgs e)
        {
            ctsClient?.Cancel();
            ctsClient = null;
            btnClientStart.Enabled = true;
            btnClientStop.Enabled = false;
            lblClientStatus.Text = "Stopping...";
        }

        private void BtnServerStart_Click(object? sender, EventArgs e)
        {
            if (ctsServer != null) return;
            if (!int.TryParse(txtListenPort.Text, out int port))
            {
                MessageBox.Show("Invalid listen port");
                return;
            }

            ctsServer = new CancellationTokenSource();
            btnServerStart.Enabled = false;
            btnServerStop.Enabled = true;
            lblServerStatus.Text = "Starting server...";

            var psk = string.IsNullOrWhiteSpace(txtServerPsk.Text) ? null : txtServerPsk.Text;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    RunServerLoop(port, psk, ctsServer.Token);
                    BeginInvoke(new Action(() => lblServerStatus.Text = "Stopped"));
                }
                catch (OperationCanceledException)
                {
                    BeginInvoke(new Action(() => lblServerStatus.Text = "Stopped"));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() => lblServerStatus.Text = "Error: " + ex.Message));
                }
            });
        }

        private void BtnServerStop_Click(object? sender, EventArgs e)
        {
            ctsServer?.Cancel();
            ctsServer = null;
            btnServerStart.Enabled = true;
            btnServerStop.Enabled = false;
            lblServerStatus.Text = "Stopping...";
        }

        private void RunClientLoop(string serverIp, int port, string? psk, uint controllerIndex, CancellationToken token)
        {
            BeginInvoke(new Action(() => lblClientStatus.Text = "Running"));

            var endpoint = new IPEndPoint(IPAddress.Parse(serverIp), port);
            using var udp = new UdpClient();

            byte[]? key = null;
            if (!string.IsNullOrEmpty(psk))
            {
                using var sha = SHA256.Create();
                key = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(psk));
            }

            try
            {
                var pub = DiscoverPublicEndpoint();
                if (pub != null)
                    BeginInvoke(new Action(() => lblClientStatus.Text = "Public: " + pub.ToString()));
            }
            catch { }

            uint seq = 0;

            while (!token.IsCancellationRequested)
            {
                XINPUT_STATE state;
                int res = XInputGetState(controllerIndex, out state);
                if (res != 0)
                {
                    BeginInvoke(new Action(() => lblClientStatus.Text = $"Controller {controllerIndex} not connected."));
                    Thread.Sleep(250);
                    continue;
                }

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
                    byte flags = 1;
                    byte[] nonce = new byte[12];
                    RandomNumberGenerator.Fill(nonce);

                    byte[] ciphertext = new byte[plaintext.Length];
                    byte[] tag = new byte[16];
                    using var aes = new AesGcm(key, 16);
                    aes.Encrypt(nonce, plaintext, ciphertext, tag);

                    using var outMs = new MemoryStream();
                    using var outBw = new BinaryWriter(outMs);
                    outBw.Write(flags);
                    outBw.Write(nonce);
                    outBw.Write(ciphertext);
                    outBw.Write(tag);
                    udp.Send(outMs.ToArray(), (int)outMs.Length, endpoint);
                }
                else
                {
                    using var outMs = new MemoryStream();
                    using var outBw = new BinaryWriter(outMs);
                    outBw.Write((byte)0);
                    outBw.Write(plaintext);
                    udp.Send(outMs.ToArray(), (int)outMs.Length, endpoint);
                }

                BeginInvoke(new Action(() => lblClientStatus.Text = $"Sent seq {seq - 1} at {DateTime.Now:T}"));
                Thread.Sleep(8);
            }

            token.ThrowIfCancellationRequested();
        }

        private void RunServerLoop(int port, string? psk, CancellationToken token)
        {
            byte[]? key = null;
            if (!string.IsNullOrEmpty(psk))
            {
                using var sha = SHA256.Create();
                key = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(psk));
            }

            using var udp = new UdpClient(port);
            udp.Client.ReceiveTimeout = 1000;

            using var client = new ViGEmClient();
            var controller = client.CreateXbox360Controller();
            controller.Connect();
            BeginInvoke(new Action(() => lblServerStatus.Text = "Server running, virtual controller connected."));

            int packetCount = 0;
            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (!token.IsCancellationRequested)
            {
                byte[] res;
                try
                {
                    res = udp.Receive(ref remoteEndPoint);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }

                if (res.Length < 1)
                    continue;

                byte flags = res[0];
                byte[] payload;

                if ((flags & 1) != 0)
                {
                    if (key == null)
                    {
                        BeginInvoke(new Action(() => lblServerStatus.Text = "Encrypted packet received but no PSK set."));
                        continue;
                    }

                    if (res.Length < 1 + 12 + 16)
                        continue;

                    byte[] nonce = new byte[12];
                    Array.Copy(res, 1, nonce, 0, 12);
                    int cipherLen = res.Length - 1 - 12 - 16;
                    if (cipherLen < 0) continue;

                    byte[] ciphertext = new byte[cipherLen];
                    Array.Copy(res, 1 + 12, ciphertext, 0, cipherLen);
                    byte[] tag = new byte[16];
                    Array.Copy(res, 1 + 12 + cipherLen, tag, 0, 16);

                    payload = new byte[cipherLen];
                    try
                    {
                        using var aes = new AesGcm(key, 16);
                        aes.Decrypt(nonce, ciphertext, tag, payload);
                    }
                    catch
                    {
                        BeginInvoke(new Action(() => lblServerStatus.Text = "Failed to decrypt packet."));
                        continue;
                    }
                }
                else
                {
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

                controller.SetSliderValue(Xbox360Slider.LeftTrigger, leftTrigger);
                controller.SetSliderValue(Xbox360Slider.RightTrigger, rightTrigger);
                controller.SetAxisValue(Xbox360Axis.LeftThumbX, thumbLX);
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, thumbLY);
                controller.SetAxisValue(Xbox360Axis.RightThumbX, thumbRX);
                controller.SetAxisValue(Xbox360Axis.RightThumbY, thumbRY);

                packetCount++;
                BeginInvoke(new Action(() =>
                {
                    lblServerStatus.Text = $"Received {packetCount} packets, seq {seq}.";
                    lblServerRemote.Text = $"Remote: {remoteEndPoint.Address}:{remoteEndPoint.Port}";
                }));
            }

            token.ThrowIfCancellationRequested();
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
