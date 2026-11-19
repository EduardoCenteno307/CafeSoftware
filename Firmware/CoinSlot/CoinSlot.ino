/*
  CoinSlot firmware v1.4 for PisoKlik
  Runs on the ESP8266 (NodeMCU, Wemos D1 mini) and on the Arduino Mega.

  Counts the pulses from the Allan coin acceptor (through the PC817 isolation
  module) and reports each coin to the PC as a NUMBER OF PULSES.
  What a pulse count is worth is decided in PisoKlik (Settings > Coin table),
  not in this firmware.

  Messages to the PC (one per line):
    READY                      sent once at boot
    PULSES,<seq>,<count>       a coin sent <count> pulses. Repeated every second until acknowledged.
    PONG                       reply to PING
    DBG,...                    debug text. PisoKlik ignores these lines.

  Messages from the PC:
    ACK,<seq>                  the PC got coin <seq>
    PING                       is the controller alive?

  Test commands (type in the Arduino Serial Monitor, line ending "Newline"):
    LIVE       live view of the coin pin on/off (prints the level every second and on every change)
    CAL        calibration view on/off: shows pulse counts, sends NOTHING to the PC
    RAW        raw mode on/off: shows every pulse, the time between pulses and noise
    STATUS     show pin state, edges seen, pending coins, uptime
    SCAN       find which pin the coin signal arrives on (15 seconds)
    SIM,<n>    pretend a coin sent <n> pulses (only if ENABLE_SIM is 1)
    ACK,<n>    pretend to be the PC acknowledging coin <n>
*/

#include <Arduino.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// ============================ BOARD ===============================
// The sketch picks these automatically from the board selected in the Arduino IDE.

#if defined(ESP8266)
  #include <strings.h>
  #define BOARD_NAME   "ESP8266"
  #define COIN_PIN     14        // GPIO14 = D5 on NodeMCU and Wemos D1 mini. Do not use GPIO16 (D0).
  #define LED_ON       LOW       // the built-in LED is active low
  #define LED_OFF      HIGH
  #ifndef IRAM_ATTR
    #define IRAM_ATTR  ICACHE_RAM_ATTR
  #endif
  // Pins checked by the SCAN command: D1, D2, D3, D4, D5, D6, D7
  const uint8_t SCAN_PINS[]  = { 5, 4, 0, 2, 14, 12, 13 };
  const char*   SCAN_NAMES[] = { "D1", "D2", "D3", "D4", "D5", "D6", "D7" };

#elif defined(__AVR_ATmega2560__) || defined(__AVR_ATmega1280__)
  #define BOARD_NAME   "Arduino Mega"
  #define COIN_PIN     2         // must be an interrupt pin on the Mega: 2, 3, 18, 19, 20 or 21
  #define LED_ON       HIGH      // the built-in LED (pin 13) is active high
  #define LED_OFF      LOW
  #define IRAM_ATTR
  // Pins checked by the SCAN command: the Mega's interrupt pins
  const uint8_t SCAN_PINS[]  = { 2, 3, 18, 19, 20, 21 };
  const char*   SCAN_NAMES[] = { "2", "3", "18", "19", "20", "21" };

#else
  #error "This sketch supports the ESP8266 and the Arduino Mega."
#endif

// ============================ SETTINGS ============================

#define PULSE_ACTIVE_LOW  true     // true: the pin goes LOW during a pulse (PC817 output pulls it down)
#define SERIAL_BAUD       115200   // must match the baud rate in PisoKlik > Settings

#define DEBOUNCE_US       5000UL   // edges closer together than this are noise
#define GAP_MS            300UL    // no pulse for this long = the coin has finished
#define RESEND_MS         1000UL   // repeat an unacknowledged coin every second
#define MAX_PULSES        50       // a count above this is treated as noise and thrown away
#define ENABLE_SIM        1        // 1 = allow the SIM,<n> test command. Set to 0 for the shop.

const uint8_t SCAN_COUNT = sizeof(SCAN_PINS);

// ============================ STATE ===============================

#define EDGE_LOG 32                // pulses remembered for RAW mode

