#include <ESP8266WiFi.h>
#include <EEPROM.h>

// Wi-Fi / socket params (populated from EEPROM or SetupWifi)
String ssid     = "";
String password = "";
String remoteIp = ""; // used in client mode
bool wifiConnected = false;

WiFiServer wifiServer(80);
WiFiClient wifiClient;
const uint16_t remotePort = 6510; // adjust if needed

struct wifiInfo_struct {
  byte ssidLength = 0x00;
  char ssid[33];   // 32 + null
  byte pwdLength = 0x00;
  char pwd[255];   // up to 254 + null 
  byte ipLength = 0x00;
  char ipAddr[16]; // "255.255.255.255" + null  
  uint16_t portNumber;
  byte clientMode = 0x01; // 0 = server, 1 = client (default)
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
  Serial.begin(9600);
  pinMode(LED_BUILTIN, OUTPUT);
  digitalWrite(LED_BUILTIN, HIGH);

  EEPROM.begin(sizeof(wifiInfo));
  EEPROM.get(0, wifiInfo);   
  
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
    remoteIp = String((char*)wifiInfo.ipAddr);
  }

  // needed until I get the clientMode hooked in
  wifiInfo.clientMode = 0x01; 
  
  //uint16_t portNumber = (highByte << 8) | lowByte;
 
  if (ssid.length() > 0 && password.length() > 0) {
    ConnectToWIFI();
  } else {
    Serial.println("No WiFi creds stored, waiting for SetupWifi.");
  }
}

void loop() {
  
  if (!wifiConnected) {
    SetupWifi(); // waits for serial setup commands when not connected
    return;
  }

  // choose mode based on stored flag
  bool inClientMode = (wifiInfo.clientMode == 1);

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
  if (!Serial.available()) return;

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

  int modeByte = readByteBlocking();
  if (modeByte < 0) return;
  byte mode = (byte)modeByte; // 0 server, 1 client

  // store into EEPROM structure
  wifiInfo.ssidLength = (byte)ssidLen;
  memset(wifiInfo.ssid, 0, sizeof(wifiInfo.ssid));
  if (ssidLen > 0) strncpy((char*)wifiInfo.ssid, tmpSSID, ssidLen);

  wifiInfo.pwdLength = (byte)pwdLen;
  memset(wifiInfo.pwd, 0, sizeof(wifiInfo.pwd));
  if (pwdLen > 0) strncpy((char*)wifiInfo.pwd, tmpPWD, pwdLen);

  wifiInfo.ipLength = (byte)ipLen;
  memset(wifiInfo.ipAddr, 0, sizeof(wifiInfo.ipAddr));
  if (ipLen > 0) strncpy((char*)wifiInfo.ipAddr, tmpIP, ipLen);

  wifiInfo.clientMode = mode ? 1 : 0;

  // persist
  EEPROM.put(0, wifiInfo);
  EEPROM.commit();

  // update runtime strings
  ssid = String(tmpSSID);
  password = String(tmpPWD);
  remoteIp = String(tmpIP);

  Serial.print("Stored SSID: "); Serial.println(ssid);
  Serial.print("Stored PWD length: "); Serial.println(wifiInfo.pwdLength);
  Serial.print("Stored IP: "); Serial.println(remoteIp);
  Serial.print("ClientMode: "); Serial.println(wifiInfo.clientMode ? "client" : "server");

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
  if (wifiInfo.clientMode == 0) {
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

/* Client mode: maintain connection to remoteIp:remotePort and forward between Serial and socket */
void HandleClientMode() {
  if (remoteIp.length() == 0) {
    Serial.println("No remote IP configured for client mode");
    delay(1000);
    return;
  }

  if (!wifiClient || !wifiClient.connected()) {
    if (wifiClient.connect(remoteIp.c_str(), remotePort)) {
      wifiClient.setNoDelay(true);
      socketConnected = true;
      Serial.print("Connected to remote ");
      Serial.print(remoteIp);
      Serial.print(":");
      Serial.println(remotePort);
    } else {
      socketConnected = false;
      Serial.print("Connect failed to ");
      Serial.print(remoteIp);
      Serial.print(":");
      Serial.println(remotePort);
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
