const unsigned long airDuration = 200000;  // 200 ms
const unsigned long intervalDuration = 300000; // 300 ms
unsigned long finishDuration = 10000000; // infinite if zero

unsigned long now;

unsigned long airIntervalTime = 0;
unsigned long airFinalTime = 0;

unsigned long waterDuration = 50000;
unsigned long waterTime = 0;

const unsigned long waterAmount[] = {0, 8000, 16000, 24000, 32000, 40000, 44000, 48000, 51000, 55000, 58000, 61000, 64000, 66000, 68000, 70000};
// waterDuration: 58 ms = 10 ul
// 40 ms = 5 ul


#define STANDBY 0
#define ON 1
#define OFF 2
#define DONE 3
int airState = STANDBY;
bool waterState = false;
const int airPin = 0;
const int waterPin = 1;

void setup() {
    Serial.begin(115200);
    pinMode(airPin, OUTPUT);
    pinMode(waterPin, OUTPUT);
    digitalWriteFast(airPin, LOW);
    digitalWriteFast(waterPin, LOW);
    Serial.println("Ready");
}

void loop() {
    now = micros();
    checkSerial();
    checkAir();
    checkWater();
}

void checkSerial() {
    if (Serial.available()) {
    	  char cmd = Serial.read();
    	
        if (cmd == '?' || cmd == 'h') {
            Serial.println("==== Help ====");
            Serial.println("r: give water reward");
            Serial.println("p: give air puff");
            Serial.println("0: turn off both water and air values");
            Serial.println("i: open the water value for 1 sec");
            Serial.println("v10: set the water volume to 10 ul");
            Serial.println("d58000: set the water valve duration as 58 msec");
            Serial.println("f10: set the air puff duration to 10 sec");
        }
        else if (cmd == 'p') {
            if (airState == STANDBY  || airState == DONE) {
                airState = ON;
                airFinalTime = now;
                airIntervalTime = now;
                digitalWriteFast(airPin, HIGH);
                Serial.println("Air start");
            }
        }
        else if (cmd == '0') {
            airState = STANDBY;
            waterState = false;
            digitalWriteFast(airPin, LOW);
            digitalWriteFast(waterPin, LOW);
            Serial.println("Done");
        }
        else if (cmd == 'r')
        {
            waterState = true;
            waterTime = now;
            digitalWriteFast(waterPin, HIGH);
            Serial.println("Water start");
        }
        else if (cmd == 'i')
        {
            digitalWriteFast(waterPin, HIGH);
            delay(1000);
            digitalWriteFast(waterPin, LOW);
        }
        else if (cmd == 'v')
        {
            int volume = Serial.parseInt();
            if (volume >= 1 && volume <= 15)
            {
                waterDuration = waterAmount[volume];
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
        else if (cmd == 'f')
        {
            int duration = Serial.parseInt();
            if (duration >= 1 && duration <= 100)
            {
                finishDuration = duration*1000000;
                Serial.print("Air duration: ");
                Serial.println(finishDuration);
            }
            else
            {
                Serial.print("Air duration: ");
                Serial.print(finishDuration);
                Serial.println(" (unchanged)");
            }
        }
    }
}

void checkWater() {
    if (waterState) {
        if (now - waterDuration >= waterTime) {
            waterState = false;
            digitalWriteFast(waterPin, LOW);
            Serial.println("Water end");
        }
    }
}

void checkAir() {
    if (airState == ON) {
        if (finishDuration > 0 && now - finishDuration >= airFinalTime) {
    	    airState = DONE;
    	    digitalWriteFast(airPin, LOW);
    	}    	
        else if (now - airDuration >= airIntervalTime) {
            airState = OFF;
            airIntervalTime = now;
            digitalWriteFast(airPin, LOW);
        }

    }
    else if (airState == OFF) {
        if (now - intervalDuration >= airIntervalTime) {
            airState = ON;
            airIntervalTime = now;
            digitalWrite(airPin, HIGH);
        }
    }
}