volatile uint32_t totalEdges   = 0;   // every edge the interrupt has ever seen (including noise)
volatile uint16_t pulseCount   = 0;
volatile uint16_t noiseCount   = 0;   // edges thrown away by the debounce filter
volatile uint32_t lastEdgeUs   = 0;
volatile uint32_t firstPulseMs = 0;
volatile uint32_t lastPulseMs  = 0;

volatile uint32_t edgeGapUs[EDGE_LOG];   // time since the previous accepted pulse
volatile uint8_t  edgeHead = 0;
volatile uint8_t  edgeTail = 0;

// Coins waiting for the PC to acknowledge them (oldest first, sent one at a time)
const uint8_t QSIZE = 32;
struct Coin { uint32_t seq; uint16_t pulses; };
Coin     queue[QSIZE];
uint8_t  qCount = 0;
uint32_t nextSeq = 1;
bool     headSent = false;
uint32_t headSentMs = 0;

bool     calibration = false;
bool     rawMode = false;
bool     liveMode = false;
uint8_t  liveLast = 1;
uint32_t liveNextPrint = 0;
uint32_t ledUntil = 0;

char     lineBuf[48];
uint8_t  lineLen = 0;

bool     scanning = false;
uint32_t scanEnd = 0;
uint8_t  scanLast[sizeof(SCAN_PINS)];

// ============================ HELPERS =============================

// printf for any board (the Mega's Serial has no printf)
void say(const char *fmt, ...)
{
  char buf[128];
  va_list args;
  va_start(args, fmt);
  vsnprintf(buf, sizeof(buf), fmt, args);
  va_end(args);
  Serial.print(buf);
}

// Reads the edge counter safely (it is 32 bits and the interrupt changes it)
uint32_t edgesSeen()
{
  noInterrupts();
  uint32_t v = totalEdges;
  interrupts();
  return v;
}

// ============================ PULSES ==============================

void IRAM_ATTR onPulse()
{
  totalEdges++;

  uint32_t nowUs = micros();
  uint32_t gapUs = nowUs - lastEdgeUs;

  if (gapUs < DEBOUNCE_US)             // too soon after the last pulse: noise
  {
    if (noiseCount < 65535) noiseCount++;
    return;
  }
  lastEdgeUs = nowUs;

  uint32_t ms = millis();
  if (pulseCount == 0) firstPulseMs = ms;
  pulseCount++;
  lastPulseMs = ms;

  uint8_t next = (edgeHead + 1) % EDGE_LOG;
  if (next != edgeTail)                // remember it for RAW mode
  {
    edgeGapUs[edgeHead] = gapUs;
    edgeHead = next;
  }
}

// True when a coin has finished sending pulses. Takes the counts and clears them.
bool takePulses(uint16_t &count, uint32_t &durationMs, uint16_t &noise)
{
  bool ready = false;
  noInterrupts();
  if (pulseCount > 0 && (millis() - lastPulseMs) >= GAP_MS)
  {
    count = pulseCount;
    durationMs = lastPulseMs - firstPulseMs;
    noise = noiseCount;
    pulseCount = 0;
    noiseCount = 0;
    ready = true;
  }
  interrupts();
  return ready;
}

void clearPulses()
{
  noInterrupts();
  pulseCount = 0;
  noiseCount = 0;
  interrupts();
}

// RAW mode: print every pulse as it arrives
void printEdges()
{
  while (edgeTail != edgeHead)
  {
    noInterrupts();
    uint32_t gap = edgeGapUs[edgeTail];
    interrupts();
    edgeTail = (edgeTail + 1) % EDGE_LOG;

    if (gap > GAP_MS * 1000UL)
      say("DBG,EDGE,pulse (first of a new coin)\n");
    else
      say("DBG,EDGE,pulse, %lu.%lu ms after the previous one\n",
          (unsigned long)(gap / 1000), (unsigned long)((gap % 1000) / 100));
  }
}

// ============================ LED =================================

void blinkLed(uint16_t ms)
{
  digitalWrite(LED_BUILTIN, LED_ON);
  ledUntil = millis() + ms;
}

// ============================ COIN QUEUE ==========================

