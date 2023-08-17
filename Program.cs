using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace traffic_carousel
{
    internal static class Program
    {

        private static NotifyIcon? notifyIcon;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            System.ComponentModel.Container components = new();

            ContextMenuStrip contextMenuStrip = new(components)
            {
                ImageScalingSize = new Size(20, 20),
                Size = new Size(105, 28),
            };
            ToolStripMenuItem exitToolStripMenuItem = new()
            {
                Size = new Size(104, 24),
                Text = "Exit",
            };
            exitToolStripMenuItem.Click += exitToolStripMenuItem_Click;
            contextMenuStrip.Items.AddRange(new ToolStripItem[] { exitToolStripMenuItem });


            notifyIcon = new(components)
            {
                Text = "Traffic Carousel",
                Visible = true,
                Icon = SystemIcons.Application,
                ContextMenuStrip = contextMenuStrip,
            };

            AsyncTasks tasks = new();

            Task task1 = tasks.TrafficMonitor(notifyIcon);
            Task task2 = tasks.DrawTrayIcon(notifyIcon);
            Application.Run();
        }

        private static void exitToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            Application.Exit();
        }
    }


    public class IconPainter
    {
        private Pen pen;

        public IconPainter()
        {
            pen = new(GetColor(), 25);
        }

        ~IconPainter()
        {
            pen.Dispose();
        }

        private Color GetColor()
        {
            string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            string keyName = "SystemUsesLightTheme";

            using RegistryKey key = Registry.CurrentUser;
            using RegistryKey? registryKey = key.OpenSubKey(subKey);
            if (registryKey == null)
            {
                return Color.White;
            }

            var usesLight = registryKey.GetValue(keyName);
            if (usesLight == null)
            {
                return Color.White;
            }

            if ((int)usesLight == 1)
            {
                return Color.Black;
            }
            else
            {
                return Color.White;
            }
        }

        public Icon Draw(float outerStartAngle, float outerExtendAngle, float innerStartAngle, float innerExtendAngle)
        {
            using Bitmap bitmap = new Bitmap(256, 256);
            using Graphics graphics = Graphics.FromImage(bitmap);
            Rectangle OuterRect = new Rectangle(30, 30, 196, 196);
            graphics.DrawArc(pen, OuterRect, outerStartAngle, 280 + outerExtendAngle);

            Rectangle innerRect = new Rectangle(75, 75, 106, 106);
            graphics.DrawArc(pen, innerRect, innerStartAngle, 280 + innerExtendAngle);

            IntPtr Hicon = bitmap.GetHicon();
            Icon newIcon = Icon.FromHandle(Hicon);
            return newIcon;
        }
    }

    public class AsyncTasks
    {
        private float download_ratio = 0;
        private float upload_ratio = 0;

        public AsyncTasks() { }

        public async Task TrafficMonitor(NotifyIcon trayIcon)
        {
            NetworkInterface? iface = null;
            long down_prep = -1;
            long up_prep = -1;

            for (; ; )
            {
                await Task.Delay(1000);
                if (iface == null || iface.GetIPProperties().GatewayAddresses.Count == 0)
                {
                    down_prep = -1;
                    up_prep = -1;
                    iface = AdapterInUse.Get();
                    continue;
                }

                if (down_prep == -1 || up_prep == -1)
                {
                    down_prep = iface.GetIPStatistics().BytesReceived;
                    up_prep = iface.GetIPStatistics().BytesSent;
                    continue;
                }

                long received = iface.GetIPStatistics().BytesReceived;
                long sent = iface.GetIPStatistics().BytesSent;
                string message = string.Format("U: {0:0.00} KB/s\nD: {1:0.00} KB/s", (sent - up_prep) / 1024.0, (received - down_prep) / 1024.0);
                System.Diagnostics.Debug.WriteLine(message);

                trayIcon.Text = message;

                int maxbytes = 1024 * 1024;
                long up = sent - up_prep;
                long down = received - down_prep;
                if (down > maxbytes) { down = maxbytes; }
                if (up > maxbytes) { up = maxbytes; }

                download_ratio = (float)down / maxbytes;
                upload_ratio = (float)up / maxbytes;

                down_prep = received;
                up_prep = sent;

                GC.Collect();
            }
        }

        public async Task DrawTrayIcon(NotifyIcon trayIcon)
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = CharSet.Auto)]
            extern static bool DestroyIcon(IntPtr handle);

            int loopTimes = 0;

            float down_speed = 0F;
            float up_speed = 0F;
            float max_speed = 150F;
            float friction = 1.5F;

            float request_down_speed;
            float request_up_speed;

            float up_displacement = 0;
            float down_displacement = 0;

            IconPainter iconPainter = new();
            trayIcon.Icon = iconPainter.Draw(0, 0, 0, 0);

            for (; ; )
            {
                request_down_speed = max_speed * download_ratio;
                request_up_speed = max_speed * upload_ratio;

                float down_delta = request_down_speed - down_speed;
                if (down_delta > 0)
                {
                    down_speed += down_delta * 0.4F;
                }
                else
                {
                    down_speed -= friction;
                }
                if (down_speed < friction)
                {
                    down_speed = 0;
                }

                float up_delta = request_up_speed - up_speed;
                if (up_delta > 0)
                {
                    up_speed += up_delta * 0.4F;
                }
                else
                {
                    up_speed -= friction;
                }
                if (up_speed < friction)
                {
                    up_speed = 0;
                }

                if (down_speed > 0 || up_speed > 0)
                {
                    down_displacement = (down_displacement + down_speed) % 360;
                    up_displacement = (up_displacement + up_speed) % 360;

                    trayIcon.Icon.Dispose();
                    DestroyIcon(trayIcon.Icon.Handle);
                    Icon newIcon = iconPainter.Draw(down_displacement, down_speed * 0.4F, up_displacement, up_speed * 0.4F);
                    trayIcon.Icon = newIcon;
                }

                loopTimes += 1;
                if (loopTimes > 100)
                {
                    GC.Collect();
                    loopTimes = 0;
                }
                await Task.Delay(30);
            }
        }

        public class AdapterInUse
        {
            public AdapterInUse() { }

            public static NetworkInterface? Get()
            {
                NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

                NetworkInterface? iface = networkInterfaces
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Where(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                    .FirstOrDefault();
                return iface;
            }

        }
    }
}