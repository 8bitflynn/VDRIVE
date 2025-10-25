#include <ESP8266WiFi.h>
#include <EEPROM.h>

// Wi-Fi / socket params (populated from EEPROM or SetupWifi)
String ssid     = "";
String password = "";
String remoteIP = ""; // server IP address to connect to in client mode
uint16_t port = 6510; // client port  / server listen port number
bool wifiConnected = false;

// a bit of time to 'R'eset or 'D'ump in serial monitor
const int startupCmdWindowMilliseconds = 10000; // 10 seconds

WiFiServer wifiServer(port);
WiFiClient wifiClient;

struct wifiInfo_struct {  
  byte marker = 0xA5; // not sent just helps line up struct
  byte ssidLength = 0x00;
  char ssid[33];   // 32 + null
  byte pwdLength = 0x00;
  char pwd[255];   // up to 254 + null 
  byte ipLength = 0x00;
  char ipAddr[16]; // "255.255.255.255" + null  // client mode
  uint16_t port; // server port number or client port number
  byte mode = 0x00; // 1 = server, 0 = client (default)
} wifiInfo;

// firmware runtime flags
bool socketConnected = false;

// sync byte used by the serial setup protocol
// note: any Serial.println can be done for 
// debugging in monitor as long as it does 
// not contain a '+' or 0x2b as that is
// what will sync the first frame
const byte SYNC_BYTE = 0x2B;

void setup() {
  delay(500); // give USB time to settle
  Serial.begin(9600);  
  Serial.println("Booting firmware");

  pinMode(LED_BUILTIN, OUTPUT);
  digitalWrite(LED_BUILTIN, HIGH);

  // short delay before startup for reset and dump
  // 'R' = Reset EEPROM (writes zeros)
  // 'D' = Dump EEPROM
  Serial.println("Startup command window...");
  EEPROM.begin(sizeof(wifiInfo));
  unsigned long start = millis();
  while (millis() - start < startupCmdWindowMilliseconds) {
    if (Serial.available()) {
      int b = Serial.read();
      if (b == 'R') {
        Serial.println("EEPROM reset requested.");
        for (int i = 0; i < sizeof(wifiInfo); ++i) EEPROM.write(i, 0);
        EEPROM.commit();
        Serial.println("EEPROM cleared. Rebooting...");
        ESP.restart(); // soft reboot
        return;
      }
      if (b == 'D') {
        Serial.println("EEPROM dump:");
        for (int i = 0; i < sizeof(wifiInfo); ++i) {
          byte val = EEPROM.read(i);
          Serial.print(val, HEX);
          Serial.print(" ");
        }
        Serial.println();
      }
    }
  }
 
  EEPROM.get(0, wifiInfo);  

  Serial.print("EEPROM marker: ");
  Serial.println(wifiInfo.marker, HEX);

   if (wifiInfo.marker != 0xA5) {
    Serial.println("EEPROM marker missing or invalid.");
    return; // Skip loading corrupted or uninitialized data
  }
  
  // restore values from EEPROM if present
  if (wifiInfo.ssidLength > 0 && wifiInfo.ssidLength < sizeof(wifiInfo.ssid)) {
    wifiInfo.ssid[wifiInfo.ssidLength] = 0;
    ssid = String((char*)wifiInfo.ssid);
  }
  if (wifiInfo.pwdLength > 0 && wifiInfo.pwdLength < sizeof(wifiInfo.pwd)) {
    wifiInfo.pwd[wifiInfo.pwdLength] = 0;
    password = String((char*)wifiInfo.pwd);
  }
  if (wifiInfo.ipLength > 0 && wifiInfo.ipLength < sizeof(wifiInfo.ipAddr)) {
    wifiInfo.ipAddr[wifiInfo.ipLength] = 0;
    remoteIP = String((char*)wifiInfo.ipAddr);
  } 
  if (wifiInfo.port > 0) {
    port = wifiInfo.port;
  }

  Serial.print("EEPROM SSID length: "); Serial.println(wifiInfo.ssidLength);
  Serial.print("EEPROM PWD length: "); Serial.println(wifiInfo.pwdLength);
  Serial.print("EEPROM Mode: "); Serial.println(wifiInfo.mode == 0 ? "client" : "server");
 
  if (ssid.length() > 0 && password.length() > 0) {
    Serial.println("Connecting to WiFi");
    ConnectToWIFI();
  } else {
    Serial.println("No WiFi creds stored, waiting for SetupWifi.");
  }
}

