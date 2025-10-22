using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using FutronicAttendanceSystem.Utils;

namespace FutronicAttendanceSystem.UI
{
    /// <summary>
    /// Activity item for the feed
    /// </summary>
    public class ActivityItem
    {
        public DateTime Timestamp { get; set; }
        public string UserName { get; set; }
        public string Action { get; set; } // "Time In" or "Time Out"
        public string Location { get; set; } // "inside" or "outside"
        public bool Success { get; set; }
        public string StatusMessage { get; set; }
    }

    /// <summary>
    /// Modern UI panel for dual sensor management
    /// </summary>
    public class DualSensorPanel : Panel
    {
        // UI Components for Inside Sensor
        private Panel panelInside;
        private Label lblInsideTitle;
        private Label lblInsideStatus;
        private Label lblInsideDevice;
        private PictureBox picInsideFingerprint;
        private Label lblInsideLastScan;
        private CheckBox toggleInsideEnable;

        // UI Components for Outside Sensor
        private Panel panelOutside;
        private Label lblOutsideTitle;
        private Label lblOutsideStatus;
        private Label lblOutsideDevice;
        private PictureBox picOutsideFingerprint;
        private Label lblOutsideLastScan;
        private CheckBox toggleOutsideEnable;

        // Activity Feed
        private Panel panelActivityFeed;
        private ListBox lstActivityFeed;
        private Label lblActivityTitle;

        // Room info
        private Label lblRoomInfo;
        private Label lblTimeInfo;
        private Button btnChangeConfig;
        private Button btnTestInside;
        private Button btnTestOutside;
        
        // Live configuration controls
        private Button btnShowConfig;
        private Panel panelLiveConfig;
        private ComboBox cmbInsideSensorSelect;
        private ComboBox cmbOutsideSensorSelect;
        private Button btnApplyConfig;
        private Button btnRefreshDevices;
        private bool configPanelVisible = false;

        private Timer clockTimer;
        private List<ActivityItem> activityItems = new List<ActivityItem>();

        public event EventHandler<bool> InsideSensorEnabledChanged;
        public event EventHandler<bool> OutsideSensorEnabledChanged;
        public event EventHandler ChangeConfigurationRequested;
        public event EventHandler TestInsideSensorRequested;
        public event EventHandler TestOutsideSensorRequested;
        public event EventHandler<(int insideIndex, int outsideIndex)> SensorReassignmentRequested;

        public DualSensorPanel()
        {
            InitializeComponents();
            StartClockTimer();
        }
        
        // Called from MainForm after the panel is fully initialized
        public void InitializeDeviceList()
        {
            // Auto-load device list on startup for easy testing
            RefreshAvailableDevices();
        }

        private void InitializeComponents()
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(245, 247, 250);
            this.Padding = new Padding(20);

            // Header section
            CreateHeaderSection();

            // Inside sensor panel (left side)
            CreateInsideSensorPanel();

            // Outside sensor panel (right side)
            CreateOutsideSensorPanel();

            // Activity feed (bottom)
            CreateActivityFeedPanel();
        }

        private void CreateHeaderSection()
        {
            var headerPanel = new Panel
            {
                Location = new Point(20, 20),
                Size = new Size(this.Width - 40, 80),
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Add shadow effect (simulated with border)
            headerPanel.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, headerPanel.ClientRectangle,
                    Color.FromArgb(220, 223, 230), ButtonBorderStyle.Solid);
            };

