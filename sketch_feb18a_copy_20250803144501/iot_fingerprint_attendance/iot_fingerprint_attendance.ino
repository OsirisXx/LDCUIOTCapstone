/*
 * IoT Solenoid Lock Control System
 * ESP32 + Solenoid Lock
 * Controls solenoid lock via web API
 * 
 * SIMPLIFIED INSTRUCTOR LOGIC:
 * - Any successful instructor scan with active schedule = door opens
 * - No complex state management (start/end session) - backend handles that
 * - Prevents inconsistent door access for instructors
 */

#include <WiFi.h>
#include <HTTPClient.h>
#include <WiFiUdp.h>
#include <ArduinoJson.h>
#include <WebServer.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

// WiFi credentials - 2.4GHz Network (ESP32 Compatible)
const char* ssid = "WIFi2.4";         // Your current WiFi network
const char* password = "fOrtnite901_"; // Your WiFi password

// Security - API Key for authentication
const char* API_KEY = "LDCU_IOT_2025_SECURE_KEY_XYZ123"; // Change this to your own secret key

// Server configuration - Fully automatic discovery
const char* serverPort = "5000";

// Automatic IP discovery
String discoveredServerIP = "";
bool ipDiscovered = false;

// Manual IP override (uncomment and set if auto-discovery fails)
const char* manualServerIP = "192.168.137.1"; // Server PC Hotspot IP - ESP32 connects via WiFi hotspot

// Alternative: Use localhost if on same machine
// const char* manualServerIP = "localhost"; // Alternative for testing

// Web server for lock control
WebServer server(80);


// No LED pins needed (you don't have LEDs)

// Buzzer pin (not connected but defined to avoid compile errors)
#define BUZZER_PIN 19

// Solenoid Lock Control
#define RELAY_PIN 5     // Relay control pin for solenoid lock
#define LOCK_DURATION 3000  // How long to keep lock open (3 seconds)

// OLED Display Configuration (I2C)
#define SCREEN_WIDTH 128
#define SCREEN_HEIGHT 64
#define OLED_RESET -1
#define SCREEN_ADDRESS 0x3C

// I2C OLED: SCL=GPIO 21, SDA=GPIO 22 (but user has SDA on GPIO 23)
// Initialize OLED display for I2C
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, OLED_RESET);

// Global variable to track if display is working
bool displayWorking = false;

// Lock timing variables for non-blocking operation
unsigned long lockOpenTime = 0;
bool lockIsOpen = false;

// Display timing variables for non-blocking operation
unsigned long displayOnTime = 0;
bool displayIsOn = false;
const unsigned long DISPLAY_DURATION = 3000; // Display stays on for 3 seconds


void setup() {
  Serial.begin(115200);
  delay(1000);
  
  // Initialize I2C with custom pins: SDA=23, SCL=21
  Wire.begin(23, 21);  // SDA on GPIO 23, SCL on GPIO 21
  
  // Initialize OLED display (simple approach like working code)
  Serial.println("🖥️ Initializing OLED display...");
  
  if (display.begin(SSD1306_SWITCHCAPVCC, 0x3C)) {
    Serial.println("✅ OLED display initialized successfully!");
    displayWorking = true;
  } else {
    Serial.println("❌ OLED display initialization failed!");
    Serial.println("Continuing without display...");
    displayWorking = false;
  }
  
  // Show initial display (like working code)
  if (displayWorking) {
    display.clearDisplay();
    display.setTextSize(1);
    display.setTextColor(SSD1306_WHITE);
    display.setCursor(0,0);
    display.println("IoT Lock System");
    display.println("Initializing...");
    display.display();
    delay(2000); // Show for 2 seconds like working code
  }
  
  // Initialize pins
  pinMode(BUZZER_PIN, OUTPUT);
  pinMode(RELAY_PIN, OUTPUT); // Initialize relay pin
  digitalWrite(RELAY_PIN, LOW); // Ensure relay is off initially
  
  Serial.println("=== IoT Solenoid Lock Control System ===");
  
  // Connect to WiFi
  connectToWiFi();
  
     Serial.println("🔧 Solenoid lock initialized on pin " + String(RELAY_PIN));
   Serial.println("🔓 Lock will open for instructors and admins only");
   
   // Setup web server for lock control
   setupWebServer();
   server.begin();
   Serial.println("🌐 Web server started on port 80");
   Serial.println("🔗 Lock control endpoint: http://" + WiFi.localIP().toString() + "/api/lock-control");
   
  Serial.println("System ready! Solenoid lock controller initialized.");
  
  // Show startup message for 3 seconds to confirm OLED connection
  if (displayWorking) {
    Serial.println("🖥️ Testing OLED display connection...");
    displayStartupMessage();
    delay(3000);
    
    // Turn off display to save power
    Serial.println("🖥️ OLED test complete - turning off to save power");
    turnOffDisplay();
  } else {
    Serial.println("🖥️ OLED display not working - skipping startup test");
  }
  
  beepSuccess();
}

