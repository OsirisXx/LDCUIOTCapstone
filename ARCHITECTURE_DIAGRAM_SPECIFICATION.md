# IoT Attendance System - Architecture Diagram Specification

This document provides detailed specifications for creating the system architecture diagram, including component icons, names, connections, protocols, and visual styling.

---

## Quick Reference: Icon Labels & Line Labels

### What to Put ON Icons (Component Labels)

| Component | Icon Label/Text |
|-----------|----------------|
| **Faculty Member** | "Faculty Member"<br>*Interacts with external ESP32* |
| **Student** | "Student"<br>*Interacts with internal ESP32* |
| **Administrator** | "Administrator"<br>*Manages system via dashboard* |
| **Admin Dashboard** | "Admin Dashboard (React)"<br>*www.llone.edu.ph*<br>*Port 443* |
| **React Frontend** | "React Frontend (SPA)"<br>*React.js 18+*<br>*Port 3000/443* |
| **Node.js Backend** | "Node.js Backend (REST API)"<br>*Express.js*<br>*Port 5000* |
| **FutronicAttendanceSystem** | "PC Gateway"<br>*C# .NET 8.0 Desktop App*<br>*Futronic SDK* |
| **MySQL Database System** | "MySQL Database System"<br>*MySQL 8.0+*<br>*Port 3306, SSL/TLS* |
| **MySQL Local Server** | "MySQL Local Server"<br>*Port 3306* |
| **Local Web Server** | "Local Web Server"<br>*Device Management* |
| **ESP32** | "ESP32 Controller"<br>*WiFi 2.4 GHz*<br>*Port 80 (HTTP)* |
| **WiFi Connectivity** | "WiFi Connectivity"<br>*IEEE 802.11 b/g/n*<br>*2.4 GHz* |
| **RFID Scanner** | "RFID Scanner"<br>*USB HID*<br>*Keyboard Emulation* |
| **Futronic FS80H** | "Futronic FS80H"<br>*Fingerprint Scanner*<br>*USB Proprietary* |
| **Relay Module** | "Relay Module"<br>*GPIO Control*<br>*Pin GPIO 5* |
| **OLED Display** | "0.96 inch OLED"<br>*I2C Protocol*<br>*128x64 pixels* |
| **Solenoid Lock** | "Solenoid Lock"<br>*12V DC*<br>*3 sec pulse* |
| **Power Supply** | "12V Power Supply"<br>*DC Power* |

### What to Label LINES (Connection Labels)

| Connection Type | Line Label |
|----------------|------------|
| **React ↔ Node.js** | `HTTPS / REST API (TLS, Port 443)` |
| **Node.js ↔ MySQL** | `MySQL Protocol (TCP 3306, SSL/TLS)` |
| **Futronic App ↔ Node.js** | `HTTP / REST API (Port 5000)` |
| **Futronic App ↔ MySQL** | `MySQL Protocol (TCP 3306, SSL/TLS)` |
| **Node.js ↔ ESP32** | `HTTP Lock Control (Port 80)` |
| **Futronic App ↔ ESP32** | `HTTP Lock Control (Port 80)` |
| **PC Gateway ↔ ESP32** | `UART / USB-Serial` |
| **ESP32 ↔ Relay** | `GPIO Control Signal (GPIO 5)` |
| **ESP32 ↔ OLED** | `I2C Protocol (SDA: GPIO 21, SCL: GPIO 22)` |
| **Relay ↔ Solenoid** | `Electrical Activation (12V DC)` |
| **WiFi ↔ ESP32** | `IEEE 802.11 b/g/n (2.4 GHz)` |
| **RFID ↔ Futronic App** | `USB HID (Keyboard Emulation)` |
| **Fingerprint ↔ Futronic App** | `USB Proprietary (Futronic SDK)` |
| **RFID ↔ MySQL Local** | `Data Storage` |
| **Fingerprint ↔ MySQL Local** | `Data Storage` |
| **User ↔ Hardware** | `Interacts with` |
| **Admin ↔ Dashboard** | `Accesses` |

---

## Diagram Structure Overview

The architecture is organized into **5 vertical layers** (top to bottom):

