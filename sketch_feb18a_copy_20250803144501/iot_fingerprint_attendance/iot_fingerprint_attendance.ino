/*
 * IoT Fingerprint Attendance System with Solenoid Lock Control
 * ESP32 + R307 Fingerprint Scanner + Solenoid Lock
 * Connects to WiFi and sends attendance data to backend server
 * Controls solenoid lock for instructor access
 */

#include <WiFi.h>
#include <HTTPClient.h>
#include <WiFiUdp.h>
#include <Adafruit_Fingerprint.h>
#include <ArduinoJson.h>
#include <WebServer.h>

// WiFi credentials - 2.4GHz Network (ESP32 Compatible)
const char* ssid = "WIFi2.4";         // 2.4GHz network
const char* password = "fOrtnite901_"; // Same password

// Server configuration - Fully automatic discovery
const char* serverPort = "5000";
const char* serverPath = "/api/logs/attendance-logs";
const char* roomId = "DEFAULT_ROOM_ID"; // Set this to your actual room ID

// Device configuration - CRITICAL for security
const char* deviceLocation = "outside"; // "inside" or "outside" - IMPORTANT for access control

// Automatic IP discovery
String discoveredServerIP = "";
bool ipDiscovered = false;

// Manual IP override (uncomment and set if auto-discovery fails)
const char* manualServerIP = "192.168.1.11"; // Your computer's IP

// Alternative: Use localhost if on same machine
// const char* manualServerIP = "localhost"; // Alternative for testing

// Web server for lock control
WebServer server(80);

// Hardware Serial for R307 (GPIO21 = RX, GPIO22 = TX)
HardwareSerial mySerial(1);
Adafruit_Fingerprint finger = Adafruit_Fingerprint(&mySerial);

// LED pins for status indication
#define LED_WIFI 2      // Built-in LED for WiFi status
#define LED_SUCCESS 4   // Green LED for successful scan
#define LED_ERROR 5     // Red LED for errors

// Buzzer pin for audio feedback
#define BUZZER_PIN 18

// Solenoid Lock Control
#define RELAY_PIN 5     // Relay control pin for solenoid lock
#define LOCK_DURATION 3000  // How long to keep lock open (3 seconds)

void setup() {
  Serial.begin(115200);
  delay(1000);
  
  // Initialize pins
  pinMode(LED_WIFI, OUTPUT);
  pinMode(LED_SUCCESS, OUTPUT);
  pinMode(LED_ERROR, OUTPUT);
  pinMode(BUZZER_PIN, OUTPUT);
  pinMode(RELAY_PIN, OUTPUT); // Initialize relay pin
  digitalWrite(RELAY_PIN, LOW); // Ensure relay is off initially
  
  // Turn off all LEDs initially
  digitalWrite(LED_WIFI, LOW);
  digitalWrite(LED_SUCCESS, LOW);
  digitalWrite(LED_ERROR, LOW);
  
     Serial.println("=== IoT Fingerprint Attendance System with Solenoid Lock ===");
  
  // Initialize fingerprint sensor
  Serial.println("🔧 Initializing fingerprint sensor...");
  mySerial.begin(57600, SERIAL_8N1, 21, 22); // RX = 21, TX = 22
  delay(1000); // Give sensor time to initialize
  
  finger.begin(57600);
  delay(1000); // Give library time to initialize
  
  Serial.println("🔍 Verifying sensor communication...");
  if (finger.verifyPassword()) {
    Serial.println("✓ Fingerprint sensor found!");
    Serial.println("Sensor is ready for fingerprint operations");
  } else {
    Serial.println("✗ Fingerprint sensor not found!");
    Serial.println("Troubleshooting:");
    Serial.println("1. Check wiring: RX=21, TX=22");
    Serial.println("2. Check power supply (3.3V)");
    Serial.println("3. Check baud rate (57600)");
    Serial.println("4. Try different baud rates: 9600, 19200, 38400, 57600");
    blinkError();
    while (true) {
      delay(1000);
    }
  }
  
  // Connect to WiFi
  connectToWiFi();
  
     Serial.println("🔧 Solenoid lock initialized on pin " + String(RELAY_PIN));
   Serial.println("🔓 Lock will open for instructors and admins only");
   
   // Setup web server for lock control
   setupWebServer();
   server.begin();
   Serial.println("🌐 Web server started on port 80");
   Serial.println("🔗 Lock control endpoint: http://" + WiFi.localIP().toString() + "/api/lock-control");
   
   Serial.println("System ready! Place finger on scanner...");
   beepSuccess();
}