// Variables for periodic server discovery
unsigned long lastDiscoveryAttempt = 0;
const unsigned long DISCOVERY_INTERVAL = 300000; // 5 minutes

void loop() {
  // Check WiFi connection
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("WiFi disconnected. Reconnecting...");
    connectToWiFi();
  } else {
    
    // Periodic server discovery (every 5 minutes)
    if (millis() - lastDiscoveryAttempt > DISCOVERY_INTERVAL) {
      if (!ipDiscovered) {
        Serial.println("🔄 Periodic server discovery...");
        discoverServerIP();
      }
      lastDiscoveryAttempt = millis();
    }
  }
  
  // Handle lock timing (non-blocking)
  if (lockIsOpen && (millis() - lockOpenTime >= LOCK_DURATION)) {
    Serial.println("⏰ Lock duration expired - closing lock automatically");
    closeLock();
  }
  
  // Handle display timing (non-blocking)
  if (displayIsOn && (millis() - displayOnTime >= DISPLAY_DURATION)) {
    Serial.println("⏰ Display duration expired - turning off display automatically");
    turnOffDisplay();
  }
  
  // Handle web server requests
  server.handleClient();

  // Handle Serial commands
  handleSerialCommands();

  delay(100);
}

void connectToWiFi() {
  Serial.print("Connecting to WiFi: ");
  Serial.println(ssid);

  // Disconnect first to avoid "sta is connecting" error
  WiFi.disconnect(true);
  delay(1000);

  // Set WiFi mode
  WiFi.mode(WIFI_STA);
  delay(100);

  // Begin connection
  WiFi.begin(ssid, password);

  int attempts = 0;
  while (WiFi.status() != WL_CONNECTED && attempts < 30) {
    delay(500);
    Serial.print(".");
    attempts++;

    // Print status for debugging
    if (attempts % 10 == 0) {
      Serial.println();
      Serial.print("WiFi Status: ");
      Serial.println(WiFi.status());
      Serial.print("Attempt: ");
      Serial.println(attempts);
    }
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.println();
    Serial.println("✓ WiFi connected!");
    Serial.print("IP address: ");
    Serial.println(WiFi.localIP());
    Serial.print("Signal strength: ");
    Serial.print(WiFi.RSSI());
    Serial.println(" dBm");
    
    // Try to discover server IP
    discoverServerIP();
  } else {
    Serial.println();
    Serial.println("✗ WiFi connection failed!");
    Serial.print("Final status: ");
    Serial.println(WiFi.status());
    Serial.println("Check:");
    Serial.println("1. WiFi name: " + String(ssid));
    Serial.println("2. WiFi password: " + String(password));
    Serial.println("3. WiFi signal strength");
    Serial.println("4. 2.4GHz vs 5GHz band");
  }
}
  