1. **User Interface Layer** - Users (Faculty, Student, Administrator)
2. **Dashboards Layer** - Admin Dashboard (Web Interface)
3. **Application Layer** - Software Applications (React, Node.js, C# Desktop App)
4. **Database Layer** - Data Storage (MySQL Systems)
5. **Infrastructure Layer** - Hardware Devices (ESP32, Sensors, Locks, etc.)

---

## Complete Component List

### User Interface Layer

#### 1. Faculty Member
- **Icon**: 👤 Human silhouette (professor icon)
- **Label**: "Faculty Member"<br>*Interacts with external ESP32*
- **Color**: Blue (#3B82F6)
- **Position**: Left side
- **Connects to**: External ESP32 Peripherals

#### 2. Student
- **Icon**: 👤 Human silhouette (student cap icon)
- **Label**: "Student"<br>*Interacts with internal ESP32*
- **Color**: Green (#10B981)
- **Position**: Center
- **Connects to**: Internal ESP32 Peripherals

#### 3. Administrator
- **Icon**: 👤 Human silhouette (shield/badge icon)
- **Label**: "Administrator"<br>*Manages system via dashboard*
- **Color**: Purple (#8B5CF6)
- **Position**: Right side
- **Connects to**: Admin Dashboard

---

### Dashboards Layer

#### 4. Admin Dashboard
- **Icon**: 🌐 Web browser window icon
- **Label**: "Admin Dashboard (React)"<br>*www.llone.edu.ph*<br>*Port 443*
- **Color**: Blue (#2563EB)
- **Shape**: Rectangle with rounded corners (browser window)
- **Position**: Center
- **Technology**: React SPA
- **Connects to**: Administrator, React Frontend, Node.js Backend

---

### Application Layer

#### 5. React Frontend
- **Icon**: ⚛️ React logo (atom symbol)
- **Label**: "React Frontend (SPA)"<br>*React.js 18+*<br>*Port 3000/443*
- **Color**: Cyan (#06B6D4)
- **Shape**: Hexagon
- **Position**: Left side
- **Technology**: React.js 18+, TailwindCSS, Axios
- **Port**: 3000 (Development), 443 (Production)
- **Connects to**: Admin Dashboard, Node.js Backend

#### 6. Node.js Backend
- **Icon**: 🟢 Node.js logo (green hexagon)
- **Label**: "Node.js Backend (REST API)"<br>*Express.js*<br>*Port 5000*
- **Color**: Green (#68A063)
- **Shape**: Hexagon
- **Position**: Center-left
- **Technology**: Node.js, Express.js
- **Port**: 5000
- **Connects to**: React Frontend, Admin Dashboard, FutronicAttendanceSystem, MySQL Database, ESP32

#### 7. FutronicAttendanceSystem (PC Gateway)
- **Icon**: 💻 Desktop computer icon
- **Label**: "PC Gateway"<br>*C# .NET 8.0 Desktop App*<br>*Futronic SDK*
- **Color**: Orange (#F97316)
- **Shape**: Rectangle with rounded corners
- **Position**: Center
- **Technology**: C# .NET 8.0, Windows Forms, Futronic SDK
- **Platform**: Windows PC
- **Connects to**: External/Internal ESP32 Peripherals, Node.js Backend, MySQL Database, ESP32, RFID Scanner, Fingerprint Scanner

#### 8. External and Internal ESP32 Peripherals (Logical Group)
- **Icon**: 🔌 USB connector icon
- **Label**: "ESP32 Peripherals"<br>*External & Internal Sensors*
- **Color**: Gray (#6B7280)
- **Shape**: Rectangle
- **Position**: Right side
- **Note**: Logical grouping of hardware sensors (not physical ESP32)
- **Connects to**: Faculty Member, Student, FutronicAttendanceSystem

---

### Database Layer

#### 9. MySQL Database System
- **Icon**: 🐬 MySQL dolphin logo or database cylinder
- **Label**: "MySQL Database System"<br>*MySQL 8.0+*<br>*Port 3306, SSL/TLS*
- **Color**: Blue (#00758F)
- **Shape**: Cylinder stack (database icon)
- **Position**: Left side
- **Technology**: MySQL 8.0+
- **Port**: 3306
- **Protocol**: MySQL Protocol (TCP) with SSL/TLS
- **Connects to**: Node.js Backend, FutronicAttendanceSystem

#### 10. MySQL Local Server
- **Icon**: 🐬 MySQL dolphin logo or local database icon
- **Label**: "MySQL Local Server"<br>*Port 3306*
- **Color**: Light Blue (#60A5FA)
- **Shape**: Cylinder stack
- **Position**: Far right
- **Technology**: MySQL 8.0+
- **Port**: 3306
- **Connects to**: RFID Scanner, Fingerprint Scanner, Local Web Server

#### 11. Local Web Server
- **Icon**: 🌐 Cloud/server icon
- **Label**: "Local Web Server"<br>*Device Management*
- **Color**: Light Cyan (#67E8F9)
- **Shape**: Cloud/rounded rectangle
- **Position**: Center-right
- **Connects to**: MySQL Local Server, ESP32

---

### Infrastructure Layer

#### 12. ESP32
- **Icon**: 🔌 Microcontroller/ESP32 board icon
- **Label**: "ESP32 Controller"<br>*WiFi 2.4 GHz*<br>*Port 80 (HTTP)*
- **Color**: Red-Orange (#EF4444)
- **Shape**: Rectangle with rounded corners
- **Position**: Center
- **Technology**: ESP32, Arduino framework
- **WiFi**: IEEE 802.11 b/g/n (2.4 GHz)
- **Web Server**: Port 80 (HTTP)
- **Connects to**: PC Gateway, Local Web Server, Node.js Backend, FutronicAttendanceSystem, WiFi Connectivity, Relay Module, OLED Display

#### 13. WiFi Connectivity
- **Icon**: 📶 WiFi signal icon or radio waves
- **Label**: "WiFi Connectivity"<br>*IEEE 802.11 b/g/n*<br>*2.4 GHz*
- **Color**: Blue (#3B82F6)
- **Shape**: Radiating waves or WiFi symbol
- **Position**: Top-left
- **Technology**: IEEE 802.11 b/g/n
- **Frequency**: 2.4 GHz
- **Connects to**: ESP32

#### 14. RFID Scanner
- **Icon**: 📱 RFID card/reader icon
- **Label**: "RFID Scanner"<br>*USB HID*<br>*Keyboard Emulation*
- **Color**: Purple (#A855F7)
- **Shape**: Rectangle with card icon
- **Position**: Left side
- **Technology**: USB HID (Keyboard emulation)
- **Connects to**: MySQL Local Server, FutronicAttendanceSystem

#### 15. Futronic FS80H Fingerprint Scanner
- **Icon**: 👆 Fingerprint icon
- **Label**: "Futronic FS80H"<br>*Fingerprint Scanner*<br>*USB Proprietary*
- **Color**: Teal (#14B8A6)
- **Shape**: Rectangle with fingerprint icon
- **Position**: Center-left
- **Technology**: USB Proprietary (Futronic SDK)
- **SDK**: FTRAPI.dll, ftrSDKHelper13.dll
- **Connects to**: MySQL Local Server, FutronicAttendanceSystem

#### 16. Relay Module
- **Icon**: ⚡ Relay/switch icon
- **Label**: "Relay Module"<br>*GPIO Control*<br>*Pin GPIO 5*
- **Color**: Yellow (#FBBF24)
- **Shape**: Rectangle with switch icon
- **Position**: Center-right
- **Technology**: Digital relay
- **Pin**: GPIO 5
- **Signal**: HIGH (3.3V) = ON, LOW (0V) = OFF
- **Connects to**: ESP32, Solenoid Lock

#### 17. 0.96 inch OLED Display
- **Icon**: 📺 Small screen/monitor icon
- **Label**: "0.96 inch OLED"<br>*I2C Protocol*<br>*128x64 pixels*
- **Color**: Dark Gray (#374151)
- **Shape**: Rectangle (represents screen)
- **Position**: Right side
- **Technology**: I2C OLED Display
- **Resolution**: 128x64 pixels
- **Address**: 0x3C (I2C)
- **Pins**: SDA (GPIO 21), SCL (GPIO 22)
- **Library**: Adafruit_SSD1306
- **Connects to**: ESP32

#### 18. Solenoid Lock
- **Icon**: 🔒 Lock icon
- **Label**: "Solenoid Lock"<br>*12V DC*<br>*3 sec pulse*
- **Color**: Dark Gray (#1F2937)
- **Shape**: Lock/cylinder icon
- **Position**: Far right
- **Technology**: Solenoid actuator
- **Power**: 12V DC
- **Duration**: 3 seconds (pulse)
- **Connects to**: Relay Module, Power Supply

#### 19. Power Supply
- **Icon**: 🔌 Power plug icon
- **Label**: "12V Power Supply"<br>*DC Power*
- **Color**: Black (#000000) or Dark Gray (#1F2937)
- **Shape**: Power supply/plug icon
- **Position**: Bottom-right
- **Technology**: DC Power Supply
- **Voltage**: 12V DC
- **Connects to**: Solenoid Lock

---

## Connection Matrix

### All Connections Between Components

| From | To | Label | Protocol | Port | Direction | Color |
|------|-----|-------|----------|------|-----------|-------|
| **Administrator** | **Admin Dashboard** | `Accesses` | User Interaction | - | Bidirectional | Purple |
| **Faculty Member** | **External ESP32 Peripherals** | `Interacts with` | User Interaction | - | Bidirectional | Blue |
| **Student** | **Internal ESP32 Peripherals** | `Interacts with` | User Interaction | - | Bidirectional | Green |
| **Admin Dashboard** | **React Frontend** | `Rendered by` | HTTPS/REST API | 443 | Bidirectional | Green |
| **Admin Dashboard** | **Node.js Backend** | `API Requests` | HTTPS/REST API | 443 | Bidirectional | Green |
| **React Frontend** | **Node.js Backend** | `HTTPS / REST API (TLS, Port 443)` | HTTPS (TLS) | 443/5000 | Bidirectional | Green |
| **Node.js Backend** | **MySQL Database System** | `MySQL Protocol (TCP 3306, SSL/TLS)` | MySQL Protocol (TCP) | 3306 | Bidirectional | Blue (#00758F) |
| **Node.js Backend** | **ESP32** | `HTTP Lock Control (Port 80)` | HTTP | 80 | Bidirectional | Orange |
| **FutronicAttendanceSystem** | **Node.js Backend** | `HTTP / REST API (Port 5000)` | HTTP | 5000 | Bidirectional | Blue |
| **FutronicAttendanceSystem** | **MySQL Database System** | `MySQL Protocol (TCP 3306, SSL/TLS)` | MySQL Protocol (TCP) | 3306 | Bidirectional | Blue (#00758F) |
| **FutronicAttendanceSystem** | **ESP32** | `HTTP Lock Control (Port 80)` | HTTP | 80 | Bidirectional | Orange |
| **FutronicAttendanceSystem** | **RFID Scanner** | `USB HID (Keyboard Emulation)` | USB HID | - | Unidirectional | Purple |
| **FutronicAttendanceSystem** | **Futronic FS80H** | `USB Proprietary (Futronic SDK)` | USB Proprietary | - | Unidirectional | Teal |
| **External/Internal ESP32 Peripherals** | **FutronicAttendanceSystem** | `Biometric Data Capture (RFID or Fingerprint)` | USB HID/USB Proprietary | - | Bidirectional | Gray |
| **PC Gateway** | **ESP32** | `UART / USB-Serial` | UART/USB-Serial | - | Bidirectional | Gray |
| **Local Web Server** | **ESP32** | `HTTPS / REST API (TLS, Port 443)` | HTTPS (TLS) | 443 | Bidirectional | Green |
| **Local Web Server** | **MySQL Local Server** | `Database Access` | MySQL Protocol (TCP) | 3306 | Bidirectional | Light Blue |
| **RFID Scanner** | **MySQL Local Server** | `Data Storage` | Data Storage | - | Unidirectional | Purple |
| **Futronic FS80H** | **MySQL Local Server** | `Data Storage` | Data Storage | - | Unidirectional | Teal |
| **WiFi Connectivity** | **ESP32** | `IEEE 802.11 b/g/n (2.4 GHz)` | WiFi | - | Unidirectional | Blue |
| **ESP32** | **Relay Module** | `GPIO Control Signal (GPIO 5)` | GPIO Digital Signal | GPIO 5 | Unidirectional | Red |
| **ESP32** | **OLED Display** | `I2C Protocol (SDA: GPIO 21, SCL: GPIO 22)` | I2C | Address 0x3C | Bidirectional | Teal |
| **Relay Module** | **Solenoid Lock** | `Electrical Activation (12V DC)` | Electrical | 12V DC | Unidirectional | Yellow |
| **Power Supply** | **Solenoid Lock** | `12V DC Power` | Electrical | 12V DC | Unidirectional | Yellow |

---

## Connection Type Specifications

### 1. HTTPS/REST API (TLS, Port 443)
- **Style**: Solid line with arrow (bidirectional)
- **Color**: Green (#10B981)
- **Label**: `HTTPS / REST API (TLS, Port 443)`
- **Protocol**: HTTPS with TLS encryption
- **Port**: 443 (Production) or 5000 (Development)
- **Authentication**: JWT Bearer Token (where applicable)
- **Used between**: React Frontend ↔ Node.js Backend, Admin Dashboard ↔ Node.js Backend, Local Web Server ↔ ESP32

### 2. HTTP/REST API (Port 5000)
- **Style**: Solid line with arrow (bidirectional)
- **Color**: Blue (#3B82F6)
- **Label**: `HTTP / REST API (Port 5000)`
- **Protocol**: HTTP
- **Port**: 5000
- **Authentication**: None (internal network) or API Key
- **Used between**: FutronicAttendanceSystem ↔ Node.js Backend

### 3. HTTP Lock Control (Port 80)
- **Style**: Solid line with arrow (bidirectional)
- **Color**: Orange (#F97316)
- **Label**: `HTTP Lock Control (Port 80)`
- **Protocol**: HTTP
- **Port**: 80
- **Authentication**: API Key (`X-API-Key` header)
- **Used between**: Node.js Backend ↔ ESP32, FutronicAttendanceSystem ↔ ESP32

### 4. MySQL Protocol (TCP 3306, SSL/TLS)
- **Style**: Solid line with arrow (bidirectional)
- **Color**: Blue (#00758F)
- **Label**: `MySQL Protocol (TCP 3306, SSL/TLS)`
- **Protocol**: MySQL Protocol over TCP
- **Port**: 3306
- **Encryption**: SSL/TLS enabled
- **Used between**: Node.js Backend ↔ MySQL Database System, FutronicAttendanceSystem ↔ MySQL Database System

### 5. UART/USB-Serial
- **Style**: Dashed line with arrow (bidirectional)
- **Color**: Gray (#6B7280)
- **Label**: `UART / USB-Serial`
- **Protocol**: Serial communication
- **Note**: Optional connection method
- **Used between**: PC Gateway ↔ ESP32

### 6. GPIO Control Signal
- **Style**: Solid line with arrow (unidirectional)
- **Color**: Red (#EF4444)
- **Label**: `GPIO Control Signal (GPIO 5)`
- **Protocol**: GPIO Digital Signal
- **Pin**: GPIO 5
- **Signal**: HIGH/LOW (3.3V/0V)
- **Used between**: ESP32 → Relay Module

### 7. I2C Protocol
- **Style**: Solid line with arrow (bidirectional)
- **Color**: Teal (#14B8A6)
- **Label**: `I2C Protocol (SDA: GPIO 21, SCL: GPIO 22)`
- **Protocol**: I2C (Inter-Integrated Circuit)
- **Pins**: SDA (GPIO 21), SCL (GPIO 22)
- **Address**: 0x3C
- **Used between**: ESP32 ↔ OLED Display

### 8. WiFi (IEEE 802.11 b/g/n)
- **Style**: Radiating waves or dashed line with arrow
- **Color**: Blue (#3B82F6)
- **Label**: `IEEE 802.11 b/g/n (2.4 GHz)`
- **Protocol**: WiFi
- **Frequency**: 2.4 GHz
- **Standard**: IEEE 802.11 b/g/n
- **Used between**: WiFi Connectivity → ESP32

### 9. USB HID (Keyboard Emulation)
- **Style**: Solid line with arrow (unidirectional)
- **Color**: Purple (#A855F7)
- **Label**: `USB HID (Keyboard Emulation)`
- **Protocol**: USB HID
- **Used between**: FutronicAttendanceSystem → RFID Scanner

### 10. USB Proprietary (Futronic SDK)
- **Style**: Solid line with arrow (unidirectional)
- **Color**: Teal (#14B8A6)
- **Label**: `USB Proprietary (Futronic SDK)`
- **Protocol**: USB Proprietary
- **SDK**: FTRAPI.dll, ftrSDKHelper13.dll
- **Used between**: FutronicAttendanceSystem → Futronic FS80H

### 11. Electrical Activation
- **Style**: Thick solid line with arrow (unidirectional)
- **Color**: Yellow (#FBBF24)
- **Label**: `Electrical Activation (12V DC)`
- **Protocol**: Electrical
- **Voltage**: 12V DC
- **Duration**: 3 seconds (pulse)
- **Used between**: Relay Module → Solenoid Lock, Power Supply → Solenoid Lock

---

## Visual Styling Guidelines

### Color Scheme

#### Primary Colors
- **Blue**: #3B82F6 (Primary UI elements, web services)
- **Green**: #10B981 (Success, database, Node.js)
- **Orange**: #F97316 (Desktop applications, hardware)
- **Purple**: #A855F7 (RFID, authentication)
- **Teal**: #14B8A6 (Fingerprint, I2C)
- **Red**: #EF4444 (ESP32, GPIO)
- **Yellow**: #FBBF24 (Relay, electrical)

#### Secondary Colors
- **Gray**: #6B7280 (Infrastructure, peripherals)
- **Dark Gray**: #374151 (Display, locks)
- **Cyan**: #06B6D4 (React frontend)
- **Light Blue**: #60A5FA (Local database)
- **Dark Blue**: #00758F (MySQL Database System)

### Component Shapes

- **User Icons**: Circle or human silhouette
- **Web Applications**: Rounded rectangle (browser window)
- **Backend Services**: Hexagon (application layer)
- **Databases**: Cylinder stack
- **Hardware Devices**: Rectangle with rounded corners
- **Networks**: Radiating waves or cloud
- **Electrical Components**: Rectangle with icon

### Text Styling

- **Component Names**: Bold, 12-14pt font
- **Connection Labels**: Regular, 10-11pt font
- **Protocol Labels**: Italic, 9-10pt font
- **Port Numbers**: Monospace font, 9pt

### Layout Guidelines

1. **Vertical Layers**: Arrange components in 5 distinct horizontal layers
2. **Spacing**: Maintain consistent spacing between layers (40-50px)
3. **Alignment**: Align components within each layer horizontally
4. **Connection Routing**: Use curved or straight lines, avoid crossing when possible
5. **Legend**: Include a legend showing connection types and colors

---

## Legend/Key

### Connection Types
- **Solid Line (Green)**: HTTPS/REST API (TLS, Port 443)
- **Solid Line (Blue)**: HTTP/REST API (Port 5000)
- **Solid Line (Orange)**: HTTP Lock Control (Port 80)
- **Solid Line (Dark Blue)**: MySQL Protocol (TCP 3306, SSL/TLS)
- **Dashed Line (Gray)**: UART/USB-Serial
- **Solid Line (Red)**: GPIO Control Signal
- **Solid Line (Teal)**: I2C Protocol
- **Radiating Waves (Blue)**: WiFi (IEEE 802.11 b/g/n)
- **Solid Line (Purple)**: USB HID
- **Solid Line (Teal)**: USB Proprietary
- **Thick Line (Yellow)**: Electrical Activation

### Component Categories
- **Users**: Human icons (Blue, Green, Purple)
- **Web Services**: Browser/Rectangle (Blue, Cyan)
- **Backend Services**: Hexagon (Green)
- **Desktop Applications**: Rectangle (Orange)
- **Databases**: Cylinder (Blue, Light Blue)
- **Hardware**: Rectangle with icon (Various colors)
- **Networks**: Waves/Cloud (Blue)

---

## Additional Notes for Diagram Creation

### Important Details to Include

1. **Port Numbers**: Always include port numbers in connection labels
2. **Protocols**: Specify full protocol names (e.g., "MySQL Protocol (TCP 3306, SSL/TLS)")
3. **Authentication**: Note authentication methods (JWT, API Key, etc.)
4. **Direction**: Use arrows to indicate data flow direction
5. **Bidirectional**: Use double arrows for bidirectional connections
6. **Layer Separation**: Clearly separate the 5 layers visually

### Diagram Tools Recommendations

- **Draw.io (diagrams.net)**: Free, supports custom icons and styling
- **Lucidchart**: Professional diagramming with collaboration
- **Visio**: Microsoft Office diagramming tool
- **Miro**: Collaborative whiteboard with diagramming features
- **Figma**: Design tool with diagram capabilities

### Icon Resources

- **Font Awesome**: https://fontawesome.com/icons (for web icons)
- **Material Design Icons**: https://materialdesignicons.com/
- **Flaticon**: https://www.flaticon.com/ (for custom icons)
- **Icons8**: https://icons8.com/ (for technology icons)