void enqueueCoin(uint16_t pulses)
{
  if (qCount >= QSIZE)
  {
    say("DBG,QUEUE_FULL,coin lost\n");
    return;
  }
  queue[qCount].seq = nextSeq++;
  queue[qCount].pulses = pulses;
  qCount++;
  if (qCount == 1) headSent = false;
}

void ackCoin(uint32_t seq)
{
  if (qCount > 0 && queue[0].seq == seq)
  {
    for (uint8_t i = 1; i < qCount; i++) queue[i - 1] = queue[i];
    qCount--;
    headSent = false;                  // send the next one right away
    say("DBG,ACKED,%lu\n", (unsigned long)seq);
  }
  else
  {
    say("DBG,ACK,%lu ignored (not the oldest pending coin)\n", (unsigned long)seq);
  }
}

// Sends the oldest coin and repeats it until acknowledged
void serviceQueue()
{
  if (qCount == 0) return;
  if (!headSent || (millis() - headSentMs) >= RESEND_MS)
  {
    say("PULSES,%lu,%u\n", (unsigned long)queue[0].seq, queue[0].pulses);
    headSent = true;
    headSentMs = millis();
  }
}

void handlePulses(uint16_t count, uint32_t durationMs, uint16_t noise)
{
  if (calibration)
  {
    say("DBG,CAL,pulses=%u,time=%lums,noise edges=%u\n", count, (unsigned long)durationMs, noise);
    blinkLed(150);
    return;
  }

  if (count > MAX_PULSES)
  {
    say("DBG,REJECT,pulses=%u (more than MAX_PULSES, treated as noise),noise edges=%u\n", count, noise);
    blinkLed(600);
    return;
  }

  say("DBG,COIN,pulses=%u,time=%lums,noise edges=%u\n", count, (unsigned long)durationMs, noise);
  enqueueCoin(count);
  blinkLed(150);
}

// ============================ TEST COMMANDS =======================

void printStatus()
{
  say("DBG,STATUS,board=%s,uptime=%lus,pending=%u,next_seq=%lu\n",
      BOARD_NAME, (unsigned long)(millis() / 1000), qCount, (unsigned long)nextSeq);
  say("DBG,STATUS,calibration=%s,raw=%s,live=%s\n",
      calibration ? "on" : "off", rawMode ? "on" : "off", liveMode ? "on" : "off");
  say("DBG,STATUS,coin pin=%d,pin_state=%s,expected idle state=%s\n",
      COIN_PIN, digitalRead(COIN_PIN) ? "HIGH" : "LOW", PULSE_ACTIVE_LOW ? "HIGH" : "LOW");
  say("DBG,STATUS,edges_seen=%lu (0 = nothing has ever reached the pin)\n",
      (unsigned long)edgesSeen());
}

// LIVE mode: shows the pin level, so you can see the pin react when you touch a wire or insert a coin
void startLive()
{
  liveLast = digitalRead(COIN_PIN);
  liveNextPrint = millis();
}

void runLive()
{
  uint8_t v = digitalRead(COIN_PIN);
  uint32_t now = millis();

  if (v != liveLast)
  {
    liveLast = v;
    say("DBG,LIVE,pin %d -> %s\n", COIN_PIN, v ? "HIGH" : "LOW");
    liveNextPrint = now + 1000;
  }
  else if ((int32_t)(now - liveNextPrint) >= 0)
  {
    say("DBG,LIVE,pin %d = %s (edges seen: %lu)\n",
        COIN_PIN, v ? "HIGH" : "LOW", (unsigned long)edgesSeen());
    liveNextPrint = now + 1000;
  }
}

void startScan()
{
  for (uint8_t i = 0; i < SCAN_COUNT; i++)
  {
    pinMode(SCAN_PINS[i], INPUT_PULLUP);
    scanLast[i] = digitalRead(SCAN_PINS[i]);
    say("DBG,SCAN,pin %s idle=%s\n", SCAN_NAMES[i], scanLast[i] ? "HIGH" : "LOW");
  }
  scanning = true;
  scanEnd = millis() + 15000;
  say("DBG,SCAN,started: insert a coin now (15 seconds)\n");
}