// Function to automatically discover server IP
void discoverServerIP() {
  Serial.println("🔍 Discovering server IP...");
  
  // Get our own IP to determine network range
  IPAddress localIP = WiFi.localIP();
  String networkPrefix = String(localIP[0]) + "." + String(localIP[1]) + "." + String(localIP[2]);
  
  Serial.print("📡 ESP32 IP: ");
  Serial.println(localIP.toString());
  Serial.print("📡 Network: ");
  Serial.println(networkPrefix + ".x");
  
  // Try UDP discovery first (fastest)
  discoverServerUDP();
  if (ipDiscovered) {
    return;
  }
  
  Serial.println("🔍 UDP discovery failed, trying smart HTTP discovery...");
  
  // Smart discovery: Try likely server IPs based on ESP32's network
  // Most routers assign IPs in sequence, so if ESP32 is .12, server might be .11, .10, .1, etc.
  int likelyIPs[] = {
    localIP[3] - 1,  // One less than ESP32 (e.g., if ESP32 is .12, try .11)
    localIP[3] - 2,  // Two less than ESP32
    localIP[3] - 3,  // Three less than ESP32
    1,               // Router/Gateway (common server location)
    2,               // Common server IP
    10,              // Common server IP
    100,             // Common server IP
    101,             // Common server IP
    200,             // Common server IP
    254              // Last IP in range
  };
  
  Serial.println("🚀 Smart discovery - checking likely IPs...");
  for (int i = 0; i < sizeof(likelyIPs) / sizeof(likelyIPs[0]); i++) {
    int lastOctet = likelyIPs[i];
    
    // Skip invalid IPs and our own IP
    if (lastOctet < 1 || lastOctet > 254 || lastOctet == localIP[3]) {
      continue;
    }
    
    String testIP = networkPrefix + "." + String(lastOctet);
    String testURL = "http://" + testIP + ":" + String(serverPort) + "/api/health";
    
    Serial.print("🔍 Testing: ");
    Serial.println(testIP);
    
    HTTPClient http;
    http.setTimeout(5000); // Increased timeout for better reliability
    
    if (http.begin(testURL)) {
      Serial.print("  📡 HTTP request sent to: ");
      Serial.println(testURL);
      
      int httpCode = http.GET();
      Serial.print("  📥 HTTP response code: ");
      Serial.println(httpCode);
      
      if (httpCode == 200) {
        String response = http.getString();
        Serial.print("  📄 Response: ");
        Serial.println(response);
        
        // Check if it's our IoT Attendance System
        if (response.indexOf("IoT Attendance System") != -1) {
          discoveredServerIP = testIP;
          ipDiscovered = true;
          Serial.print("✅ Server found at: ");
          Serial.println(discoveredServerIP);
          http.end();
          return;
        } else {
          Serial.println("  ❌ Response doesn't contain 'IoT Attendance System'");
        }
      } else if (httpCode > 0) {
        Serial.print("  ❌ HTTP error: ");
        Serial.println(httpCode);
      } else {
        Serial.println("  ❌ HTTP request failed");
      }
      http.end();
    } else {
      Serial.println("  ❌ HTTP begin failed");
    }
    delay(200); // Faster than before
  }
  
  Serial.println("⚠️ Smart discovery failed, trying full scan...");
  
  // Full scan as last resort (but much faster now)
  for (int lastOctet = 1; lastOctet <= 254; lastOctet++) {
    // Skip our own IP and already checked IPs
    if (lastOctet == localIP[3]) {
      continue;
    }
    
    // Skip already checked likely IPs
    bool alreadyChecked = false;
    for (int i = 0; i < sizeof(likelyIPs) / sizeof(likelyIPs[0]); i++) {
      if (lastOctet == likelyIPs[i]) {
        alreadyChecked = true;
        break;
      }
    }
    if (alreadyChecked) continue;
    
    String testIP = networkPrefix + "." + String(lastOctet);
    String testURL = "http://" + testIP + ":" + String(serverPort) + "/api/health";
    
    HTTPClient http;
    http.setTimeout(1000); // Faster timeout for full scan
    
    if (http.begin(testURL)) {
      int httpCode = http.GET();
      if (httpCode == 200) {
        String response = http.getString();
        if (response.indexOf("IoT Attendance System") != -1) {
          discoveredServerIP = testIP;
          ipDiscovered = true;
          Serial.print("✅ Server found at: ");
          Serial.println(discoveredServerIP);
          http.end();
          return;
        }
      }
      http.end();
    }
    
    delay(50); // Much faster scan
    
    // Print progress every 100 attempts
    if (lastOctet % 100 == 0) {
      Serial.print("📊 Scan progress: ");
      Serial.print(lastOctet);
      Serial.println("/254");
    }
  }
  
  Serial.println("⚠️ Server not found in network scan");
  Serial.println("📝 Will retry on next connection attempt");
  ipDiscovered = false;
}
  
// Alternative discovery method using UDP broadcast
void discoverServerUDP() {
  Serial.println("📡 UDP broadcast discovery...");
  
  WiFiUDP udp;
  udp.begin(8888); // Listen on port 8888
  
  // Send broadcast packet to the local network
  udp.beginPacket("255.255.255.255", 8888);
  udp.write((uint8_t*)"IOT_ATTENDANCE_DISCOVERY", strlen("IOT_ATTENDANCE_DISCOVERY"));
  udp.endPacket();
  
  // Wait for response
  unsigned long startTime = millis();
  while (millis() - startTime < 3000) { // Wait 3 seconds (faster)
    int packetSize = udp.parsePacket();
    if (packetSize) {
      String response = udp.readString();
      if (response.indexOf("IOT_ATTENDANCE_SERVER") != -1) {
        discoveredServerIP = udp.remoteIP().toString();
        ipDiscovered = true;
        Serial.print("✅ Server found via UDP at: ");
        Serial.println(discoveredServerIP);
        udp.stop();
        return;
      }
    }
    delay(50); // Faster polling
  }
  
  Serial.println("⚠️ UDP discovery failed");
  udp.stop();
}



 // Web Server Setup
 void setupWebServer() {
   // Lock control endpoint (for fingerprint authentication)
   server.on("/api/lock-control", HTTP_POST, handleLockControl);
   
   // RFID scan endpoint (for RFID authentication)
   server.on("/api/rfid-scan", HTTP_POST, handleRfidScan);
   
   // Health check endpoint
   server.on("/api/health", HTTP_GET, handleHealthCheck);
   
   // Default handler
   server.onNotFound(handleNotFound);
 }

