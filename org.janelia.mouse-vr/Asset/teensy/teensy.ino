const unsigned long airDuration = 200;  // 200 ms
const unsigned long intervalDuration = 300; // 300 ms
const unsigned long finishDuration = 0; // infinite if zero
unsigned long now;
unsigned long targetTime = 0;
unsigned long startTime = 0;

#define STANDBY 0
#define AIRON 1
#define AIROFF 2
#define DONE 3
int state = STANDBY;
const int airPin = 0;
const int waterPin = 1;

void setup() {
    Serial.begin(115200);
    pinMode(airPin, OUTPUT);
    pinMode(waterPin, OUTPUT);
    digitalWriteFast(airPin, LOW);
    digitalWriteFast(waterPin, HIGH); // high state is the off state for syringe pump
}

void loop() {
    now = millis();
    checkSerial();
    checkAir();
}

void checkSerial() {
    if (Serial.available()) {
    	char cmd = Serial.read();
    	
    	if (cmd == 'p'){
            if(state == STANDBY  || state == DONE) {
               state = AIRON;
               startTime = now;
               targetTime = now;
               digitalWriteFast(airPin, HIGH);
               Serial.println("Air start");
            }
        }
    	else if (cmd == 'r'){
               digitalWriteFast(waterPin, LOW);
               Serial.println("Water start");
        }
        else if (cmd == '0'){
            state = STANDBY;
            digitalWriteFast(airPin, LOW);
            digitalWriteFast(waterPin, HIGH);
            Serial.println("Done");
        }
    }
}

void checkAir() {
    if (state == AIRON) {
        if (finishDuration > 0 && now - finishDuration >= startTime) {
    	    state = DONE;
    	    digitalWriteFast(airPin, LOW);
    	}    	
        else if (now - airDuration >= targetTime) {
            state = AIROFF;
            targetTime = now;
            digitalWriteFast(airPin, LOW);
        }

    }
    else if (state == AIROFF) {
        if (now - intervalDuration >= targetTime) {
            state = AIRON;
            targetTime = now;
            digitalWrite(airPin, HIGH);
        }
    }
}
