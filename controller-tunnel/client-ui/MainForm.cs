using System;
using System.Net;
using System.Net.Sockets;
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

            uint seq = 0;
            while (!token.IsCancellationRequested)
            {
                // Call existing client code via process start? For simplicity, send a heartbeat packet here.
                try
                {
                    using var ms = new System.IO.MemoryStream();
                    using var bw = new System.IO.BinaryWriter(ms);
                    bw.Write(seq++);
                    bw.Write(DateTime.UtcNow.Ticks);
                    bw.Write((ushort)0);
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((short)0);
                    bw.Write((short)0);
                    bw.Write((short)0);
                    bw.Write((short)0);

                    byte[] data = ms.ToArray();
                    // simple unencrypted heartbeat for now; reuse console client for real controller polling
                    using var outMs = new System.IO.MemoryStream();
                    using var outBw = new System.IO.BinaryWriter(outMs);
                    outBw.Write((byte)0);
                    outBw.Write(data);
                    udp.Send(outMs.ToArray(), (int)outMs.Length, endpoint);

                    BeginInvoke(new Action(() => lblStatus.Text = $"Sent seq {seq - 1} at {DateTime.Now:T}"));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() => lblStatus.Text = "Send error: " + ex.Message));
                }

                Thread.Sleep(50);
            }
        }
    }
}