// OLED Display Functions
void displayMessage(String line1, String line2 = "", String line3 = "", String line4 = "") {
  if (!displayWorking) {
    Serial.println("🖥️ Display not working - skipping display update");
    return;
  }
  
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 0);
  
  if (line1.length() > 0) {
    display.println(line1);
  }
  if (line2.length() > 0) {
    display.println(line2);
  }
  if (line3.length() > 0) {
    display.println(line3);
  }
  if (line4.length() > 0) {
    display.println(line4);
  }
  
  display.display();
}

void displayFingerprintMatch(String userName, bool lockOpened, String userType = "") {
  if (!displayWorking) {
    Serial.println("🖥️ Display not working - skipping fingerprint match display");
    return;
  }
  
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 0);
  
  display.println("Fingerprint match");
  display.println("found");
  display.println("");
  display.println(userName);
  display.println("");
  
  if (lockOpened) {
    display.setTextColor(SSD1306_WHITE);
    if (userType == "instructor") {
      display.println("Door unlocked");
      display.println("(Instructor)");
    } else {
      display.println("Door unlocked");
      display.println("(Student)");
    }
  } else {
    display.setTextColor(SSD1306_WHITE);
    if (userType == "student") {
      display.println("Access denied");
      display.println("No active session");
    } else {
      display.println("Access denied");
    }
  }
  
  display.display();
  
  // Turn on display with timer
  turnOnDisplay();
}

void displayRfidScanResult(String userName, bool lockOpened, String userType = "") {
  if (!displayWorking) {
    Serial.println("🖥️ Display not working - skipping RFID scan display");
    return;
  }
  
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 0);
  
  display.println("RFID card");
  display.println("scanned");
  display.println("");
  display.println(userName);
  display.println("");
  
  if (lockOpened) {
    display.setTextColor(SSD1306_WHITE);
    if (userType == "instructor") {
      display.println("Door unlocked");
      display.println("(Instructor)");
    } else {
      display.println("Door unlocked");
      display.println("(Student)");
    }
  } else {
    display.setTextColor(SSD1306_WHITE);
    if (userType == "student") {
      display.println("Access denied");
      display.println("No active session");
    } else {
      display.println("Access denied");
    }
  }
  
  display.display();
  
  // Turn on display with timer
  turnOnDisplay();
}

void displayNoMatch() {
  if (!displayWorking) {
    Serial.println("🖥️ Display not working - skipping no match display");
    return;
  }
  
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 0);
  display.println("No match found");
  display.println("");
  display.println("Try again");
  display.display();
  
  // Turn on display with timer
  turnOnDisplay();
}

void displaySystemStatus() {
  if (!displayWorking) {
    Serial.println("🖥️ Display not working - skipping system status display");
    return;
  }
  
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 0);
  display.println("IoT Lock System");
  display.println("Ready");
  display.println("");
  display.print("IP: ");
  display.println(WiFi.localIP());
  display.println("");
  display.println("Waiting for scan...");
  display.display();
  
  // Turn on display with timer
  turnOnDisplay();
}

void turnOnDisplay() {
  if (!displayWorking) {
    Serial.println("🖥️ Display not working - cannot turn on");
    return;
  }
  
  // Set display timer
  displayIsOn = true;
  displayOnTime = millis();
  
  Serial.println("🖥️ Display turned on - will auto-turn off in " + String(DISPLAY_DURATION/1000) + " seconds");
}

void turnOffDisplay() {
  if (!displayWorking) {
    return;
  }
  
  display.clearDisplay();
  display.display();
  
  // Reset display timer
  displayIsOn = false;
  displayOnTime = 0;
  
  Serial.println("🖥️ Display turned off");
}

void displayStartupMessage() {
  if (!displayWorking) {
    Serial.println("🖥️ Display not working - skipping startup message");
    return;
  }
  
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 0);
  display.println("=== OLED TEST ===");
  display.println("");
  display.println("Display Connected");
  display.println("Successfully!");
  display.println("");
  display.println("System Starting...");
  display.display();
  
  // Turn on display with timer
  turnOnDisplay();
}

