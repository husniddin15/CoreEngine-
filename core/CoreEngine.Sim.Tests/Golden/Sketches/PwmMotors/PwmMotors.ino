// Golden test sketch: two motors on an L298N with their speed set by analogWrite on ENA and ENB, the
// common way to wire it (the owner's first own robot, 2026-09-25). ENA is on D5 (Timer0, OC0B), ENB on D10
// (Timer1, OC1B), so both kinds of timer drive a PWM pin.
#define ENA 5
#define IN1 6
#define IN2 7

#define IN3 8
#define IN4 9
#define ENB 10

int motorSpeed = 180;

void setup() {
  pinMode(ENA, OUTPUT);
  pinMode(IN1, OUTPUT);
  pinMode(IN2, OUTPUT);

  pinMode(IN3, OUTPUT);
  pinMode(IN4, OUTPUT);
  pinMode(ENB, OUTPUT);
}

void loop() {
  // Left motor forward
  digitalWrite(IN1, HIGH);
  digitalWrite(IN2, LOW);

  // Right motor forward
  digitalWrite(IN3, HIGH);
  digitalWrite(IN4, LOW);

  // Motor speed
  analogWrite(ENA, motorSpeed);
  analogWrite(ENB, motorSpeed);
}