void loop() {
  
 if (!wifiConnected) {
  SetupWifi();
  delay(1000); // prevent tight loop
  Serial.println("Waiting for setup frame...");
  return;
  }
  
  // choose mode based on stored flag
  bool inClientMode = (wifiInfo.mode == 0); // 0 client mode (default), 1 server mode

  if (inClientMode) {
    HandleClientMode();
  } else {
    HandleServerMode();
  }
}

/*
  Serial-based setup protocol (single-shot, sync-driven).
  Expected sequence after sending sync byte (0x2B):
   - ssidLength (1 byte)
   - ssid chars (ssidLength bytes)
   - pwdLength (1 byte)
   - pwd chars (pwdLength bytes)
   - ipLength (1 byte)   // 0 means "no ip" / server mode only
   - ip chars (ipLength bytes) // if ipLength > 0
   - clientMode (1 byte) // 0 = server, 1 = client
*/
void SetupWifi() {  
  if (!Serial.available()){    
    return;
  }

  int b = Serial.read(); 
  if (b != SYNC_BYTE) return;

  // wait for lengths/fields, reading with small timeout
  auto readByteBlocking = [](unsigned long timeoutMs = 2000UL) -> int {
    unsigned long start = millis();
    while (!Serial.available()) {
      if (millis() - start > timeoutMs) return -1;
      delay(5);
    }
    return Serial.read();
  };

  int ssidLen = readByteBlocking();
  if (ssidLen < 0 || ssidLen > 32) return;
  char tmpSSID[33] = {0};
  for (int i = 0; i < ssidLen; ++i) {
    int c = readByteBlocking();
    if (c < 0) return;
    tmpSSID[i] = (char)c;
  }

  int pwdLen = readByteBlocking();
  if (pwdLen < 0 || pwdLen >= 255) return;
  char tmpPWD[255];
  memset(tmpPWD, 0, sizeof(tmpPWD));
  for (int i = 0; i < pwdLen; ++i) {
    int c = readByteBlocking();
    if (c < 0) return;
    tmpPWD[i] = (char)c;
  }

  int ipLen = readByteBlocking();
  if (ipLen < 0 || ipLen > 15) return;
  char tmpIP[16] = {0};
  for (int i = 0; i < ipLen; ++i) {
    int c = readByteBlocking();
    if (c < 0) return;
    tmpIP[i] = (char)c;
  }

  // 2 bytes for client port
  byte portLo = readByteBlocking();
  byte portHi = readByteBlocking();
  
  uint16_t port = (portHi << 8) | portLo;

  int modeByte = readByteBlocking();
  if (modeByte < 0) return;
  byte mode = (byte)modeByte; // 0 server, 1 client

  // store into EEPROM structure
  wifiInfo.ssidLength = (byte)ssidLen;
  memset(wifiInfo.ssid, 0, sizeof(wifiInfo.ssid));
  if (ssidLen > 0) {
    strncpy((char*)wifiInfo.ssid, tmpSSID, ssidLen);
    wifiInfo.ssid[ssidLen] = '\0'; // ensure termination
  }

  wifiInfo.pwdLength = (byte)pwdLen;
  memset(wifiInfo.pwd, 0, sizeof(wifiInfo.pwd));
  if (pwdLen > 0) {
    strncpy((char*)wifiInfo.pwd, tmpPWD, pwdLen);
    wifiInfo.pwd[pwdLen] = '\0'; // ensure termination
  }  

  wifiInfo.ipLength = (byte)ipLen;
  memset(wifiInfo.ipAddr, 0, sizeof(wifiInfo.ipAddr));
  if (ipLen > 0) {
    strncpy((char*)wifiInfo.ipAddr, tmpIP, ipLen);
    wifiInfo.ipAddr[ipLen] = '\0'; // ensure termination
  }

  // client port 2 bytes
  wifiInfo.port = port;

  // server or client (0=client, 1=server)
  wifiInfo.mode = mode;

  // persist
  wifiInfo.marker = 0xA5; 
  EEPROM.put(0, wifiInfo);
  EEPROM.commit();

  // update runtime strings
  ssid = String(tmpSSID);
  password = String(tmpPWD);
  remoteIP = String(tmpIP);

  Serial.print("Stored SSID: "); Serial.println(ssid);
  Serial.print("Stored PWD length: "); Serial.println(wifiInfo.pwdLength);
  Serial.print("Stored IP: "); Serial.println(remoteIP);
  Serial.print("Mode: "); Serial.println(wifiInfo.mode == 1 ? "server" : "client");

  ConnectToWIFI();
}