// Handle lock control requests with API key authentication
void handleLockControl() {
  // Debug: Print all headers
  Serial.println("🔍 Debug - All headers received:");
  for (int i = 0; i < server.headers(); i++) {
    Serial.print("  ");
    Serial.print(server.headerName(i));
    Serial.print(": ");
    Serial.println(server.header(i));
  }
  
  // TEMPORARY: Skip API key check for testing
  Serial.println("⚠️ API key check temporarily disabled for testing");
  
  // Check for API key in header
  // if (!server.hasHeader("X-API-Key")) {
  //   Serial.println("❌ No API key provided");
  //   server.send(401, "application/json", "{\"error\":\"Unauthorized - No API key\"}");
  //   return;
  // }
  
  // String providedKey = server.header("X-API-Key");
  // if (providedKey != API_KEY) {
  //   Serial.println("❌ Invalid API key: " + providedKey);
  //   server.send(403, "application/json", "{\"error\":\"Forbidden - Invalid API key\"}");
  //   return;
  // }
  
  // Serial.println("✅ API key validated");
  
  if (server.hasArg("plain")) {
    String jsonString = server.arg("plain");
    Serial.println("🔓 Lock control request received: " + jsonString);
    
    DynamicJsonDocument doc(512);
    DeserializationError error = deserializeJson(doc, jsonString);
    
    if (error) {
      Serial.println("❌ JSON parsing failed");
      server.send(400, "application/json", "{\"error\":\"Invalid JSON\"}");
      return;
    }
    
    String action = doc["action"];
    String user = doc["user"];
    String userType = doc["userType"] | "";
    bool sessionActive = doc["sessionActive"] | false;
    
    Serial.print("🔓 Action: ");
    Serial.println(action);
    Serial.print("👤 User: ");
    Serial.println(user);
    Serial.print("👤 User Type: ");
    Serial.println(userType);
    Serial.print("📅 Session Active: ");
    Serial.println(sessionActive ? "Yes" : "No");
    
    // Determine if lock should open based on user type and session state
    bool shouldOpenLock = false;
    if (userType == "instructor") {
      // Instructors can always open the door (simplified logic to avoid state confusion)
      shouldOpenLock = true;
    } else if (userType == "student" && sessionActive) {
      // Students can open door during active session (even if already signed in)
      shouldOpenLock = true;
    }
    
    // Display fingerprint match result on OLED
    displayFingerprintMatch(user, shouldOpenLock, userType);
    
    // Also show in Serial for debugging
    Serial.println("🔐 FINGERPRINT AUTHENTICATION RESULT:");
    Serial.print("👤 User: ");
    Serial.println(user);
    Serial.print("🔓 Lock Action: ");
    Serial.println(shouldOpenLock ? "OPEN" : "DENIED");
    Serial.print("👤 User Type: ");
    Serial.println(userType);
    Serial.print("📅 Session Active: ");
    Serial.println(sessionActive ? "YES" : "NO");
    Serial.print("💡 Logic: ");
    if (userType == "instructor") {
      Serial.println("Instructor - Always allowed (simplified logic)");
    } else if (userType == "student" && sessionActive) {
      Serial.println("Student with active session - Allowed (can go in/out)");
    } else {
      Serial.println("Student without active session - Denied");
    }
    
    if (action == "open" && shouldOpenLock) {
      Serial.println("🔓 Opening solenoid lock via web request...");
      openLock();
      beepSuccess();
      server.send(200, "application/json", "{\"message\":\"Lock opened successfully\",\"action\":\"open\",\"user\":\"" + user + "\"}");
    } else if (action == "open" && !shouldOpenLock) {
      Serial.println("❌ Access denied - lock not opened");
      beepError();
      server.send(200, "application/json", "{\"message\":\"Access denied\",\"action\":\"denied\",\"user\":\"" + user + "\"}");
    } else if (action == "close") {
      Serial.println("🔒 Closing solenoid lock via web request...");
      closeLock();
      server.send(200, "application/json", "{\"message\":\"Lock closed successfully\",\"action\":\"close\",\"user\":\"" + user + "\"}");
    } else {
      server.send(400, "application/json", "{\"error\":\"Invalid action. Use 'open' or 'close'\"}");
    }
    
    // Note: Display will be turned off by the main loop timing mechanism
  } else {
    server.send(400, "application/json", "{\"error\":\"No JSON data received\"}");
  }
}

 // Handle health check requests
 void handleHealthCheck() {
   String response = "{\"status\":\"OK\",\"device\":\"ESP32 Solenoid Lock Controller\",\"ip\":\"" + WiFi.localIP().toString() + "\",\"uptime\":" + String(millis()) + "}";
   server.send(200, "application/json", response);
 }

 // Handle RFID scan requests
 void handleRfidScan() {
   Serial.println("🔖 RFID scan request received");
   
   if (server.hasArg("plain")) {
     String jsonString = server.arg("plain");
     Serial.println("🔖 RFID scan request received: " + jsonString);
     
     DynamicJsonDocument doc(512);
     DeserializationError error = deserializeJson(doc, jsonString);
     
     if (error) {
       Serial.println("❌ JSON parsing failed");
       server.send(400, "application/json", "{\"error\":\"Invalid JSON\"}");
       return;
     }
     
     String rfidData = doc["rfid_data"];
     String user = doc["user"];
     String userType = doc["userType"] | "";
     bool sessionActive = doc["sessionActive"] | false;
     
     Serial.print("🔖 RFID Data: ");
     Serial.println(rfidData);
     Serial.print("👤 User: ");
     Serial.println(user);
     Serial.print("👤 User Type: ");
     Serial.println(userType);
     Serial.print("📅 Session Active: ");
     Serial.println(sessionActive ? "Yes" : "No");
     
     // Determine if lock should open based on user type and session state
     bool shouldOpenLock = false;
     if (userType == "instructor") {
       // Instructors can always open the door (simplified logic to avoid state confusion)
       shouldOpenLock = true;
     } else if (userType == "student" && sessionActive) {
       // Students can open door during active session (even if already signed in)
       shouldOpenLock = true;
     }
     
     // Display RFID scan result on OLED
     displayRfidScanResult(user, shouldOpenLock, userType);
     
     // Also show in Serial for debugging
     Serial.println("🔖 RFID AUTHENTICATION RESULT:");
     Serial.print("👤 User: ");
     Serial.println(user);
     Serial.print("🔖 RFID: ");
     Serial.println(rfidData);
     Serial.print("🔓 Lock Action: ");
     Serial.println(shouldOpenLock ? "OPEN" : "DENIED");
     Serial.print("👤 User Type: ");
     Serial.println(userType);
     Serial.print("📅 Session Active: ");
     Serial.println(sessionActive ? "YES" : "NO");
     Serial.print("💡 Logic: ");
     if (userType == "instructor") {
       Serial.println("Instructor - Always allowed (simplified logic)");
     } else if (userType == "student" && sessionActive) {
       Serial.println("Student with active session - Allowed (can go in/out)");
     } else {
       Serial.println("Student without active session - Denied");
     }
     
     if (shouldOpenLock) {
       Serial.println("🔓 Opening solenoid lock via RFID scan...");
       openLock();
       beepSuccess();
       server.send(200, "application/json", "{\"message\":\"RFID scan processed - Lock opened\",\"action\":\"open\",\"user\":\"" + user + "\",\"rfid\":\"" + rfidData + "\"}");
     } else {
       Serial.println("❌ Access denied - RFID scan not authorized");
       beepError();
       server.send(200, "application/json", "{\"message\":\"RFID scan processed - Access denied\",\"action\":\"denied\",\"user\":\"" + user + "\",\"rfid\":\"" + rfidData + "\"}");
     }
     
     // Note: Display will be turned off by the main loop timing mechanism
   } else {
     server.send(400, "application/json", "{\"error\":\"No JSON data received\"}");
   }
 }

 // Handle not found requests
 void handleNotFound() {
   server.send(404, "application/json", "{\"error\":\"Endpoint not found\"}");
 }

 // Solenoid Lock Control Functions