// Variables for periodic server discovery
unsigned long lastDiscoveryAttempt = 0;
const unsigned long DISCOVERY_INTERVAL = 300000; // 5 minutes

void loop() {
  // Check WiFi connection
  if (WiFi.status() != WL_CONNECTED) {
    digitalWrite(LED_WIFI, LOW);
    Serial.println("WiFi disconnected. Reconnecting...");
    connectToWiFi();
  } else {
    digitalWrite(LED_WIFI, HIGH);
    
    // Periodic server discovery (every 5 minutes)
    if (millis() - lastDiscoveryAttempt > DISCOVERY_INTERVAL) {
      if (!ipDiscovered) {
        Serial.println("🔄 Periodic server discovery...");
        discoverServerIP();
      }
      lastDiscoveryAttempt = millis();
    }
  }
  
     // Handle web server requests
   server.handleClient();

   // Handle Serial commands for enrollment
   handleSerialCommands();

   // Check for fingerprint
   int fingerprintId = getFingerprintID();
  if (fingerprintId > 0) {
    Serial.print("Fingerprint detected: ID #");
    Serial.println(fingerprintId);

    // Send attendance data to server
    if (sendAttendanceData(fingerprintId)) {
      Serial.println("✓ Attendance recorded successfully!");
      digitalWrite(LED_SUCCESS, HIGH);
      beepSuccess();
      delay(2000);
      digitalWrite(LED_SUCCESS, LOW);
    } else {
      Serial.println("✗ Failed to record attendance");
      digitalWrite(LED_ERROR, HIGH);
      beepError();
      delay(2000);
      digitalWrite(LED_ERROR, LOW);
    }

    // Wait before next scan
    delay(3000);
  }

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
    digitalWrite(LED_WIFI, !digitalRead(LED_WIFI)); // Blink while connecting
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
    digitalWrite(LED_WIFI, HIGH);
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
    digitalWrite(LED_WIFI, LOW);
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

int getFingerprintID() {
uint8_t p = finger.getImage();
  
// Debug: Print what's happening
switch (p) {
  case FINGERPRINT_OK:
    Serial.println("✅ Image captured successfully");
    break;
  case FINGERPRINT_NOFINGER:
    // Serial.println("No finger detected"); // Comment out to reduce spam
    return -1;
  case FINGERPRINT_PACKETRECIEVEERR:
    Serial.println("❌ Communication error");
    return -1;
  case FINGERPRINT_IMAGEFAIL:
    Serial.println("❌ Image capture failed");
    return -1;
  default:
    Serial.print("❌ Unknown error: 0x");
    Serial.println(p, HEX);
    return -1;
}
  
Serial.println("🔄 Converting image...");
p = finger.image2Tz();
if (p != FINGERPRINT_OK) {
  Serial.print("❌ Image conversion failed: 0x");
  Serial.println(p, HEX);
  return -1;
}
  
Serial.println("🔍 Searching for fingerprint...");
p = finger.fingerSearch();
if (p == FINGERPRINT_OK) {
  Serial.print("✅ Found ID #");
  Serial.print(finger.fingerID);
  Serial.print(" with confidence ");
  Serial.println(finger.confidence);
  return finger.fingerID;
} else if (p == FINGERPRINT_NOTFOUND) {
  Serial.println("❌ Fingerprint not found in database");
  return -1;
} else {
  Serial.print("❌ Search failed: 0x");
  Serial.println(p, HEX);
  return -1;
}
}

bool sendAttendanceData(int fingerprintId) {
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("WiFi not connected");
    return false;
  }

  // Build server URL dynamically - use discovered IP if available
  if (!ipDiscovered) {
    Serial.println("🔄 Server IP not discovered, attempting discovery...");
    discoverServerIP();
  }
  
  String serverIP;
  if (ipDiscovered) {
    serverIP = discoveredServerIP;
  } else {
    // Try manual IP if auto-discovery failed
    #ifdef manualServerIP
      serverIP = String(manualServerIP);
      Serial.println("🔧 Using manual server IP: " + serverIP);
    #else
      Serial.println("❌ Cannot send data - server not found");
      Serial.println("💡 Tip: Uncomment manualServerIP in the code and set it to your computer's IP");
      return false;
    #endif
  }
  
  String serverURL = "http://" + serverIP + ":" + String(serverPort) + String(serverPath);
  
  Serial.print("Connecting to server: ");
  Serial.println(serverURL);

  HTTPClient http;
  http.setTimeout(10000); // 10 second timeout

  if (!http.begin(serverURL)) {
    Serial.println("HTTP begin failed!");
    return false;
  }

  http.addHeader("Content-Type", "application/json");

     // Create JSON payload with proper room_id and location
   String jsonString = "{\"fingerprint_id\":" + String(fingerprintId) + ",\"location\":\"" + String(deviceLocation) + "\"";
   if (strlen(roomId) > 0) {
     jsonString += ",\"room_id\":\"" + String(roomId) + "\"";
   }
   jsonString += "}";

   Serial.print("Sending data: ");
   Serial.println(jsonString);
   Serial.print("To server: ");
   Serial.println(serverURL);

   int httpResponseCode = http.POST(jsonString);

   Serial.print("HTTP Response Code: ");
   Serial.println(httpResponseCode);

   if (httpResponseCode > 0) {
     String response = http.getString();
     Serial.print("Server Response: ");
     Serial.println(response);

     // SECURITY: Only allow lock control for outside scanners and authorized users
     if (httpResponseCode == 200 || httpResponseCode == 201) {
       Serial.print("🔍 Device location: ");
       Serial.println(deviceLocation);
       
       // Only outside scanners should control the lock
       if (String(deviceLocation) == "outside") {
                 // Parse response to check user type for lock control (backend returns "type":"instructor" in user object)
        if (response.indexOf("\"type\":\"instructor\"") != -1) {
          Serial.println("🔓 Instructor detected on OUTSIDE scanner - Opening solenoid lock!");
          openLock();
        } else if (response.indexOf("\"type\":\"admin\"") != -1) {
          Serial.println("🔓 Admin detected on OUTSIDE scanner - Opening solenoid lock!");
          openLock();
        } else if (response.indexOf("\"type\":\"student\"") != -1) {
          Serial.println("🚫 Student detected on OUTSIDE scanner - NO LOCK ACCESS");
        } else {
          Serial.println("⚠️ Unknown user type on OUTSIDE scanner - NO LOCK ACCESS");
          Serial.println("📄 Server response: " + response); // Debug: Show actual response
        }
       } else {
                 // Inside scanner - never controls lock directly (backend returns "type":"instructor" in user object)
        if (response.indexOf("\"type\":\"instructor\"") != -1) {
          Serial.println("📍 Instructor detected on INSIDE scanner - No lock control (attendance only)");
        } else if (response.indexOf("\"type\":\"student\"") != -1) {
          Serial.println("📚 Student detected on INSIDE scanner - Attendance recorded");
        } else if (response.indexOf("\"type\":\"admin\"") != -1) {
          Serial.println("👤 Admin detected on INSIDE scanner - No lock control (attendance only)");
        } else {
          Serial.println("⚠️ Unknown user type - No access");
          Serial.println("📄 Server response: " + response); // Debug: Show actual response
        }
       }
     } else {
       Serial.println("❌ Server returned error - No lock access");
     }

     http.end();
     return (httpResponseCode == 200 || httpResponseCode == 201);
   } else {
     Serial.print("HTTP Error: ");
     Serial.println(httpResponseCode);
     Serial.println("Troubleshooting:");
     Serial.println("1. Check if backend server is running: npm start");
     Serial.println("2. Verify server IP: " + discoveredServerIP);
     Serial.println("3. Check firewall settings");
     Serial.println("4. Try: curl http://" + discoveredServerIP + ":" + String(serverPort) + String(serverPath));
     http.end();
     return false;
   }
 }

 // Web Server Setup
 void setupWebServer() {
   // Lock control endpoint
   server.on("/api/lock-control", HTTP_POST, handleLockControl);
   
   // Health check endpoint
   server.on("/api/health", HTTP_GET, handleHealthCheck);
   
   // Default handler
   server.onNotFound(handleNotFound);
 }

 // Handle lock control requests
 void handleLockControl() {
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
     
     Serial.print("🔓 Action: ");
     Serial.println(action);
     Serial.print("👤 User: ");
     Serial.println(user);
     
     if (action == "open") {
       Serial.println("🔓 Opening solenoid lock via web request...");
       openLock();
       server.send(200, "application/json", "{\"message\":\"Lock opened successfully\",\"action\":\"open\",\"user\":\"" + user + "\"}");
     } else if (action == "close") {
       Serial.println("🔒 Closing solenoid lock via web request...");
       closeLock();
       server.send(200, "application/json", "{\"message\":\"Lock closed successfully\",\"action\":\"close\",\"user\":\"" + user + "\"}");
     } else {
       server.send(400, "application/json", "{\"error\":\"Invalid action. Use 'open' or 'close'\"}");
     }
   } else {
     server.send(400, "application/json", "{\"error\":\"No JSON data received\"}");
   }
 }

 // Handle health check requests
 void handleHealthCheck() {
   String response = "{\"status\":\"OK\",\"device\":\"ESP32 Solenoid Lock Controller\",\"ip\":\"" + WiFi.localIP().toString() + "\",\"uptime\":" + String(millis()) + "}";
   server.send(200, "application/json", response);
 }

 // Handle not found requests
 void handleNotFound() {
   server.send(404, "application/json", "{\"error\":\"Endpoint not found\"}");
 }

 // Solenoid Lock Control Functions
 void openLock() {
   Serial.println("🔓 Opening solenoid lock...");
   digitalWrite(RELAY_PIN, HIGH); // Activate relay (HIGH = ON for most relay modules)
   Serial.println("✅ Lock opened - Relay activated");
   
   // Keep lock open for specified duration
   delay(LOCK_DURATION);
   
   // Close lock
   closeLock();
 }

 void closeLock() {
   Serial.println("🔒 Closing solenoid lock...");
   digitalWrite(RELAY_PIN, LOW); // Deactivate relay (LOW = OFF)
   Serial.println("✅ Lock closed - Relay deactivated");
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
  for (int i = 0; i < 5; i++) {
    digitalWrite(LED_ERROR, HIGH);
    delay(200);
    digitalWrite(LED_ERROR, LOW);
    delay(200);
  }
}