void ConnectToWIFI() {
  Serial.print("Connecting to WiFi: ");
  Serial.println(ssid);

  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid.c_str(), password.c_str());

  unsigned long start = millis();
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
    if (millis() - start > 15000UL) {
      Serial.println();
      Serial.println("WiFi connect timeout, will retry from loop.");
      return;
    }
  }

  Serial.println();
  Serial.print("Connected, IP: ");
  Serial.println(WiFi.localIP());

  wifiConnected = true;

  // If server mode, start server
  if (wifiInfo.mode == 1) {
    wifiServer.begin();
    wifiServer.setNoDelay(true);
    Serial.println("Server started on port 80");
  } else {
    Serial.println("Configured for client mode");
  }
}

/* Server mode: accept incoming client and forward bytes between Serial and socket */
void HandleServerMode() {

  if (wifiServer.hasClient()) {
    WiFiClient client = wifiServer.available();
    if (!client) return;

    Serial.println("Client connected (server mode)");
    socketConnected = true;

    while (client.connected()) {
      // from C64 -> socket
      if (Serial.available()) {
        digitalWrite(LED_BUILTIN, LOW);
        char c = Serial.read();
        client.write((uint8_t)c);
      }

      // from socket -> C64
      while (client.available()) {
        digitalWrite(LED_BUILTIN, LOW);
        char c = client.read();
        Serial.write((uint8_t)c);
      }
      
      Serial.flush();
      digitalWrite(LED_BUILTIN, HIGH);
      delay(1);
    }

    client.stop();
    socketConnected = false;
    Serial.println("Client disconnected (server mode)");
  }
}

/* Client mode: maintain connection to remoteIP:remotePort and forward between Serial and socket */
void HandleClientMode() {
  if (remoteIP.length() == 0) {
    Serial.println("No remote IP configured for client mode");
    delay(1000);
    return;
  }

  if (!wifiClient || !wifiClient.connected()) {
    if (wifiClient.connect(remoteIP.c_str(), port)) {
      wifiClient.setNoDelay(true);
      socketConnected = true;
      Serial.print("Connected to remote ");
      Serial.print(remoteIP);
      Serial.print(":");
      Serial.println(port);
    } else {
      socketConnected = false;
      Serial.print("Connect failed to ");
      Serial.print(remoteIP);
      Serial.print(":");
      Serial.println(port);
      delay(2000); // backoff before retry
      return;
    }
  }

  // forward data
  if (Serial.available()) {
    digitalWrite(LED_BUILTIN, LOW);
    char c = Serial.read();
    wifiClient.write((uint8_t)c);
  }

  while (wifiClient.available()) {
    digitalWrite(LED_BUILTIN, LOW);
    char c = wifiClient.read();
    Serial.write((uint8_t)c);
  }

  Serial.flush();
  digitalWrite(LED_BUILTIN, HIGH);  
}