void openLock() {
  Serial.println("🔓 Opening solenoid lock...");
  Serial.print("🔧 Relay Pin: ");
  Serial.print(RELAY_PIN);
  Serial.print(" | Before: ");
  Serial.println(digitalRead(RELAY_PIN) ? "HIGH" : "LOW");
  
  digitalWrite(RELAY_PIN, HIGH); // Activate relay (HIGH = ON for most relay modules)
  
  Serial.print("🔧 Relay Pin: ");
  Serial.print(RELAY_PIN);
  Serial.print(" | After: ");
  Serial.println(digitalRead(RELAY_PIN) ? "HIGH" : "LOW");
  
  Serial.println("✅ Lock opened - Relay activated");
  Serial.println("🔓 Lock will stay open for " + String(LOCK_DURATION/1000) + " seconds");
  
  // Set timer for non-blocking lock control
  lockOpenTime = millis();
  lockIsOpen = true;
  
  Serial.println("🔧 Lock timer set - lockIsOpen = " + String(lockIsOpen));
}

void closeLock() {
  Serial.println("🔒 Closing solenoid lock...");
  Serial.print("🔧 Relay Pin: ");
  Serial.print(RELAY_PIN);
  Serial.print(" | Before: ");
  Serial.println(digitalRead(RELAY_PIN) ? "HIGH" : "LOW");
  
  digitalWrite(RELAY_PIN, LOW); // Deactivate relay (LOW = OFF)
  
  Serial.print("🔧 Relay Pin: ");
  Serial.print(RELAY_PIN);
  Serial.print(" | After: ");
  Serial.println(digitalRead(RELAY_PIN) ? "HIGH" : "LOW");
  
  Serial.println("✅ Lock closed - Relay deactivated");
  
  // Reset lock timer
  lockIsOpen = false;
  lockOpenTime = 0;
  
  Serial.println("🔧 Lock timer reset - lockIsOpen = " + String(lockIsOpen));
}

 // Manual lock control via Serial commands
 void handleLockCommands(String command) {
   if (command == "lock_open") {
     Serial.println("🔓 Manual lock open command received");
     openLock();
   } else if (command == "lock_close") {
     Serial.println("🔒 Manual lock close command received");
     closeLock();
  } else if (command == "lock_test") {
    Serial.println("🧪 Testing solenoid lock...");
    Serial.println("Opening lock for 2 seconds...");
    digitalWrite(RELAY_PIN, HIGH);
    delay(2000);
    digitalWrite(RELAY_PIN, LOW);
    Serial.println("✅ Lock test complete");
  }
 }

