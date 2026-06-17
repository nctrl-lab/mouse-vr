unsigned long now;

unsigned long waterDuration = 65000;
unsigned long waterTime = 0;

const unsigned long waterAmount[] = {13000,26000,39000,52000,65000,72000,79000,86000,93000,100000,107400,114800,122200,129600,137000};

// 2026.06.02
// 05ul = 65ms
// 10ul = 100ms
// 15ul = 137ms

bool waterState = false;
bool triggerState = false;

#define triggerPin 0
#define waterPin   1
#define startPin   2
#define endPin     3
#define leftPin    4
#define rewardPin  5

// startPin: HIGH = delay start, LOW = cue start
// endPin: HIGH = outcome start, LOW = outcome end / next trial start
// leftPin: HIGH = left, LOW = right

#define rewardOn()  {digitalWriteFast(waterPin, HIGH); digitalWriteFast(rewardPin, HIGH);}
#define rewardOff() {digitalWriteFast(waterPin, LOW);  digitalWriteFast(rewardPin, LOW);}
#define reset()     {rewardOff(); digitalWriteFast(triggerPin, LOW); digitalWriteFast(startPin, LOW); digitalWriteFast(endPin, LOW); digitalWriteFast(leftPin, LOW); waterState = false; triggerState = false;}

void setup() {
    Serial.begin(115200);
    Serial.setTimeout(10);
    pinMode(triggerPin, OUTPUT);
    pinMode(waterPin, OUTPUT);
    pinMode(startPin, OUTPUT);
    pinMode(endPin, OUTPUT);
    pinMode(leftPin, OUTPUT);
    pinMode(rewardPin, OUTPUT);
    reset();
    Serial.println("Ready");
}

void loop() {
    now = micros();
    checkSerial();
    checkWater();
}

void checkSerial() {
    if (Serial.available()) {
        char cmd = Serial.read();

        if (cmd == '?' || cmd == 'h') {
            Serial.println("==== Help ====");
            Serial.println("w: give water reward");
            Serial.println("0: reset (all outputs off)");
            Serial.println("i: open the water valve for 1 sec");
            Serial.println("v10: set the water volume to 10 ul (1-15)");
            Serial.println("d58000: set the water valve duration as 58 msec");
            Serial.println("s: session start (trigger on)");
            Serial.println("S: trial start (delay start)");
            Serial.println("L: left cue / R: right cue (cue start)");
            Serial.println("l: left choice / r: right choice (outcome start)");
            Serial.println("e: session end");
        }
        else if (cmd == '0') {
            reset();
        }
        else if (cmd == 'w')
        {
            waterState = true;
            waterTime = now;
            rewardOn();
        }
        else if (cmd == 'i')
        {
            rewardOn();
            delay(1000);
            rewardOff();
        }
        else if (cmd == 'v')
        {
            int volume = Serial.parseInt();
            if (volume >= 1 && volume <= 15)
            {
                waterDuration = waterAmount[volume - 1];
                Serial.print("Water volume: ");
                Serial.println(volume);
                Serial.print("Water duration: ");
                Serial.println(waterDuration);
            }
            else
            {
                Serial.print("Water duration: ");
                Serial.print(waterDuration);
                Serial.println(" (unchanged)");
            }
        }
        else if (cmd == 'd')
        {
            int duration = Serial.parseInt();
            if (duration >= 1000 && duration <= 10000000)
            {
                waterDuration = duration;
                Serial.print("Water duration: ");
                Serial.println(waterDuration);
            }
            else
            {
                Serial.print("Water duration: ");
                Serial.print(waterDuration);
                Serial.println(" (unchanged)");
            }
        }
        else if (cmd == 's' && triggerState == false)  // session start: trigger on, enables task commands
        {
          digitalWriteFast(triggerPin, HIGH);
          triggerState = true;
        }

        if (triggerState == false) {
          return;
        }

        if (cmd == 'S')  // trial start
        {
          digitalWriteFast(startPin, HIGH);  // delay start
          digitalWriteFast(endPin, LOW);     // outcome end / next trial start
          digitalWriteFast(leftPin, LOW);
        }
        else if (cmd == 'L') // left cue
        {
          digitalWriteFast(leftPin, HIGH);
          digitalWriteFast(startPin, LOW);  // cue start
        }
        else if (cmd == 'R') // right cue
        {
          digitalWriteFast(leftPin, LOW);
          digitalWriteFast(startPin, LOW);  // cue start
        }
        else if (cmd == 'l') // left choice
        {
          digitalWriteFast(leftPin, HIGH);
          digitalWriteFast(endPin, HIGH);  // outcome start
        }
        else if (cmd == 'r') // right choice
        {
          digitalWriteFast(leftPin, LOW);
          digitalWriteFast(endPin, HIGH);  // outcome start
        }
        else if (cmd == 'e')  // session end
        {
          reset();
        }
    }
}

void checkWater() {
    if (waterState) {
        if (now - waterTime >= waterDuration) {
            rewardOff();
            waterState = false;
        }
    }
}