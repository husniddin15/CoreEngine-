// Golden test sketch: the classic Blink.
// The on-board LED (pin 13 = PB5) toggles every 1000 ms.

void setup() {
  pinMode(LED_BUILTIN, OUTPUT);
}

void loop() {
  digitalWrite(LED_BUILTIN, HIGH);
  delay(1000);
  digitalWrite(LED_BUILTIN, LOW);
  delay(1000);
}
