/*
 * IoT Solenoid Lock Control System
 * ESP32 + Solenoid Lock
 * Controls solenoid lock via web API
 */

#include <WiFi.h>
#include <HTTPClient.h>
#include <WiFiUdp.h>
#include <ArduinoJson.h>
#include <WebServer.h>

// WiFi credentials - 2.4GHz Network (ESP32 Compatible)
const char* ssid = "WIFi2.4";         // 2.4GHz network
const char* password = "fOrtnite901_"; // Same password

// Security - API Key for authentication
const char* API_KEY = "LDCU_IOT_2025_SECURE_KEY_XYZ123"; // Change this to your own secret key

// Server configuration - Fully automatic discovery
const char* serverPort = "5000";

// Automatic IP discovery
String discoveredServerIP = "";
bool ipDiscovered = false;

// Manual IP override (uncomment and set if auto-discovery fails)
const char* manualServerIP = "192.168.1.11"; // Your computer's IP

// Alternative: Use localhost if on same machine
// const char* manualServerIP = "localhost"; // Alternative for testing

// Web server for lock control
WebServer server(80);


// No LED pins needed (you don't have LEDs)

// Buzzer pin for audio feedback
#define BUZZER_PIN 18

// Solenoid Lock Control
#define RELAY_PIN 5     // Relay control pin for solenoid lock
#define LOCK_DURATION 3000  // How long to keep lock open (3 seconds)

void setup() {
  Serial.begin(115200);
  delay(1000);
  
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
   // Lock control endpoint
   server.on("/api/lock-control", HTTP_POST, handleLockControl);
   
   // Health check endpoint
   server.on("/api/health", HTTP_GET, handleHealthCheck);
   
   // Default handler
   server.onNotFound(handleNotFound);
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
      Serial.println("status: Show system status");
      Serial.println("discover: Manually discover server IP");
      Serial.println("setip X.X.X.X: Manually set server IP");
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
