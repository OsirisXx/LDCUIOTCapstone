using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FutronicAttendanceSystem.Database;
using FutronicAttendanceSystem.Database.Models;
using FutronicAttendanceSystem.Utils;

namespace FutronicAttendanceSystem.UI
{
    public class StartupConfigDialog : Form
    {
        private ComboBox cmbRoom;
        private ComboBox cmbInsideSensor;
        private ComboBox cmbOutsideSensor;
        private CheckBox chkRememberConfig;
        private CheckBox chkTestMode;
        private Button btnConnect;
        private Button btnCancel;
        private Label lblStatus;

        private List<Room> availableRooms;
        private List<UsbDeviceInfo> availableDevices;
        private DatabaseManager dbManager;

        public DeviceConfiguration SelectedConfiguration { get; private set; }

        public StartupConfigDialog(DatabaseManager db)
        {
            this.dbManager = db;
            InitializeDialog();
            LoadRoomsAndDevices();
        }

        private void InitializeDialog()
        {
            // Dialog settings
            this.Text = "🏫 Device Configuration - Dual Sensor Setup";
            this.Size = new Size(650, 580);  // Increased from 550 to 580 to show buttons
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(245, 247, 250);

            // Title label
            var lblTitle = new Label
            {
                Text = "Configure Dual Fingerprint Sensor System",
                Location = new Point(20, 20),
                Size = new Size(600, 30),
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            this.Controls.Add(lblTitle);

            var lblSubtitle = new Label
            {
                Text = "Select the room and assign sensors for inside and outside door positions",
                Location = new Point(20, 55),
                Size = new Size(600, 20),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(108, 117, 125)
            };
            this.Controls.Add(lblSubtitle);

            // Room selection panel
            var panelRoom = CreateStyledPanel(new Point(20, 90), new Size(600, 90), "📍 Room Selection");
            
            var lblRoom = new Label
            {
                Text = "Select Room:",
                Location = new Point(15, 35),
                Size = new Size(100, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            panelRoom.Controls.Add(lblRoom);

            cmbRoom = new ComboBox
            {
                Location = new Point(125, 32),
                Size = new Size(450, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10)
            };
            cmbRoom.SelectedIndexChanged += CmbRoom_SelectedIndexChanged;
            panelRoom.Controls.Add(cmbRoom);

            this.Controls.Add(panelRoom);

            // Inside sensor panel
            var panelInside = CreateStyledPanel(new Point(20, 195), new Size(600, 90), "🟢 Inside Door Sensor");
            
            var lblInside = new Label
            {
                Text = "Assign Sensor:",
                Location = new Point(15, 35),
                Size = new Size(100, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            panelInside.Controls.Add(lblInside);

            cmbInsideSensor = new ComboBox
            {
                Location = new Point(125, 32),
                Size = new Size(450, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10)
            };
            panelInside.Controls.Add(cmbInsideSensor);

            this.Controls.Add(panelInside);

            // Outside sensor panel
            var panelOutside = CreateStyledPanel(new Point(20, 300), new Size(600, 90), "🔴 Outside Door Sensor");
            
            var lblOutside = new Label
            {
                Text = "Assign Sensor:",
                Location = new Point(15, 35),
                Size = new Size(100, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            panelOutside.Controls.Add(lblOutside);

            cmbOutsideSensor = new ComboBox
            {
                Location = new Point(125, 32),
                Size = new Size(450, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10)
            };
            panelOutside.Controls.Add(cmbOutsideSensor);

            this.Controls.Add(panelOutside);

            // Options
            chkRememberConfig = new CheckBox
            {
                Text = "Remember this configuration for next startup",
                Location = new Point(30, 405),
                Size = new Size(350, 25),
                Font = new Font("Segoe UI", 9),
                Checked = true
            };
            this.Controls.Add(chkRememberConfig);

            chkTestMode = new CheckBox
            {
                Text = "Enable Test Mode (simulate dual sensors with 1 device)",
                Location = new Point(30, 430),
                Size = new Size(400, 25),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(255, 140, 0)
            };
            chkTestMode.CheckedChanged += ChkTestMode_CheckedChanged;
            this.Controls.Add(chkTestMode);

            // Status label
            lblStatus = new Label
            {
                Text = "",
                Location = new Point(30, 465),
                Size = new Size(580, 30),  // Increased height for multi-line status
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(40, 167, 69)  // Changed to green for positive messages
            };
            this.Controls.Add(lblStatus);

            // Buttons - moved down to be fully visible
            btnConnect = new Button
            {
                Text = "Connect & Start",
                Location = new Point(400, 505),  // Moved from 485 to 505
                Size = new Size(130, 40),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.Click += BtnConnect_Click;
            this.Controls.Add(btnConnect);

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(545, 505),  // Moved from 485 to 505
                Size = new Size(80, 40),
                BackColor = Color.FromArgb(108, 117, 125),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);
        }

        private Panel CreateStyledPanel(Point location, Size size, string title)
        {
            var panel = new Panel
            {
                Location = location,
                Size = size,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblTitle = new Label
            {
                Text = title,
                Location = new Point(10, 8),
                Size = new Size(580, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 58, 64)
            };
            panel.Controls.Add(lblTitle);

            return panel;
        }

        private void LoadRoomsAndDevices()
        {
            try
            {
                lblStatus.Text = "Loading rooms and detecting sensors...";
                lblStatus.ForeColor = Color.FromArgb(0, 123, 255);
                Application.DoEvents();

                // Load rooms from database
                availableRooms = dbManager.GetAllAvailableRooms();
                cmbRoom.Items.Clear();
                foreach (var room in availableRooms)
                {
                    cmbRoom.Items.Add(room);
                }
                cmbRoom.DisplayMember = "DisplayName";

                if (cmbRoom.Items.Count > 0)
                {
                    cmbRoom.SelectedIndex = 0;
                }

                // Enumerate USB fingerprint devices
                availableDevices = UsbDeviceHelper.EnumerateFingerprintDevices();
                
                cmbInsideSensor.Items.Clear();
                cmbOutsideSensor.Items.Clear();
                
                // Add "None" option first
                var noneOption = new UsbDeviceInfo
                {
                    DeviceId = "NONE",
                    FriendlyName = "None (No sensor)",
                    Description = "None",
                    DeviceIndex = -1
                };
                
                cmbInsideSensor.Items.Add(noneOption);
                cmbOutsideSensor.Items.Add(noneOption);

                // Add actual devices
                foreach (var device in availableDevices)
                {
                    cmbInsideSensor.Items.Add(device);
                    cmbOutsideSensor.Items.Add(device);
                }

                // Select first real device for inside, None for outside by default
                if (cmbInsideSensor.Items.Count > 1)
                {
                    cmbInsideSensor.SelectedIndex = 1;  // First real device
                }
                else
                {
                    cmbInsideSensor.SelectedIndex = 0;  // None if no devices
                }

                if (cmbOutsideSensor.Items.Count > 2)
                {
                    cmbOutsideSensor.SelectedIndex = 1; // Select second device by default
                }
                else if (cmbOutsideSensor.Items.Count > 0)
                {
                    cmbOutsideSensor.SelectedIndex = 0; // Same device (test mode)
                }

                lblStatus.Text = $"✓ Found {cmbRoom.Items.Count} room(s) and {availableDevices.Count} sensor(s)";
                lblStatus.ForeColor = Color.FromArgb(40, 167, 69);
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"⚠ Error loading data: {ex.Message}";
                lblStatus.ForeColor = Color.FromArgb(220, 53, 69);
                MessageBox.Show($"Error loading configuration:\n{ex.Message}", "Configuration Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CmbRoom_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbRoom.SelectedItem is Room room)
            {
                lblStatus.Text = $"Selected: {room.DisplayName}";
                lblStatus.ForeColor = Color.FromArgb(0, 123, 255);
            }
        }

        private void ChkTestMode_CheckedChanged(object sender, EventArgs e)
        {
            if (chkTestMode.Checked)
            {
                // In test mode, both sensors can use the same device
                if (cmbInsideSensor.Items.Count > 0 && cmbOutsideSensor.SelectedIndex != cmbInsideSensor.SelectedIndex)
                {
                    cmbOutsideSensor.SelectedIndex = cmbInsideSensor.SelectedIndex;
                }
                lblStatus.Text = "⚠ Test Mode: Both sensors will use the same physical device";
                lblStatus.ForeColor = Color.FromArgb(255, 140, 0);
            }
            else
            {
                lblStatus.Text = "Production Mode: Assign different sensors for inside/outside";
                lblStatus.ForeColor = Color.FromArgb(0, 123, 255);
            }
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                // Validate selections
                if (cmbRoom.SelectedItem == null)
                {
                    MessageBox.Show("Please select a room.", "Validation Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (cmbInsideSensor.SelectedItem == null || cmbOutsideSensor.SelectedItem == null)
                {
                    MessageBox.Show("Please assign sensors (or select None).", "Validation Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var selectedRoom = (Room)cmbRoom.SelectedItem;
                var insideDevice = (UsbDeviceInfo)cmbInsideSensor.SelectedItem;
                var outsideDevice = (UsbDeviceInfo)cmbOutsideSensor.SelectedItem;

                // Check if at least one sensor is assigned
                if (insideDevice.DeviceId == "NONE" && outsideDevice.DeviceId == "NONE")
                {
                    MessageBox.Show("At least one sensor must be assigned.\n\nYou cannot select None for both sensors.", 
                        "Validation Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Warn if same device is selected for both (unless test mode or one is None)
                if (!chkTestMode.Checked && 
                    insideDevice.DeviceId != "NONE" && 
                    outsideDevice.DeviceId != "NONE" &&
                    insideDevice.DeviceIndex == outsideDevice.DeviceIndex)
                {
                    var result = MessageBox.Show(
                        "You have selected the same sensor for both inside and outside.\n\n" +
                        "This will work but both scans will use the same physical device.\n" +
                        "Enable Test Mode or select different sensors.\n\n" +
                        "Continue anyway?",
                        "Same Sensor Warning",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.No)
                    {
                        return;
                    }
                }

                // Create configuration
                SelectedConfiguration = new DeviceConfiguration
                {
                    RoomId = selectedRoom.RoomId,
                    RoomName = selectedRoom.DisplayName,
                    Building = selectedRoom.Building,
                    TestMode = chkTestMode.Checked,
                    InsideSensor = insideDevice.DeviceId != "NONE" ? new SensorConfig
                    {
                        UsbDevicePath = insideDevice.DevicePath,
                        DeviceId = $"{selectedRoom.RoomNumber}_Inside",
                        Enabled = true,
                        SensorIndex = insideDevice.DeviceIndex
                    } : null,  // null if None selected
                    OutsideSensor = outsideDevice.DeviceId != "NONE" ? new SensorConfig
                    {
                        UsbDevicePath = outsideDevice.DevicePath,
                        DeviceId = $"{selectedRoom.RoomNumber}_Outside",
                        Enabled = true,
                        SensorIndex = outsideDevice.DeviceIndex
                    } : null  // null if None selected
                };

                // Save configuration if requested
                if (chkRememberConfig.Checked)
                {
                    DeviceConfigManager.Instance.SaveConfiguration(SelectedConfiguration);
                }

                // Register devices in database (only if not None)
                if (insideDevice.DeviceId != "NONE")
                {
                    dbManager.RegisterDualSensorDevice(
                        selectedRoom.RoomNumber, 
                        selectedRoom.RoomId, 
                        "Inside", 
                        insideDevice.DeviceIndex);
                }

                if (outsideDevice.DeviceId != "NONE")
                {
                    dbManager.RegisterDualSensorDevice(
                        selectedRoom.RoomNumber, 
                        selectedRoom.RoomId, 
                        "Outside", 
                        outsideDevice.DeviceIndex);
                }

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating configuration:\n{ex.Message}", "Configuration Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Show dialog and return configuration (or null if cancelled)
        /// </summary>
        public static DeviceConfiguration ShowConfigDialog(DatabaseManager dbManager)
        {
            using (var dialog = new StartupConfigDialog(dbManager))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.SelectedConfiguration;
                }
                return null;
            }
        }
    }
}

