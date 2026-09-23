// Golden test sketch: millis() accuracy at 115200 baud.
// Prints millis() every 250 ms; the printed values must match emulated time.

void setup() {
  Serial.begin(115200);
}

void loop() {
  Serial.println(millis());
  delay(250);
}