void beepSuccess() {
  tone(BUZZER_PIN, 1000, 200);
  delay(250);
  tone(BUZZER_PIN, 1500, 200);
}

void beepError() {
  tone(BUZZER_PIN, 500, 500);
  delay(600);
  tone(BUZZER_PIN, 300, 500);
}

void blinkError() {
  // No LED needed - just beep for error
  for (int i = 0; i < 5; i++) {
    tone(BUZZER_PIN, 2000, 200);
    delay(400);
  }
}



// Handle Serial commands
void handleSerialCommands() {
  if (Serial.available()) {
    String command = Serial.readStringUntil('\n');
    command.trim();
    
    if (command == "status") {
      Serial.println("=== System Status ===");
      Serial.print("WiFi: ");
      Serial.println(WiFi.status() == WL_CONNECTED ? "Connected" : "Disconnected");
      Serial.print("IP: ");
      Serial.println(WiFi.localIP());
  Serial.print("Server: ");
  if (ipDiscovered) {
    Serial.println("http://" + discoveredServerIP + ":" + String(serverPort));
  } else {
    Serial.println("Not discovered");
  }
    } else if (command == "discover") {
      Serial.println("🔍 Manually triggering server discovery...");
      discoverServerIP();
    } else if (command == "testserver") {
      Serial.println("🧪 Testing server connection...");
      if (WiFi.status() != WL_CONNECTED) {
        Serial.println("❌ WiFi not connected");
        return;
      }
      
      if (!ipDiscovered) {
        Serial.println("❌ Server IP not discovered. Run 'discover' command first.");
        return;
      }
      
      String testURL = "http://" + discoveredServerIP + ":5000/api/health";
      Serial.print("Testing: ");
      Serial.println(testURL);
      
      HTTPClient http;
      http.setTimeout(10000);
      
      if (http.begin(testURL)) {
        int httpCode = http.GET();
        Serial.print("HTTP Code: ");
        Serial.println(httpCode);
        
        if (httpCode == 200) {
          String response = http.getString();
          Serial.print("Response: ");
          Serial.println(response);
          
          if (response.indexOf("IoT Attendance System") != -1) {
            Serial.println("✅ Server connection successful!");
          } else {
            Serial.println("❌ Server response doesn't contain expected text");
          }
        } else {
          Serial.println("❌ HTTP request failed");
        }
        http.end();
      } else {
        Serial.println("❌ HTTP begin failed");
      }
     } else if (command == "webstatus") {
       Serial.println("🌐 Web Server Status:");
       Serial.print("  IP Address: ");
       Serial.println(WiFi.localIP().toString());
       Serial.print("  Port: 80");
       Serial.println();
       Serial.println("🔗 Available endpoints:");
       Serial.println("  POST /api/lock-control - Control solenoid lock (fingerprint)");
       Serial.println("  POST /api/rfid-scan - Handle RFID card scans");
       Serial.println("  GET  /api/health - Health check");
       Serial.println();
       Serial.println("📡 Test fingerprint: curl -X POST http://" + WiFi.localIP().toString() + "/api/lock-control -H 'Content-Type: application/json' -d '{\"action\":\"open\",\"user\":\"test\"}'");
       Serial.println("📡 Test RFID: curl -X POST http://" + WiFi.localIP().toString() + "/api/rfid-scan -H 'Content-Type: application/json' -d '{\"rfid_data\":\"123456\",\"user\":\"test\"}'");
         } else if (command.startsWith("setip ")) {
       String newIP = command.substring(6);
       discoveredServerIP = newIP;
       ipDiscovered = true;
       Serial.println("🔧 Server IP manually set to: " + discoveredServerIP);
    } else if (command == "display_on") {
      Serial.println("🖥️ Turning on OLED display...");
      displaySystemStatus();
    } else if (command == "display_off") {
      Serial.println("🖥️ Turning off OLED display...");
      turnOffDisplay();
    } else if (command == "display_test") {
      Serial.println("🖥️ Testing OLED display...");
      displayStartupMessage();
      delay(3000);
      turnOffDisplay();
      Serial.println("🖥️ OLED test complete");
    } else if (command == "display_status") {
      Serial.println("🖥️ OLED Display Status:");
      Serial.print("Display Working: ");
      Serial.println(displayWorking ? "YES" : "NO");
      if (displayWorking) {
        Serial.println("✅ OLED is connected and functional");
      } else {
        Serial.println("❌ OLED is not working - check wiring");
        Serial.println("Wiring: SDA->D21, SCL->D22, VCC->3.3V, GND->GND");
      }
    } else if (command == "help") {
      Serial.println("=== Available Commands ===");
      Serial.println("status: Show system status");
      Serial.println("discover: Manually discover server IP");
      Serial.println("setip X.X.X.X: Manually set server IP");
      Serial.println("testserver: Test server connection");
      Serial.println("lock_open: Manually open solenoid lock");
      Serial.println("lock_close: Manually close solenoid lock");
      Serial.println("lock_test: Test solenoid lock (2 seconds)");
      Serial.println("webstatus: Show web server status");
      Serial.println("display_on: Turn on OLED display");
      Serial.println("display_off: Turn off OLED display");
      Serial.println("display_test: Test OLED display (3 sec)");
      Serial.println("display_status: Check OLED display status");
      Serial.println("rfid_test: Test RFID endpoint with sample data");
      Serial.println("lock_status: Check current lock status");
      Serial.println("help: Show this help message");
    } else if (command == "rfid_test") {
      Serial.println("🧪 Testing RFID endpoint...");
      Serial.println("Simulating RFID scan with sample data...");
      
      // Simulate RFID scan with sample data
      String testRfidData = "123456789";
      String testUser = "Test User";
      String testUserType = "instructor";
      bool testSessionActive = true;
      
      Serial.println("🔖 Simulating RFID scan:");
      Serial.print("  RFID Data: ");
      Serial.println(testRfidData);
      Serial.print("  User: ");
      Serial.println(testUser);
      Serial.print("  User Type: ");
      Serial.println(testUserType);
      Serial.print("  Session Active: ");
      Serial.println(testSessionActive ? "Yes" : "No");
      
      // Test the RFID scan logic
      bool shouldOpenLock = false;
      if (testUserType == "instructor") {
        shouldOpenLock = true;
      } else if (testUserType == "student" && testSessionActive) {
        shouldOpenLock = true;
      }
      
      Serial.print("🔓 Lock Action: ");
      Serial.println(shouldOpenLock ? "OPEN" : "DENIED");
      Serial.print("💡 Logic: ");
      if (testUserType == "instructor") {
        Serial.println("Instructor - Always allowed (simplified logic)");
      } else if (testUserType == "student" && testSessionActive) {
        Serial.println("Student with active session - Allowed (can go in/out)");
      } else {
        Serial.println("Student without active session - Denied");
      }
      
      // Display on OLED
      displayRfidScanResult(testUser, shouldOpenLock, testUserType);
      
      // Test lock if should open
      if (shouldOpenLock) {
        Serial.println("🔓 Opening lock for RFID test...");
        openLock();
      }
      
      delay(3000);
      turnOffDisplay();
      Serial.println("🧪 RFID test complete");
    } else if (command == "lock_status") {
      Serial.println("🔒 Lock Status:");
      Serial.print("  Lock State: ");
      Serial.println(lockIsOpen ? "OPEN" : "CLOSED");
      Serial.print("  Relay Pin State: ");
      Serial.println(digitalRead(RELAY_PIN) ? "HIGH (ON)" : "LOW (OFF)");
      if (lockIsOpen) {
        unsigned long timeRemaining = LOCK_DURATION - (millis() - lockOpenTime);
        Serial.print("  Time Remaining: ");
        Serial.print(timeRemaining / 1000);
        Serial.println(" seconds");
      }
      Serial.print("  Lock Duration: ");
      Serial.print(LOCK_DURATION / 1000);
      Serial.println(" seconds");
      
      Serial.println("🖥️ Display Status:");
      Serial.print("  Display State: ");
      Serial.println(displayIsOn ? "ON" : "OFF");
      if (displayIsOn) {
        unsigned long timeRemaining = DISPLAY_DURATION - (millis() - displayOnTime);
        Serial.print("  Time Remaining: ");
        Serial.print(timeRemaining / 1000);
        Serial.println(" seconds");
      }
      Serial.print("  Display Duration: ");
      Serial.print(DISPLAY_DURATION / 1000);
      Serial.println(" seconds");
    } else {
       // Handle lock control commands
       handleLockCommands(command);
     }
   }
 }