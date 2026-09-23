// Golden test sketch: a two-wheel obstacle avoider.
// L298N motor driver with ENA/ENB jumpers fitted (full speed), HC-SR04 ultrasonic sensor.
// Uses only digital pins, so it runs on the Phase 0 emulator.

const int IN1 = 5, IN2 = 6;   // left motor
const int IN3 = 7, IN4 = 8;   // right motor
const int TRIG = 9, ECHO = 10;

void setup() {
  pinMode(IN1, OUTPUT);
  pinMode(IN2, OUTPUT);
  pinMode(IN3, OUTPUT);
  pinMode(IN4, OUTPUT);
  pinMode(TRIG, OUTPUT);
  pinMode(ECHO, INPUT);
  Serial.begin(115200);
}

long readDistanceCm() {
  digitalWrite(TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(TRIG, LOW);
  long duration = pulseIn(ECHO, HIGH, 30000);  // microseconds, 0 if nothing is in range
  if (duration == 0) return 999;
  return duration / 58;
}

// -1 = backward, 0 = brake, +1 = forward
void drive(int left, int right) {
  digitalWrite(IN1, left > 0);
  digitalWrite(IN2, left < 0);
  digitalWrite(IN3, right > 0);
  digitalWrite(IN4, right < 0);
}

long lastCm = -1;
int unchanged = 0;   // readings in a row that did not change while driving forward

void loop() {
  long cm = readDistanceCm();
  Serial.println(cm);
  if (cm < 25) {
    drive(-1, -1);   // back off
    delay(250);
    drive(1, -1);    // turn right in place
    delay(350);
    unchanged = 0;
  } else {
    // The narrow ultrasonic beam misses boxes beside the robot. If the distance stays the
    // same for about a second while driving forward, the robot is probably stuck on one.
    if (labs(cm - lastCm) <= 1) unchanged++;
    else unchanged = 0;
    if (unchanged >= 15) {
      drive(-1, -1);
      delay(400);
      drive(-1, 1);  // turn left in place
      delay(300);
      unchanged = 0;
    } else {
      drive(1, 1);
    }
  }
  lastCm = cm;
  delay(60);         // the HC-SR04 needs at least 60 ms between measurements
}