            lblRoomInfo = new Label
            {
                Text = "🏫 Dual Sensor Attendance System",
                Location = new Point(20, 15),
                Size = new Size(600, 25),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            headerPanel.Controls.Add(lblRoomInfo);

            lblTimeInfo = new Label
            {
                Text = DateTime.Now.ToString("dddd, MMMM dd, yyyy - hh:mm:ss tt"),
                Location = new Point(20, 45),
                Size = new Size(600, 20),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(108, 117, 125)
            };
            headerPanel.Controls.Add(lblTimeInfo);

            btnShowConfig = new Button
            {
                Text = "🔧 Hide Config",  // Start as "Hide" since panel is visible
                Location = new Point(headerPanel.Width - 290, 25),
                Size = new Size(130, 35),
                BackColor = Color.FromArgb(255, 193, 7),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnShowConfig.FlatAppearance.BorderSize = 0;
            btnShowConfig.Click += BtnShowConfig_Click;
            headerPanel.Controls.Add(btnShowConfig);

            btnChangeConfig = new Button
            {
                Text = "⚙ Change Room",
                Location = new Point(headerPanel.Width - 150, 25),
                Size = new Size(130, 35),
                BackColor = Color.FromArgb(0, 123, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnChangeConfig.FlatAppearance.BorderSize = 0;
            btnChangeConfig.Click += (s, e) => ChangeConfigurationRequested?.Invoke(this, EventArgs.Empty);
            headerPanel.Controls.Add(btnChangeConfig);

            this.Controls.Add(headerPanel);
            
            // Create live configuration panel (initially hidden)
            CreateLiveConfigPanel();
        }
        
        private void BtnShowConfig_Click(object sender, EventArgs e)
        {
            configPanelVisible = !configPanelVisible;
            panelLiveConfig.Visible = configPanelVisible;
            btnShowConfig.Text = configPanelVisible ? "🔧 Hide Config" : "🔧 Show Config";
            
            if (configPanelVisible)
            {
                RefreshAvailableDevices();
            }
        }
        
        private void CreateLiveConfigPanel()
        {
            panelLiveConfig = new Panel
            {
                Location = new Point(20, 110),
                Size = new Size(this.Width - 40, 120),
                BackColor = Color.FromArgb(255, 248, 225),
                Visible = true,  // Show by default so admin can see configuration
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            configPanelVisible = true; // Set to true since we're showing it
            
            panelLiveConfig.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, panelLiveConfig.ClientRectangle,
                    Color.FromArgb(255, 193, 7), ButtonBorderStyle.Solid);
            };
            
            var lblTitle = new Label
            {
                Text = "⚙️ Live Sensor Configuration - Reassign Sensors (Testing: Use same device for both)",
                Location = new Point(15, 10),
                Size = new Size(650, 25),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            panelLiveConfig.Controls.Add(lblTitle);
            
            // Inside sensor selection
            var lblInside = new Label
            {
                Text = "Inside Sensor:",
                Location = new Point(15, 45),
                Size = new Size(100, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            panelLiveConfig.Controls.Add(lblInside);
            
            cmbInsideSensorSelect = new ComboBox
            {
                Location = new Point(120, 42),
                Size = new Size(300, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9)
            };
            panelLiveConfig.Controls.Add(cmbInsideSensorSelect);
            
            // Outside sensor selection
            var lblOutside = new Label
            {
                Text = "Outside Sensor:",
                Location = new Point(15, 80),
                Size = new Size(100, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            panelLiveConfig.Controls.Add(lblOutside);
            
            cmbOutsideSensorSelect = new ComboBox
            {
                Location = new Point(120, 77),
                Size = new Size(300, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9)
            };
            panelLiveConfig.Controls.Add(cmbOutsideSensorSelect);
            
            // Refresh button
            btnRefreshDevices = new Button
            {
                Text = "🔄 Refresh",
                Location = new Point(430, 42),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(108, 117, 125),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9),
                Cursor = Cursors.Hand
            };
            btnRefreshDevices.FlatAppearance.BorderSize = 0;
            btnRefreshDevices.Click += (s, e) => RefreshAvailableDevices();
            panelLiveConfig.Controls.Add(btnRefreshDevices);
            
            // Apply button
            btnApplyConfig = new Button
            {
                Text = "✓ Apply Changes",
                Location = new Point(430, 77),
                Size = new Size(150, 30),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnApplyConfig.FlatAppearance.BorderSize = 0;
            btnApplyConfig.Click += BtnApplyConfig_Click;
            panelLiveConfig.Controls.Add(btnApplyConfig);
            
            this.Controls.Add(panelLiveConfig);
        }
        
        private void RefreshAvailableDevices()
        {
            try
            {
                var devices = UsbDeviceHelper.EnumerateFingerprintDevices();
                
                cmbInsideSensorSelect.Items.Clear();
                cmbOutsideSensorSelect.Items.Clear();
                
                if (devices.Count == 0)
                {
                    cmbInsideSensorSelect.Items.Add("No devices detected");
                    cmbOutsideSensorSelect.Items.Add("No devices detected");
                    cmbInsideSensorSelect.SelectedIndex = 0;
                    cmbOutsideSensorSelect.SelectedIndex = 0;
                    btnApplyConfig.Enabled = false;
                    return;
                }
                
                foreach (var device in devices)
                {
                    cmbInsideSensorSelect.Items.Add(device);
                    cmbOutsideSensorSelect.Items.Add(device);
                }
                
                if (cmbInsideSensorSelect.Items.Count > 0)
                    cmbInsideSensorSelect.SelectedIndex = 0;
                    
                if (cmbOutsideSensorSelect.Items.Count > 1)
                    cmbOutsideSensorSelect.SelectedIndex = 1;
                else if (cmbOutsideSensorSelect.Items.Count > 0)
                    cmbOutsideSensorSelect.SelectedIndex = 0;
                    
                btnApplyConfig.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing devices:\n{ex.Message}", "Device Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnApplyConfig_Click(object sender, EventArgs e)
        {
            if (cmbInsideSensorSelect.SelectedItem is UsbDeviceInfo insideDevice &&
                cmbOutsideSensorSelect.SelectedItem is UsbDeviceInfo outsideDevice)
            {
                var result = MessageBox.Show(
                    $"Apply new sensor configuration?\n\n" +
                    $"Inside: {insideDevice.FriendlyName}\n" +
                    $"Outside: {outsideDevice.FriendlyName}\n\n" +
                    $"This will restart the sensor operations.",
                    "Confirm Sensor Reassignment",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                    
                if (result == DialogResult.Yes)
                {
                    SensorReassignmentRequested?.Invoke(this, (insideDevice.DeviceIndex, outsideDevice.DeviceIndex));
                    configPanelVisible = false;
                    panelLiveConfig.Visible = false;
                    btnShowConfig.Text = "🔧 Sensor Config";
                }
            }
            else
            {
                MessageBox.Show("Please select valid devices for both sensors.", "Invalid Selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CreateInsideSensorPanel()
        {
            panelInside = new Panel
            {
                Location = new Point(20, 260),  // Was 250, move to 260
                Size = new Size((this.Width - 60) / 2, 350),  // Reduce height slightly
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom
            };

            panelInside.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, panelInside.ClientRectangle,
                    Color.FromArgb(220, 223, 230), ButtonBorderStyle.Solid);
            };

            lblInsideTitle = new Label
            {
                Text = "📍 INSIDE DOOR SENSOR",
                Location = new Point(15, 15),
                Size = new Size(panelInside.Width - 30, 25),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 167, 69)
            };
            panelInside.Controls.Add(lblInsideTitle);

            lblInsideStatus = new Label
            {
                Text = "● Active",
                Location = new Point(15, 45),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 167, 69)
            };
            panelInside.Controls.Add(lblInsideStatus);

            toggleInsideEnable = new CheckBox
            {
                Text = "Enable Sensor",
                Location = new Point(panelInside.Width - 130, 45),
                Size = new Size(115, 20),
                Font = new Font("Segoe UI", 9),
                Checked = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            toggleInsideEnable.CheckedChanged += (s, e) =>
            {
                InsideSensorEnabledChanged?.Invoke(this, toggleInsideEnable.Checked);
                UpdateInsideStatus(toggleInsideEnable.Checked ? "Active" : "Disabled");
            };
            panelInside.Controls.Add(toggleInsideEnable);

            lblInsideDevice = new Label
            {
                Text = "Device: Sensor #1",
                Location = new Point(15, 70),
                Size = new Size(panelInside.Width - 30, 20),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(108, 117, 125)
            };
            panelInside.Controls.Add(lblInsideDevice);

            picInsideFingerprint = new PictureBox
            {
                Location = new Point((panelInside.Width - 180) / 2, 100),
                Size = new Size(180, 200),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(248, 249, 250),
                SizeMode = PictureBoxSizeMode.Zoom,
                Anchor = AnchorStyles.Top
            };
            panelInside.Controls.Add(picInsideFingerprint);

            lblInsideLastScan = new Label
            {
                Text = "💚 Ready to scan...",
                Location = new Point(15, 310),
                Size = new Size(panelInside.Width - 30, 60),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(108, 117, 125),
                TextAlign = ContentAlignment.TopLeft
            };
            panelInside.Controls.Add(lblInsideLastScan);

            // Test button
            btnTestInside = new Button
            {
                Text = "🧪 Test Scan",
                Location = new Point(15, panelInside.Height - 45),
                Size = new Size(panelInside.Width - 30, 30),
                BackColor = Color.FromArgb(255, 193, 7),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            btnTestInside.FlatAppearance.BorderSize = 0;
            btnTestInside.Click += (s, e) => TestInsideSensorRequested?.Invoke(this, EventArgs.Empty);
            panelInside.Controls.Add(btnTestInside);

            this.Controls.Add(panelInside);
        }

        private void CreateOutsideSensorPanel()
        {
            int leftMargin = 20 + (this.Width - 60) / 2 + 20;
            
            panelOutside = new Panel
            {
                Location = new Point(leftMargin, 260),  // Was 250, move to 260
                Size = new Size((this.Width - 60) / 2, 350),  // Reduce height slightly
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
            };

            panelOutside.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, panelOutside.ClientRectangle,
                    Color.FromArgb(220, 223, 230), ButtonBorderStyle.Solid);
            };

            lblOutsideTitle = new Label
            {
                Text = "📍 OUTSIDE DOOR SENSOR",
                Location = new Point(15, 15),
                Size = new Size(panelOutside.Width - 30, 25),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 53, 69)
            };
            panelOutside.Controls.Add(lblOutsideTitle);

            lblOutsideStatus = new Label
            {
                Text = "● Active",
                Location = new Point(15, 45),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 167, 69)
            };
            panelOutside.Controls.Add(lblOutsideStatus);

            toggleOutsideEnable = new CheckBox
            {
                Text = "Enable Sensor",
                Location = new Point(panelOutside.Width - 130, 45),
                Size = new Size(115, 20),
                Font = new Font("Segoe UI", 9),
                Checked = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            toggleOutsideEnable.CheckedChanged += (s, e) =>
            {
                OutsideSensorEnabledChanged?.Invoke(this, toggleOutsideEnable.Checked);
                UpdateOutsideStatus(toggleOutsideEnable.Checked ? "Active" : "Disabled");
            };
            panelOutside.Controls.Add(toggleOutsideEnable);

            lblOutsideDevice = new Label
            {
                Text = "Device: Sensor #2",
                Location = new Point(15, 70),
                Size = new Size(panelOutside.Width - 30, 20),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(108, 117, 125)
            };
            panelOutside.Controls.Add(lblOutsideDevice);

            picOutsideFingerprint = new PictureBox
            {
                Location = new Point((panelOutside.Width - 180) / 2, 100),
                Size = new Size(180, 200),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(248, 249, 250),
                SizeMode = PictureBoxSizeMode.Zoom,
                Anchor = AnchorStyles.Top
            };
            panelOutside.Controls.Add(picOutsideFingerprint);

            lblOutsideLastScan = new Label
            {
                Text = "💚 Ready to scan...",
                Location = new Point(15, 310),
                Size = new Size(panelOutside.Width - 30, 60),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(108, 117, 125),
                TextAlign = ContentAlignment.TopLeft
            };
            panelOutside.Controls.Add(lblOutsideLastScan);

            // Test button
            btnTestOutside = new Button
            {
                Text = "🧪 Test Scan",
                Location = new Point(15, panelOutside.Height - 45),
                Size = new Size(panelOutside.Width - 30, 30),
                BackColor = Color.FromArgb(255, 193, 7),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            btnTestOutside.FlatAppearance.BorderSize = 0;
            btnTestOutside.Click += (s, e) => TestOutsideSensorRequested?.Invoke(this, EventArgs.Empty);
            panelOutside.Controls.Add(btnTestOutside);

            this.Controls.Add(panelOutside);
        }

        private void CreateActivityFeedPanel()
        {
            panelActivityFeed = new Panel
            {
                Location = new Point(20, 540),
                Size = new Size(this.Width - 40, 200),
                BackColor = Color.White,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            panelActivityFeed.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, panelActivityFeed.ClientRectangle,
                    Color.FromArgb(220, 223, 230), ButtonBorderStyle.Solid);
            };

            lblActivityTitle = new Label
            {
                Text = "📊 Recent Activity Feed",
                Location = new Point(15, 15),
                Size = new Size(panelActivityFeed.Width - 30, 25),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            panelActivityFeed.Controls.Add(lblActivityTitle);

            lstActivityFeed = new ListBox
            {
                Location = new Point(15, 50),
                Size = new Size(panelActivityFeed.Width - 30, panelActivityFeed.Height - 65),
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(248, 249, 250),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            panelActivityFeed.Controls.Add(lstActivityFeed);

            this.Controls.Add(panelActivityFeed);
        }

        private void StartClockTimer()
        {
            clockTimer = new Timer();
            clockTimer.Interval = 1000; // Update every second
            clockTimer.Tick += (s, e) =>
            {
                if (lblTimeInfo != null && !lblTimeInfo.IsDisposed)
                {
                    lblTimeInfo.Text = DateTime.Now.ToString("dddd, MMMM dd, yyyy - hh:mm:ss tt");
                }
            };
            clockTimer.Start();
        }

        public void UpdateRoomInfo(string roomName, string building)
        {
            if (lblRoomInfo != null)
            {
                lblRoomInfo.Text = $"🏫 {roomName} - {building}";
            }
        }

        public void UpdateInsideDeviceInfo(string deviceInfo)
        {
            if (lblInsideDevice != null)
            {
                lblInsideDevice.Text = $"Device: {deviceInfo}";
            }
        }

        public void UpdateOutsideDeviceInfo(string deviceInfo)
        {
            if (lblOutsideDevice != null)
            {
                lblOutsideDevice.Text = $"Device: {deviceInfo}";
            }
        }

        public void UpdateInsideStatus(string status)
        {
            if (lblInsideStatus != null)
            {
                lblInsideStatus.Text = $"● {status}";
                lblInsideStatus.ForeColor = status == "Active" ? 
                    Color.FromArgb(40, 167, 69) : Color.FromArgb(220, 53, 69);
            }
        }

        public void UpdateOutsideStatus(string status)
        {
            if (lblOutsideStatus != null)
            {
                lblOutsideStatus.Text = $"● {status}";
                lblOutsideStatus.ForeColor = status == "Active" ? 
                    Color.FromArgb(40, 167, 69) : Color.FromArgb(220, 53, 69);
            }
        }

        public void UpdateInsideFingerprintImage(Bitmap image)
        {
            if (picInsideFingerprint != null)
            {
                picInsideFingerprint.Image = image;
            }
        }

        public void UpdateOutsideFingerprintImage(Bitmap image)
        {
            if (picOutsideFingerprint != null)
            {
                picOutsideFingerprint.Image = image;
            }
        }

        public void UpdateInsideLastScan(string userName, string status, bool success)
        {
            if (lblInsideLastScan != null)
            {
                string icon = success ? "✓" : "✗";
                lblInsideLastScan.Text = $"{icon} {userName}\n{status}\n{DateTime.Now:hh:mm:ss tt}";
                lblInsideLastScan.ForeColor = success ? Color.FromArgb(40, 167, 69) : Color.FromArgb(220, 53, 69);
            }
        }

        public void UpdateOutsideLastScan(string userName, string status, bool success)
        {
            if (lblOutsideLastScan != null)
            {
                string icon = success ? "✓" : "✗";
                lblOutsideLastScan.Text = $"{icon} {userName}\n{status}\n{DateTime.Now:hh:mm:ss tt}";
                lblOutsideLastScan.ForeColor = success ? Color.FromArgb(40, 167, 69) : Color.FromArgb(220, 53, 69);
            }
        }

        public void AddActivityItem(ActivityItem item)
        {
            activityItems.Insert(0, item); // Add to beginning
            
            // Keep only last 50 items
            if (activityItems.Count > 50)
            {
                activityItems = activityItems.Take(50).ToList();
            }

            RefreshActivityFeed();
        }

        private void RefreshActivityFeed()
        {
            if (lstActivityFeed != null)
            {
                lstActivityFeed.Items.Clear();
                foreach (var item in activityItems)
                {
                    string icon = item.Success ? "✓" : "✗";
                    string locationIcon = item.Location == "inside" ? "🟢" : "🔴";
                    string text = $"{item.Timestamp:HH:mm:ss} {locationIcon} {item.UserName} - {item.Action} ({item.Location}) {icon}";
                    lstActivityFeed.Items.Add(text);
                }
            }
        }

        public void SetInsideSensorEnabled(bool enabled)
        {
            if (toggleInsideEnable != null)
            {
                toggleInsideEnable.Checked = enabled;
            }
        }

        public void SetOutsideSensorEnabled(bool enabled)
        {
            if (toggleOutsideEnable != null)
            {
                toggleOutsideEnable.Checked = enabled;
            }
        }

        public bool IsInsideSensorEnabled => toggleInsideEnable?.Checked ?? false;
        public bool IsOutsideSensorEnabled => toggleOutsideEnable?.Checked ?? false;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                clockTimer?.Stop();
                clockTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