void runScan()
{
  for (uint8_t i = 0; i < SCAN_COUNT; i++)
  {
    uint8_t v = digitalRead(SCAN_PINS[i]);
    if (v != scanLast[i])
    {
      scanLast[i] = v;
      say("DBG,SCAN,pin %s -> %s\n", SCAN_NAMES[i], v ? "HIGH" : "LOW");
    }
  }
  if ((int32_t)(millis() - scanEnd) >= 0)
  {
    scanning = false;
    clearPulses();
    say("DBG,SCAN,finished. The pin that changed is your coin pin.\n");
  }
}

#if ENABLE_SIM
void simulate(int n)
{
  if (n < 1 || n > MAX_PULSES) return;
  noInterrupts();
  if (pulseCount == 0) firstPulseMs = millis();
  pulseCount += n;
  lastPulseMs = millis();
  interrupts();
  say("DBG,SIM,%d pulses\n", n);
}
#endif

// ============================ SERIAL INPUT ========================

void handleLine(char *line)
{
  if (strcasecmp(line, "PING") == 0)            { say("PONG\n"); return; }
  if (strncasecmp(line, "ACK,", 4) == 0)        { ackCoin(strtoul(line + 4, NULL, 10)); return; }

  if (strcasecmp(line, "LIVE") == 0)
  {
    liveMode = !liveMode;
    if (liveMode) startLive();
    say("DBG,LIVE,live view %s\n", liveMode ? "ON" : "OFF");
    return;
  }
  if (strcasecmp(line, "CAL") == 0)
  {
    calibration = !calibration;
    say("DBG,CAL,calibration view %s%s\n", calibration ? "ON" : "OFF",
        calibration ? " (coins are NOT sent to the PC)" : "");
    return;
  }
  if (strcasecmp(line, "RAW") == 0)
  {
    rawMode = !rawMode;
    noInterrupts();
    edgeTail = edgeHead;               // forget old pulses
    interrupts();
    say("DBG,RAW,raw mode %s\n", rawMode ? "ON" : "OFF");
    return;
  }
  if (strcasecmp(line, "STATUS") == 0)          { printStatus(); return; }
  if (strcasecmp(line, "SCAN") == 0)            { startScan(); return; }

#if ENABLE_SIM
  if (strncasecmp(line, "SIM,", 4) == 0)        { simulate(atoi(line + 4)); return; }
#endif
}

void readSerial()
{
  while (Serial.available())
  {
    char c = Serial.read();
    if (c == '\r') continue;
    if (c == '\n')
    {
      lineBuf[lineLen] = 0;
      if (lineLen > 0) handleLine(lineBuf);
      lineLen = 0;
    }
    else if (lineLen < sizeof(lineBuf) - 1) lineBuf[lineLen++] = c;
    else lineLen = 0;                  // line too long: throw it away
  }
}

// ============================ MAIN ================================

void setup()
{
  Serial.begin(SERIAL_BAUD);

  pinMode(LED_BUILTIN, OUTPUT);
  digitalWrite(LED_BUILTIN, LED_OFF);

  pinMode(COIN_PIN, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(COIN_PIN), onPulse, PULSE_ACTIVE_LOW ? FALLING : RISING);

  delay(300);
  say("READY\n");
  say("DBG,FW,CoinSlot 1.4,board=%s,coin pin=%d,baud=%ld\n", BOARD_NAME, COIN_PIN, (long)SERIAL_BAUD);
}

void loop()
{
  readSerial();

  if (liveMode) runLive();

  if (rawMode) printEdges();
  else         edgeTail = edgeHead;    // nobody is reading the pulse log

  if (scanning)
  {
    runScan();
  }
  else
  {
    uint16_t count, noise;
    uint32_t durationMs;
    if (takePulses(count, durationMs, noise)) handlePulses(count, durationMs, noise);
    serviceQueue();
  }

  if (ledUntil != 0 && (int32_t)(millis() - ledUntil) >= 0)
  {
    digitalWrite(LED_BUILTIN, LED_OFF);
    ledUntil = 0;
  }
}