// Function to enroll new fingerprints (call via Serial commands)
void enrollFingerprint(uint8_t id) {
  int p = -1;
  Serial.print("Enrolling ID #");
  Serial.println(id);
  Serial.println("Place finger to enroll...");
  
  while (p != FINGERPRINT_OK) {
    p = finger.getImage();
    if (p == FINGERPRINT_NOFINGER) continue;
    if (p != FINGERPRINT_OK) {
      Serial.println("Image capture failed");
      return;
    }
  }
  
  p = finger.image2Tz(1);
  if (p != FINGERPRINT_OK) {
    Serial.println("Error converting image");
    return;
  }
  
  Serial.println("Remove finger...");
  delay(2000);
  while (finger.getImage() != FINGERPRINT_NOFINGER);
  
  Serial.println("Place same finger again...");
  while ((p = finger.getImage()) != FINGERPRINT_OK);
  
  p = finger.image2Tz(2);
  if (p != FINGERPRINT_OK) {
    Serial.println("Error converting image 2");
    return;
  }
  
  p = finger.createModel();
  if (p != FINGERPRINT_OK) {
    Serial.println("Fingerprints don't match");
    return;
  }
  
  p = finger.storeModel(id);
  if (p == FINGERPRINT_OK) {
    Serial.println("✓ Fingerprint stored!");
    beepSuccess();
  } else {
    Serial.println("✗ Error storing fingerprint");
    beepError();
  }
}

