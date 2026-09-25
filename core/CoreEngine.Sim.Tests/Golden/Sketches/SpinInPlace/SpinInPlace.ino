// Golden test sketch: the owner's turnRight() (2026-09-25), held. The left motor runs forward and the right
// one backward, both at analogWrite 250 on ENA (D5) and ENB (D10), so a two-wheeled robot turns on the spot.
// The benchmark times the turn with and without a ball caster.
#define ENA 5
#define IN1 6
#define IN2 7

#define IN3 8
#define IN4 9
#define ENB 10

void setup() {
  pinMode(ENA, OUTPUT);
  pinMode(IN1, OUTPUT);
  pinMode(IN2, OUTPUT);

  pinMode(IN3, OUTPUT);
  pinMode(IN4, OUTPUT);
  pinMode(ENB, OUTPUT);

  // Left motor forward
  digitalWrite(IN1, LOW);
  digitalWrite(IN2, HIGH);

  // Right motor backward
  digitalWrite(IN3, HIGH);
  digitalWrite(IN4, LOW);

  analogWrite(ENA, 250);
  analogWrite(ENB, 250);
}

void loop() {
}
