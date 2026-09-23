// Golden test sketch: Serial output at 9600 baud.
// Prints a banner, then "tick N" every 100 ms.

unsigned long counter = 0;

void setup() {
  Serial.begin(9600);
  Serial.println("CoreEngine");
}

void loop() {
  Serial.print("tick ");
  Serial.println(counter++);
  delay(100);
}
