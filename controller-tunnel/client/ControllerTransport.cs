using System;
using System.Collections.Generic;
using System.Text;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage.Streams;

namespace ControllerTunnel
{
    public readonly struct GyroSample
    {
        public GyroSample(bool hasGyro, float x, float y, float z)
        {
            HasGyro = hasGyro;
            X = x;
            Y = y;
            Z = z;
        }

        public bool HasGyro { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public static GyroSample Empty => new GyroSample(false, 0f, 0f, 0f);
    }

    public static class ControllerTransport
    {
        private const ushort SonyVendorId = 0x054C;
        private const float GyroCountsPerDegreeSecond = 1024f;
        private const int CalibrationMilliseconds = 2000;
        private const float StationaryDeadzoneDegreesPerSecond = 0.75f;
        private static readonly ushort[] SupportedSonyProducts = { 0x05C4, 0x09CC, 0x0CE6, 0x0DF2 };
        private static readonly object HidLock = new object();
        private static readonly List<HidDevice> MotionDevices = new List<HidDevice>();
        private static readonly HashSet<string> MotionDeviceIds = new HashSet<string>();
        private static GyroSample latestGyro = GyroSample.Empty;
        private static GyroSample latestRawGyro = GyroSample.Empty;
        private static bool hidInitialized;
        private static string lastHidDebug = "Sony motion HID not initialized yet.";
        private static DateTime calibrationStartedUtc = DateTime.MinValue;
        private static int calibrationSampleCount;
        private static double calibrationSumX;
        private static double calibrationSumY;
        private static double calibrationSumZ;
        private static float gyroBiasX;
        private static float gyroBiasY;
        private static float gyroBiasZ;
        private static bool gyroCalibrated;

        public static GyroSample ReadGyroSample()
        {
            EnsureMotionHidInitialized();

            lock (HidLock)
            {
                return latestGyro;
            }
        }

        public static void ResetGyroCalibration()
        {
            lock (HidLock)
            {
                ResetGyroCalibrationLocked();
            }
        }

        public static string GetGyroStatus()
        {
            EnsureMotionHidInitialized();

            lock (HidLock)
            {
                if (!latestRawGyro.HasGyro)
                    return "Waiting for gyro report...";

                if (!gyroCalibrated)
                    return $"Calibrating gyro: keep controller still ({calibrationSampleCount} samples)";

                return
                    $"Gyro deg/sec: {latestGyro.X:F2}, {latestGyro.Y:F2}, {latestGyro.Z:F2}\r\n" +
                    $"Raw counts: {latestRawGyro.X:F0}, {latestRawGyro.Y:F0}, {latestRawGyro.Z:F0}  Bias: {gyroBiasX:F0}, {gyroBiasY:F0}, {gyroBiasZ:F0}";
            }
        }

        private static void EnsureMotionHidInitialized()
        {
            lock (HidLock)
            {
                if (hidInitialized)
                    return;

                hidInitialized = true;
            }

            try
            {
                foreach (ushort productId in SupportedSonyProducts)
                {
                    OpenSonyMotionDevices(productId, 0x05); // Game Pad
                    OpenSonyMotionDevices(productId, 0x04); // Joystick
                }

                lock (HidLock)
                {
                    lastHidDebug = MotionDevices.Count == 0
                        ? "No supported Sony motion HID found. If DS4Windows is hiding the real controller, expose/emulate a DualShock device instead of only Xbox 360."
                        : $"Sony motion HID devices: {MotionDevices.Count}";
                }
            }
            catch (Exception ex)
            {
                lock (HidLock)
                    lastHidDebug = "Sony motion HID init failed: " + ex.Message;
            }
        }

        private static void OpenSonyMotionDevices(ushort productId, ushort usageId)
        {
            string selector = HidDevice.GetDeviceSelector(0x01, usageId, SonyVendorId, productId);
            var devices = DeviceInformation.FindAllAsync(selector).AsTask().GetAwaiter().GetResult();

            foreach (var info in devices)
            {
                var device = HidDevice.FromIdAsync(info.Id, Windows.Storage.FileAccessMode.Read)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                if (device == null)
                    continue;

                lock (HidLock)
                {
                    if (!MotionDeviceIds.Add(info.Id))
                    {
                        device.Dispose();
                        continue;
                    }

                    MotionDevices.Add(device);
                }

                device.InputReportReceived += MotionDevice_InputReportReceived;
            }
        }

        private static void MotionDevice_InputReportReceived(HidDevice sender, HidInputReportReceivedEventArgs args)
        {
            byte[] report = ReadReportBytes(args.Report);
            if (!TryParseSonyMotion(sender.ProductId, report, out float x, out float y, out float z))
                return;

            lock (HidLock)
            {
                latestRawGyro = new GyroSample(true, x, y, z);
                UpdateGyroCalibrationLocked(x, y, z);
            }
        }

        private static byte[] ReadReportBytes(HidInputReport report)
        {
            byte[] data = new byte[report.Data.Length];
            DataReader.FromBuffer(report.Data).ReadBytes(data);

            byte[] bytes = new byte[data.Length + 1];
            bytes[0] = (byte)report.Id;
            Array.Copy(data, 0, bytes, 1, data.Length);
            return bytes;
        }

        private static bool TryParseSonyMotion(ushort productId, IReadOnlyList<byte> report, out float pitch, out float yaw, out float roll)
        {
            pitch = 0f;
            yaw = 0f;
            roll = 0f;

            int offset;
            if (productId == 0x0CE6 || productId == 0x0DF2)
            {
                offset = report.Count > 0 && report[0] == 0x31 ? 17 : 16;
            }
            else
            {
                offset = report.Count > 0 && report[0] == 0x11 ? 15 : 13;
            }

            if (report.Count < offset + 6)
                return false;

            pitch = ReadInt16(report, offset);
            yaw = ReadInt16(report, offset + 2);
            roll = ReadInt16(report, offset + 4);
            return true;
        }

        private static short ReadInt16(IReadOnlyList<byte> bytes, int offset)
        {
            return (short)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        private static void UpdateGyroCalibrationLocked(float x, float y, float z)
        {
            if (calibrationStartedUtc == DateTime.MinValue)
                ResetGyroCalibrationLocked();

            if (!gyroCalibrated)
            {
                calibrationSampleCount++;
                calibrationSumX += x;
                calibrationSumY += y;
                calibrationSumZ += z;

                if ((DateTime.UtcNow - calibrationStartedUtc).TotalMilliseconds < CalibrationMilliseconds)
                {
                    latestGyro = GyroSample.Empty;
                    return;
                }

                gyroBiasX = (float)(calibrationSumX / calibrationSampleCount);
                gyroBiasY = (float)(calibrationSumY / calibrationSampleCount);
                gyroBiasZ = (float)(calibrationSumZ / calibrationSampleCount);
                gyroCalibrated = true;
                lastHidDebug = $"Sony motion HID devices: {MotionDevices.Count}. Gyro calibrated from {calibrationSampleCount} still samples.";
            }

            float calibratedX = ApplyDeadzone((x - gyroBiasX) / GyroCountsPerDegreeSecond);
            float calibratedY = ApplyDeadzone((y - gyroBiasY) / GyroCountsPerDegreeSecond);
            float calibratedZ = ApplyDeadzone((z - gyroBiasZ) / GyroCountsPerDegreeSecond);
            latestGyro = new GyroSample(true, calibratedX, calibratedY, calibratedZ);
        }

        private static void ResetGyroCalibrationLocked()
        {
            latestGyro = GyroSample.Empty;
            calibrationStartedUtc = DateTime.UtcNow;
            calibrationSampleCount = 0;
            calibrationSumX = 0;
            calibrationSumY = 0;
            calibrationSumZ = 0;
            gyroBiasX = 0f;
            gyroBiasY = 0f;
            gyroBiasZ = 0f;
            gyroCalibrated = false;
        }

        private static float ApplyDeadzone(float value)
        {
            return Math.Abs(value) < StationaryDeadzoneDegreesPerSecond ? 0f : value;
        }

        public static string GetRawControllerDebugInfo()
        {
            var builder = new StringBuilder();
            EnsureMotionHidInitialized();

            lock (HidLock)
            {
                builder.AppendLine(lastHidDebug);
                builder.AppendLine(latestRawGyro.HasGyro
                    ? GetGyroStatus()
                    : "No motion report received yet.");
            }

            return builder.ToString();
        }
    }
}