// Handle Serial commands for enrollment
void handleSerialCommands() {
  if (Serial.available()) {
    String command = Serial.readStringUntil('\n');
    command.trim();
    
    // Check if it's just a number (for quick enrollment)
    if (command.length() > 0 && command.length() <= 3) {
      int id = command.toInt();
      if (id > 0 && id < 128) {
        Serial.println("=== Quick Enrollment Mode ===");
        Serial.print("Enrolling fingerprint ID #");
        Serial.println(id);
        enrollFingerprint(id);
        return;
      }
    }
    
    // Check for "enroll X" command
    if (command.startsWith("enroll ")) {
      int id = command.substring(7).toInt();
      if (id > 0 && id < 128) {
        enrollFingerprint(id);
      } else {
        Serial.println("Invalid ID. Use 1-127");
      }
    } else if (command == "status") {
      Serial.println("=== System Status ===");
      Serial.print("WiFi: ");
      Serial.println(WiFi.status() == WL_CONNECTED ? "Connected" : "Disconnected");
      Serial.print("IP: ");
      Serial.println(WiFi.localIP());
      Serial.print("Server: ");
      if (ipDiscovered) {
        Serial.println("http://" + discoveredServerIP + ":" + String(serverPort) + String(serverPath));
      } else {
        Serial.println("Not discovered");
      }
    } else if (command == "discover") {
      Serial.println("🔍 Manually triggering server discovery...");
      discoverServerIP();
         } else if (command == "test") {
       Serial.println("🧪 Testing fingerprint sensor...");
       Serial.println("Place your finger on the sensor now...");
       delay(3000);
       
       uint8_t p = finger.getImage();
       Serial.print("Image capture result: 0x");
       Serial.println(p, HEX);
       
       if (p == FINGERPRINT_OK) {
         Serial.println("✅ Finger detected!");
         p = finger.image2Tz();
         Serial.print("Image conversion result: 0x");
         Serial.println(p, HEX);
         
         if (p == FINGERPRINT_OK) {
           Serial.println("✅ Image converted successfully!");
         }
       } else if (p == FINGERPRINT_NOFINGER) {
         Serial.println("❌ No finger detected");
       } else {
         Serial.println("❌ Error detecting finger");
       }
     } else if (command == "testserver") {
       Serial.println("🧪 Testing server connection...");
       if (WiFi.status() != WL_CONNECTED) {
         Serial.println("❌ WiFi not connected");
         return;
       }
       
       String testURL = "http://192.168.1.11:5000/api/health";
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
       Serial.println("  POST /api/lock-control - Control solenoid lock");
       Serial.println("  GET  /api/health - Health check");
       Serial.println();
       Serial.println("📡 Test with: curl -X POST http://" + WiFi.localIP().toString() + "/api/lock-control -H 'Content-Type: application/json' -d '{\"action\":\"open\",\"user\":\"test\"}'");
         } else if (command.startsWith("setip ")) {
       String newIP = command.substring(6);
       discoveredServerIP = newIP;
       ipDiscovered = true;
       Serial.println("🔧 Server IP manually set to: " + discoveredServerIP);
     } else if (command == "help") {
       Serial.println("=== Available Commands ===");
       Serial.println("1-127: Quick enroll fingerprint with that ID");
       Serial.println("enroll X: Enroll fingerprint with ID X (1-127)");
       Serial.println("status: Show system status");
       Serial.println("discover: Manually discover server IP");
       Serial.println("setip X.X.X.X: Manually set server IP");
       Serial.println("test: Test fingerprint sensor");
       Serial.println("testserver: Test server connection");
       Serial.println("lock_open: Manually open solenoid lock");
       Serial.println("lock_close: Manually close solenoid lock");
       Serial.println("lock_test: Test solenoid lock (2 seconds)");
       Serial.println("webstatus: Show web server status");
       Serial.println("help: Show this help message");
     } else {
       // Handle lock control commands
       handleLockCommands(command);
     }
   }
 }
